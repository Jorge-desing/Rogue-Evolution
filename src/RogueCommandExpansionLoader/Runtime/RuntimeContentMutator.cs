using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;

namespace RogueCommandExpansionLoader.Runtime
{
    /// <summary>
    /// Adds the game collections whose runtime shapes were verified for 1.0.9:
    /// entity balancing rows/index/factory mappings/value caches, and progression stages/default AI.
    /// It deliberately does not alter blueprint pools, worlds, or other game data.
    /// </summary>
    internal sealed class RuntimeContentMutator
    {
        private const BindingFlags StaticFields = BindingFlags.Public | BindingFlags.NonPublic |
            BindingFlags.Static | BindingFlags.FlattenHierarchy;
        private const BindingFlags InstanceFields = BindingFlags.Public | BindingFlags.NonPublic |
            BindingFlags.Instance | BindingFlags.FlattenHierarchy;

        private readonly object _gate = new object();
        private readonly IRuntimeContentProvider _provider;
        private readonly Action<string> _log;

        private readonly Type _entityRowType;
        private readonly Type _balancingType;
        private readonly Type _progressionType;
        private readonly Type _stageType;
        private readonly Type _stageParametersType;
        private readonly Type _defaultAiType;
        private readonly Type _intValueCacheEntryType;
        private readonly Type _floatValueCacheEntryType;

        private readonly FieldInfo _entityIdField;
        private readonly FieldInfo _factoryForEntityIdField;
        private readonly FieldInfo _nullableStringValueField;
        private readonly FieldInfo _nullableStringHasValueField;
        private readonly FieldInfo _entityRowsField;
        private readonly FieldInfo _entityIndexField;
        private readonly FieldInfo _factoryEntityIdOfField;
        private readonly FieldInfo _imageOfField;
        private readonly FieldInfo _changeableIntValueCacheField;
        private readonly FieldInfo _changeableFloatValueCacheField;
        private readonly FieldInfo[] _progressionFields;
        private readonly FieldInfo _stagesField;
        private readonly FieldInfo _defaultAisField;

        private readonly Dictionary<object, HashSet<string>> _appliedContributionKeys =
            new Dictionary<object, HashSet<string>>(ReferenceIdentityComparer.Instance);
        private object _lastGameBalancing;
        private bool _entityRegistrationCompleted;
        private readonly int _unityMainThreadId;
        private bool _offThreadWarningLogged;

        public RuntimeContentMutator(
            Assembly gameAssembly,
            IRuntimeContentProvider provider,
            Action<string> logger)
        {
            if (gameAssembly == null)
            {
                throw new ArgumentNullException("gameAssembly");
            }

            if (provider == null)
            {
                throw new ArgumentNullException("provider");
            }

            _provider = provider;
            _log = logger;
            _unityMainThreadId = Thread.CurrentThread.ManagedThreadId;

            _entityRowType = RuntimeReflection.FindUniqueType(gameAssembly,
                "EntityBalancingParameters");
            Type entityStoreType = RuntimeReflection.FindUniqueType(gameAssembly,
                "EntityBalancingStore");
            _balancingType = RuntimeReflection.FindUniqueType(gameAssembly,
                "GameBalancingScriptableObject");
            _progressionType = RuntimeReflection.FindUniqueType(gameAssembly,
                "LevelProgressionParametersScriptableObject");
            _stageType = RuntimeReflection.FindUniqueType(gameAssembly, "Stage");
            _stageParametersType = RuntimeReflection.FindUniqueType(gameAssembly,
                "StageParametersScriptableObject");
            _defaultAiType = RuntimeReflection.FindUniqueType(gameAssembly,
                "DefaultAiBehaviour");

            if (!_entityRowType.IsValueType)
            {
                throw new InvalidOperationException("EntityBalancingParameters is not a value type.");
            }

            _entityIdField = RuntimeReflection.FindField(_entityRowType, "entityId", InstanceFields);
            if (_entityIdField.FieldType != typeof(string))
            {
                throw new InvalidOperationException("EntityBalancingParameters.entityId is not a string.");
            }

            _factoryForEntityIdField = RuntimeReflection.FindField(_entityRowType,
                "factoryForEntityId", InstanceFields);
            Type nullableStringType = _factoryForEntityIdField.FieldType;
            if (_factoryForEntityIdField.IsStatic || !nullableStringType.IsValueType ||
                !RuntimeReflection.HasSimpleName(nullableStringType, "NullableString"))
            {
                throw new InvalidOperationException(
                    "EntityBalancingParameters.factoryForEntityId is not NullableString.");
            }

            _nullableStringValueField = RuntimeReflection.FindField(nullableStringType,
                "value", InstanceFields);
            _nullableStringHasValueField = RuntimeReflection.FindField(nullableStringType,
                "hasValue", InstanceFields);
            if (_nullableStringValueField.IsStatic ||
                _nullableStringValueField.FieldType != typeof(string) ||
                _nullableStringHasValueField.IsStatic ||
                _nullableStringHasValueField.FieldType != typeof(bool))
            {
                throw new InvalidOperationException(
                    "NullableString.value/hasValue do not have the verified string/bool shape.");
            }

            _entityRowsField = RuntimeReflection.FindField(entityStoreType,
                "EntityBalancingParametersList", StaticFields);
            if (!_entityRowsField.IsStatic ||
                !RuntimeReflection.IsExactList(_entityRowsField.FieldType, _entityRowType))
            {
                throw new InvalidOperationException(
                    "EntityBalancingParametersList is not List<EntityBalancingParameters>.");
            }

            _entityIndexField = RuntimeReflection.FindField(entityStoreType,
                "ParameterListIndexOf", StaticFields);
            Type expectedIndexType = typeof(Dictionary<string, int>);
            if (!_entityIndexField.IsStatic || _entityIndexField.FieldType != expectedIndexType)
            {
                throw new InvalidOperationException(
                    "ParameterListIndexOf is not Dictionary<string, int>.");
            }

            _factoryEntityIdOfField = RuntimeReflection.FindField(entityStoreType,
                "FactoryEntityIdOf", StaticFields);
            if (!_factoryEntityIdOfField.IsStatic ||
                _factoryEntityIdOfField.FieldType != typeof(Dictionary<string, string>))
            {
                throw new InvalidOperationException(
                    "FactoryEntityIdOf is not Dictionary<string, string>.");
            }

            _imageOfField = RuntimeReflection.FindField(entityStoreType, "ImageOf", StaticFields);
            if (!_imageOfField.IsStatic ||
                _imageOfField.FieldType != typeof(Dictionary<string, UnityEngine.Sprite>))
            {
                throw new InvalidOperationException(
                    "ImageOf is not Dictionary<string, Sprite>.");
            }

            Type changeableValueType = entityStoreType.GetNestedType("ChangeableValue",
                BindingFlags.Public | BindingFlags.NonPublic);
            if (changeableValueType == null || !changeableValueType.IsEnum)
            {
                throw new InvalidOperationException(
                    "EntityBalancingStore.ChangeableValue is not the verified enum type.");
            }

            _intValueCacheEntryType = typeof(Dictionary<,>).MakeGenericType(
                changeableValueType, typeof(int));
            _floatValueCacheEntryType = typeof(Dictionary<,>).MakeGenericType(
                changeableValueType, typeof(float));
            Type intValueCacheType = typeof(Dictionary<,>).MakeGenericType(
                typeof(string), _intValueCacheEntryType);
            Type floatValueCacheType = typeof(Dictionary<,>).MakeGenericType(
                typeof(string), _floatValueCacheEntryType);

            _changeableIntValueCacheField = RuntimeReflection.FindField(entityStoreType,
                "ChangeableIntValueCache", StaticFields);
            if (!_changeableIntValueCacheField.IsStatic ||
                _changeableIntValueCacheField.FieldType != intValueCacheType)
            {
                throw new InvalidOperationException(
                    "ChangeableIntValueCache does not have the verified nested dictionary shape.");
            }

            _changeableFloatValueCacheField = RuntimeReflection.FindField(entityStoreType,
                "ChangeableFloatValueCache", StaticFields);
            if (!_changeableFloatValueCacheField.IsStatic ||
                _changeableFloatValueCacheField.FieldType != floatValueCacheType)
            {
                throw new InvalidOperationException(
                    "ChangeableFloatValueCache does not have the verified nested dictionary shape.");
            }

            string[] progressionFieldNames =
            {
                "levelProgressionParameters",
                "levelProgressionParametersHeat",
                "levelProgressionParametersFirstRun"
            };
            _progressionFields = new FieldInfo[progressionFieldNames.Length];
            for (int i = 0; i < progressionFieldNames.Length; i++)
            {
                FieldInfo field = RuntimeReflection.FindField(_balancingType,
                    progressionFieldNames[i], InstanceFields);
                if (field.IsStatic || field.FieldType != _progressionType)
                {
                    throw new InvalidOperationException("Game balancing field '" +
                        progressionFieldNames[i] + "' is not a progression ScriptableObject.");
                }

                _progressionFields[i] = field;
            }

            _stagesField = RuntimeReflection.FindField(_progressionType, "stages", InstanceFields);
            if (_stagesField.IsStatic || !RuntimeReflection.IsExactList(_stagesField.FieldType, _stageType))
            {
                throw new InvalidOperationException(
                    "Level progression stages is not List<Stage>.");
            }

            FieldInfo stageParametersField = RuntimeReflection.FindField(_stageType,
                "stageParameters", InstanceFields);
            if (stageParametersField.FieldType != _stageParametersType)
            {
                throw new InvalidOperationException("Stage.stageParameters is not a StageParametersScriptableObject.");
            }

            _defaultAisField = RuntimeReflection.FindField(_progressionType, "defaultAis", InstanceFields);
            if (_defaultAisField.IsStatic ||
                !RuntimeReflection.IsExactList(_defaultAisField.FieldType, _defaultAiType))
            {
                throw new InvalidOperationException(
                    "Level progression defaultAis is not List<DefaultAiBehaviour>.");
            }
        }

        public bool RegisterEntityRows()
        {
            if (!EnsureUnityMainThread("entity registration"))
            {
                return false;
            }

            bool notifyProvider = false;
            bool registrationSucceeded = false;
            lock (_gate)
            {
                if (_entityRegistrationCompleted)
                {
                    return true;
                }

                IList rowsList = _entityRowsField.GetValue(null) as IList;
                IDictionary index = _entityIndexField.GetValue(null) as IDictionary;
                IDictionary factoryMap = _factoryEntityIdOfField.GetValue(null) as IDictionary;
                IDictionary imageCache = _imageOfField.GetValue(null) as IDictionary;
                IDictionary intValueCache = _changeableIntValueCacheField.GetValue(null) as IDictionary;
                IDictionary floatValueCache = _changeableFloatValueCacheField.GetValue(null) as IDictionary;
                if (rowsList == null || index == null || factoryMap == null || imageCache == null ||
                    intValueCache == null || floatValueCache == null)
                {
                    Log("Entity balancing rows, index, factory map, image cache, or value caches are not initialized; " +
                        "content rows were not added.");
                    return false;
                }

                try
                {
                    IEnumerable<object> rows = _provider.GetEntityParameterRows();
                    List<object> materializedRows = new List<object>();
                    if (rows != null)
                    {
                        foreach (object row in rows)
                        {
                            materializedRows.Add(row);
                        }
                    }

                    if (!TryRegisterEntityRows(materializedRows, rowsList, index, factoryMap, imageCache,
                            intValueCache, floatValueCache))
                    {
                        return false;
                    }

                    _entityRegistrationCompleted = true;
                    notifyProvider = true;
                    registrationSucceeded = true;
                }
                catch (Exception exception)
                {
                    Log("Could not finish registering mod entity rows: " + exception.Message);
                    return false;
                }
            }

            if (notifyProvider)
            {
                try
                {
                    _provider.OnEntityRowsRegistered();
                }
                catch (Exception exception)
                {
                    Log("Could not refresh content-dependent game caches: " + exception.Message);
                }
            }

            return registrationSucceeded;
        }

        public void ApplyProgressionFrom(object gameBalancingScriptableObject)
        {
            if (!EnsureUnityMainThread("progression registration"))
            {
                return;
            }

            if (gameBalancingScriptableObject == null ||
                !_balancingType.IsInstanceOfType(gameBalancingScriptableObject))
            {
                Log("SetScriptableObject supplied an unexpected game balancing object.");
                return;
            }

            lock (_gate)
            {
                _lastGameBalancing = gameBalancingScriptableObject;
            }

            ApplyProgression(gameBalancingScriptableObject);
        }

        public void ApplyCachedProgression()
        {
            if (!EnsureUnityMainThread("cached progression registration"))
            {
                return;
            }

            object balancing;
            lock (_gate)
            {
                balancing = _lastGameBalancing;
            }

            if (balancing != null)
            {
                ApplyProgression(balancing);
            }
        }

        private bool TryRegisterEntityRows(
            IList<object> suppliedRows,
            IList rows,
            IDictionary index,
            IDictionary factoryMap,
            IDictionary imageCache,
            IDictionary intValueCache,
            IDictionary floatValueCache)
        {
            List<object> rowsToAppend = new List<object>();
            List<string> entityIdsToAppend = new List<string>();
            HashSet<string> incomingIdSet = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> availableEntityIds = new HashSet<string>(StringComparer.Ordinal);
            Dictionary<string, string> factoryByProduct =
                new Dictionary<string, string>(StringComparer.Ordinal);

            for (int i = 0; i < rows.Count; i++)
            {
                string existingId = ReadEntityId(rows[i]);
                if (!String.IsNullOrWhiteSpace(existingId) &&
                    IsIndexedRow(index, rows, existingId))
                {
                    availableEntityIds.Add(existingId);
                }
            }

            for (int i = 0; i < suppliedRows.Count; i++)
            {
                object row = suppliedRows[i];
                if (row == null || row.GetType() != _entityRowType)
                {
                    Log("Entity row batch contains an object whose runtime type is not " +
                        "EntityBalancingParameters; the batch was not registered.");
                    return false;
                }

                string entityId = ReadEntityId(row);
                if (String.IsNullOrWhiteSpace(entityId))
                {
                    Log("Entity row batch contains an empty entityId; the batch was not registered.");
                    return false;
                }

                if (!incomingIdSet.Add(entityId))
                {
                    Log("Entity row batch contains duplicate entityId '" + entityId +
                        "'; the batch was not registered.");
                    return false;
                }

                if (index.Contains(entityId))
                {
                    if (!IsIndexedRow(index, rows, entityId))
                    {
                        Log("Entity index/list disagree for '" + entityId +
                            "'; the batch was not registered.");
                        return false;
                    }

                    Log("Entity ID '" + entityId +
                        "' is already registered by the game or another content source; " +
                        "the batch was not registered.");
                    return false;
                }
                else
                {
                    for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
                    {
                        if (String.Equals(ReadEntityId(rows[rowIndex]), entityId,
                                StringComparison.Ordinal))
                        {
                            Log("Entity row '" + entityId +
                                "' exists without a matching index; the batch was not registered.");
                            return false;
                        }
                    }
                }

                if (intValueCache.Contains(entityId) || floatValueCache.Contains(entityId))
                {
                    Log("Entity value cache already contains unregistered ID '" + entityId +
                        "'; the batch was not registered.");
                    return false;
                }

                rowsToAppend.Add(row);
                entityIdsToAppend.Add(entityId);
                availableEntityIds.Add(entityId);

                bool hasProduct;
                string productId;
                if (!TryReadFactoryProduct(row, entityId, out hasProduct, out productId))
                {
                    return false;
                }

                if (hasProduct)
                {
                    string existingFactoryId;
                    if (factoryByProduct.TryGetValue(productId, out existingFactoryId) &&
                        !String.Equals(existingFactoryId, entityId, StringComparison.Ordinal))
                    {
                        Log("Product '" + productId + "' is assigned to more than one factory; " +
                            "the batch was not registered.");
                        return false;
                    }

                    factoryByProduct[productId] = entityId;
                }
            }

            foreach (KeyValuePair<string, string> relation in factoryByProduct)
            {
                if (!availableEntityIds.Contains(relation.Key))
                {
                    Log("Factory '" + relation.Value + "' references unavailable product '" +
                        relation.Key + "'; the batch was not registered.");
                    return false;
                }

                if (factoryMap.Contains(relation.Key))
                {
                    string registeredFactory;
                    try
                    {
                        registeredFactory = factoryMap[relation.Key] as string;
                    }
                    catch (Exception exception)
                    {
                        Log("Factory map entry for product '" + relation.Key +
                            "' could not be read: " + exception.Message);
                        return false;
                    }

                    if (!String.Equals(registeredFactory, relation.Value, StringComparison.Ordinal))
                    {
                        Log("Product '" + relation.Key + "' is already assigned to factory '" +
                            registeredFactory + "'; the batch was not registered.");
                        return false;
                    }
                }
            }

            int originalRowCount = rows.Count;
            List<object> appendedRows = new List<object>(rowsToAppend.Count);
            List<string> appendedRowIds = new List<string>(rowsToAppend.Count);
            List<KeyValuePair<string, int>> addedIndexMappings =
                new List<KeyValuePair<string, int>>(rowsToAppend.Count);
            List<KeyValuePair<string, int>> attemptedIndexMappings =
                new List<KeyValuePair<string, int>>(rowsToAppend.Count);
            List<KeyValuePair<string, string>> addedFactoryMappings =
                new List<KeyValuePair<string, string>>(factoryByProduct.Count);
            List<KeyValuePair<string, string>> attemptedFactoryMappings =
                new List<KeyValuePair<string, string>>(factoryByProduct.Count);
            var attemptedImageIds = new List<string>(entityIdsToAppend.Count);
            List<KeyValuePair<string, object>> attemptedIntValueCacheEntries =
                new List<KeyValuePair<string, object>>(rowsToAppend.Count);
            List<KeyValuePair<string, object>> addedIntValueCacheEntries =
                new List<KeyValuePair<string, object>>(rowsToAppend.Count);
            List<KeyValuePair<string, object>> attemptedFloatValueCacheEntries =
                new List<KeyValuePair<string, object>>(rowsToAppend.Count);
            List<KeyValuePair<string, object>> addedFloatValueCacheEntries =
                new List<KeyValuePair<string, object>>(rowsToAppend.Count);
            try
            {
                for (int i = 0; i < rowsToAppend.Count; i++)
                {
                    rows.Add(rowsToAppend[i]);
                    appendedRows.Add(rowsToAppend[i]);
                    appendedRowIds.Add(entityIdsToAppend[i]);
                }

                for (int i = 0; i < entityIdsToAppend.Count; i++)
                {
                    string entityId = entityIdsToAppend[i];
                    if (!imageCache.Contains(entityId))
                    {
                        continue;
                    }

                    object previousImage = imageCache[entityId];
                    if (previousImage == null)
                    {
                        attemptedImageIds.Add(entityId);
                        imageCache.Remove(entityId);
                        if (imageCache.Contains(entityId))
                        {
                            throw new InvalidOperationException("Cached image for '" + entityId + "' could not be invalidated.");
                        }
                    }
                }

                for (int i = 0; i < entityIdsToAppend.Count; i++)
                {
                    string entityId = entityIdsToAppend[i];
                    object intEntry = Activator.CreateInstance(_intValueCacheEntryType);
                    object floatEntry = Activator.CreateInstance(_floatValueCacheEntryType);
                    var intCacheMapping = new KeyValuePair<string, object>(entityId, intEntry);
                    var floatCacheMapping = new KeyValuePair<string, object>(entityId, floatEntry);

                    attemptedIntValueCacheEntries.Add(intCacheMapping);
                    intValueCache.Add(entityId, intEntry);
                    addedIntValueCacheEntries.Add(intCacheMapping);

                    attemptedFloatValueCacheEntries.Add(floatCacheMapping);
                    floatValueCache.Add(entityId, floatEntry);
                    addedFloatValueCacheEntries.Add(floatCacheMapping);
                }

                for (int i = 0; i < rowsToAppend.Count; i++)
                {
                    string entityId = entityIdsToAppend[i];
                    int position = originalRowCount + i;
                    attemptedIndexMappings.Add(new KeyValuePair<string, int>(entityId, position));
                    index.Add(entityId, position);
                    addedIndexMappings.Add(new KeyValuePair<string, int>(entityId, position));
                }

                foreach (KeyValuePair<string, string> relation in factoryByProduct)
                {
                    if (factoryMap.Contains(relation.Key))
                    {
                        continue;
                    }

                    attemptedFactoryMappings.Add(relation);
                    factoryMap.Add(relation.Key, relation.Value);
                    addedFactoryMappings.Add(relation);
                }

                return true;
            }
            catch (Exception exception)
            {
                bool rollbackComplete = RollbackEntityRegistration(rows, index, factoryMap,
                    imageCache, attemptedImageIds,
                    intValueCache, floatValueCache,
                    appendedRows, appendedRowIds, attemptedIndexMappings,
                    addedIndexMappings, attemptedFactoryMappings, addedFactoryMappings,
                    attemptedIntValueCacheEntries, addedIntValueCacheEntries,
                    attemptedFloatValueCacheEntries, addedFloatValueCacheEntries);
                string rollbackStatus = rollbackComplete
                    ? "all loader changes were rolled back"
                    : "rollback could not be fully verified; a shared registration conflicted or could not be removed safely";
                Log("Could not commit entity rows and factory/product mappings; " +
                    rollbackStatus + ": " + exception.Message);
                return false;
            }
        }

        private bool HasValueCacheEntry(IDictionary cache, string entityId, Type expectedEntryType)
        {
            try
            {
                if (!cache.Contains(entityId))
                {
                    return false;
                }

                object entry = cache[entityId];
                return entry != null && entry.GetType() == expectedEntryType && entry is IDictionary;
            }
            catch
            {
                return false;
            }
        }

        private bool RollbackValueCache(
            IDictionary cache,
            IList<KeyValuePair<string, object>> attemptedEntries,
            IList<KeyValuePair<string, object>> addedEntries,
            HashSet<string> preserveRegistrationIds,
            string valueKind)
        {
            bool rollbackComplete = true;
            var addedIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < addedEntries.Count; i++)
            {
                addedIds.Add(addedEntries[i].Key);
            }

            for (int i = attemptedEntries.Count - 1; i >= 0; i--)
            {
                KeyValuePair<string, object> attempted = attemptedEntries[i];
                if (preserveRegistrationIds.Contains(attempted.Key))
                {
                    rollbackComplete = false;
                    continue;
                }

                try
                {
                    if (!cache.Contains(attempted.Key))
                    {
                        continue;
                    }

                    object currentEntry = cache[attempted.Key];
                    if (!ReferenceEquals(currentEntry, attempted.Value))
                    {
                        preserveRegistrationIds.Add(attempted.Key);
                        rollbackComplete = false;
                        Log("Preserved the " + valueKind + " value-cache entry for '" +
                            attempted.Key + "' because its identity changed during registration rollback.");
                        continue;
                    }

                    cache.Remove(attempted.Key);
                    if (cache.Contains(attempted.Key))
                    {
                        preserveRegistrationIds.Add(attempted.Key);
                        rollbackComplete = false;
                        Log("The " + valueKind + " value-cache entry for '" + attempted.Key +
                            "' remained after rollback attempted to remove it.");
                    }
                }
                catch (Exception exception)
                {
                    preserveRegistrationIds.Add(attempted.Key);
                    rollbackComplete = false;
                    string commitStatus = addedIds.Contains(attempted.Key)
                        ? "registered"
                        : "attempted during registration";
                    Log("Could not safely roll back the " + valueKind + " value-cache entry for '" +
                        attempted.Key + "' that was " + commitStatus + "; its row will be preserved: " +
                        exception.Message);
                }
            }

            return rollbackComplete;
        }

        private bool RestorePreservedValueCacheEntries(
            IDictionary cache,
            IList<KeyValuePair<string, object>> attemptedEntries,
            IList rows,
            HashSet<string> appendedRowIdSet,
            HashSet<string> preserveRegistrationIds,
            Type expectedEntryType,
            string valueKind)
        {
            bool restoreComplete = true;
            for (int i = 0; i < attemptedEntries.Count; i++)
            {
                KeyValuePair<string, object> attempted = attemptedEntries[i];
                string entityId = attempted.Key;
                if (!preserveRegistrationIds.Contains(entityId) ||
                    !appendedRowIdSet.Contains(entityId) || !ContainsEntityRow(rows, entityId))
                {
                    continue;
                }

                try
                {
                    if (cache.Contains(entityId))
                    {
                        if (!HasValueCacheEntry(cache, entityId, expectedEntryType))
                        {
                            restoreComplete = false;
                            Log("Could not verify the preserved " + valueKind +
                                " value-cache entry for '" + entityId + "'.");
                        }

                        continue;
                    }

                    cache.Add(entityId, attempted.Value);
                    if (!HasValueCacheEntry(cache, entityId, expectedEntryType))
                    {
                        restoreComplete = false;
                        Log("Could not restore the " + valueKind + " value-cache entry for preserved row '" +
                            entityId + "'.");
                    }
                }
                catch (Exception exception)
                {
                    restoreComplete = false;
                    Log("Could not restore the " + valueKind + " value-cache entry for preserved row '" +
                        entityId + "': " + exception.Message);
                }
            }

            return restoreComplete;
        }

        private bool RestorePreservedEntityIndexes(
            IList rows,
            IDictionary index,
            IList<string> appendedRowIds,
            HashSet<string> preserveRegistrationIds)
        {
            bool restoreComplete = true;
            for (int i = 0; i < appendedRowIds.Count; i++)
            {
                string entityId = appendedRowIds[i];
                if (!preserveRegistrationIds.Contains(entityId))
                {
                    continue;
                }

                int matchingRows = 0;
                int matchingPosition = -1;
                for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
                {
                    if (!String.Equals(ReadEntityId(rows[rowIndex]), entityId,
                            StringComparison.Ordinal))
                    {
                        continue;
                    }

                    matchingRows++;
                    matchingPosition = rowIndex;
                }

                if (matchingRows == 0)
                {
                    continue;
                }

                if (matchingRows != 1)
                {
                    restoreComplete = false;
                    Log("Could not restore the entity index for preserved row '" + entityId +
                        "' because the row ID is not unique.");
                    continue;
                }

                try
                {
                    if (index.Contains(entityId))
                    {
                        int currentPosition = Convert.ToInt32(index[entityId]);
                        if (currentPosition != matchingPosition)
                        {
                            restoreComplete = false;
                            Log("Could not restore the entity index for preserved row '" + entityId +
                                "' because another mapping points to position " + currentPosition + ".");
                        }

                        continue;
                    }

                    index.Add(entityId, matchingPosition);
                    if (!index.Contains(entityId) ||
                        Convert.ToInt32(index[entityId]) != matchingPosition)
                    {
                        restoreComplete = false;
                        Log("Could not verify the restored entity index for preserved row '" +
                            entityId + "'.");
                    }
                }
                catch (Exception exception)
                {
                    restoreComplete = false;
                    Log("Could not restore the entity index for preserved row '" + entityId +
                        "': " + exception.Message);
                }
            }

            return restoreComplete;
        }

        private bool IsIndexedRow(IDictionary index, IList rows, string entityId)
        {
            if (!index.Contains(entityId))
            {
                return false;
            }

            try
            {
                int position = Convert.ToInt32(index[entityId]);
                int matchingRows = 0;
                int matchingPosition = -1;
                for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
                {
                    if (String.Equals(ReadEntityId(rows[rowIndex]), entityId, StringComparison.Ordinal))
                    {
                        matchingRows++;
                        matchingPosition = rowIndex;
                    }
                }

                return matchingRows == 1 && position == matchingPosition &&
                    position >= 0 && position < rows.Count;
            }
            catch
            {
                return false;
            }
        }

        private bool TryReadFactoryProduct(
            object row,
            string factoryId,
            out bool hasProduct,
            out string productId)
        {
            hasProduct = false;
            productId = null;
            try
            {
                object nullableString = _factoryForEntityIdField.GetValue(row);
                hasProduct = (bool)_nullableStringHasValueField.GetValue(nullableString);
                productId = _nullableStringValueField.GetValue(nullableString) as string;
            }
            catch (Exception exception)
            {
                Log("Could not read factoryForEntityId on '" + factoryId + "': " + exception.Message);
                return false;
            }

            if (hasProduct && String.IsNullOrWhiteSpace(productId))
            {
                Log("Factory '" + factoryId + "' declares an empty product ID; " +
                    "the batch was not registered.");
                return false;
            }

            if (!hasProduct && !String.IsNullOrEmpty(productId))
            {
                Log("Factory '" + factoryId + "' has a product value without NullableString.hasValue; " +
                    "the batch was not registered.");
                return false;
            }

            return true;
        }

        private bool RollbackEntityRegistration(
            IList rows,
            IDictionary index,
            IDictionary factoryMap,
            IDictionary imageCache,
            IList<string> attemptedImageIds,
            IDictionary intValueCache,
            IDictionary floatValueCache,
            IList<object> appendedRows,
            IList<string> appendedRowIds,
            IList<KeyValuePair<string, int>> attemptedIndexMappings,
            IList<KeyValuePair<string, int>> addedIndexMappings,
            IList<KeyValuePair<string, string>> attemptedFactoryMappings,
            IList<KeyValuePair<string, string>> addedFactoryMappings,
            IList<KeyValuePair<string, object>> attemptedIntValueCacheEntries,
            IList<KeyValuePair<string, object>> addedIntValueCacheEntries,
            IList<KeyValuePair<string, object>> attemptedFloatValueCacheEntries,
            IList<KeyValuePair<string, object>> addedFloatValueCacheEntries)
        {
            bool rollbackComplete = true;
            var preserveRegistrationIds = new HashSet<string>(StringComparer.Ordinal);
            var appendedRowIdSet = new HashSet<string>(appendedRowIds, StringComparer.Ordinal);

            var addedIndexIdSet = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < addedIndexMappings.Count; i++)
            {
                addedIndexIdSet.Add(addedIndexMappings[i].Key);
            }

            for (int i = 0; i < attemptedIndexMappings.Count; i++)
            {
                string entityId = attemptedIndexMappings[i].Key;
                if (addedIndexIdSet.Contains(entityId))
                {
                    continue;
                }

                try
                {
                    if (index.Contains(entityId))
                    {
                        preserveRegistrationIds.Add(entityId);
                        rollbackComplete = false;
                        Log("Preserved entity row and index entry for '" + entityId +
                            "' because an untracked mapping appeared while the batch was committing.");
                    }
                }
                catch (Exception exception)
                {
                    preserveRegistrationIds.Add(entityId);
                    rollbackComplete = false;
                    Log("Could not inspect the entity index entry for '" + entityId +
                        "' during rollback; its row will be preserved: " + exception.Message);
                }
            }

            for (int i = 0; i < addedIndexMappings.Count; i++)
            {
                KeyValuePair<string, int> addedMapping = addedIndexMappings[i];
                try
                {
                    if (!index.Contains(addedMapping.Key))
                    {
                        continue;
                    }

                    int currentPosition = Convert.ToInt32(index[addedMapping.Key]);
                    if (currentPosition != addedMapping.Value)
                    {
                        preserveRegistrationIds.Add(addedMapping.Key);
                        rollbackComplete = false;
                        Log("Preserved entity row and index entry for '" + addedMapping.Key +
                            "' because its mapping changed during the batch commit.");
                    }
                }
                catch (Exception exception)
                {
                    preserveRegistrationIds.Add(addedMapping.Key);
                    rollbackComplete = false;
                    Log("Could not inspect entity index entry for '" + addedMapping.Key +
                        "' during rollback; its row will be preserved: " + exception.Message);
                }
            }

            var addedProductIdSet = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < addedFactoryMappings.Count; i++)
            {
                addedProductIdSet.Add(addedFactoryMappings[i].Key);
            }

            for (int i = 0; i < attemptedFactoryMappings.Count; i++)
            {
                KeyValuePair<string, string> attemptedMapping = attemptedFactoryMappings[i];
                try
                {
                    if (!factoryMap.Contains(attemptedMapping.Key))
                    {
                        continue;
                    }

                    string currentFactoryId = factoryMap[attemptedMapping.Key] as string;
                    bool mappingWasAdded = addedProductIdSet.Contains(attemptedMapping.Key);
                    bool mappingStillMatches = String.Equals(currentFactoryId,
                        attemptedMapping.Value, StringComparison.Ordinal);
                    bool relatedRowWasPreserved = preserveRegistrationIds.Contains(attemptedMapping.Key) ||
                        preserveRegistrationIds.Contains(attemptedMapping.Value);
                    if (!mappingWasAdded || !mappingStillMatches || relatedRowWasPreserved)
                    {
                        rollbackComplete = false;
                        PreserveFactoryMappingRows(preserveRegistrationIds, appendedRowIdSet,
                            attemptedMapping.Key, currentFactoryId);
                        if (!mappingWasAdded)
                        {
                            Log("Preserved the factory map entry for product '" + attemptedMapping.Key +
                                "' and its related loader rows because a registration appeared while the batch was committing.");
                        }
                        else if (!mappingStillMatches)
                        {
                            Log("Preserved factory map entry for product '" + attemptedMapping.Key +
                                "' with current factory '" + (currentFactoryId ?? "<non-string>") +
                                "' because its value changed during rollback; related loader rows will be preserved.");
                        }
                        else
                        {
                            Log("Preserved factory map entry for product '" + attemptedMapping.Key +
                                "' because a related loader row must remain registered.");
                        }
                    }
                }
                catch (Exception exception)
                {
                    PreserveFactoryMappingRows(preserveRegistrationIds, appendedRowIdSet,
                        attemptedMapping.Key, attemptedMapping.Value);
                    rollbackComplete = false;
                    Log("Could not inspect the factory map entry for product '" +
                        attemptedMapping.Key + "' during rollback; related rows will be preserved: " +
                        exception.Message);
                }
            }

            rollbackComplete &= RollbackValueCache(intValueCache,
                attemptedIntValueCacheEntries, addedIntValueCacheEntries,
                preserveRegistrationIds, "integer");
            rollbackComplete &= RollbackValueCache(floatValueCache,
                attemptedFloatValueCacheEntries, addedFloatValueCacheEntries,
                preserveRegistrationIds, "float");

            for (int i = addedFactoryMappings.Count - 1; i >= 0; i--)
            {
                KeyValuePair<string, string> addedMapping = addedFactoryMappings[i];
                try
                {
                    if (preserveRegistrationIds.Contains(addedMapping.Key) ||
                        preserveRegistrationIds.Contains(addedMapping.Value))
                    {
                        rollbackComplete = false;
                        continue;
                    }

                    if (!factoryMap.Contains(addedMapping.Key))
                    {
                        continue;
                    }

                    string currentFactoryId = factoryMap[addedMapping.Key] as string;
                    if (String.Equals(currentFactoryId, addedMapping.Value, StringComparison.Ordinal))
                    {
                        factoryMap.Remove(addedMapping.Key);
                        if (factoryMap.Contains(addedMapping.Key))
                        {
                            currentFactoryId = factoryMap[addedMapping.Key] as string;
                            PreserveFactoryMappingRows(preserveRegistrationIds, appendedRowIdSet,
                                addedMapping.Key, currentFactoryId);
                            rollbackComplete = false;
                            Log("Factory map entry for product '" + addedMapping.Key +
                                "' remained during rollback; its related loader rows will be preserved.");
                        }
                    }
                    else
                    {
                        PreserveFactoryMappingRows(preserveRegistrationIds, appendedRowIdSet,
                            addedMapping.Key, currentFactoryId);
                        rollbackComplete = false;
                        Log("Preserved factory map entry for product '" + addedMapping.Key +
                            "' and its related loader rows because its value changed during rollback.");
                    }
                }
                catch (Exception exception)
                {
                    try
                    {
                        string currentFactoryId = factoryMap.Contains(addedMapping.Key)
                            ? factoryMap[addedMapping.Key] as string
                            : addedMapping.Value;
                        PreserveFactoryMappingRows(preserveRegistrationIds, appendedRowIdSet,
                            addedMapping.Key, currentFactoryId);
                    }
                    catch
                    {
                        PreserveFactoryMappingRows(preserveRegistrationIds, appendedRowIdSet,
                            addedMapping.Key, addedMapping.Value);
                    }

                    rollbackComplete = false;
                    Log("Could not safely roll back factory map entry for product '" +
                        addedMapping.Key + "'; its related loader rows will be preserved: " +
                        exception.Message);
                }
            }

            for (int i = attemptedImageIds.Count - 1; i >= 0; i--)
            {
                string entityId = attemptedImageIds[i];

                if (preserveRegistrationIds.Contains(entityId))
                {
                    rollbackComplete = false;
                    continue;
                }

                try
                {
                    if (imageCache.Contains(entityId))
                    {
                        if (imageCache[entityId] != null)
                        {
                            preserveRegistrationIds.Add(entityId);
                            rollbackComplete = false;
                            Log("Preserved a newly populated image cache entry for '" + entityId + "' during rollback.");
                        }
                        else
                        {
                            imageCache.Remove(entityId);
                        }
                    }
                }
                catch (Exception exception)
                {
                    preserveRegistrationIds.Add(entityId);
                    rollbackComplete = false;
                    Log("Could not safely remove the image cache entry for '" + entityId +
                        "' during rollback: " + exception.Message);
                }
            }
            bool[] removedRows = new bool[appendedRows.Count];
            for (int i = appendedRows.Count - 1; i >= 0; i--)
            {
                string entityId = appendedRowIds[i];
                if (preserveRegistrationIds.Contains(entityId))
                {
                    rollbackComplete = false;
                    break;
                }

                int matchingIdCount = 0;
                int matchingValueCount = 0;
                int matchingRowPosition = -1;

                for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
                {
                    object currentRow = rows[rowIndex];
                    if (!String.Equals(ReadEntityId(currentRow), entityId, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    matchingIdCount++;
                    if (Object.Equals(currentRow, appendedRows[i]))
                    {
                        matchingValueCount++;
                        matchingRowPosition = rowIndex;
                    }
                }

                if (matchingIdCount == 0)
                {
                    removedRows[i] = true;
                    continue;
                }

                if (matchingIdCount != 1 || matchingValueCount != 1 ||
                    matchingRowPosition != rows.Count - 1)
                {
                    rollbackComplete = false;
                    break;
                }

                try
                {
                    rows.RemoveAt(matchingRowPosition);
                    removedRows[i] = true;
                }
                catch (Exception exception)
                {
                    rollbackComplete = false;
                    preserveRegistrationIds.Add(entityId);
                    Log("Could not remove the loader-owned entity row for '" + entityId +
                        "'; its registration mappings will be preserved: " + exception.Message);
                    break;
                }
            }

            for (int i = 0; i < appendedRowIds.Count; i++)
            {
                if (removedRows[i])
                {
                    continue;
                }

                string entityId = appendedRowIds[i];
                preserveRegistrationIds.Add(entityId);
                rollbackComplete = false;
                Log("Preserved registration mappings for loader row '" + entityId +
                    "' because rollback could not safely remove it from the end of the shared list.");
            }

            for (int i = addedFactoryMappings.Count - 1; i >= 0; i--)
            {
                KeyValuePair<string, string> addedMapping = addedFactoryMappings[i];
                if (!preserveRegistrationIds.Contains(addedMapping.Key) &&
                    !preserveRegistrationIds.Contains(addedMapping.Value))
                {
                    continue;
                }

                try
                {
                    if (factoryMap.Contains(addedMapping.Key))
                    {
                        continue;
                    }

                    if (ContainsEntityRow(rows, addedMapping.Key) &&
                        ContainsEntityRow(rows, addedMapping.Value))
                    {
                        factoryMap.Add(addedMapping.Key, addedMapping.Value);
                        Log("Restored factory map entry for product '" + addedMapping.Key +
                            "' because related loader rows had to be preserved.");
                    }
                    else
                    {
                        rollbackComplete = false;
                        Log("Could not restore factory map entry for product '" + addedMapping.Key +
                            "' because one of its related rows was removed or unavailable.");
                    }
                }
                catch (Exception exception)
                {
                    rollbackComplete = false;
                    Log("Could not restore factory map entry for product '" + addedMapping.Key +
                        "' after preserving related rows: " + exception.Message);
                }
            }

            for (int i = addedIndexMappings.Count - 1; i >= 0; i--)
            {
                KeyValuePair<string, int> addedMapping = addedIndexMappings[i];
                try
                {
                    string entityId = addedMapping.Key;
                    if (preserveRegistrationIds.Contains(entityId))
                    {
                        rollbackComplete = false;
                        continue;
                    }

                    bool rowStillExists = false;
                    for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
                    {
                        if (String.Equals(ReadEntityId(rows[rowIndex]), entityId, StringComparison.Ordinal))
                        {
                            rowStillExists = true;
                            break;
                        }
                    }

                    if (index.Contains(entityId))
                    {
                        if (rowStillExists)
                        {
                            rollbackComplete = false;
                            Log("Preserved entity index entry for '" + entityId +
                                "' because its row remains in the shared list.");
                        }
                        else
                        {
                            int currentPosition = Convert.ToInt32(index[entityId]);
                            if (currentPosition == addedMapping.Value)
                            {
                                index.Remove(entityId);
                                if (index.Contains(entityId))
                                {
                                    rollbackComplete = false;
                                    Log("Entity index entry for '" + entityId +
                                        "' remained after rollback attempted to remove it.");
                                }
                            }
                            else
                            {
                                rollbackComplete = false;
                                Log("Preserved entity index entry for '" + entityId +
                                    "' because its position changed during rollback.");
                            }
                        }
                    }
                    else if (rowStillExists)
                    {
                        rollbackComplete = false;
                    }
                }
                catch (Exception exception)
                {
                    rollbackComplete = false;
                    Log("Could not roll back entity index entry for '" + addedMapping.Key +
                        "': " + exception.Message);
                }
            }

            rollbackComplete &= RestorePreservedEntityIndexes(rows, index,
                appendedRowIds, preserveRegistrationIds);
            rollbackComplete &= RestorePreservedValueCacheEntries(intValueCache,
                attemptedIntValueCacheEntries, rows, appendedRowIdSet,
                preserveRegistrationIds, _intValueCacheEntryType, "integer");
            rollbackComplete &= RestorePreservedValueCacheEntries(floatValueCache,
                attemptedFloatValueCacheEntries, rows, appendedRowIdSet,
                preserveRegistrationIds, _floatValueCacheEntryType, "float");

            return rollbackComplete;
        }

        private string ReadEntityId(object row)
        {
            if (row == null || row.GetType() != _entityRowType)
            {
                return null;
            }

            return _entityIdField.GetValue(row) as string;
        }

        private bool ContainsEntityRow(IList rows, string entityId)
        {
            for (int i = 0; i < rows.Count; i++)
            {
                if (String.Equals(ReadEntityId(rows[i]), entityId, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static void PreserveFactoryMappingRows(
            HashSet<string> preserveRegistrationIds,
            HashSet<string> appendedRowIds,
            string productId,
            string factoryId)
        {
            if (appendedRowIds.Contains(productId))
            {
                preserveRegistrationIds.Add(productId);
            }

            if (!String.IsNullOrEmpty(factoryId) && appendedRowIds.Contains(factoryId))
            {
                preserveRegistrationIds.Add(factoryId);
            }
        }

        private bool EnsureUnityMainThread(string operation)
        {
            if (Thread.CurrentThread.ManagedThreadId == _unityMainThreadId)
            {
                return true;
            }

            bool shouldLog = false;
            lock (_gate)
            {
                if (!_offThreadWarningLogged)
                {
                    _offThreadWarningLogged = true;
                    shouldLog = true;
                }
            }

            if (shouldLog)
            {
                Log("Skipped " + operation + " because game registry lists may only be changed on " +
                    "the Unity thread that initialized the loader.");
            }

            return false;
        }

        private void ApplyProgression(object balancing)
        {
            List<ProgressionContentContribution> contributions = new List<ProgressionContentContribution>();
            try
            {
                IEnumerable<ProgressionContentContribution> source =
                    _provider.GetProgressionContributions(balancing);
                if (source != null)
                {
                    foreach (ProgressionContentContribution contribution in source)
                    {
                        if (contribution != null)
                        {
                            contributions.Add(contribution);
                        }
                    }
                }
            }
            catch (Exception exception)
            {
                Log("Could not enumerate progression contributions: " + exception.Message);
                return;
            }

            for (int i = 0; i < _progressionFields.Length; i++)
            {
                object progression;
                try
                {
                    progression = _progressionFields[i].GetValue(balancing);
                }
                catch (Exception exception)
                {
                    Log("Could not read progression field '" + _progressionFields[i].Name +
                        "': " + exception.Message);
                    continue;
                }

                if (progression == null)
                {
                    continue;
                }

                ApplyToProgression(progression, contributions, _progressionFields[i].Name);
            }
        }

        private void ApplyToProgression(
            object progression,
            IList<ProgressionContentContribution> contributions,
            string progressionFieldName)
        {
            if (!_progressionType.IsInstanceOfType(progression))
            {
                Log("Skipped an unexpected progression ScriptableObject.");
                return;
            }

            lock (_gate)
            {
                IList stages = _stagesField.GetValue(progression) as IList;
                IList defaultAis = _defaultAisField.GetValue(progression) as IList;
                if (stages == null || defaultAis == null)
                {
                    Log("Progression stages/defaultAis list is null; content was not added.");
                    return;
                }

                HashSet<string> appliedKeys;
                if (!_appliedContributionKeys.TryGetValue(progression, out appliedKeys))
                {
                    appliedKeys = new HashSet<string>(StringComparer.Ordinal);
                    _appliedContributionKeys.Add(progression, appliedKeys);
                }

                for (int i = 0; i < contributions.Count; i++)
                {
                    ProgressionContentContribution contribution = contributions[i];
                    if (String.IsNullOrWhiteSpace(contribution.Key) ||
                        appliedKeys.Contains(contribution.Key))
                    {
                        continue;
                    }

                    List<object> contributionStages;
                    List<object> contributionAi;
                    if (!TryMaterializeCompatible(contribution.Stages, _stageType,
                            "stage", contribution.Key, out contributionStages) ||
                        !TryMaterializeCompatible(contribution.AiBehaviours, _defaultAiType,
                            "DefaultAiBehaviour", contribution.Key, out contributionAi))
                    {
                        continue;
                    }

                    try
                    {
                        int firstStageSlot = stages.Count;
                        int firstAiSlot = defaultAis.Count;
                        int appendedStages = AppendByReference(stages, contributionStages);
                        int appendedAiProfiles = AppendByReference(defaultAis, contributionAi);
                        appliedKeys.Add(contribution.Key);

                        Log("Processed progression contribution '" + contribution.Key +
                            "' to '" + progressionFieldName + "': progression list slots stages [" +
                            firstStageSlot + ", " + stages.Count + "), DefaultAiBehaviour [" +
                            firstAiSlot + ", " + defaultAis.Count + "); appendedStages=" +
                            appendedStages + ", appendedAiProfiles=" + appendedAiProfiles + ".");
                    }
                    catch (Exception exception)
                    {
                        Log("Could not fully apply progression contribution '" + contribution.Key +
                            "': " + exception.Message);
                    }
                }
            }
        }

        private bool TryMaterializeCompatible(
            IEnumerable<object> source,
            Type expectedType,
            string contentKind,
            string contributionKey,
            out List<object> values)
        {
            values = new List<object>();
            if (source == null)
            {
                return true;
            }

            try
            {
                foreach (object value in source)
                {
                    if (value == null || !expectedType.IsInstanceOfType(value))
                    {
                        Log("Contribution '" + contributionKey + "' contains an incompatible " +
                            contentKind + " object; the contribution was skipped.");
                        return false;
                    }

                    values.Add(value);
                }
            }
            catch (Exception exception)
            {
                Log("Could not read " + contentKind + " objects from contribution '" +
                    contributionKey + "': " + exception.Message);
                return false;
            }

            return true;
        }

        private static int AppendByReference(IList target, IList<object> source)
        {
            int appendedCount = 0;
            for (int i = 0; i < source.Count; i++)
            {
                bool alreadyPresent = false;
                for (int j = 0; j < target.Count; j++)
                {
                    if (Object.ReferenceEquals(target[j], source[i]))
                    {
                        alreadyPresent = true;
                        break;
                    }
                }

                if (!alreadyPresent)
                {
                    target.Add(source[i]);
                    appendedCount++;
                }
            }

            return appendedCount;
        }

        private void Log(string message)
        {
            if (_log == null)
            {
                return;
            }

            try
            {
                _log("[ExpansionLoader] " + message);
            }
            catch
            {
                // A logging failure must not interrupt a game hook.
            }
        }

        private sealed class ReferenceIdentityComparer : IEqualityComparer<object>
        {
            public static readonly ReferenceIdentityComparer Instance = new ReferenceIdentityComparer();

            private ReferenceIdentityComparer()
            {
            }

            public new bool Equals(object left, object right)
            {
                return Object.ReferenceEquals(left, right);
            }

            public int GetHashCode(object value)
            {
                return RuntimeHelpers.GetHashCode(value);
            }
        }
    }
}
