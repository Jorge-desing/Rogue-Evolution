using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace RogueCommandExpansionLoader.Runtime
{
    /// <summary>
    /// Pure diagnostic DTO returned when granting credits to the game bank.
    /// Decoupled from Unity types.
    /// </summary>
    public readonly struct CreditGrantResult
    {
        public CreditGrantResult(bool success, string accountName, int amountGranted, int currentBalance, string message)
        {
            Success = success;
            AccountName = accountName ?? string.Empty;
            AmountGranted = amountGranted;
            CurrentBalance = currentBalance;
            Message = message ?? string.Empty;
        }

        public bool Success { get; }
        public string AccountName { get; }
        public int AmountGranted { get; }
        public int CurrentBalance { get; }
        public string Message { get; }
    }

    /// <summary>
    /// Pure diagnostic DTO returned when injecting a dynamic card change into an in-game entity.
    /// Decoupled from Unity types.
    /// </summary>
    public readonly struct CardChangeApplyResult
    {
        public CardChangeApplyResult(bool success, string targetEntityKeyId, string valueToChange, string operation, float value, string message)
        {
            Success = success;
            TargetEntityKeyId = targetEntityKeyId ?? string.Empty;
            ValueToChange = valueToChange ?? string.Empty;
            Operation = operation ?? string.Empty;
            Value = value;
            Message = message ?? string.Empty;
        }

        public bool Success { get; }
        public string TargetEntityKeyId { get; }
        public string ValueToChange { get; }
        public string Operation { get; }
        public float Value { get; }
        public string Message { get; }
    }

    /// <summary>
    /// Pure diagnostic DTO returned when dispatching a runtime tactical command to a unit.
    /// Decoupled from Unity types.
    /// </summary>
    public readonly struct UnitCommandResult
    {
        public UnitCommandResult(bool success, string entityKeyId, string commandType, float targetX, float targetZ, string message)
        {
            Success = success;
            EntityKeyId = entityKeyId ?? string.Empty;
            CommandType = commandType ?? string.Empty;
            TargetX = targetX;
            TargetZ = targetZ;
            Message = message ?? string.Empty;
        }

        public bool Success { get; }
        public string EntityKeyId { get; }
        public string CommandType { get; }
        public float TargetX { get; }
        public float TargetZ { get; }
        public string Message { get; }
    }

    /// <summary>
    /// Pure diagnostic DTO returned when triggering a unit active skill.
    /// Decoupled from Unity types.
    /// </summary>
    public readonly struct ActiveSkillOrderResult
    {
        public ActiveSkillOrderResult(bool success, string entityKeyId, string skillTargetMode, float targetX, float targetZ, string message)
        {
            Success = success;
            EntityKeyId = entityKeyId ?? string.Empty;
            SkillTargetMode = skillTargetMode ?? string.Empty;
            TargetX = targetX;
            TargetZ = targetZ;
            Message = message ?? string.Empty;
        }

        public bool Success { get; }
        public string EntityKeyId { get; }
        public string SkillTargetMode { get; }
        public float TargetX { get; }
        public float TargetZ { get; }
        public string Message { get; }
    }

    /// <summary>
    /// Pure diagnostic DTO returned when applying health, armor, shield, or damage adjustments to an entity.
    /// Decoupled from Unity types.
    /// </summary>
    public readonly struct VitalityAdjustmentResult
    {
        public VitalityAdjustmentResult(bool success, string entityKeyId, string adjustmentType, float amountApplied, float newHealth, float newArmor, float newShield, string message)
        {
            Success = success;
            EntityKeyId = entityKeyId ?? string.Empty;
            AdjustmentType = adjustmentType ?? string.Empty;
            AmountApplied = amountApplied;
            NewHealth = newHealth;
            NewArmor = newArmor;
            NewShield = newShield;
            Message = message ?? string.Empty;
        }

        public bool Success { get; }
        public string EntityKeyId { get; }
        public string AdjustmentType { get; }
        public float AmountApplied { get; }
        public float NewHealth { get; }
        public float NewArmor { get; }
        public float NewShield { get; }
        public string Message { get; }
    }

    /// <summary>
    /// Pure diagnostic DTO returned when applying or clearing status effects on an entity.
    /// Decoupled from Unity types.
    /// </summary>
    public readonly struct StatusEffectOrderResult
    {
        public StatusEffectOrderResult(bool success, string entityKeyId, string statusEffectName, float durationSeconds, bool isApplied, string message)
        {
            Success = success;
            EntityKeyId = entityKeyId ?? string.Empty;
            StatusEffectName = statusEffectName ?? string.Empty;
            DurationSeconds = durationSeconds;
            IsApplied = isApplied;
            Message = message ?? string.Empty;
        }

        public bool Success { get; }
        public string EntityKeyId { get; }
        public string StatusEffectName { get; }
        public float DurationSeconds { get; }
        public bool IsApplied { get; }
        public string Message { get; }
    }

    /// <summary>
    /// Safe in-game gameplay and diagnostic command director allowing authorized automated agents
    /// and clean-room testing harnesses to inspect and command match systems without GUI interaction.
    /// </summary>
    public static class RuntimeGameplayCommandDirector
    {
        private const int MaxCreditGrant = 500000;

        /// <summary>
        /// Grants economy credits to a bank account (default: "Player").
        /// </summary>
        public static CreditGrantResult TryGrantCredits(int amount, string accountName = "Player")
        {
            string account = string.IsNullOrWhiteSpace(accountName) ? "Player" : accountName.Trim();

            if (amount <= 0)
            {
                return new CreditGrantResult(false, account, 0, 0, "Credit grant amount must be positive.");
            }

            if (amount > MaxCreditGrant)
            {
                return new CreditGrantResult(false, account, 0, 0,
                    "Credit grant amount exceeds maximum allowed cap of " + MaxCreditGrant + ".");
            }

            try
            {
                Bank.Deposit(account, amount);
                int currentBalance = 0;
                try
                {
                    currentBalance = Bank.ActualBalance(account);
                }
                catch
                {
                    // Balance check failed but deposit succeeded
                }

                return new CreditGrantResult(true, account, amount, currentBalance,
                    "Deposited " + amount + " credits into " + account + " account.");
            }
            catch (Exception ex)
            {
                return new CreditGrantResult(false, account, 0, 0,
                    "Failed to deposit credits: " + ex.Message);
            }
        }

        /// <summary>
        /// Applies an in-game data-driven card change to an entity via the native
        /// <c>EntityBalancingStore.AddInGameCardChange</c> pipeline.
        /// </summary>
        public static CardChangeApplyResult TryApplyCardChange(CardChangeRequest request, string targetEntityKeyId = null)
        {
            if (string.IsNullOrWhiteSpace(request.ValueToChange))
            {
                return new CardChangeApplyResult(false, targetEntityKeyId, string.Empty, request.Operation, request.Value,
                    "Card change request must specify a valid ValueToChange.");
            }

            try
            {
                ExistingControllers existing = ExistingControllers.Instance;
                if (existing == null)
                {
                    return new CardChangeApplyResult(false, targetEntityKeyId, request.ValueToChange, request.Operation, request.Value,
                        "ExistingControllers instance is not available.");
                }

                EntityController targetEntity = null;
                if (!string.IsNullOrWhiteSpace(targetEntityKeyId))
                {
                    targetEntity = FindEntityByKey(existing, targetEntityKeyId);
                    if (targetEntity == null)
                    {
                        return new CardChangeApplyResult(false, targetEntityKeyId, request.ValueToChange, request.Operation, request.Value,
                            "Target entity '" + targetEntityKeyId + "' not found or destroyed.");
                    }
                }
                else
                {
                    // If no target specified, default to first living player unit or engineering HQ
                    targetEntity = FindDefaultPlayerTarget(existing);
                    if (targetEntity == null)
                    {
                        return new CardChangeApplyResult(false, string.Empty, request.ValueToChange, request.Operation, request.Value,
                            "No living player entity found to receive card change.");
                    }
                }

                Assembly gameAssembly = targetEntity.GetType().Assembly ?? typeof(EntityController).Assembly;
                bool applied = RuntimeCardChangeApplier.ApplyCardChange(gameAssembly, targetEntity, request);

                string entityKey = targetEntity.EntityId ?? targetEntity.name ?? "entity";
                if (applied)
                {
                    return new CardChangeApplyResult(true, entityKey, request.ValueToChange, request.Operation, request.Value,
                        "Card change applied successfully via native EntityBalancingStore pipeline.");
                }
                else
                {
                    return new CardChangeApplyResult(false, entityKey, request.ValueToChange, request.Operation, request.Value,
                        "Native EntityBalancingStore pipeline rejected or failed to apply card change.");
                }
            }
            catch (Exception ex)
            {
                return new CardChangeApplyResult(false, targetEntityKeyId, request.ValueToChange, request.Operation, request.Value,
                    "Error applying card change: " + ex.Message);
            }
        }

        /// <summary>
        /// Dispatches a movement command to a living unit by entity key ID.
        /// </summary>
        public static UnitCommandResult TryOrderUnitMove(string entityKeyId, float destinationX, float destinationZ)
        {
            if (string.IsNullOrWhiteSpace(entityKeyId))
            {
                return new UnitCommandResult(false, string.Empty, "Move", destinationX, destinationZ,
                    "Entity key ID cannot be empty.");
            }

            try
            {
                ExistingControllers existing = ExistingControllers.Instance;
                if (existing == null)
                {
                    return new UnitCommandResult(false, entityKeyId, "Move", destinationX, destinationZ,
                        "ExistingControllers instance is not available.");
                }

                EntityController unit = FindEntityByKey(existing, entityKeyId);
                if (unit == null)
                {
                    return new UnitCommandResult(false, entityKeyId, "Move", destinationX, destinationZ,
                        "Unit '" + entityKeyId + "' not found or destroyed.");
                }

                Vector3 targetPos = new Vector3(destinationX, 0f, destinationZ);
                EntityCommand moveCmd = EntityCommand.Move(targetPos);
                moveCmd.tag = "rcl-command-director";

                unit.ExecuteCommand(moveCmd, EntityController.CommandProcessingType.ExecuteBeforeCurrentCommand);

                return new UnitCommandResult(true, entityKeyId, "Move", destinationX, destinationZ,
                    "Move command dispatched toward (" + destinationX + ", " + destinationZ + ").");
            }
            catch (Exception ex)
            {
                return new UnitCommandResult(false, entityKeyId, "Move", destinationX, destinationZ,
                    "Error executing unit move: " + ex.Message);
            }
        }

        /// <summary>
        /// Dispatches an attack command on a target entity.
        /// </summary>
        public static UnitCommandResult TryOrderUnitAttack(string entityKeyId, string targetEntityKeyId)
        {
            if (string.IsNullOrWhiteSpace(entityKeyId))
            {
                return new UnitCommandResult(false, string.Empty, "AttackMoveOnTarget", 0f, 0f,
                    "Attacker entity key ID cannot be empty.");
            }

            if (string.IsNullOrWhiteSpace(targetEntityKeyId))
            {
                return new UnitCommandResult(false, entityKeyId, "AttackMoveOnTarget", 0f, 0f,
                    "Target entity key ID cannot be empty.");
            }

            try
            {
                ExistingControllers existing = ExistingControllers.Instance;
                if (existing == null)
                {
                    return new UnitCommandResult(false, entityKeyId, "AttackMoveOnTarget", 0f, 0f,
                        "ExistingControllers instance is not available.");
                }

                EntityController attacker = FindEntityByKey(existing, entityKeyId);
                if (attacker == null)
                {
                    return new UnitCommandResult(false, entityKeyId, "AttackMoveOnTarget", 0f, 0f,
                        "Attacker unit '" + entityKeyId + "' not found or destroyed.");
                }

                EntityController target = FindEntityByKey(existing, targetEntityKeyId);
                if (target == null)
                {
                    return new UnitCommandResult(false, entityKeyId, "AttackMoveOnTarget", 0f, 0f,
                        "Target entity '" + targetEntityKeyId + "' not found or destroyed.");
                }

                EntityCommand attackCmd = EntityCommand.AttackMoveOnTarget(
                    target,
                    EntityCommand.Role.ThisCommandWantsPathToBeCleared);
                attackCmd.tag = "rcl-command-director";

                attacker.ExecuteCommand(attackCmd, EntityController.CommandProcessingType.ExecuteBeforeCurrentCommand);

                float tx = target.Position.x;
                float tz = target.Position.z;

                return new UnitCommandResult(true, entityKeyId, "AttackMoveOnTarget", tx, tz,
                    "Attack command dispatched against target '" + targetEntityKeyId + "'.");
            }
            catch (Exception ex)
            {
                return new UnitCommandResult(false, entityKeyId, "AttackMoveOnTarget", 0f, 0f,
                    "Error executing unit attack: " + ex.Message);
            }
        }

        /// <summary>
        /// Dispatches an active tactical skill command on a unit (Self, on Target, or at Position).
        /// </summary>
        public static ActiveSkillOrderResult TryOrderActiveSkill(
            string entityKeyId,
            string targetEntityKeyId = null,
            float? targetX = null,
            float? targetZ = null)
        {
            if (string.IsNullOrWhiteSpace(entityKeyId))
            {
                return new ActiveSkillOrderResult(false, string.Empty, "None", 0f, 0f,
                    "Entity key ID cannot be empty.");
            }

            try
            {
                ExistingControllers existing = ExistingControllers.Instance;
                if (existing == null)
                {
                    return new ActiveSkillOrderResult(false, entityKeyId, "None", 0f, 0f,
                        "ExistingControllers instance is not available.");
                }

                EntityController caster = FindEntityByKey(existing, entityKeyId);
                if (caster == null)
                {
                    return new ActiveSkillOrderResult(false, entityKeyId, "None", 0f, 0f,
                        "Caster unit '" + entityKeyId + "' not found or destroyed.");
                }

                EntityCommand skillCmd;
                string mode;
                float tx = 0f;
                float tz = 0f;

                if (!string.IsNullOrWhiteSpace(targetEntityKeyId))
                {
                    EntityController target = FindEntityByKey(existing, targetEntityKeyId);
                    if (target == null)
                    {
                        return new ActiveSkillOrderResult(false, entityKeyId, "Target", 0f, 0f,
                            "Skill target entity '" + targetEntityKeyId + "' not found or destroyed.");
                    }

                    skillCmd = EntityCommand.ActiveSkillOnTarget(target);
                    mode = "Target";
                    tx = target.Position.x;
                    tz = target.Position.z;
                }
                else if (targetX.HasValue && targetZ.HasValue)
                {
                    Vector3 pos = new Vector3(targetX.Value, 0f, targetZ.Value);
                    skillCmd = EntityCommand.ActiveSkillAtPosition(pos);
                    mode = "Position";
                    tx = targetX.Value;
                    tz = targetZ.Value;
                }
                else
                {
                    skillCmd = EntityCommand.ActiveSkillWithoutTarget();
                    mode = "Self";
                    tx = caster.Position.x;
                    tz = caster.Position.z;
                }

                skillCmd.tag = "rcl-command-director";
                caster.ExecuteCommand(skillCmd, EntityController.CommandProcessingType.ExecuteBeforeCurrentCommand);

                return new ActiveSkillOrderResult(true, entityKeyId, mode, tx, tz,
                    "Active skill command (" + mode + ") dispatched successfully.");
            }
            catch (Exception ex)
            {
                return new ActiveSkillOrderResult(false, entityKeyId, "Error", 0f, 0f,
                    "Error executing active skill: " + ex.Message);
            }
        }

        /// <summary>
        /// Safely adjusts health, armor, shield, or damage on an entity for testing and calibration.
        /// </summary>
        public static VitalityAdjustmentResult TryAdjustEntityVitality(
            string entityKeyId,
            string adjustmentType,
            float amount,
            bool ignoreArmor = false)
        {
            if (string.IsNullOrWhiteSpace(entityKeyId))
            {
                return new VitalityAdjustmentResult(false, string.Empty, adjustmentType, 0f, 0f, 0f, 0f,
                    "Entity key ID cannot be empty.");
            }

            if (amount <= 0f || float.IsNaN(amount) || float.IsInfinity(amount))
            {
                return new VitalityAdjustmentResult(false, entityKeyId, adjustmentType, 0f, 0f, 0f, 0f,
                    "Adjustment amount must be positive and finite.");
            }

            try
            {
                ExistingControllers existing = ExistingControllers.Instance;
                if (existing == null)
                {
                    return new VitalityAdjustmentResult(false, entityKeyId, adjustmentType, 0f, 0f, 0f, 0f,
                        "ExistingControllers instance is not available.");
                }

                EntityController entity = FindEntityByKey(existing, entityKeyId);
                if (entity == null)
                {
                    return new VitalityAdjustmentResult(false, entityKeyId, adjustmentType, 0f, 0f, 0f, 0f,
                        "Entity '" + entityKeyId + "' not found or destroyed.");
                }

                string type = (adjustmentType ?? string.Empty).Trim();

                if (string.Equals(type, "Heal", StringComparison.OrdinalIgnoreCase))
                {
                    entity.Heal(amount, null, false, false);
                }
                else if (string.Equals(type, "RepairArmor", StringComparison.OrdinalIgnoreCase))
                {
                    entity.RepairArmor(amount, null, false);
                }
                else if (string.Equals(type, "ChargeShield", StringComparison.OrdinalIgnoreCase))
                {
                    entity.ChargeShield(amount, true);
                }
                else if (string.Equals(type, "Damage", StringComparison.OrdinalIgnoreCase))
                {
                    entity.TakeDamage(amount, null, false, ignoreArmor);
                }
                else
                {
                    return new VitalityAdjustmentResult(false, entityKeyId, type, 0f, entity.CurrentHealth, entity.CurrentArmor, entity.CurrentShield,
                        "Unsupported adjustment type '" + type + "'. Supported: Heal, RepairArmor, ChargeShield, Damage.");
                }

                return new VitalityAdjustmentResult(true, entityKeyId, type, amount,
                    entity.CurrentHealth, entity.CurrentArmor, entity.CurrentShield,
                    "Vitality adjustment (" + type + ") of " + amount + " applied successfully.");
            }
            catch (Exception ex)
            {
                return new VitalityAdjustmentResult(false, entityKeyId, adjustmentType, 0f, 0f, 0f, 0f,
                    "Error adjusting entity vitality: " + ex.Message);
            }
        }

        /// <summary>
        /// Applies a status effect (e.g. Emp, Burn, SlowedDown, Stun) to an entity.
        /// </summary>
        public static StatusEffectOrderResult TryApplyStatusEffect(
            string entityKeyId,
            string statusEffectName,
            float durationSeconds = 10f)
        {
            if (string.IsNullOrWhiteSpace(entityKeyId))
            {
                return new StatusEffectOrderResult(false, string.Empty, statusEffectName, 0f, false,
                    "Entity key ID cannot be empty.");
            }

            if (string.IsNullOrWhiteSpace(statusEffectName))
            {
                return new StatusEffectOrderResult(false, entityKeyId, string.Empty, 0f, false,
                    "Status effect name cannot be empty.");
            }

            try
            {
                ExistingControllers existing = ExistingControllers.Instance;
                if (existing == null)
                {
                    return new StatusEffectOrderResult(false, entityKeyId, statusEffectName, 0f, false,
                        "ExistingControllers instance is not available.");
                }

                EntityController entity = FindEntityByKey(existing, entityKeyId);
                if (entity == null)
                {
                    return new StatusEffectOrderResult(false, entityKeyId, statusEffectName, 0f, false,
                        "Entity '" + entityKeyId + "' not found or destroyed.");
                }

                if (!Enum.TryParse<StatusEffect>(statusEffectName, true, out StatusEffect effect))
                {
                    return new StatusEffectOrderResult(false, entityKeyId, statusEffectName, 0f, false,
                        "Unknown status effect name '" + statusEffectName + "'.");
                }

                float duration = Mathf.Clamp(durationSeconds, 0.5f, 300f);
                entity.SetActiveStatusEffect(effect, SetStatusEffect.DurationType.Seconds, duration);

                return new StatusEffectOrderResult(true, entityKeyId, effect.ToString(), duration, true,
                    "Status effect '" + effect + "' applied for " + duration + "s.");
            }
            catch (Exception ex)
            {
                return new StatusEffectOrderResult(false, entityKeyId, statusEffectName, 0f, false,
                    "Error applying status effect: " + ex.Message);
            }
        }

        /// <summary>
        /// Clears an active status effect from an entity.
        /// </summary>
        public static StatusEffectOrderResult TryClearStatusEffect(
            string entityKeyId,
            string statusEffectName)
        {
            if (string.IsNullOrWhiteSpace(entityKeyId))
            {
                return new StatusEffectOrderResult(false, string.Empty, statusEffectName, 0f, false,
                    "Entity key ID cannot be empty.");
            }

            if (string.IsNullOrWhiteSpace(statusEffectName))
            {
                return new StatusEffectOrderResult(false, entityKeyId, string.Empty, 0f, false,
                    "Status effect name cannot be empty.");
            }

            try
            {
                ExistingControllers existing = ExistingControllers.Instance;
                if (existing == null)
                {
                    return new StatusEffectOrderResult(false, entityKeyId, statusEffectName, 0f, false,
                        "ExistingControllers instance is not available.");
                }

                EntityController entity = FindEntityByKey(existing, entityKeyId);
                if (entity == null)
                {
                    return new StatusEffectOrderResult(false, entityKeyId, statusEffectName, 0f, false,
                        "Entity '" + entityKeyId + "' not found or destroyed.");
                }

                if (!Enum.TryParse<StatusEffect>(statusEffectName, true, out StatusEffect effect))
                {
                    return new StatusEffectOrderResult(false, entityKeyId, statusEffectName, 0f, false,
                        "Unknown status effect name '" + statusEffectName + "'.");
                }

                entity.RemoveStatusEffect(effect);

                return new StatusEffectOrderResult(true, entityKeyId, effect.ToString(), 0f, false,
                    "Status effect '" + effect + "' removed successfully.");
            }
            catch (Exception ex)
            {
                return new StatusEffectOrderResult(false, entityKeyId, statusEffectName, 0f, false,
                    "Error clearing status effect: " + ex.Message);
            }
        }

        private static EntityController FindEntityByKey(ExistingControllers existing, string key)
        {
            if (existing == null || string.IsNullOrWhiteSpace(key))
            {
                return null;
            }

            var candidates = new List<EntityController>();
            try
            {
                HashSet<EntityController> playerUnits = existing.PlayerUnits();
                if (playerUnits != null) candidates.AddRange(playerUnits);
                HashSet<EntityController> aiUnits = existing.AiUnits();
                if (aiUnits != null) candidates.AddRange(aiUnits);
                HashSet<EntityController> aiBuildings = existing.AiBuildings();
                if (aiBuildings != null) candidates.AddRange(aiBuildings);
                HashSet<EntityController> playerBuildings = existing.PlayerBuildings();
                if (playerBuildings != null) candidates.AddRange(playerBuildings);
                HashSet<EntityController> knownBuildings = existing.PlayerBuildingsKnownToAi();
                if (knownBuildings != null) candidates.AddRange(knownBuildings);
                EntityController hq = existing.EngineeringHq();
                if (hq != null) candidates.Add(hq);
            }
            catch
            {
                // Degrade gracefully
            }

            foreach (EntityController entity in candidates)
            {
                if (entity == null || entity.IsDestroyed || entity.DestroyHasBeenStarted)
                {
                    continue;
                }

                string id = entity.EntityId ?? string.Empty;
                string name = entity.name ?? string.Empty;

                if (string.Equals(id, key, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(name, key, StringComparison.OrdinalIgnoreCase))
                {
                    return entity;
                }
            }

            return null;
        }

        private static EntityController FindDefaultPlayerTarget(ExistingControllers existing)
        {
            try
            {
                HashSet<EntityController> playerUnits = existing.PlayerUnits();
                if (playerUnits != null)
                {
                    foreach (EntityController unit in playerUnits)
                    {
                        if (unit != null && !unit.IsDestroyed && !unit.DestroyHasBeenStarted)
                        {
                            return unit;
                        }
                    }
                }

                EntityController hq = existing.EngineeringHq();
                if (hq != null && !hq.IsDestroyed && !hq.DestroyHasBeenStarted)
                {
                    return hq;
                }
            }
            catch
            {
                // Degrade gracefully
            }

            return null;
        }
    }
}
