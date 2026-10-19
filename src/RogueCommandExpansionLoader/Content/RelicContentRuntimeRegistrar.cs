using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using RogueCommandExpansionLoader.Runtime;
using UnityEngine;

namespace RogueCommandExpansionLoader.Content
{
    /// <summary>
    /// Adds pack-authored card changes through the game's existing relic balance store. The native
    /// SetScriptableObject method only accepts its first initialization, so later registrations
    /// append to the initialized native parameter list and its own ID index instead of replacing it.
    /// </summary>
    internal sealed class RelicContentRuntimeRegistrar
    {
        private const BindingFlags StaticFields = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        private const BindingFlags InstanceFields = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        private readonly MethodInfo _setScriptableObject;
        private readonly FieldInfo _nativeRelicAssetField;
        private readonly FieldInfo _nativeParametersField;
        private readonly FieldInfo _parameterIndexField;
        private readonly FieldInfo _imageCacheField;
        private readonly Type _relicParametersType;
        private readonly Type _relicScriptableObjectType;
        private readonly Type _cardChangeType;

        public RelicContentRuntimeRegistrar(Assembly gameAssembly)
        {
            if (gameAssembly == null)
            {
                throw new ArgumentNullException("gameAssembly");
            }

            Type storeType = RequireType(gameAssembly, "RelicBalancingStore");
            Type relicBalanceType = RequireType(gameAssembly, "RelicBalancingScriptableObject");
            _relicParametersType = RequireType(gameAssembly, "RelicBalancingParameters");
            _relicScriptableObjectType = RequireType(gameAssembly, "RelicScriptableObject");
            _cardChangeType = RequireType(gameAssembly, "CardChangeScriptableObject");

            _setScriptableObject = storeType.GetMethod("SetScriptableObject", StaticFields);
            ParameterInfo[] setterParameters = _setScriptableObject == null
                ? new ParameterInfo[0]
                : _setScriptableObject.GetParameters();
            if (_setScriptableObject == null || !_setScriptableObject.IsStatic ||
                _setScriptableObject.ReturnType != typeof(void) || setterParameters.Length != 1 ||
                setterParameters[0].ParameterType != relicBalanceType)
            {
                throw new InvalidOperationException(
                    "RelicBalancingStore.SetScriptableObject must remain a static void method taking exactly RelicBalancingScriptableObject.");
            }

            _nativeRelicAssetField = RequireField(storeType, "_relicBalancingScriptableObject",
                StaticFields, relicBalanceType);
            _parameterIndexField = RequireField(storeType, "ParameterListIndexOf", StaticFields,
                typeof(Dictionary<string, int>));
            _imageCacheField = RequireField(storeType, "ImageOf", StaticFields,
                typeof(Dictionary<string, Sprite>));
            _nativeParametersField = RequireField(relicBalanceType, "parameters", InstanceFields,
                typeof(List<>).MakeGenericType(_relicParametersType));

            RequireField(_relicParametersType, "relicId", InstanceFields, typeof(string));
            RequireField(_relicParametersType, "scriptableObject", InstanceFields, _relicScriptableObjectType);
            RequireField(_relicParametersType, "imageLocation", InstanceFields, typeof(string));
            RequireField(_relicParametersType, "coinsAmount", InstanceFields, typeof(int));
            RequireField(_relicParametersType, "tech", InstanceFields, null);
            RequireField(_relicParametersType, "rarity", InstanceFields, null);
            RequireField(_relicParametersType, "offeredSystemTags", InstanceFields, null);
            RequireField(_relicParametersType, "neededSystemTags", InstanceFields, null);
            RequireField(_relicParametersType, "neededExperienceLevel", InstanceFields, typeof(int));
            RequireField(_relicParametersType, "inactive", InstanceFields, typeof(bool));
            RequireField(_relicParametersType, "isAllowedForDemo", InstanceFields, typeof(bool));
            RequireField(_relicParametersType, "isForSpecialists", InstanceFields, typeof(bool));
            RequireField(_relicScriptableObjectType, "cardChanges", InstanceFields,
                typeof(List<>).MakeGenericType(_cardChangeType));
        }

        public void Register(
            ContentRelicDefinition[] definitions,
            Func<ContentRelicDefinition, Sprite> loadImage,
            List<ScriptableObject> retainedScriptableObjects)
        {
            if (definitions == null || definitions.Length == 0)
            {
                return;
            }

            if (loadImage == null || retainedScriptableObjects == null)
            {
                throw new ArgumentNullException(loadImage == null ? "loadImage" : "retainedScriptableObjects");
            }

            object nativeAsset = _nativeRelicAssetField.GetValue(null);
            if (nativeAsset == null)
            {
                throw new InvalidOperationException(
                    "The native RelicBalancingStore has not initialized its RelicBalancingScriptableObject yet.");
            }

            IList parameters = _nativeParametersField.GetValue(nativeAsset) as IList;
            IDictionary parameterIndices = _parameterIndexField.GetValue(null) as IDictionary;
            IDictionary imageCache = _imageCacheField.GetValue(null) as IDictionary;
            if (parameters == null || parameterIndices == null || imageCache == null)
            {
                throw new InvalidOperationException(
                    "The initialized relic parameter list, ID index, or image cache has an unexpected runtime shape.");
            }

            var pending = new List<PendingRelic>(definitions.Length);
            var seenIds = new HashSet<string>(StringComparer.Ordinal);
            try
            {
                for (int i = 0; i < definitions.Length; i++)
                {
                    ContentRelicDefinition definition = definitions[i];
                    if (definition == null || String.IsNullOrWhiteSpace(definition.id) ||
                        !seenIds.Add(definition.id))
                    {
                        throw new InvalidOperationException("Pack relic IDs must be present and unique before registration.");
                    }

                    if (parameterIndices.Contains(definition.id) || imageCache.Contains(definition.id))
                    {
                        throw new InvalidOperationException("Relic ID '" + definition.id +
                            "' is already registered by the game or another content pack.");
                    }

                    Sprite image = loadImage(definition);
                    if (image == null)
                    {
                        throw new InvalidOperationException("Relic image asset '" + definition.imageAssetName +
                            "' could not be loaded from its declared AssetBundle.");
                    }

                    pending.Add(BuildPendingRelic(definition, image));
                }
            }
            catch
            {
                for (int i = 0; i < pending.Count; i++)
                {
                    DestroyPending(pending[i]);
                }

                throw;
            }

            var addedIds = new List<string>(pending.Count);
            int originalParameterCount = parameters.Count;
            int originalRetainedObjectCount = retainedScriptableObjects.Count;
            try
            {
                for (int i = 0; i < pending.Count; i++)
                {
                    PendingRelic relic = pending[i];
                    int index = parameters.Count;
                    parameters.Add(relic.BalanceParameters);
                    parameterIndices.Add(relic.Id, index);
                    addedIds.Add(relic.Id);
                    imageCache.Add(relic.Id, relic.Image);
                }

                for (int i = 0; i < pending.Count; i++)
                {
                    retainedScriptableObjects.Add(pending[i].RelicObject);
                    retainedScriptableObjects.AddRange(pending[i].CardChanges);
                }
            }
            catch
            {
                for (int i = addedIds.Count - 1; i >= 0; i--)
                {
                    parameterIndices.Remove(addedIds[i]);
                    imageCache.Remove(addedIds[i]);
                }

                while (parameters.Count > originalParameterCount)
                {
                    parameters.RemoveAt(parameters.Count - 1);
                }

                if (retainedScriptableObjects.Count > originalRetainedObjectCount)
                {
                    retainedScriptableObjects.RemoveRange(
                        originalRetainedObjectCount,
                        retainedScriptableObjects.Count - originalRetainedObjectCount);
                }

                for (int i = 0; i < pending.Count; i++)
                {
                    DestroyPending(pending[i]);
                }

                throw;
            }
        }

        private PendingRelic BuildPendingRelic(ContentRelicDefinition definition, Sprite image)
        {
            var changes = new List<ScriptableObject>(definition.cardChanges.Length);
            var typedChanges = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(_cardChangeType));
            ScriptableObject relicObject = null;
            try
            {
                for (int i = 0; i < definition.cardChanges.Length; i++)
                {
                    ContentCardChangeDefinition source = definition.cardChanges[i];
                    ScriptableObject change = ScriptableObject.CreateInstance(_cardChangeType);
                    SetEnumField(change, "valueToChange", source.valueToChange);
                    SetEnumField(change, "operation", source.operation);
                    SetField(change, "value", source.value);
                    SetField(change, "multiplyValueWithCurrentHeatLevel", source.multiplyValueWithCurrentHeatLevel);
                    SetField(change, "multiplyValueWithMetaProgressionMultiplier",
                        source.multiplyValueWithMetaProgressionMultiplier);
                    SetEnumField(change, "side", source.side);
                    SetField(change, "onlyForTheseEntityIds",
                        new List<string>(source.onlyForTheseEntityIds ?? new string[0]));
                    SetField(change, "entityIdsNotAllowed", new List<string>());
                    typedChanges.Add(change);
                    changes.Add(change);
                }

                relicObject = ScriptableObject.CreateInstance(_relicScriptableObjectType);
                SetField(relicObject, "cardChanges", typedChanges);
                relicObject.name = definition.id;

                object balanceParameters = Activator.CreateInstance(_relicParametersType);
                SetStructField(balanceParameters, "relicId", definition.id);
                SetStructField(balanceParameters, "scriptableObject", relicObject);
                // RelicBalancingStore.Image() returns its cached bundle Sprite before consulting
                // Resources.Load(imageLocation), which cannot resolve assets from mod AssetBundles.
                SetStructField(balanceParameters, "imageLocation", "RCL/" + definition.id);
                SetStructField(balanceParameters, "coinsAmount", 0);
                SetEnumStructField(balanceParameters, "tech", definition.tech);
                SetEnumStructField(balanceParameters, "rarity", definition.rarity);
                SetEnumStructField(balanceParameters, "offeredSystemTags", definition.offeredSystemTags);
                SetEnumStructField(balanceParameters, "neededSystemTags", definition.neededSystemTags);
                SetStructField(balanceParameters, "neededExperienceLevel", definition.neededExperienceLevel);
                SetStructField(balanceParameters, "inactive", false);
                SetStructField(balanceParameters, "isAllowedForDemo", definition.isAllowedForDemo);
                SetStructField(balanceParameters, "isForSpecialists", definition.isForSpecialists);

                return new PendingRelic(definition.id, balanceParameters, relicObject, changes, image);
            }
            catch
            {
                if (relicObject != null)
                {
                    UnityEngine.Object.Destroy(relicObject);
                }

                for (int i = 0; i < changes.Count; i++)
                {
                    UnityEngine.Object.Destroy(changes[i]);
                }

                throw;
            }
        }

        private void DestroyPending(PendingRelic relic)
        {
            if (relic == null)
            {
                return;
            }

            if (relic.RelicObject != null)
            {
                UnityEngine.Object.Destroy(relic.RelicObject);
            }

            for (int i = 0; i < relic.CardChanges.Count; i++)
            {
                if (relic.CardChanges[i] != null)
                {
                    UnityEngine.Object.Destroy(relic.CardChanges[i]);
                }
            }
        }

        private static Type RequireType(Assembly gameAssembly, string typeName)
        {
            Type type = gameAssembly.GetType(typeName, false);
            if (type == null)
            {
                throw new InvalidOperationException("The game type " + typeName + " was not found.");
            }

            return type;
        }

        private static FieldInfo RequireField(Type declaringType, string name, BindingFlags flags, Type expectedType)
        {
            FieldInfo field = declaringType.GetField(name, flags);
            if (field == null || expectedType != null && field.FieldType != expectedType)
            {
                throw new InvalidOperationException("The game member " + declaringType.FullName + "." + name +
                    " is missing or has an unexpected type.");
            }

            return field;
        }

        private static void SetField(object target, string name, object value)
        {
            FieldInfo field = target.GetType().GetField(name, InstanceFields);
            if (field == null || value != null && !field.FieldType.IsInstanceOfType(value))
            {
                throw new InvalidOperationException("The game member " + target.GetType().FullName + "." + name +
                    " cannot accept the requested card change value.");
            }

            field.SetValue(target, value);
        }

        private static void SetEnumField(object target, string name, string value)
        {
            FieldInfo field = target.GetType().GetField(name, InstanceFields);
            if (field == null || !field.FieldType.IsEnum)
            {
                throw new InvalidOperationException("The game member " + target.GetType().FullName + "." + name +
                    " is not the expected enum field.");
            }

            string nativeValue = value;
            // The pack uses the player-facing Health attribute name, while Rogue Command's
            // CardChangeScriptableObject.ChangeableValue enum names the corresponding max-HP
            // entry MaxHealth. Resolve that one documented alias before parsing the native enum.
            if (String.Equals(name, "valueToChange", StringComparison.Ordinal) &&
                String.Equals(field.FieldType.Name, "ChangeableValue", StringComparison.Ordinal) &&
                String.Equals(value, "Health", StringComparison.Ordinal))
            {
                nativeValue = "MaxHealth";
            }

            field.SetValue(target, Enum.Parse(field.FieldType, nativeValue, false));
        }

        private void SetStructField(object target, string name, object value)
        {
            FieldInfo field = _relicParametersType.GetField(name, InstanceFields);
            if (field == null || value != null && !field.FieldType.IsInstanceOfType(value))
            {
                throw new InvalidOperationException("The game member " + _relicParametersType.FullName + "." + name +
                    " cannot accept the requested relic value.");
            }

            field.SetValue(target, value);
        }

        private void SetEnumStructField(object target, string name, string value)
        {
            FieldInfo field = _relicParametersType.GetField(name, InstanceFields);
            if (field == null || !field.FieldType.IsEnum)
            {
                throw new InvalidOperationException("The game member " + _relicParametersType.FullName + "." + name +
                    " is not the expected enum field.");
            }

            field.SetValue(target, Enum.Parse(field.FieldType, value, false));
        }

        private sealed class PendingRelic
        {
            public PendingRelic(
                string id,
                object balanceParameters,
                ScriptableObject relicObject,
                List<ScriptableObject> cardChanges,
                Sprite image)
            {
                Id = id;
                BalanceParameters = balanceParameters;
                RelicObject = relicObject;
                CardChanges = cardChanges;
                Image = image;
            }

            public string Id { get; private set; }
            public object BalanceParameters { get; private set; }
            public ScriptableObject RelicObject { get; private set; }
            public List<ScriptableObject> CardChanges { get; private set; }
            public Sprite Image { get; private set; }
        }
    }
}
