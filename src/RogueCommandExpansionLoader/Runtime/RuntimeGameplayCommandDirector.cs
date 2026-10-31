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

    /// Pure diagnostic DTO returned when executing selection or control group operations.

    /// Decoupled from Unity types.

    /// </summary>

    public readonly struct SelectionCommandResult

    {

        public SelectionCommandResult(bool success, string commandType, int entitiesSelectedCount, string message)

        {

            Success = success;

            CommandType = commandType ?? string.Empty;

            EntitiesSelectedCount = entitiesSelectedCount;

            Message = message ?? string.Empty;

        }



        public bool Success { get; }

        public string CommandType { get; }

        public int EntitiesSelectedCount { get; }

        public string Message { get; }

    }



    /// <summary>

    /// Pure diagnostic DTO returned when toggling or revealing Fog of War.

    /// Decoupled from Unity types.

    /// </summary>

    public readonly struct FogCommandResult

    {

        public FogCommandResult(bool success, bool isDisabled, string message)

        {

            Success = success;

            IsDisabled = isDisabled;

            Message = message ?? string.Empty;

        }



        public bool Success { get; }

        public bool IsDisabled { get; }

        public string Message { get; }

    }



    /// <summary>

    /// Pure diagnostic DTO returned when modifying or querying match time scale.

    /// Decoupled from Unity types.

    /// </summary>

    public readonly struct GameSpeedCommandResult

    {

        public GameSpeedCommandResult(bool success, float previousTimeScale, float currentTimeScale, string message)

        {

            Success = success;

            PreviousTimeScale = previousTimeScale;

            CurrentTimeScale = currentTimeScale;

            Message = message ?? string.Empty;

        }



        public bool Success { get; }

        public float PreviousTimeScale { get; }

        public float CurrentTimeScale { get; }

        public string Message { get; }

    }



    /// <summary>

    /// Pure diagnostic DTO returned when commanding match victory or defeat.

    /// Decoupled from Unity types.

    /// </summary>

    public readonly struct MatchControlResult

    {

        public MatchControlResult(bool success, string outcome, string message)

        {

            Success = success;

            Outcome = outcome ?? string.Empty;

            Message = message ?? string.Empty;

        }



        public bool Success { get; }

        public string Outcome { get; }

        public string Message { get; }

    }



    /// <summary>

    /// Pure diagnostic DTO returned when dynamically spawning an entity on the grid.

    /// Decoupled from Unity types.

    /// </summary>

    public readonly struct EntitySpawnResult

    {

        public EntitySpawnResult(bool success, string entityKeyId, string entityId, string side, float spawnedX, float spawnedY, float spawnedZ, float health, string message)

        {

            Success = success;

            EntityKeyId = entityKeyId ?? string.Empty;

            EntityId = entityId ?? string.Empty;

            Side = side ?? string.Empty;

            SpawnedX = spawnedX;

            SpawnedY = spawnedY;

            SpawnedZ = spawnedZ;

            Health = health;

            Message = message ?? string.Empty;

        }



        public bool Success { get; }

        public string EntityKeyId { get; }

        public string EntityId { get; }

        public string Side { get; }

        public float SpawnedX { get; }

        public float SpawnedY { get; }

        public float SpawnedZ { get; }

        public float Health { get; }

        public string Message { get; }

    }



    /// <summary>

    /// Safe in-game gameplay and diagnostic command director allowing authorized automated agents

    /// and clean-room testing harnesses to inspect and command match systems without GUI interaction.

    /// </summary>

    /// <summary>

    /// Pure diagnostic DTO returned when controlling AI Director state or attack waves.

    /// Decoupled from Unity types.

    /// </summary>

    public readonly struct AiDirectorCommandResult

    {

        public AiDirectorCommandResult(bool success, string commandType, int waveNumber, string message)

        {

            Success = success;

            CommandType = commandType ?? string.Empty;

            WaveNumber = waveNumber;

            Message = message ?? string.Empty;

        }



        public bool Success { get; }

        public string CommandType { get; }

        public int WaveNumber { get; }

        public string Message { get; }

    }



    /// <summary>

    /// Pure diagnostic DTO returned when controlling camera pan, focus, or zoom.

    /// Decoupled from Unity types.

    /// </summary>

    public readonly struct CameraCommandResult

    {

        public CameraCommandResult(bool success, string commandType, float targetX, float targetZ, float fieldOfView, string message)

        {

            Success = success;

            CommandType = commandType ?? string.Empty;

            TargetX = targetX;

            TargetZ = targetZ;

            FieldOfView = fieldOfView;

            Message = message ?? string.Empty;

        }



        public bool Success { get; }

        public string CommandType { get; }

        public float TargetX { get; }

        public float TargetZ { get; }

        public float FieldOfView { get; }

        public string Message { get; }

    }



    /// <summary>

    /// Pure diagnostic DTO returned when commanding a harvester unit.

    /// Decoupled from Unity types.

    /// </summary>

    public readonly struct HarvesterCommandResult

    {

        public HarvesterCommandResult(bool success, string harvesterKeyId, string commandType, string message)

        {

            Success = success;

            HarvesterKeyId = harvesterKeyId ?? string.Empty;

            CommandType = commandType ?? string.Empty;

            Message = message ?? string.Empty;

        }



        public bool Success { get; }

        public string HarvesterKeyId { get; }

        public string CommandType { get; }

        public string Message { get; }

    }



    /// <summary>

    /// Pure diagnostic DTO returned when adjusting game audio mixer volumes.

    /// Decoupled from Unity types.

    /// </summary>

    public readonly struct AudioCommandResult

    {

        public AudioCommandResult(bool success, string channel, float volumeSlider, string message)

        {

            Success = success;

            Channel = channel ?? string.Empty;

            VolumeSlider = volumeSlider;

            Message = message ?? string.Empty;

        }



        public bool Success { get; }

        public string Channel { get; }

        public float VolumeSlider { get; }

        public string Message { get; }

    }



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

                HashSet<EntityController> harvesters = existing.Harvesters;

                if (harvesters != null) candidates.AddRange(harvesters);

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



        private static readonly MethodInfo SelectGameObjectMethod = typeof(PlayerUnitController).GetMethod("Select", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { typeof(GameObject) }, null);

        private static readonly MethodInfo AddToSelectionMethod = typeof(PlayerUnitController).GetMethod("AddToSelection", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { typeof(GameObject) }, null);

        private static readonly MethodInfo DeselectAllUnitsMethod = typeof(PlayerUnitController).GetMethod("DeselectAllUnits", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null);



        private static void InternalSelect(PlayerUnitController puc, GameObject go)

        {

            if (puc == null || go == null) return;

            try

            {

                SelectGameObjectMethod?.Invoke(puc, new object[] { go });

            }

            catch { }

        }



        private static void InternalAddToSelection(PlayerUnitController puc, GameObject go)

        {

            if (puc == null || go == null) return;

            try

            {

                if (AddToSelectionMethod != null)

                {

                    AddToSelectionMethod.Invoke(puc, new object[] { go });

                }

                else

                {

                    InternalSelect(puc, go);

                }

            }

            catch { }

        }



        private static void InternalDeselectAll(PlayerUnitController puc)

        {

            try

            {

                PlayerUnitController.DeselectAll_Static();

            }

            catch

            {

                try

                {

                    if (puc != null && DeselectAllUnitsMethod != null)

                    {

                        DeselectAllUnitsMethod.Invoke(puc, null);

                    }

                }

                catch { }

            }

        }



        /// <summary>

        /// Selects units by entity key IDs (or names).

        /// </summary>

        public static SelectionCommandResult TrySelectEntities(IReadOnlyList<string> entityKeyIds, bool additive = false)

        {

            if (entityKeyIds == null || entityKeyIds.Count == 0)

            {

                return new SelectionCommandResult(false, "SelectEntities", 0, "No entity keys provided.");

            }



            try

            {

                PlayerUnitController puc = ResolvePlayerUnitController();

                if (puc == null)

                {

                    return new SelectionCommandResult(false, "SelectEntities", 0, "PlayerUnitController instance is not available.");

                }



                ExistingControllers existing = ExistingControllers.Instance;

                if (existing == null)

                {

                    return new SelectionCommandResult(false, "SelectEntities", 0, "ExistingControllers instance is not available.");

                }



                if (!additive)

                {

                    InternalDeselectAll(puc);

                }



                int selectedCount = 0;

                foreach (string key in entityKeyIds)

                {

                    if (string.IsNullOrWhiteSpace(key)) continue;

                    EntityController ec = FindEntityByKey(existing, key.Trim());

                    if (ec != null && ec.gameObject != null)

                    {

                        InternalAddToSelection(puc, ec.gameObject);

                        selectedCount++;

                    }

                }



                return new SelectionCommandResult(selectedCount > 0, "SelectEntities", selectedCount,

                    "Selected " + selectedCount + " of " + entityKeyIds.Count + " requested entities.");

            }

            catch (Exception ex)

            {

                return new SelectionCommandResult(false, "SelectEntities", 0, "Error selecting entities: " + ex.Message);

            }

        }



        /// <summary>

        /// Deselects all currently selected units.

        /// </summary>

        public static SelectionCommandResult TryDeselectAll()

        {

            try

            {

                PlayerUnitController puc = ResolvePlayerUnitController();

                InternalDeselectAll(puc);

                return new SelectionCommandResult(true, "DeselectAll", 0, "Deselected all units.");

            }

            catch (Exception ex)

            {

                return new SelectionCommandResult(false, "DeselectAll", 0, "Error deselecting: " + ex.Message);

            }

        }



        /// <summary>

        /// Selects all units belonging to a specific category (CombatUnits, Harvesters, IdleHarvesters, SkillBuildings, Factories).

        /// </summary>

        public static SelectionCommandResult TrySelectCategory(string category)

        {

            if (string.IsNullOrWhiteSpace(category))

            {

                return new SelectionCommandResult(false, "SelectCategory", 0, "Category name is empty.");

            }



            try

            {

                PlayerUnitController puc = ResolvePlayerUnitController();

                if (puc == null)

                {

                    return new SelectionCommandResult(false, "SelectCategory", 0, "PlayerUnitController instance is not available.");

                }



                string cat = category.Trim();

                if (string.Equals(cat, "CombatUnits", StringComparison.OrdinalIgnoreCase) ||

                    string.Equals(cat, "Combat", StringComparison.OrdinalIgnoreCase))

                {

                    puc.SelectAllCombatUnits();

                }

                else if (string.Equals(cat, "Harvesters", StringComparison.OrdinalIgnoreCase))

                {

                    puc.SelectAllHarvesters();

                }

                else if (string.Equals(cat, "IdleHarvesters", StringComparison.OrdinalIgnoreCase))

                {

                    puc.SelectIdleHarvesters();

                }

                else if (string.Equals(cat, "SkillBuildings", StringComparison.OrdinalIgnoreCase))

                {

                    puc.SelectSkillBuildings();

                }

                else if (string.Equals(cat, "Factories", StringComparison.OrdinalIgnoreCase))

                {

                    puc.SelectAllFactories();

                }

                else

                {

                    return new SelectionCommandResult(false, "SelectCategory", 0,

                        "Unknown category '" + category + "'. Allowed: CombatUnits, Harvesters, IdleHarvesters, SkillBuildings, Factories.");

                }



                SelectionCensus census = RuntimeSelectionCensus.Current();

                return new SelectionCommandResult(true, "SelectCategory:" + cat, census.SelectedCount,

                    "Category '" + cat + "' selected (" + census.SelectedCount + " entities).");

            }

            catch (Exception ex)

            {

                return new SelectionCommandResult(false, "SelectCategory", 0, "Error selecting category: " + ex.Message);

            }

        }



        /// <summary>

        /// Orders Stop on a unit or all currently selected units.

        /// </summary>

        public static UnitCommandResult TryOrderStop(string entityKeyId = null)

        {

            try

            {

                if (!string.IsNullOrWhiteSpace(entityKeyId))

                {

                    ExistingControllers existing = ExistingControllers.Instance;

                    if (existing != null)

                    {

                        EntityController entity = FindEntityByKey(existing, entityKeyId);

                        if (entity != null)

                        {

                            PlayerUnitController puc = ResolvePlayerUnitController();

                            if (puc != null && entity.gameObject != null)

                            {

                                InternalSelect(puc, entity.gameObject);

                            }



                            PlayerUnitController.Stop_Static();

                            Vector3 pos = entity.transform != null ? entity.transform.position : Vector3.zero;

                            return new UnitCommandResult(true, entityKeyId, "Stop", pos.x, pos.z,

                                "Order Stop dispatched to entity '" + entityKeyId + "'.");

                        }

                    }



                    return new UnitCommandResult(false, entityKeyId, "Stop", 0f, 0f,

                        "Entity '" + entityKeyId + "' not found.");

                }



                PlayerUnitController.Stop_Static();

                return new UnitCommandResult(true, "SelectedUnits", "Stop", 0f, 0f,

                    "Order Stop dispatched to all selected units.");

            }

            catch (Exception ex)

            {

                return new UnitCommandResult(false, entityKeyId ?? "SelectedUnits", "Stop", 0f, 0f,

                    "Error ordering Stop: " + ex.Message);

            }

        }



        /// <summary>

        /// Orders Hold Position on a unit or all currently selected units.

        /// </summary>

        public static UnitCommandResult TryOrderHoldPosition(string entityKeyId = null)

        {

            try

            {

                if (!string.IsNullOrWhiteSpace(entityKeyId))

                {

                    ExistingControllers existing = ExistingControllers.Instance;

                    if (existing != null)

                    {

                        EntityController entity = FindEntityByKey(existing, entityKeyId);

                        if (entity != null)

                        {

                            PlayerUnitController puc = ResolvePlayerUnitController();

                            if (puc != null && entity.gameObject != null)

                            {

                                InternalSelect(puc, entity.gameObject);

                            }



                            PlayerUnitController.HoldPosition_Static();

                            Vector3 pos = entity.transform != null ? entity.transform.position : Vector3.zero;

                            return new UnitCommandResult(true, entityKeyId, "HoldPosition", pos.x, pos.z,

                                "Order HoldPosition dispatched to entity '" + entityKeyId + "'.");

                        }

                    }



                    return new UnitCommandResult(false, entityKeyId, "HoldPosition", 0f, 0f,

                        "Entity '" + entityKeyId + "' not found.");

                }



                PlayerUnitController.HoldPosition_Static();

                return new UnitCommandResult(true, "SelectedUnits", "HoldPosition", 0f, 0f,

                    "Order HoldPosition dispatched to all selected units.");

            }

            catch (Exception ex)

            {

                return new UnitCommandResult(false, entityKeyId ?? "SelectedUnits", "HoldPosition", 0f, 0f,

                    "Error ordering HoldPosition: " + ex.Message);

            }

        }



        /// <summary>

        /// Orders AttackMove toward target coordinates.

        /// </summary>

        public static UnitCommandResult TryOrderAttackMove(string entityKeyId, float targetX, float targetZ)

        {

            try

            {

                if (!string.IsNullOrWhiteSpace(entityKeyId))

                {

                    ExistingControllers existing = ExistingControllers.Instance;

                    if (existing != null)

                    {

                        EntityController entity = FindEntityByKey(existing, entityKeyId);

                        if (entity != null && entity.gameObject != null)

                        {

                            PlayerUnitController puc = ResolvePlayerUnitController();

                            if (puc != null)

                            {

                                InternalSelect(puc, entity.gameObject);

                            }

                        }

                    }

                }



                PlayerUnitController.AttackMove_Static(new Vector3(targetX, 0f, targetZ));

                return new UnitCommandResult(true, entityKeyId ?? "SelectedUnits", "AttackMove", targetX, targetZ,

                    "Order AttackMove to (" + targetX + ", " + targetZ + ") dispatched.");

            }

            catch (Exception ex)

            {

                return new UnitCommandResult(false, entityKeyId ?? "SelectedUnits", "AttackMove", targetX, targetZ,

                    "Error ordering AttackMove: " + ex.Message);

            }

        }



        /// <summary>

        /// Binds the currently selected entities to a control group index (0-9).

        /// </summary>

        public static SelectionCommandResult TryAssignControlGroup(int groupNumber)

        {

            if (groupNumber < 0 || groupNumber > 9)

            {

                return new SelectionCommandResult(false, "AssignControlGroup", 0, "Control group index must be between 0 and 9.");

            }



            try

            {

                bool assigned = PlayerUnitController.SetControlGroup_Static(groupNumber);

                SelectionCensus census = RuntimeSelectionCensus.Current();

                int count = (census.ControlGroupCounts != null && groupNumber < census.ControlGroupCounts.Count)

                    ? census.ControlGroupCounts[groupNumber]

                    : 0;



                return new SelectionCommandResult(assigned, "AssignControlGroup", count,

                    "Assigned " + count + " selected entities to Control Group " + groupNumber + ".");

            }

            catch (Exception ex)

            {

                return new SelectionCommandResult(false, "AssignControlGroup", 0,

                    "Error assigning control group: " + ex.Message);

            }

        }



        /// <summary>

        /// Selects the entities assigned to a control group index (0-9).

        /// </summary>

        public static SelectionCommandResult TrySelectControlGroup(int groupNumber)

        {

            if (groupNumber < 0 || groupNumber > 9)

            {

                return new SelectionCommandResult(false, "SelectControlGroup", 0, "Control group index must be between 0 and 9.");

            }



            try

            {

                PlayerUnitController.SelectControlGroup_Static(groupNumber);

                SelectionCensus census = RuntimeSelectionCensus.Current();

                int count = (census.ControlGroupCounts != null && groupNumber < census.ControlGroupCounts.Count)

                    ? census.ControlGroupCounts[groupNumber]

                    : 0;



                return new SelectionCommandResult(true, "SelectControlGroup", count,

                    "Selected Control Group " + groupNumber + " (" + count + " entities).");

            }

            catch (Exception ex)

            {

                return new SelectionCommandResult(false, "SelectControlGroup", 0,

                    "Error selecting control group: " + ex.Message);

            }

        }



        /// <summary>

        /// Sets Fog of War status (disabled or active) and reveals the map if disabled.

        /// </summary>

        public static FogCommandResult TrySetFogOfWar(bool disableFog)

        {

            try

            {

                Type type = typeof(UpdateFogOfWar);

                FieldInfo instanceField = type.GetField("_instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);

                object instance = instanceField?.GetValue(null) ?? UnityEngine.Object.FindObjectOfType<UpdateFogOfWar>();



                if (instance != null)

                {

                    FieldInfo disableField = type.GetField("disableFogOfWar", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                    disableField?.SetValue(instance, disableFog);

                }



                if (disableFog)

                {

                    UpdateFogOfWar.Reveal_Static();

                }



                return new FogCommandResult(true, disableFog,

                    disableFog ? "Fog of War disabled and revealed." : "Fog of War enabled.");

            }

            catch (Exception ex)

            {

                return new FogCommandResult(false, disableFog, "Error setting Fog of War: " + ex.Message);

            }

        }



        /// <summary>

        /// Triggers full-map Fog of War reveal.

        /// </summary>

        public static FogCommandResult TryRevealFogOfWar()

        {

            try

            {

                UpdateFogOfWar.Reveal_Static();

                return new FogCommandResult(true, false, "Fog of War revealed across world map.");

            }

            catch (Exception ex)

            {

                return new FogCommandResult(false, false, "Error revealing Fog of War: " + ex.Message);

            }

        }



        /// <summary>

        /// Adjusts match time scale (0.0 = pause, 1.0 = normal, up to 10.0 = fast-forward).

        /// </summary>

        public static GameSpeedCommandResult TrySetGameTimeScale(float timeScale)

        {

            try

            {

                float prev = Time.timeScale;

                float clamped = Mathf.Clamp(timeScale, 0f, 10f);

                Time.timeScale = clamped;

                return new GameSpeedCommandResult(true, prev, clamped,

                    "Game time scale set to " + clamped + "x (previous: " + prev + "x).");

            }

            catch (Exception ex)

            {

                return new GameSpeedCommandResult(false, 1f, 1f, "Error setting time scale: " + ex.Message);

            }

        }



        /// <summary>

        /// Returns the current match time scale.

        /// </summary>

        public static float GetGameTimeScale()

        {

            try

            {

                return Time.timeScale;

            }

            catch

            {

                return 1f;

            }

        }



        /// <summary>

        /// Triggers match victory or defeat sequence cleanly.

        /// </summary>

        public static MatchControlResult TryTriggerMatchEnd(bool win)

        {

            try

            {

                bool isFinishing = false;

                try

                {

                    isFinishing = FinishLevel.IsFinishing_Static();

                }

                catch { }



                if (isFinishing)

                {

                    return new MatchControlResult(false, win ? "Win" : "Lose", "Match is already finishing.");

                }



                if (win)

                {

                    FinishLevel.Win_Static(Vector3.zero, null);

                    return new MatchControlResult(true, "Win", "Victory sequence triggered successfully.");

                }

                else

                {

                    FinishLevel.Lose_Static(Vector3.zero);

                    return new MatchControlResult(true, "Lose", "Defeat sequence triggered successfully.");

                }

            }

            catch (Exception ex)

            {

                return new MatchControlResult(false, win ? "Win" : "Lose", "Error triggering match end: " + ex.Message);

            }

        }



        /// <summary>

        /// Dynamically spawns a unit or building entity at the specified world coordinates.

        /// </summary>

        public static EntitySpawnResult TrySpawnEntity(string entityId, string side, float targetX, float targetZ, string customName = "")

        {

            if (string.IsNullOrWhiteSpace(entityId))

            {

                return new EntitySpawnResult(false, string.Empty, string.Empty, string.Empty, targetX, 0f, targetZ, 0f, "Entity ID cannot be empty.");

            }



            try

            {

                string tag = string.Equals(side, "Ai", StringComparison.OrdinalIgnoreCase) ? "Ai" : "Player";

                Vector3 position = new Vector3(targetX, 0f, targetZ);



                try

                {

                    WorldGrid grid = WorldGrid.Instance;

                    if (grid != null)

                    {

                        position = grid.ClampToWorld(position);

                    }

                }

                catch { }



                string name = string.IsNullOrWhiteSpace(customName) ? entityId + "_AgentSpawn" : customName.Trim();



                EntityController entity = EntityFactory.InstantiateEntity(

                    entityId.Trim(),

                    position,

                    null,

                    tag,

                    name,

                    null,

                    UnitRole.None,

                    false,

                    "AgentRuntimeSpawn");



                if (entity == null)

                {

                    return new EntitySpawnResult(false, string.Empty, entityId, tag, position.x, position.y, position.z, 0f,

                        "EntityFactory returned null for '" + entityId + "'.");

                }



                string keyId = entity.EntityId ?? entity.name ?? entityId;

                float health = entity.CurrentHealth;



                return new EntitySpawnResult(true, keyId, entityId, tag, position.x, position.y, position.z, health,

                    "Spawned " + tag + " entity '" + entityId + "' at (" + position.x + ", " + position.z + ").");

            }

            catch (Exception ex)

            {

                return new EntitySpawnResult(false, string.Empty, entityId, side ?? string.Empty, targetX, 0f, targetZ, 0f,

                    "Error spawning entity: " + ex.Message);

            }

        }



        private static PlayerUnitController ResolvePlayerUnitController()

        {

            try

            {

                Type type = typeof(PlayerUnitController);

                FieldInfo instanceField = type.GetField("_instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);

                object instance = instanceField?.GetValue(null);

                if (instance is PlayerUnitController puc)

                {

                    return puc;

                }



                return UnityEngine.Object.FindObjectOfType<PlayerUnitController>();

            }

            catch

            {

                return null;

            }

        }



        /// <summary>

        /// Sets the current AI attack wave number.

        /// </summary>

        public static AiDirectorCommandResult TrySetAttackWaveNumber(int waveNumber)

        {

            int targetWave = Math.Max(0, waveNumber);

            try

            {

                Type type = typeof(AiBehaviourManager);

                BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;



                PropertyInfo prop = type.GetProperty("CurrentAttackWaveNumber", flags);

                if (prop != null)

                {

                    MethodInfo setter = prop.GetSetMethod(true);

                    if (setter != null)

                    {

                        object targetObj = setter.IsStatic ? null : (type.GetField("_instance", flags)?.GetValue(null) ?? UnityEngine.Object.FindObjectOfType<AiBehaviourManager>());

                        setter.Invoke(targetObj, new object[] { targetWave });

                        return new AiDirectorCommandResult(true, "SetAttackWaveNumber", targetWave,

                            "AI attack wave number set to " + targetWave + ".");

                    }

                }



                FieldInfo field = type.GetField("<CurrentAttackWaveNumber>k__BackingField", flags);

                if (field != null)

                {

                    object targetObj = field.IsStatic ? null : (type.GetField("_instance", flags)?.GetValue(null) ?? UnityEngine.Object.FindObjectOfType<AiBehaviourManager>());

                    field.SetValue(targetObj, targetWave);

                    return new AiDirectorCommandResult(true, "SetAttackWaveNumber", targetWave,

                        "AI attack wave number set to " + targetWave + ".");

                }



                return new AiDirectorCommandResult(false, "SetAttackWaveNumber", waveNumber,

                    "Could not resolve CurrentAttackWaveNumber on AiBehaviourManager.");

            }

            catch (Exception ex)

            {

                return new AiDirectorCommandResult(false, "SetAttackWaveNumber", waveNumber,

                    "Error setting attack wave number: " + ex.Message);

            }

        }



        /// <summary>

        /// Increments the AI attack wave number by 1 using native logic.

        /// </summary>

        public static AiDirectorCommandResult TryIncrementAttackWave()

        {

            try

            {

                Type type = typeof(AiBehaviourManager);

                MethodInfo m = type.GetMethod("IncrementAttackWaveNumber_Static", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);

                if (m != null)

                {

                    m.Invoke(null, null);

                    AiDirectorCensus census = RuntimeAiDirectorCensus.Current();

                    return new AiDirectorCommandResult(true, "IncrementAttackWave", census.CurrentAttackWaveNumber,

                        "AI attack wave incremented to " + census.CurrentAttackWaveNumber + ".");

                }



                return new AiDirectorCommandResult(false, "IncrementAttackWave", 0,

                    "AiBehaviourManager.IncrementAttackWaveNumber_Static not found.");

            }

            catch (Exception ex)

            {

                return new AiDirectorCommandResult(false, "IncrementAttackWave", 0,

                    "Error incrementing attack wave: " + ex.Message);

            }

        }



        /// <summary>

        /// Toggles or sets AI debug visual tags and current tactical commands on screen.

        /// </summary>

        public static AiDirectorCommandResult TryToggleAiDebugVisuals(bool? showTags, bool? showCommands)

        {

            try

            {

                Type type = typeof(AiBehaviourManager);

                FieldInfo instanceF = type.GetField("_instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);

                object instance = instanceF?.GetValue(null) ?? UnityEngine.Object.FindObjectOfType<AiBehaviourManager>();



                if (instance != null)

                {

                    if (showTags.HasValue)

                    {

                        FieldInfo tagsField = type.GetField("showAiTags", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                        tagsField?.SetValue(instance, showTags.Value);

                    }

                    else

                    {

                        MethodInfo toggleTags = type.GetMethod("ToggleShowAiTags_Static", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);

                        toggleTags?.Invoke(null, null);

                    }



                    if (showCommands.HasValue)

                    {

                        FieldInfo cmdsField = type.GetField("showCurrentCommands", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                        cmdsField?.SetValue(instance, showCommands.Value);

                    }

                    else

                    {

                        MethodInfo toggleCmds = type.GetMethod("ToggleCurrentCommands_Static", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);

                        toggleCmds?.Invoke(null, null);

                    }



                    AiDirectorCensus census = RuntimeAiDirectorCensus.Current();

                    return new AiDirectorCommandResult(true, "ToggleAiDebugVisuals", census.CurrentAttackWaveNumber,

                        "AI debug visuals updated: showAiTags=" + census.ShowAiTags + ", showCurrentCommands=" + census.ShowCurrentCommands + ".");

                }



                return new AiDirectorCommandResult(false, "ToggleAiDebugVisuals", 0,

                    "AiBehaviourManager instance is not available.");

            }

            catch (Exception ex)

            {

                return new AiDirectorCommandResult(false, "ToggleAiDebugVisuals", 0,

                    "Error updating AI debug visuals: " + ex.Message);

            }

        }



        /// <summary>

        /// Moves the main camera to target ground coordinates (X, Z).

        /// </summary>

        public static CameraCommandResult TryMoveCamera(float targetX, float targetZ)

        {

            try

            {

                Camera cam = Camera.main;

                if (cam == null)

                {

                    Type pucType = typeof(PlayerUnitController);

                    FieldInfo instanceF = pucType.GetField("_instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);

                    object puc = instanceF?.GetValue(null) ?? UnityEngine.Object.FindObjectOfType<PlayerUnitController>();

                    if (puc != null)

                    {

                        FieldInfo camF = pucType.GetField("_mainCam", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                        cam = camF?.GetValue(puc) as Camera;

                    }

                }



                if (cam == null)

                {

                    return new CameraCommandResult(false, "MoveCamera", targetX, targetZ, 0f, "Main camera not available.");

                }



                Vector3 targetGround = new Vector3(targetX, 0f, targetZ);

                try

                {

                    WorldGrid grid = WorldGrid.Instance;

                    if (grid != null)

                    {

                        targetGround = grid.ClampToWorld(targetGround);

                    }

                }

                catch { }



                float currentY = cam.transform != null ? cam.transform.position.y : 30f;

                float xOffset = 0f;

                float zOffset = -20f;



                try

                {

                    Type pucType = typeof(PlayerUnitController);

                    FieldInfo instanceF = pucType.GetField("_instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);

                    object puc = instanceF?.GetValue(null);

                    if (puc != null)

                    {

                        FieldInfo xF = pucType.GetField("MainCamOriginalXOffset", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                        if (xF != null) xOffset = Convert.ToSingle(xF.GetValue(puc));

                        FieldInfo zF = pucType.GetField("MainCamOriginalZOffset", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                        if (zF != null) zOffset = Convert.ToSingle(zF.GetValue(puc));

                    }

                }

                catch { }



                if (cam.transform != null)

                {

                    cam.transform.position = new Vector3(targetGround.x + xOffset, currentY, targetGround.z + zOffset);

                }



                float fov = cam.fieldOfView;

                return new CameraCommandResult(true, "MoveCamera", targetGround.x, targetGround.z, fov,

                    "Camera moved to (" + targetGround.x + ", " + targetGround.z + ").");

            }

            catch (Exception ex)

            {

                return new CameraCommandResult(false, "MoveCamera", targetX, targetZ, 0f,

                    "Error moving camera: " + ex.Message);

            }

        }



        /// <summary>

        /// Centers the camera on an entity by key ID using native centering logic.

        /// </summary>

        public static CameraCommandResult TryFocusCameraOnEntity(string entityKeyId)

        {

            if (string.IsNullOrWhiteSpace(entityKeyId))

            {

                return new CameraCommandResult(false, "FocusEntity", 0f, 0f, 0f, "Entity key ID is empty.");

            }



            try

            {

                ExistingControllers existing = ExistingControllers.Instance;

                if (existing == null)

                {

                    return new CameraCommandResult(false, "FocusEntity", 0f, 0f, 0f, "ExistingControllers not available.");

                }



                EntityController entity = FindEntityByKey(existing, entityKeyId.Trim());

                if (entity == null || entity.gameObject == null)

                {

                    return new CameraCommandResult(false, "FocusEntity", 0f, 0f, 0f, "Entity '" + entityKeyId + "' not found.");

                }



                PlayerUnitController puc = ResolvePlayerUnitController();

                if (puc != null)

                {

                    InternalSelect(puc, entity.gameObject);



                    try

                    {

                        MethodInfo centerMethod = typeof(PlayerUnitController).GetMethod("CenterCameraOnSelected",

                            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                        centerMethod?.Invoke(puc, null);

                    }

                    catch { }

                }



                Vector3 pos = entity.transform != null ? entity.transform.position : Vector3.zero;

                float fov = Camera.main != null ? Camera.main.fieldOfView : 45f;



                return new CameraCommandResult(true, "FocusEntity", pos.x, pos.z, fov,

                    "Camera focused on entity '" + entityKeyId + "' at (" + pos.x + ", " + pos.z + ").");

            }

            catch (Exception ex)

            {

                return new CameraCommandResult(false, "FocusEntity", 0f, 0f, 0f,

                    "Error focusing camera on entity: " + ex.Message);

            }

        }



        /// <summary>

        /// Sets main camera FOV / zoom level safely clamped to supported boundaries.

        /// </summary>

        public static CameraCommandResult TrySetCameraZoom(float fieldOfView)

        {

            try

            {

                Camera cam = Camera.main;

                if (cam == null)

                {

                    return new CameraCommandResult(false, "SetCameraZoom", 0f, 0f, fieldOfView, "Main camera not available.");

                }



                float minFov = 20f;

                float maxFov = 80f;



                try

                {

                    object zoomComp = cam.GetComponent<CameraZoom>() ?? UnityEngine.Object.FindObjectOfType<CameraZoom>();

                    if (zoomComp != null)

                    {

                        Type zType = zoomComp.GetType();

                        FieldInfo minF = zType.GetField("minFov", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                        if (minF != null) minFov = Convert.ToSingle(minF.GetValue(zoomComp));

                        FieldInfo maxF = zType.GetField("maxFov", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                        if (maxF != null) maxFov = Convert.ToSingle(maxF.GetValue(zoomComp));

                    }

                }

                catch { }



                float clamped = Mathf.Clamp(fieldOfView, minFov, maxFov);

                cam.fieldOfView = clamped;



                Vector3 pos = cam.transform != null ? cam.transform.position : Vector3.zero;

                return new CameraCommandResult(true, "SetCameraZoom", pos.x, pos.z, clamped,

                    "Camera zoom set to FOV " + clamped + " (clamped between " + minFov + " and " + maxFov + ").");

            }

            catch (Exception ex)

            {

                return new CameraCommandResult(false, "SetCameraZoom", 0f, 0f, fieldOfView,

                    "Error setting camera zoom: " + ex.Message);

            }

        }



        /// <summary>

        /// Orders a harvester unit to claim the nearest crystal node and commence harvesting.

        /// </summary>

        public static HarvesterCommandResult TryOrderHarvesterHarvest(string harvesterKeyId)

        {

            if (string.IsNullOrWhiteSpace(harvesterKeyId))

            {

                return new HarvesterCommandResult(false, string.Empty, "Harvest", "Harvester key ID is empty.");

            }



            try

            {

                ExistingControllers existing = ExistingControllers.Instance;

                if (existing == null)

                {

                    return new HarvesterCommandResult(false, harvesterKeyId, "Harvest", "ExistingControllers not available.");

                }



                EntityController entity = FindEntityByKey(existing, harvesterKeyId.Trim());

                if (entity == null || entity.gameObject == null)

                {

                    return new HarvesterCommandResult(false, harvesterKeyId, "Harvest", "Entity '" + harvesterKeyId + "' not found.");

                }



                Harvest harvest = entity.GetComponent<Harvest>();

                if (harvest == null)

                {

                    return new HarvesterCommandResult(false, harvesterKeyId, "Harvest", "Entity '" + harvesterKeyId + "' does not possess a Harvest component.");

                }



                MethodInfo m = typeof(Harvest).GetMethod("ClaimClosestCrystalAndMoveToIt", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                if (m == null)

                {

                    return new HarvesterCommandResult(false, harvesterKeyId, "Harvest",

                        "Harvest.ClaimClosestCrystalAndMoveToIt not found.");

                }

                m.Invoke(harvest, null);

                return new HarvesterCommandResult(true, harvesterKeyId, "Harvest",

                    "Harvester '" + harvesterKeyId + "' ordered to claim closest crystal and harvest.");

            }

            catch (Exception ex)

            {

                return new HarvesterCommandResult(false, harvesterKeyId, "Harvest",

                    "Error commanding harvester: " + ex.Message);

            }

        }



        /// <summary>

        /// Orders a harvester unit to deliver its cargo to the closest refinery.

        /// </summary>

        public static HarvesterCommandResult TryOrderHarvesterReturn(string harvesterKeyId)

        {

            if (string.IsNullOrWhiteSpace(harvesterKeyId))

            {

                return new HarvesterCommandResult(false, string.Empty, "ReturnToRefinery", "Harvester key ID is empty.");

            }



            try

            {

                ExistingControllers existing = ExistingControllers.Instance;

                if (existing == null)

                {

                    return new HarvesterCommandResult(false, harvesterKeyId, "ReturnToRefinery", "ExistingControllers not available.");

                }



                EntityController entity = FindEntityByKey(existing, harvesterKeyId.Trim());

                if (entity == null || entity.gameObject == null)

                {

                    return new HarvesterCommandResult(false, harvesterKeyId, "ReturnToRefinery", "Entity '" + harvesterKeyId + "' not found.");

                }



                Harvest harvest = entity.GetComponent<Harvest>();

                if (harvest == null)

                {

                    return new HarvesterCommandResult(false, harvesterKeyId, "ReturnToRefinery", "Entity '" + harvesterKeyId + "' does not possess a Harvest component.");

                }



                MethodInfo m = typeof(Harvest).GetMethod("MoveToClosestRefinery", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                if (m == null)

                {

                    return new HarvesterCommandResult(false, harvesterKeyId, "ReturnToRefinery",

                        "Harvest.MoveToClosestRefinery not found.");

                }

                m.Invoke(harvest, null);

                return new HarvesterCommandResult(true, harvesterKeyId, "ReturnToRefinery",

                    "Harvester '" + harvesterKeyId + "' ordered to return to closest refinery.");

            }

            catch (Exception ex)

            {

                return new HarvesterCommandResult(false, harvesterKeyId, "ReturnToRefinery",

                    "Error ordering harvester return: " + ex.Message);

            }

        }



        /// <summary>

        /// Sets volume slider level (0.0 to 1.0) for an audio channel (Master, Music, Gameplay, UI).

        /// </summary>

        public static AudioCommandResult TrySetAudioVolume(string channel, float sliderValue)

        {

            if (string.IsNullOrWhiteSpace(channel))

            {

                return new AudioCommandResult(false, string.Empty, sliderValue, "Audio channel name is empty.");

            }



            try

            {

                UnityEngine.Audio.AudioMixer mixer = RuntimeAudioCensus.ResolveAudioMixer();

                if (mixer == null)

                {

                    return new AudioCommandResult(false, channel, sliderValue, "AudioMixer instance is not available.");

                }



                float clamped = Mathf.Clamp(sliderValue, 0f, 1f);

                string ch = channel.Trim();

                Type helperType = typeof(AudioMixerHelper);



                string methodName = null;

                if (string.Equals(ch, "Master", StringComparison.OrdinalIgnoreCase)) methodName = "SetMasterVolume";

                else if (string.Equals(ch, "Music", StringComparison.OrdinalIgnoreCase)) methodName = "SetMusicVolume";

                else if (string.Equals(ch, "Gameplay", StringComparison.OrdinalIgnoreCase)) methodName = "SetGameplayVolume";

                else if (string.Equals(ch, "UI", StringComparison.OrdinalIgnoreCase) ||

                         string.Equals(ch, "UISelectionSounds", StringComparison.OrdinalIgnoreCase)) methodName = "SetUiSelectionSoundsVolume";

                else

                {

                    return new AudioCommandResult(false, channel, sliderValue,

                        "Unknown audio channel '" + channel + "'. Allowed: Master, Music, Gameplay, UI.");

                }



                MethodInfo m = helperType.GetMethod(methodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,

                    null, new[] { typeof(float), typeof(UnityEngine.Audio.AudioMixer) }, null);



                if (m != null)

                {

                    m.Invoke(null, new object[] { clamped, mixer });

                    return new AudioCommandResult(true, ch, clamped,

                        "Audio channel '" + ch + "' volume slider set to " + clamped + ".");

                }



                return new AudioCommandResult(false, ch, sliderValue, "Method " + methodName + " not found on AudioMixerHelper.");

            }

            catch (Exception ex)

            {

                return new AudioCommandResult(false, channel, sliderValue, "Error setting audio volume: " + ex.Message);

            }

        }



    }

}
