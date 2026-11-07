using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using RogueCommandExpansionLoader.Runtime;
using UnityEngine;

namespace RogueCommandExpansionLoader.Content
{
    /// <summary>
    /// Adds pack-authored card upgrades through the game's existing upgrade balance store.
    /// Injects into UpgradeBalancingStore, initialized native parameter list, parameter index,
    /// and image cache with transactional rollback on failure.
    /// </summary>
    internal sealed class UpgradeContentRuntimeRegistrar
    {
        private const BindingFlags StaticFields = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        private const BindingFlags InstanceFields = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        private readonly MethodInfo _setScriptableObject;
        private readonly FieldInfo _nativeUpgradeAssetField;
        private readonly FieldInfo _nativeParametersField;
        private readonly FieldInfo _parameterIndexField;
        private readonly FieldInfo _imageCacheField;
        private readonly Type _upgradeParametersType;
        private readonly Type _cardUpgradeScriptableObjectType;
        private readonly Type _cardChangeType;
        private readonly Type _typeFilterHandlingType;
        private readonly Type _attackTypeType;
        private readonly Type _skillTypeType;
        private readonly Type _unitRoleType;

        public UpgradeContentRuntimeRegistrar(Assembly gameAssembly)
        {
            if (gameAssembly == null)
            {
                throw new ArgumentNullException("gameAssembly");
            }

            Type storeType = RequireType(gameAssembly, "UpgradeBalancingStore");
            Type upgradeBalanceType = RequireType(gameAssembly, "UpgradeBalancingScriptableObject");
            _upgradeParametersType = RequireType(gameAssembly, "UpgradeBalancingParameters");
            _cardUpgradeScriptableObjectType = RequireType(gameAssembly, "CardUpgradeScriptableObject");
            _cardChangeType = RequireType(gameAssembly, "CardChangeScriptableObject");

            _setScriptableObject = storeType.GetMethod("SetScriptableObject", StaticFields);
            ParameterInfo[] setterParameters = _setScriptableObject == null
                ? new ParameterInfo[0]
                : _setScriptableObject.GetParameters();
            if (_setScriptableObject == null || !_setScriptableObject.IsStatic ||
                _setScriptableObject.ReturnType != typeof(void) || setterParameters.Length != 1 ||
                setterParameters[0].ParameterType != upgradeBalanceType)
            {
                throw new InvalidOperationException(
                    "UpgradeBalancingStore.SetScriptableObject must remain a static void method taking exactly UpgradeBalancingScriptableObject.");
            }

            _nativeUpgradeAssetField = RequireField(storeType, "_upgradeBalancingScriptableObject",
                StaticFields, upgradeBalanceType);
            _parameterIndexField = RequireField(storeType, "ParameterListIndexOf", StaticFields,
                typeof(Dictionary<string, int>));
            _imageCacheField = RequireField(storeType, "ImageOf", StaticFields,
                typeof(Dictionary<string, Sprite>));
            _nativeParametersField = RequireField(upgradeBalanceType, "parameters", InstanceFields,
                typeof(List<>).MakeGenericType(_upgradeParametersType));

            RequireField(_upgradeParametersType, "upgradeId", InstanceFields, typeof(string));
            RequireField(_upgradeParametersType, "scriptableObject", InstanceFields, _cardUpgradeScriptableObjectType);
            RequireField(_upgradeParametersType, "imageLocation", InstanceFields, typeof(string));
            RequireField(_upgradeParametersType, "coinsAmount", InstanceFields, typeof(int));
            RequireField(_upgradeParametersType, "tech", InstanceFields, null);
            RequireField(_upgradeParametersType, "rarity", InstanceFields, null);
            RequireField(_upgradeParametersType, "offeredSystemTags", InstanceFields, null);
            RequireField(_upgradeParametersType, "neededSystemTags", InstanceFields, null);
            RequireField(_upgradeParametersType, "neededExperienceLevel", InstanceFields, typeof(int));
            RequireField(_upgradeParametersType, "inactive", InstanceFields, typeof(bool));
            RequireField(_upgradeParametersType, "isAllowedForDemo", InstanceFields, typeof(bool));
            RequireField(_upgradeParametersType, "isForSpecialists", InstanceFields, typeof(bool));

            RequireField(_cardUpgradeScriptableObjectType, "cardChanges", InstanceFields,
                typeof(List<>).MakeGenericType(_cardChangeType));
            RequireField(_cardUpgradeScriptableObjectType, "applyToFactoryWhereApplicable", InstanceFields, typeof(bool));

            FieldInfo filterHandlingField = _cardUpgradeScriptableObjectType.GetField("attackTypeFilterHandling", InstanceFields);
            _typeFilterHandlingType = filterHandlingField != null ? filterHandlingField.FieldType : null;

            FieldInfo attackTypeField = _cardUpgradeScriptableObjectType.GetField("attackTypeFilter", InstanceFields);
            _attackTypeType = attackTypeField != null ? attackTypeField.FieldType : null;

            FieldInfo skillTypeField = _cardUpgradeScriptableObjectType.GetField("skillTypeFilter", InstanceFields);
            _skillTypeType = skillTypeField != null ? skillTypeField.FieldType : null;

            FieldInfo roleField = _cardUpgradeScriptableObjectType.GetField("entityMustHaveOneOfTheseRoles", InstanceFields);
            _unitRoleType = roleField != null ? roleField.FieldType : null;
        }

        public void Register(
            ContentUpgradeDefinition[] definitions,
            Func<ContentUpgradeDefinition, Sprite> loadImage,
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

            object nativeAsset = _nativeUpgradeAssetField.GetValue(null);
            if (nativeAsset == null)
            {
                throw new InvalidOperationException(
                    "The native UpgradeBalancingStore has not initialized its UpgradeBalancingScriptableObject yet.");
            }

            IList parameters = _nativeParametersField.GetValue(nativeAsset) as IList;
            IDictionary parameterIndices = _parameterIndexField.GetValue(null) as IDictionary;
            IDictionary imageCache = _imageCacheField.GetValue(null) as IDictionary;
            if (parameters == null || parameterIndices == null || imageCache == null)
            {
                throw new InvalidOperationException(
                    "The initialized upgrade parameter list, ID index, or image cache has an unexpected runtime shape.");
            }

            var pending = new List<PendingUpgrade>(definitions.Length);
            var seenIds = new HashSet<string>(StringComparer.Ordinal);
            try
            {
                for (int i = 0; i < definitions.Length; i++)
                {
                    ContentUpgradeDefinition definition = definitions[i];
                    if (definition == null || String.IsNullOrWhiteSpace(definition.id) ||
                        !seenIds.Add(definition.id))
                    {
                        throw new InvalidOperationException("Pack upgrade IDs must be present and unique before registration.");
                    }

                    if (parameterIndices.Contains(definition.id) || imageCache.Contains(definition.id))
                    {
                        throw new InvalidOperationException("Upgrade ID '" + definition.id +
                            "' is already registered by the game or another content pack.");
                    }

                    Sprite image = loadImage(definition);
                    if (image == null)
                    {
                        throw new InvalidOperationException("Upgrade image asset '" + definition.imageAssetName +
                            "' could not be loaded from its declared AssetBundle.");
                    }

                    pending.Add(BuildPendingUpgrade(definition, image));
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
                    PendingUpgrade upgrade = pending[i];
                    int index = parameters.Count;
                    parameters.Add(upgrade.BalanceParameters);
                    parameterIndices.Add(upgrade.Id, index);
                    addedIds.Add(upgrade.Id);
                    imageCache.Add(upgrade.Id, upgrade.Image);
                }

                for (int i = 0; i < pending.Count; i++)
                {
                    retainedScriptableObjects.Add(pending[i].UpgradeObject);
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

        private PendingUpgrade BuildPendingUpgrade(ContentUpgradeDefinition definition, Sprite image)
        {
            var changes = new List<ScriptableObject>((definition.cardChanges ?? new ContentCardChangeDefinition[0]).Length);
            var typedChanges = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(_cardChangeType));
            ScriptableObject upgradeObject = null;
            try
            {
                ContentCardChangeDefinition[] declaredChanges = definition.cardChanges ?? new ContentCardChangeDefinition[0];
                for (int i = 0; i < declaredChanges.Length; i++)
                {
                    ContentCardChangeDefinition source = declaredChanges[i];
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
                    SetField(change, "entityIdsNotAllowed",
                        new List<string>(source.entityIdsNotAllowed ?? new string[0]));
                    SetField(change, "changeableValueHasToBeGreaterZero", source.changeableValueHasToBeGreaterZero);

                    FieldInfo allRolesField = _cardChangeType.GetField("cardMustHaveAllOfTheseRoles", InstanceFields);
                    if (allRolesField != null && allRolesField.FieldType.IsEnum)
                    {
                        allRolesField.SetValue(change, ParseRoleBitmask(allRolesField.FieldType, source.cardMustHaveAllOfTheseRoles, 0));
                    }

                    FieldInfo oneRolesField = _cardChangeType.GetField("cardMustHaveOneOfTheseRoles", InstanceFields);
                    if (oneRolesField != null && oneRolesField.FieldType.IsEnum)
                    {
                        long allMask = -1;
                        try { allMask = Convert.ToInt64(Enum.Parse(oneRolesField.FieldType, "All", true)); } catch { }
                        oneRolesField.SetValue(change, ParseRoleBitmask(oneRolesField.FieldType, source.cardMustHaveOneOfTheseRoles, allMask));
                    }

                    FieldInfo notRolesField = _cardChangeType.GetField("cardMustNotHaveOneOfTheseRoles", InstanceFields);
                    if (notRolesField != null && notRolesField.FieldType.IsEnum)
                    {
                        notRolesField.SetValue(change, ParseRoleBitmask(notRolesField.FieldType, source.cardMustNotHaveOneOfTheseRoles, 0));
                    }

                    FieldInfo notTechsField = _cardChangeType.GetField("cardMustNotHaveOneOfTheseTechs", InstanceFields);
                    if (notTechsField != null && notTechsField.FieldType.IsEnum)
                    {
                        notTechsField.SetValue(change, ParseTechBitmask(notTechsField.FieldType, source.cardMustNotHaveOneOfTheseTechs, "Ancient"));
                    }

                    typedChanges.Add(change);
                    changes.Add(change);
                }

                upgradeObject = ScriptableObject.CreateInstance(_cardUpgradeScriptableObjectType);
                SetField(upgradeObject, "cardChanges", typedChanges);
                SetField(upgradeObject, "applyToFactoryWhereApplicable", definition.applyToFactoryWhereApplicable);

                if (_unitRoleType != null)
                {
                    long allMask = -1;
                    try { allMask = Convert.ToInt64(Enum.Parse(_unitRoleType, "All", true)); } catch { }
                    SetField(upgradeObject, "entityMustHaveOneOfTheseRoles",
                        ParseRoleBitmask(_unitRoleType, definition.entityMustHaveOneOfTheseRoles, allMask));
                    SetField(upgradeObject, "entityMustNotHaveOneOfTheseRoles",
                        ParseRoleBitmask(_unitRoleType, definition.entityMustNotHaveOneOfTheseRoles, 0));
                }

                if (_typeFilterHandlingType != null)
                {
                    string attackHandling = String.IsNullOrWhiteSpace(definition.attackTypeFilterHandling)
                        ? "AllAllowed"
                        : definition.attackTypeFilterHandling;
                    SetEnumField(upgradeObject, "attackTypeFilterHandling", attackHandling);

                    string skillHandling = String.IsNullOrWhiteSpace(definition.skillTypeFilterHandling)
                        ? "AllAllowed"
                        : definition.skillTypeFilterHandling;
                    SetEnumField(upgradeObject, "skillTypeFilterHandling", skillHandling);
                }

                if (_attackTypeType != null && !String.IsNullOrWhiteSpace(definition.attackTypeFilter))
                {
                    SetEnumField(upgradeObject, "attackTypeFilter", definition.attackTypeFilter);
                }

                if (_skillTypeType != null && !String.IsNullOrWhiteSpace(definition.skillTypeFilter))
                {
                    SetEnumField(upgradeObject, "skillTypeFilter", definition.skillTypeFilter);
                }

                if (definition.mustNotHaveOneOfTheseEntityIds != null)
                {
                    SetField(upgradeObject, "mustNotHaveOneOfTheseEntityIds",
                        new List<string>(definition.mustNotHaveOneOfTheseEntityIds));
                }

                upgradeObject.name = definition.id;

                object balanceParameters = Activator.CreateInstance(_upgradeParametersType);
                SetStructField(balanceParameters, "upgradeId", definition.id);
                SetStructField(balanceParameters, "scriptableObject", upgradeObject);
                SetStructField(balanceParameters, "imageLocation", "RCL/" + definition.id);
                SetStructField(balanceParameters, "coinsAmount", definition.coinsAmount);
                SetEnumStructField(balanceParameters, "tech", definition.tech);
                SetEnumStructField(balanceParameters, "rarity", definition.rarity);
                SetEnumStructField(balanceParameters, "offeredSystemTags", definition.offeredSystemTags);
                SetEnumStructField(balanceParameters, "neededSystemTags", definition.neededSystemTags);
                SetStructField(balanceParameters, "neededExperienceLevel", definition.neededExperienceLevel);
                SetStructField(balanceParameters, "inactive", definition.inactive);
                SetStructField(balanceParameters, "isAllowedForDemo", definition.isAllowedForDemo);
                SetStructField(balanceParameters, "isForSpecialists", definition.isForSpecialists);

                return new PendingUpgrade(definition.id, balanceParameters, upgradeObject, changes, image);
            }
            catch
            {
                if (upgradeObject != null)
                {
                    UnityEngine.Object.Destroy(upgradeObject);
                }

                for (int i = 0; i < changes.Count; i++)
                {
                    UnityEngine.Object.Destroy(changes[i]);
                }

                throw;
            }
        }

        private void DestroyPending(PendingUpgrade upgrade)
        {
            if (upgrade == null)
            {
                return;
            }

            if (upgrade.UpgradeObject != null)
            {
                UnityEngine.Object.Destroy(upgrade.UpgradeObject);
            }

            for (int i = 0; i < upgrade.CardChanges.Count; i++)
            {
                if (upgrade.CardChanges[i] != null)
                {
                    UnityEngine.Object.Destroy(upgrade.CardChanges[i]);
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
            if (field == null || (expectedType != null && field.FieldType != expectedType))
            {
                throw new InvalidOperationException("The game member " + declaringType.FullName + "." + name +
                    " is missing or has an unexpected type.");
            }

            return field;
        }

        private static void SetField(object target, string name, object value)
        {
            FieldInfo field = target.GetType().GetField(name, InstanceFields);
            if (field == null || (value != null && !field.FieldType.IsInstanceOfType(value)))
            {
                throw new InvalidOperationException("The game member " + target.GetType().FullName + "." + name +
                    " cannot accept the requested upgrade value.");
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

            field.SetValue(target, Enum.Parse(field.FieldType, value, false));
        }

        private static object ParseRoleBitmask(Type roleEnumType, string[] roles, long defaultMask)
        {
            if (roles == null || roles.Length == 0)
            {
                return Enum.ToObject(roleEnumType, defaultMask);
            }

            long mask = 0;
            for (int i = 0; i < roles.Length; i++)
            {
                string role = roles[i];
                if (String.IsNullOrWhiteSpace(role))
                {
                    continue;
                }

                mask |= Convert.ToInt64(Enum.Parse(roleEnumType, role, false));
            }

            return Enum.ToObject(roleEnumType, mask);
        }

        private static object ParseTechBitmask(Type techEnumType, string[] techs, string defaultTech)
        {
            if (techs == null || techs.Length == 0)
            {
                return String.IsNullOrWhiteSpace(defaultTech)
                    ? Enum.ToObject(techEnumType, 0)
                    : Enum.Parse(techEnumType, defaultTech, false);
            }

            long mask = 0;
            for (int i = 0; i < techs.Length; i++)
            {
                string tech = techs[i];
                if (String.IsNullOrWhiteSpace(tech))
                {
                    continue;
                }

                mask |= Convert.ToInt64(Enum.Parse(techEnumType, tech, false));
            }

            return Enum.ToObject(techEnumType, mask);
        }

        private void SetStructField(object target, string name, object value)
        {
            FieldInfo field = _upgradeParametersType.GetField(name, InstanceFields);
            if (field == null || (value != null && !field.FieldType.IsInstanceOfType(value)))
            {
                throw new InvalidOperationException("The game member " + _upgradeParametersType.FullName + "." + name +
                    " cannot accept the requested upgrade value.");
            }

            field.SetValue(target, value);
        }

        private void SetEnumStructField(object target, string name, string value)
        {
            FieldInfo field = _upgradeParametersType.GetField(name, InstanceFields);
            if (field == null || !field.FieldType.IsEnum)
            {
                throw new InvalidOperationException("The game member " + _upgradeParametersType.FullName + "." + name +
                    " is not the expected enum field.");
            }

            field.SetValue(target, Enum.Parse(field.FieldType, value, false));
        }

        private sealed class PendingUpgrade
        {
            public PendingUpgrade(
                string id,
                object balanceParameters,
                ScriptableObject upgradeObject,
                List<ScriptableObject> cardChanges,
                Sprite image)
            {
                Id = id;
                BalanceParameters = balanceParameters;
                UpgradeObject = upgradeObject;
                CardChanges = cardChanges;
                Image = image;
            }

            public string Id { get; }
            public object BalanceParameters { get; }
            public ScriptableObject UpgradeObject { get; }
            public List<ScriptableObject> CardChanges { get; }
            public Sprite Image { get; }
        }
    }
}
