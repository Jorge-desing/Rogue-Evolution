using System;

using System.Collections.Generic;

using System.Reflection;

using UnityEngine;



namespace RogueCommandExpansionLoader.Runtime

{

    /// <summary>
    /// Result of a no-focus native building placement preview or commit.
    /// </summary>
    public readonly struct BuildingPlacementCommandResult
    {
        public BuildingPlacementCommandResult(
            bool success, bool committed, string entityId, int cost, int balanceBefore,
            float worldX, float worldY, float worldZ, int cellCount, string message,
            IReadOnlyList<BuildingPlacementCellDiagnostic> cellDiagnostics = null,
            int omittedCellDiagnosticCount = 0)
        {
            Success = success;
            Committed = committed;
            EntityId = entityId ?? string.Empty;
            Cost = cost;
            BalanceBefore = balanceBefore;
            WorldX = worldX;
            WorldY = worldY;
            WorldZ = worldZ;
            CellCount = cellCount;
            Message = message ?? string.Empty;
            CellDiagnostics = cellDiagnostics ?? Array.Empty<BuildingPlacementCellDiagnostic>();
            OmittedCellDiagnosticCount = Math.Max(0, omittedCellDiagnosticCount);
        }

        public bool Success { get; }
        public bool Committed { get; }
        public string EntityId { get; }
        public int Cost { get; }
        public int BalanceBefore { get; }
        public float WorldX { get; }
        public float WorldY { get; }
        public float WorldZ { get; }
        public int CellCount { get; }
        public string Message { get; }
        public IReadOnlyList<BuildingPlacementCellDiagnostic> CellDiagnostics { get; }
        public int OmittedCellDiagnosticCount { get; }
    }

    /// <summary>
    /// Read-only cell telemetry explaining native placement preflight results.
    /// </summary>
    public readonly struct BuildingPlacementCellDiagnostic
    {
        public BuildingPlacementCellDiagnostic(
            int gridX, int gridY, string worldObject, string heightLayer,
            bool withinGrid, bool ground, bool obstacle, bool building, bool ramp,
            bool crystal, bool unit, bool unitBlocksPlacement, bool multiLayer,
            bool buildingInProgress, bool secondaryFogOccluded,
            bool sameHeightLayerAsFootprint, IReadOnlyList<string> blockingReasons,
            IReadOnlyList<BuildingPlacementOccupantDiagnostic> occupantEntities)
        {
            GridX = gridX;
            GridY = gridY;
            WorldObject = worldObject ?? string.Empty;
            HeightLayer = heightLayer ?? string.Empty;
            WithinGrid = withinGrid;
            Ground = ground;
            Obstacle = obstacle;
            Building = building;
            Ramp = ramp;
            Crystal = crystal;
            Unit = unit;
            UnitBlocksPlacement = unitBlocksPlacement;
            MultiLayer = multiLayer;
            BuildingInProgress = buildingInProgress;
            SecondaryFogOccluded = secondaryFogOccluded;
            SameHeightLayerAsFootprint = sameHeightLayerAsFootprint;
            BlockingReasons = blockingReasons ?? Array.Empty<string>();
            OccupantEntities = occupantEntities ?? Array.Empty<BuildingPlacementOccupantDiagnostic>();
        }

        public int GridX { get; }
        public int GridY { get; }
        public string WorldObject { get; }
        public string HeightLayer { get; }
        public bool WithinGrid { get; }
        public bool Ground { get; }
        public bool Obstacle { get; }
        public bool Building { get; }
        public bool Ramp { get; }
        public bool Crystal { get; }
        public bool Unit { get; }
        public bool UnitBlocksPlacement { get; }
        public bool MultiLayer { get; }
        public bool BuildingInProgress { get; }
        public bool SecondaryFogOccluded { get; }
        public bool SameHeightLayerAsFootprint { get; }
        public IReadOnlyList<string> BlockingReasons { get; }
        public IReadOnlyList<BuildingPlacementOccupantDiagnostic> OccupantEntities { get; }
    }

    /// <summary>
    /// Live native entity occupying a grid cell in a placement footprint.
    /// </summary>
    public readonly struct BuildingPlacementOccupantDiagnostic
    {
        public BuildingPlacementOccupantDiagnostic(
            string entityId, string entityKeyId, int uniqueEntityId,
            string gameObjectName, bool isBuilding, bool isUnit, int gridSize)
        {
            EntityId = entityId ?? string.Empty;
            EntityKeyId = entityKeyId ?? string.Empty;
            UniqueEntityId = uniqueEntityId;
            GameObjectName = gameObjectName ?? string.Empty;
            IsBuilding = isBuilding;
            IsUnit = isUnit;
            GridSize = gridSize;
        }

        public string EntityId { get; }
        public string EntityKeyId { get; }
        public int UniqueEntityId { get; }
        public string GameObjectName { get; }
        public bool IsBuilding { get; }
        public bool IsUnit { get; }
        public int GridSize { get; }
    }

    /// <summary>
    /// Bounded search for a nearby location accepted by native placement checks.
    /// </summary>
    public readonly struct BuildingPlacementSearchResult
    {
        public BuildingPlacementSearchResult(
            bool found, float originX, float originZ, int radiusCells,
            int candidatesChecked, BuildingPlacementCommandResult candidate, string message)
        {
            Found = found;
            OriginX = originX;
            OriginZ = originZ;
            RadiusCells = radiusCells;
            CandidatesChecked = candidatesChecked;
            Candidate = candidate;
            Message = message ?? string.Empty;
        }

        public bool Found { get; }
        public float OriginX { get; }
        public float OriginZ { get; }
        public int RadiusCells { get; }
        public int CandidatesChecked { get; }
        public BuildingPlacementCommandResult Candidate { get; }
        public string Message { get; }
    }

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

    /// Pure diagnostic DTO returned when executing a bounded tactical pulse around a unit.

    /// </summary>

    public readonly struct TacticalPulseResult

    {

        public TacticalPulseResult(

            bool success,

            string entityKeyId,

            string pulseType,

            int affectedEntityCount,

            float damageApplied,

            float healingApplied,

            int statusEffectApplicationCount,

            string message)

        {

            Success = success;

            EntityKeyId = entityKeyId ?? string.Empty;

            PulseType = pulseType ?? string.Empty;

            AffectedEntityCount = affectedEntityCount;

            DamageApplied = damageApplied;

            HealingApplied = healingApplied;

            StatusEffectApplicationCount = statusEffectApplicationCount;

            Message = message ?? string.Empty;

        }

        public bool Success { get; }

        public string EntityKeyId { get; }

        public string PulseType { get; }

        public int AffectedEntityCount { get; }

        public float DamageApplied { get; }

        public float HealingApplied { get; }

        public int StatusEffectApplicationCount { get; }

        public string Message { get; }

    }


    /// <summary>
    /// Pure diagnostic DTO returned when a director sweeps hostile units around a source.
    /// </summary>
    public readonly struct ThreatSweepResult
    {
        public ThreatSweepResult(
            bool success,
            string sourceEntityKeyId,
            string sweepType,
            string statusEffectName,
            float radius,
            float durationSeconds,
            int affectedEntityCount,
            int omittedTargetCount,
            IReadOnlyList<string> targetEntityKeyIds,
            string message)
        {
            Success = success;
            SourceEntityKeyId = sourceEntityKeyId ?? string.Empty;
            SweepType = sweepType ?? string.Empty;
            StatusEffectName = statusEffectName ?? string.Empty;
            Radius = radius;
            DurationSeconds = durationSeconds;
            AffectedEntityCount = affectedEntityCount;
            OmittedTargetCount = omittedTargetCount;
            TargetEntityKeyIds = targetEntityKeyIds ?? new List<string>().AsReadOnly();
            Message = message ?? string.Empty;
        }

        public bool Success { get; }
        public string SourceEntityKeyId { get; }
        public string SweepType { get; }
        public string StatusEffectName { get; }
        public float Radius { get; }
        public float DurationSeconds { get; }
        public int AffectedEntityCount { get; }
        public int OmittedTargetCount { get; }
        public IReadOnlyList<string> TargetEntityKeyIds { get; }
        public string Message { get; }
    }



    /// <summary>
    /// Pure diagnostic DTO returned when coordinating bounded attacks against one hostile target.
    /// </summary>
    public readonly struct CoordinatedAssaultResult
    {
        public CoordinatedAssaultResult(
            bool success,
            string targetEntityKeyId,
            int requestedAttackerCount,
            int dispatchedAttackerCount,
            int omittedAttackerCount,
            IReadOnlyList<string> dispatchedAttackerEntityKeyIds,
            float targetX,
            float targetZ,
            string message)
        {
            Success = success;
            TargetEntityKeyId = targetEntityKeyId ?? string.Empty;
            RequestedAttackerCount = requestedAttackerCount;
            DispatchedAttackerCount = dispatchedAttackerCount;
            OmittedAttackerCount = omittedAttackerCount;
            DispatchedAttackerEntityKeyIds = dispatchedAttackerEntityKeyIds ?? new List<string>().AsReadOnly();
            TargetX = targetX;
            TargetZ = targetZ;
            Message = message ?? string.Empty;
        }

        public bool Success { get; }
        public string TargetEntityKeyId { get; }
        public int RequestedAttackerCount { get; }
        public int DispatchedAttackerCount { get; }
        public int OmittedAttackerCount { get; }
        public IReadOnlyList<string> DispatchedAttackerEntityKeyIds { get; }
        public float TargetX { get; }
        public float TargetZ { get; }
        public string Message { get; }
    }


    /// <summary>
    /// Pure diagnostic DTO returned when ordering an evasive retreat away from a threat.
    /// Decoupled from Unity types.
    /// </summary>
    public readonly struct RetreatResult
    {
        public RetreatResult(
            bool success,
            string entityKeyId,
            float threatX,
            float threatZ,
            float retreatX,
            float retreatZ,
            float distance,
            string message)
        {
            Success = success;
            EntityKeyId = entityKeyId ?? string.Empty;
            ThreatX = threatX;
            ThreatZ = threatZ;
            RetreatX = retreatX;
            RetreatZ = retreatZ;
            Distance = distance;
            Message = message ?? string.Empty;
        }

        public bool Success { get; }
        public string EntityKeyId { get; }
        public float ThreatX { get; }
        public float ThreatZ { get; }
        public float RetreatX { get; }
        public float RetreatZ { get; }
        public float Distance { get; }
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



    /// <summary>
    /// Pure diagnostic DTO returned when adjusting or granting entity combat veterancy / XP.
    /// Decoupled from Unity types.
    /// </summary>
    public readonly struct VeterancyCommandResult
    {
        public VeterancyCommandResult(bool success, string entityKeyId, int currentRank, int currentXp, string message)
        {
            Success = success;
            EntityKeyId = entityKeyId ?? string.Empty;
            CurrentRank = currentRank;
            CurrentXp = currentXp;
            Message = message ?? string.Empty;
        }

        public bool Success { get; }
        public string EntityKeyId { get; }
        public int CurrentRank { get; }
        public int CurrentXp { get; }
        public string Message { get; }
    }

    /// <summary>
    /// Pure diagnostic DTO returned when dispatching a dynamic command aura or captain link pulse.
    /// Decoupled from Unity types.
    /// </summary>
    public readonly struct CommandAuraResult
    {
        public CommandAuraResult(bool success, string captainKeyId, string auraType, int linkedFollowersCount, float radius, float durationSeconds, string message)
        {
            Success = success;
            CaptainKeyId = captainKeyId ?? string.Empty;
            AuraType = auraType ?? string.Empty;
            LinkedFollowersCount = linkedFollowersCount;
            Radius = radius;
            DurationSeconds = durationSeconds;
            Message = message ?? string.Empty;
        }

        public bool Success { get; }
        public string CaptainKeyId { get; }
        public string AuraType { get; }
        public int LinkedFollowersCount { get; }
        public float Radius { get; }
        public float DurationSeconds { get; }
        public string Message { get; }
    }

    /// <summary>
    /// Pure diagnostic DTO returned when spawning or clearing battlefield environmental hazard zones.
    /// Decoupled from Unity types.
    /// </summary>
    public readonly struct HazardCommandResult
    {
        public HazardCommandResult(bool success, string zoneId, string hazardType, float centerX, float centerZ, float radius, float durationSeconds, float tickDamage, string message)
        {
            Success = success;
            ZoneId = zoneId ?? string.Empty;
            HazardType = hazardType ?? string.Empty;
            CenterX = centerX;
            CenterZ = centerZ;
            Radius = radius;
            DurationSeconds = durationSeconds;
            TickDamage = tickDamage;
            Message = message ?? string.Empty;
        }

        public bool Success { get; }
        public string ZoneId { get; }
        public string HazardType { get; }
        public float CenterX { get; }
        public float CenterZ { get; }
        public float Radius { get; }
        public float DurationSeconds { get; }
        public float TickDamage { get; }
        public string Message { get; }
    }


    /// <summary>
    /// Pure diagnostic DTO returned when applying or switching a unit loadout / variant.
    /// Decoupled from Unity types.
    /// </summary>
    public readonly struct UnitLoadoutResult
    {
        public UnitLoadoutResult(bool success, string entityKeyId, string loadoutId, string message)
        {
            Success = success;
            EntityKeyId = entityKeyId ?? string.Empty;
            LoadoutId = loadoutId ?? string.Empty;
            Message = message ?? string.Empty;
        }

        public bool Success { get; }
        public string EntityKeyId { get; }
        public string LoadoutId { get; }
        public string Message { get; }
    }

    /// <summary>
    /// Pure diagnostic DTO returned when applying or switching a unit visual variant.
    /// Supports the canonical two-level system: Level 1 Primary Chassis and Level 2 Tactical Variants.
    /// Decoupled from Unity types.
    /// </summary>
    public readonly struct UnitVisualVariantResult
    {
        public UnitVisualVariantResult(
            bool success,
            string entityKeyId,
            string variantId,
            string presetName,
            string message,
            int moduleCount = 0)
        {
            Success = success;
            EntityKeyId = entityKeyId ?? string.Empty;
            VariantId = variantId ?? string.Empty;
            PresetName = presetName ?? string.Empty;
            Message = message ?? string.Empty;
            ModuleCount = Math.Max(0, moduleCount);
        }

        public bool Success { get; }
        public string EntityKeyId { get; }
        public string VariantId { get; }
        public string PresetName { get; }
        public string Message { get; }
        public int ModuleCount { get; }
    }

    /// <summary>
    /// Pure diagnostic DTO returned when manually warming an EntityFactory pre-spawn pool.
    /// Decoupled from Unity types.
    /// </summary>
    public readonly struct PrespawnWarmResult
    {
        public PrespawnWarmResult(bool success, string entityId, int requestedCount, int warmedCount, string message)
        {
            Success = success;
            EntityId = entityId ?? string.Empty;
            RequestedCount = requestedCount;
            WarmedCount = warmedCount;
            Message = message ?? string.Empty;
        }

        public bool Success { get; }
        public string EntityId { get; }
        public int RequestedCount { get; }
        public int WarmedCount { get; }
        public string Message { get; }
    }

    /// <summary>
    /// Pure diagnostic DTO returned when setting an AI tactical doctrine and aggression scaling.
    /// Decoupled from Unity types.
    /// </summary>
    public readonly struct AiDoctrineResult
    {
        public AiDoctrineResult(bool success, string doctrineName, float aggressionMultiplier, string message)
        {
            Success = success;
            DoctrineName = doctrineName ?? string.Empty;
            AggressionMultiplier = aggressionMultiplier;
            Message = message ?? string.Empty;
        }

        public bool Success { get; }
        public string DoctrineName { get; }
        public float AggressionMultiplier { get; }
        public string Message { get; }
    }

    /// <summary>
    /// Pure diagnostic DTO returned when injecting a unit into an AI base deck.
    /// Decoupled from Unity types.
    /// </summary>
    public readonly struct AiDeckInjectionResult
    {
        public AiDeckInjectionResult(bool success, int baseIndex, string unitId, int countAtStart, int maxLiving, string message)
        {
            Success = success;
            BaseIndex = baseIndex;
            UnitId = unitId ?? string.Empty;
            CountAtStart = countAtStart;
            MaxLiving = maxLiving;
            Message = message ?? string.Empty;
        }

        public bool Success { get; }
        public int BaseIndex { get; }
        public string UnitId { get; }
        public int CountAtStart { get; }
        public int MaxLiving { get; }
        public string Message { get; }
    }

    /// <summary>
    /// Pure diagnostic DTO returned when dispatching a native patrol route to a unit.
    /// Decoupled from Unity types.
    /// </summary>
    public readonly struct UnitPatrolResult
    {
        public UnitPatrolResult(bool success, string entityKeyId, int waypointCount, string message)
        {
            Success = success;
            EntityKeyId = entityKeyId ?? string.Empty;
            WaypointCount = waypointCount;
            Message = message ?? string.Empty;
        }

        public bool Success { get; }
        public string EntityKeyId { get; }
        public int WaypointCount { get; }
        public string Message { get; }
    }

    /// <summary>
    /// Pure diagnostic DTO returned when dispatching a single command type to a group of units.
    /// Decoupled from Unity types.
    /// </summary>
    public readonly struct GroupCommandResult
    {
        public GroupCommandResult(bool success, string commandType, int targetedCount, int dispatchedCount, string message)
        {
            Success = success;
            CommandType = commandType ?? string.Empty;
            TargetedCount = targetedCount;
            DispatchedCount = dispatchedCount;
            Message = message ?? string.Empty;
        }

        public bool Success { get; }
        public string CommandType { get; }
        public int TargetedCount { get; }
        public int DispatchedCount { get; }
        public string Message { get; }
    }

    /// <summary>
    /// Pure diagnostic DTO returned when dispatching a tactical formation movement to multiple units.
    /// Decoupled from Unity types.
    /// </summary>
    public readonly struct FormationOrderResult
    {
        public FormationOrderResult(bool success, string formationType, int targetedCount, int dispatchedCount, float targetX, float targetZ, float spacing, string message)
        {
            Success = success;
            FormationType = formationType ?? string.Empty;
            TargetedCount = targetedCount;
            DispatchedCount = dispatchedCount;
            TargetX = targetX;
            TargetZ = targetZ;
            Spacing = spacing;
            Message = message ?? string.Empty;
        }

        public bool Success { get; }
        public string FormationType { get; }
        public int TargetedCount { get; }
        public int DispatchedCount { get; }
        public float TargetX { get; }
        public float TargetZ { get; }
        public float Spacing { get; }
        public string Message { get; }
    }

    /// <summary>
    /// Pure diagnostic row for a unit's active tactical combat stance.
    /// Decoupled from Unity types.
    /// </summary>
    public readonly struct TacticalStanceDiagnosticRow
    {
        public TacticalStanceDiagnosticRow(string entityKeyId, string entityId, string stance, bool isControlledByPlayer, float moveSpeed, float weaponRange)
        {
            EntityKeyId = entityKeyId ?? string.Empty;
            EntityId = entityId ?? string.Empty;
            Stance = stance ?? string.Empty;
            IsControlledByPlayer = isControlledByPlayer;
            MoveSpeed = moveSpeed;
            WeaponRange = weaponRange;
        }

        public string EntityKeyId { get; }
        public string EntityId { get; }
        public string Stance { get; }
        public bool IsControlledByPlayer { get; }
        public float MoveSpeed { get; }
        public float WeaponRange { get; }
    }

    /// <summary>
    /// Pure diagnostic DTO returned when applying or updating a unit's tactical combat stance.
    /// Decoupled from Unity types.
    /// </summary>
    public readonly struct UnitTacticalStanceResult
    {
        public UnitTacticalStanceResult(bool success, string entityKeyId, string stance, int affectedUnitCount, string message)
        {
            Success = success;
            EntityKeyId = entityKeyId ?? string.Empty;
            Stance = stance ?? string.Empty;
            AffectedUnitCount = affectedUnitCount;
            Message = message ?? string.Empty;
        }

        public bool Success { get; }
        public string EntityKeyId { get; }
        public string Stance { get; }
        public int AffectedUnitCount { get; }
        public string Message { get; }
    }

    /// <summary>
    /// Census of tactical combat stances across living units.
    /// Decoupled from Unity types.
    /// </summary>
    public sealed class TacticalStanceCensus
    {
        public int OmittedUnitCount { get; }
        public int TotalTrackedUnits { get; }
        public int AggressiveCount { get; }
        public int DefensiveCount { get; }
        public int KitingCount { get; }
        public int HoldFireCount { get; }
        public int StandardCount { get; }
        public IReadOnlyList<TacticalStanceDiagnosticRow> StanceRows { get; }

        public TacticalStanceCensus(
            int omittedUnitCount,
            int totalTrackedUnits,
            int aggressiveCount,
            int defensiveCount,
            int kitingCount,
            int holdFireCount,
            int standardCount,
            IReadOnlyList<TacticalStanceDiagnosticRow> stanceRows)
        {
            OmittedUnitCount = omittedUnitCount;
            TotalTrackedUnits = totalTrackedUnits;
            AggressiveCount = aggressiveCount;
            DefensiveCount = defensiveCount;
            KitingCount = kitingCount;
            HoldFireCount = holdFireCount;
            StandardCount = standardCount;
            StanceRows = stanceRows ?? new List<TacticalStanceDiagnosticRow>().AsReadOnly();
        }

        public static TacticalStanceCensus Empty { get; } =
            new TacticalStanceCensus(0, 0, 0, 0, 0, 0, 0, new List<TacticalStanceDiagnosticRow>().AsReadOnly());
    }

    /// <summary>
    /// Pure diagnostic row for a unit's active elite affixes and traits.
    /// Decoupled from Unity types.
    /// </summary>
    public readonly struct UnitAffixDiagnosticRow
    {
        public UnitAffixDiagnosticRow(string entityKeyId, string entityId, bool isControlledByPlayer, IReadOnlyList<string> affixes)
        {
            EntityKeyId = entityKeyId ?? string.Empty;
            EntityId = entityId ?? string.Empty;
            IsControlledByPlayer = isControlledByPlayer;
            Affixes = affixes ?? new List<string>().AsReadOnly();
        }

        public string EntityKeyId { get; }
        public string EntityId { get; }
        public bool IsControlledByPlayer { get; }
        public IReadOnlyList<string> Affixes { get; }
    }

    /// <summary>
    /// Pure diagnostic DTO returned when adding or removing an elite affix on a unit.
    /// Decoupled from Unity types.
    /// </summary>
    public readonly struct UnitAffixResult
    {
        public UnitAffixResult(bool success, string entityKeyId, string affix, string action, int activeAffixCount, string message)
        {
            Success = success;
            EntityKeyId = entityKeyId ?? string.Empty;
            Affix = affix ?? string.Empty;
            Action = action ?? string.Empty;
            ActiveAffixCount = activeAffixCount;
            Message = message ?? string.Empty;
        }

        public bool Success { get; }
        public string EntityKeyId { get; }
        public string Affix { get; }
        public string Action { get; }
        public int ActiveAffixCount { get; }
        public string Message { get; }
    }

    /// <summary>
    /// Census of active elite affixes and traits applied to living units.
    /// Decoupled from Unity types.
    /// </summary>
    public sealed class UnitAffixCensus
    {
        public int OmittedUnitCount { get; }
        public int TotalTrackedUnits { get; }
        public int TotalActiveAffixes { get; }
        public IReadOnlyList<UnitAffixDiagnosticRow> AffixRows { get; }

        public UnitAffixCensus(
            int omittedUnitCount,
            int totalTrackedUnits,
            int totalActiveAffixes,
            IReadOnlyList<UnitAffixDiagnosticRow> affixRows)
        {
            OmittedUnitCount = omittedUnitCount;
            TotalTrackedUnits = totalTrackedUnits;
            TotalActiveAffixes = totalActiveAffixes;
            AffixRows = affixRows ?? new List<UnitAffixDiagnosticRow>().AsReadOnly();
        }

        public static UnitAffixCensus Empty { get; } =
            new UnitAffixCensus(0, 0, 0, new List<UnitAffixDiagnosticRow>().AsReadOnly());
    }

    /// <summary>
    /// Pure diagnostic DTO returned when manually triggering an AI assault wave.
    /// Decoupled from Unity types.
    /// </summary>
    public readonly struct AiWaveDispatchResult
    {
        public AiWaveDispatchResult(bool success, int waveNumber, float aggressionBoost, int dispatchedUnitCount, string message)
        {
            Success = success;
            WaveNumber = waveNumber;
            AggressionBoost = aggressionBoost;
            DispatchedUnitCount = dispatchedUnitCount;
            Message = message ?? string.Empty;
        }

        public bool Success { get; }
        public int WaveNumber { get; }
        public float AggressionBoost { get; }
        public int DispatchedUnitCount { get; }
        public string Message { get; }
    }

    public static class RuntimeGameplayCommandDirector

    {


        private static readonly Dictionary<string, string> _activeUnitVariants = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, Dictionary<string, float>> _activeUnitLoadoutStats =
            new Dictionary<string, Dictionary<string, float>>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, int> _prespawnPoolQuotas = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, int> _prespawnPoolWarmed = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private static readonly object _doctrineLock = new object();
        private static string _activeAiDoctrine = "Standard";
        private static float _aiAggressionMultiplier = 1.0f;
        private static readonly List<AiDeckEntryDiagnosticRow> _injectedAiDeckEntries = new List<AiDeckEntryDiagnosticRow>();
        private static readonly Dictionary<string, string> _activeTacticalStances = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, List<string>> _activeUnitAffixes = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string> _lastUnitCommandKind = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string> _lastUnitCommandProcessingType = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string> _lastUnitCommandTag = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, int> _unitCommandStepCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, int> _factoryProductionCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private static string _lastPlacementGhostEntityId = string.Empty;
        private static readonly object _commandLoopGate = new object();
        private static readonly StatusEffect[] _allStatusEffects = (StatusEffect[])Enum.GetValues(typeof(StatusEffect));
        private static FieldInfo _cachedTickDurationField;

        private static string ResolveEntityIdString(EntityController unit)
        {
            if (unit == null) return string.Empty;
            try
            {
                string id = unit.EntityId;
                if (!string.IsNullOrEmpty(id)) return id;
            }
            catch
            {
            }
            PropertyInfo prop = unit.GetType().GetProperty("EntityId") ?? unit.GetType().GetProperty("entityId");
            if (prop != null)
            {
                return prop.GetValue(unit, null) as string ?? string.Empty;
            }
            FieldInfo field = unit.GetType().GetField("entityId") ?? unit.GetType().GetField("EntityId");
            if (field != null)
            {
                return field.GetValue(unit) as string ?? string.Empty;
            }
            return string.Empty;
        }

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

        /// <summary>
        /// Orders an evasive retreat away from a threat position.
        /// The retreat distance is bounded and validated for finite inputs.
        /// </summary>
        public static RetreatResult TryOrderRetreat(string entityKeyId, float threatX, float threatZ, float distance)
        {
            if (string.IsNullOrWhiteSpace(entityKeyId))
            {
                return new RetreatResult(false, string.Empty, threatX, threatZ, 0f, 0f, distance,
                    "Entity key ID cannot be empty.");
            }

            if (float.IsNaN(threatX) || float.IsInfinity(threatX) ||
                float.IsNaN(threatZ) || float.IsInfinity(threatZ) ||
                float.IsNaN(distance) || float.IsInfinity(distance))
            {
                return new RetreatResult(false, entityKeyId, threatX, threatZ, 0f, 0f, distance,
                    "Retreat threat coordinates and distance must be finite.");
            }

            float retreatDistance = Mathf.Clamp(distance, 3f, 50f);

            try
            {
                ExistingControllers existing = ExistingControllers.Instance;
                if (existing == null)
                {
                    return new RetreatResult(false, entityKeyId, threatX, threatZ, 0f, 0f, retreatDistance,
                        "ExistingControllers instance is not available.");
                }

                EntityController unit = FindEntityByKey(existing, entityKeyId);
                if (unit == null || unit.IsDestroyed || unit.DestroyHasBeenStarted || !unit.IsUnit)
                {
                    return new RetreatResult(false, entityKeyId, threatX, threatZ, 0f, 0f, retreatDistance,
                        "Unit '" + entityKeyId + "' not found or destroyed.");
                }

                Vector3 pos = unit.Position;
                float dx = pos.x - threatX;
                float dz = pos.z - threatZ;
                float len = (float)Math.Sqrt(dx * dx + dz * dz);
                float dirX;
                float dirZ;
                if (len <= 0.0001f)
                {
                    dirX = 0f;
                    dirZ = 1f;
                }
                else
                {
                    dirX = dx / len;
                    dirZ = dz / len;
                }

                float retreatX = pos.x + dirX * retreatDistance;
                float retreatZ = pos.z + dirZ * retreatDistance;

                EntityCommand moveCmd = EntityCommand.Move(new Vector3(retreatX, 0f, retreatZ));
                moveCmd.tag = "rcl-retreat";
                unit.ExecuteCommand(moveCmd, EntityController.CommandProcessingType.ExecuteBeforeCurrentCommand);

                return new RetreatResult(true, entityKeyId, threatX, threatZ, retreatX, retreatZ, retreatDistance,
                    "Retreat command dispatched away from threat.");
            }
            catch (Exception ex)
            {
                return new RetreatResult(false, entityKeyId, threatX, threatZ, 0f, 0f, retreatDistance,
                    "Error executing retreat: " + ex.Message);
            }
        }

        public static UnitCommandResult TryOrderUnitMove(string entityKeyId, float destinationX, float destinationZ)

        {

            if (string.IsNullOrWhiteSpace(entityKeyId))

            {

                return new UnitCommandResult(false, string.Empty, "Move", destinationX, destinationZ,

                    "Entity key ID cannot be empty.");

            }



            if (float.IsNaN(destinationX) || float.IsInfinity(destinationX) ||

                float.IsNaN(destinationZ) || float.IsInfinity(destinationZ))

            {

                return new UnitCommandResult(false, string.Empty, "Move", destinationX, destinationZ,

                    "Move destination coordinates must be finite.");

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

                if (attacker.IsEnemy == null || !attacker.IsEnemy(target))
                {
                    return new UnitCommandResult(false, entityKeyId, "AttackMoveOnTarget", 0f, 0f,
                        "Target entity '" + targetEntityKeyId + "' is not hostile to attacker '" + entityKeyId + "'.");
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

                if (!caster.HasActiveSkill)

                {

                    return new ActiveSkillOrderResult(false, entityKeyId, "None", 0f, 0f,

                        "Entity '" + entityKeyId + "' does not possess an active skill.");

                }



                EntityCommand skillCmd;

                string mode;

                float tx = 0f;

                float tz = 0f;



                if (caster.SkillInfo != null && caster.SkillInfo.targetOrigin == TargetOrigin.Self)
                {
                    skillCmd = EntityCommand.ActiveSkillWithoutTarget();
                    mode = "Self";
                    tx = caster.Position.x;
                    tz = caster.Position.z;
                }
                else if (!string.IsNullOrWhiteSpace(targetEntityKeyId))

                {

                    EntityController target = FindEntityByKey(existing, targetEntityKeyId);

                    if (target == null)

                    {

                        return new ActiveSkillOrderResult(false, entityKeyId, "Target", 0f, 0f,

                            "Skill target entity '" + targetEntityKeyId + "' not found or destroyed.");

                    }



                    Vector3 targetPosition = target.Position;
                    if (caster.SkillInfo != null && caster.SkillInfo.targetOrigin == TargetOrigin.ChosenLocation)
                    {
                        skillCmd = EntityCommand.ActiveSkillAtPosition(targetPosition);
                        mode = "Position";
                    }
                    else
                    {
                        skillCmd = EntityCommand.ActiveSkillOnTarget(target);
                        mode = "Target";
                    }

                    tx = targetPosition.x;

                    tz = targetPosition.z;

                }

                else if (targetX.HasValue || targetZ.HasValue)

                {

                    if (!targetX.HasValue || !targetZ.HasValue)

                    {

                        return new ActiveSkillOrderResult(false, entityKeyId, "Position", 0f, 0f,

                            "Both targetX and targetZ coordinates must be provided for a positioned skill.");

                    }

                    if (float.IsNaN(targetX.Value) || float.IsInfinity(targetX.Value) ||

                        float.IsNaN(targetZ.Value) || float.IsInfinity(targetZ.Value))

                    {

                        return new ActiveSkillOrderResult(false, entityKeyId, "Position", 0f, 0f,

                            "Active skill target coordinates must be finite.");

                    }



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

                    entity.Heal(amount, entity, true, true);

                }

                else if (string.Equals(type, "RepairArmor", StringComparison.OrdinalIgnoreCase))

                {

                    entity.RepairArmor(amount, entity, true);

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



        /// <summary>

        /// Executes a bounded tactical pulse (Shockwave, Rally, or Repair) around a living unit.

        /// </summary>

        public static TacticalPulseResult TryExecuteTacticalPulse(

            string entityKeyId,

            string pulseType,

            float radius,

            float magnitude,

            float durationSeconds,

            int maxTargets)

        {

            if (string.IsNullOrWhiteSpace(entityKeyId))

            {

                return new TacticalPulseResult(false, string.Empty, pulseType, 0, 0f, 0f, 0,

                    "Entity key ID cannot be empty.");

            }



            if (float.IsNaN(radius) || float.IsInfinity(radius) ||

                float.IsNaN(magnitude) || float.IsInfinity(magnitude) ||

                float.IsNaN(durationSeconds) || float.IsInfinity(durationSeconds))

            {

                return new TacticalPulseResult(false, entityKeyId, pulseType, 0, 0f, 0f, 0,

                    "Tactical pulse radius, magnitude, and duration must be finite.");

            }


            try

            {

                ExistingControllers existing = ExistingControllers.Instance;

                if (existing == null)

                {

                    return new TacticalPulseResult(false, entityKeyId, pulseType, 0, 0f, 0f, 0,

                        "ExistingControllers instance is not available.");

                }


                EntityController unit = FindEntityByKey(existing, entityKeyId.Trim());

                if (unit == null)

                {

                    return new TacticalPulseResult(false, entityKeyId, pulseType, 0, 0f, 0f, 0,

                        "Entity '" + entityKeyId + "' not found or destroyed.");

                }


                string normalizedPulseType;

                int affectedEntityCount;

                float damageApplied;

                float healingApplied;

                int statusEffectApplicationCount;

                string message;

                bool success = RuntimeUnitMechanicsDirector.TryExecuteTacticalPulse(

                    unit, pulseType, radius, magnitude, durationSeconds, maxTargets,

                    out normalizedPulseType, out affectedEntityCount, out damageApplied,

                    out healingApplied, out statusEffectApplicationCount, out message);

                return new TacticalPulseResult(success, entityKeyId, normalizedPulseType, affectedEntityCount,

                    damageApplied, healingApplied, statusEffectApplicationCount, message);

            }

            catch (Exception ex)

            {

                return new TacticalPulseResult(false, entityKeyId, pulseType, 0, 0f, 0f, 0,

                    "Error executing tactical pulse: " + ex.Message);

            }

        }


        /// <summary>
        /// Applies a bounded director sweep to nearby hostile combat units.
        /// Mark applies Marked; Suppress applies SlowedDown.
        /// </summary>
        public static ThreatSweepResult TryExecuteThreatSweep(
            string sourceEntityKeyId,
            string sweepType,
            float radius,
            float durationSeconds,
            int maxTargets)
        {
            IReadOnlyList<string> emptyTargets = new List<string>().AsReadOnly();
            if (string.IsNullOrWhiteSpace(sourceEntityKeyId))
            {
                return new ThreatSweepResult(false, string.Empty, sweepType, string.Empty,
                    radius, durationSeconds, 0, 0, emptyTargets,
                    "Source entity key ID cannot be empty.");
            }

            string normalizedSweep = string.IsNullOrWhiteSpace(sweepType) ? string.Empty : sweepType.Trim();
            string statusEffectName;
            if (string.Equals(normalizedSweep, "Mark", StringComparison.OrdinalIgnoreCase))
            {
                statusEffectName = "Marked";
            }
            else if (string.Equals(normalizedSweep, "Suppress", StringComparison.OrdinalIgnoreCase))
            {
                statusEffectName = "SlowedDown";
            }
            else
            {
                return new ThreatSweepResult(false, sourceEntityKeyId, normalizedSweep, string.Empty,
                    radius, durationSeconds, 0, 0, emptyTargets,
                    "Unknown sweep type '" + sweepType + "'. Allowed: Mark, Suppress.");
            }

            if (float.IsNaN(radius) || float.IsInfinity(radius) ||
                float.IsNaN(durationSeconds) || float.IsInfinity(durationSeconds))
            {
                return new ThreatSweepResult(false, sourceEntityKeyId, normalizedSweep, statusEffectName,
                    radius, durationSeconds, 0, 0, emptyTargets,
                    "Threat sweep radius and duration must be finite.");
            }

            float clampedRadius = Mathf.Clamp(radius, 3.0f, 50.0f);
            float clampedDuration = Mathf.Clamp(durationSeconds, 0.5f, 30.0f);
            int clampedMaxTargets = Mathf.Clamp(maxTargets, 1, 16);

            try
            {
                ExistingControllers existing = ExistingControllers.Instance;
                if (existing == null)
                {
                    return new ThreatSweepResult(false, sourceEntityKeyId, normalizedSweep, statusEffectName,
                        clampedRadius, clampedDuration, 0, 0, emptyTargets,
                        "ExistingControllers instance is not available.");
                }

                EntityController source = FindEntityByKey(existing, sourceEntityKeyId.Trim());
                if (source == null || source.gameObject == null)
                {
                    return new ThreatSweepResult(false, sourceEntityKeyId, normalizedSweep, statusEffectName,
                        clampedRadius, clampedDuration, 0, 0, emptyTargets,
                        "Source entity '" + sourceEntityKeyId + "' not found or destroyed.");
                }

                if (!Enum.TryParse<StatusEffect>(statusEffectName, true, out StatusEffect effect))
                {
                    return new ThreatSweepResult(false, sourceEntityKeyId, normalizedSweep, statusEffectName,
                        clampedRadius, clampedDuration, 0, 0, emptyTargets,
                        "Status effect '" + statusEffectName + "' is unavailable in this game build.");
                }

                Vector3 sourcePosition = source.Position;
                float radiusSquared = clampedRadius * clampedRadius;
                var candidates = new List<EntityController>();
                var seen = new HashSet<EntityController>();
                AddThreatSweepCandidates(existing.PlayerUnits(), source, sourcePosition, radiusSquared, seen, candidates);
                AddThreatSweepCandidates(existing.AiUnits(), source, sourcePosition, radiusSquared, seen, candidates);
                AddThreatSweepCandidates(existing.Harvesters, source, sourcePosition, radiusSquared, seen, candidates);
                candidates.Sort(delegate(EntityController left, EntityController right)
                {
                    float leftDistance = (left.Position - sourcePosition).sqrMagnitude;
                    float rightDistance = (right.Position - sourcePosition).sqrMagnitude;
                    int distanceComparison = leftDistance.CompareTo(rightDistance);
                    return distanceComparison != 0
                        ? distanceComparison
                        : left.UniqueEntityId.CompareTo(right.UniqueEntityId);
                });

                int selectedCount = Math.Min(clampedMaxTargets, candidates.Count);
                int affectedCount = 0;
                var targetIds = new List<string>(selectedCount);
                for (int i = 0; i < selectedCount; i++)
                {
                    EntityController target = candidates[i];
                    try
                    {
                        target.SetActiveStatusEffect(effect, SetStatusEffect.DurationType.Seconds, clampedDuration);
                        string targetId = ResolveEntityIdString(target);
                        if (string.IsNullOrWhiteSpace(targetId))
                        {
                            targetId = target.name;
                        }

                        targetIds.Add(targetId ?? string.Empty);
                        affectedCount++;
                    }
                    catch
                    {
                        // One incompatible target must not abort the bounded sweep.
                    }
                }

                int omittedCount = Math.Max(0, candidates.Count - selectedCount);
                bool success = affectedCount > 0;
                string message = success
                    ? "Director " + normalizedSweep + " sweep applied " + statusEffectName +
                      " to " + affectedCount + " hostile unit(s)."
                    : "Director " + normalizedSweep + " sweep found no eligible hostile units.";
                return new ThreatSweepResult(success, sourceEntityKeyId, normalizedSweep, statusEffectName,
                    clampedRadius, clampedDuration, affectedCount, omittedCount,
                    targetIds.AsReadOnly(), message);
            }
            catch (Exception ex)
            {
                return new ThreatSweepResult(false, sourceEntityKeyId, normalizedSweep, statusEffectName,
                    clampedRadius, clampedDuration, 0, 0, emptyTargets,
                    "Error executing director threat sweep: " + ex.Message);
            }
        }

        /// <summary>
        /// Coordinates a bounded attack against one hostile target.
        /// Friendly, non-combat, missing, and destroyed attackers are rejected fail-closed.
        /// </summary>
        public static CoordinatedAssaultResult TryExecuteCoordinatedAssault(
            string targetEntityKeyId,
            IReadOnlyList<string> attackerEntityKeyIds,
            int maxAttackers)
        {
            IReadOnlyList<string> emptyAttackers = new List<string>().AsReadOnly();
            int requestedCount = attackerEntityKeyIds != null ? attackerEntityKeyIds.Count : 0;
            int clampedMaxAttackers = Mathf.Clamp(maxAttackers, 1, 16);
            string normalizedTargetKey = string.IsNullOrWhiteSpace(targetEntityKeyId)
                ? string.Empty
                : targetEntityKeyId.Trim();

            if (string.IsNullOrEmpty(normalizedTargetKey))
            {
                return new CoordinatedAssaultResult(false, string.Empty, requestedCount, 0, requestedCount,
                    emptyAttackers, 0f, 0f, "Target entity key ID cannot be empty.");
            }

            if (requestedCount == 0)
            {
                return new CoordinatedAssaultResult(false, normalizedTargetKey, 0, 0, 0,
                    emptyAttackers, 0f, 0f, "At least one attacker entity key ID is required.");
            }

            try
            {
                ExistingControllers existing = ExistingControllers.Instance;
                if (existing == null)
                {
                    return new CoordinatedAssaultResult(false, normalizedTargetKey, requestedCount, 0, requestedCount,
                        emptyAttackers, 0f, 0f, "ExistingControllers instance is not available.");
                }

                EntityController target = FindEntityByKey(existing, normalizedTargetKey);
                if (target == null || target.gameObject == null || target.IsDestroyed || target.DestroyHasBeenStarted)
                {
                    return new CoordinatedAssaultResult(false, normalizedTargetKey, requestedCount, 0, requestedCount,
                        emptyAttackers, 0f, 0f, "Target entity '" + targetEntityKeyId + "' not found or destroyed.");
                }

                Vector3 targetPosition = target.Position;
                int consideredCount = Math.Min(requestedCount, clampedMaxAttackers);
                var dispatched = new List<string>(consideredCount);
                for (int i = 0; i < consideredCount; i++)
                {
                    string attackerKey = attackerEntityKeyIds[i];
                    if (string.IsNullOrWhiteSpace(attackerKey))
                    {
                        continue;
                    }

                    try
                    {
                        EntityController attacker = FindEntityByKey(existing, attackerKey.Trim());
                        if (attacker == null || attacker.gameObject == null || attacker.IsDestroyed ||
                            attacker.DestroyHasBeenStarted || !attacker.IsUnit || !attacker.IsCombatUnit ||
                            ReferenceEquals(attacker, target) || attacker.IsEnemy == null || !attacker.IsEnemy(target))
                        {
                            continue;
                        }

                        EntityCommand attackCommand = EntityCommand.AttackMoveOnTarget(
                            target, EntityCommand.Role.ThisCommandWantsPathToBeCleared);
                        attackCommand.tag = "rcl-coordinated-assault";
                        attacker.ExecuteCommand(
                            attackCommand,
                            EntityController.CommandProcessingType.ExecuteBeforeCurrentCommand);

                        string dispatchedKey = ResolveEntityIdString(attacker);
                        if (string.IsNullOrWhiteSpace(dispatchedKey))
                        {
                            dispatchedKey = attacker.name;
                        }

                        dispatched.Add(string.IsNullOrWhiteSpace(dispatchedKey)
                            ? attackerKey.Trim()
                            : dispatchedKey);
                    }
                    catch
                    {
                        // One incompatible attacker must not abort the bounded assault.
                    }
                }

                int omittedCount = Math.Max(0, requestedCount - dispatched.Count);
                bool success = dispatched.Count > 0;
                string message = success
                    ? "Coordinated assault dispatched " + dispatched.Count + " attacker(s) against '" +
                      normalizedTargetKey + "'."
                    : "Coordinated assault found no eligible hostile combat attackers.";
                return new CoordinatedAssaultResult(success, normalizedTargetKey, requestedCount,
                    dispatched.Count, omittedCount, dispatched.AsReadOnly(), targetPosition.x, targetPosition.z, message);
            }
            catch (Exception ex)
            {
                return new CoordinatedAssaultResult(false, normalizedTargetKey, requestedCount, 0, requestedCount,
                    emptyAttackers, 0f, 0f, "Error executing coordinated assault: " + ex.Message);
            }
        }

        private static void AddThreatSweepCandidates(
            IEnumerable<EntityController> values,
            EntityController source,
            Vector3 sourcePosition,
            float radiusSquared,
            HashSet<EntityController> seen,
            List<EntityController> candidates)
        {
            if (values == null)
            {
                return;
            }

            foreach (EntityController candidate in values)
            {
                if (candidate == null || ReferenceEquals(candidate, source) || candidate.gameObject == null ||
                    candidate.IsDestroyed || candidate.DestroyHasBeenStarted || !candidate.IsUnit ||
                    !candidate.IsCombatUnit || source.IsEnemy == null || !source.IsEnemy(candidate) || !seen.Add(candidate))
                {
                    continue;
                }

                Vector3 delta = candidate.Position - sourcePosition;
                if (delta.sqrMagnitude <= radiusSquared)
                {
                    candidates.Add(candidate);
                }
                else
                {
                    seen.Remove(candidate);
                }
            }
        }


        private static EntityController FindEntityByKey(ExistingControllers existing, string key)
        {
            return RuntimeEntityKeyResolver.FindUniqueEntityByKey(existing, key);
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

            if (string.IsNullOrWhiteSpace(entityKeyId))

            {

                return new UnitCommandResult(false, string.Empty, "AttackMove", targetX, targetZ,

                    "Entity key ID cannot be empty.");

            }



            if (float.IsNaN(targetX) || float.IsInfinity(targetX) ||

                float.IsNaN(targetZ) || float.IsInfinity(targetZ))

            {

                return new UnitCommandResult(false, string.Empty, "AttackMove", targetX, targetZ,

                    "AttackMove target coordinates must be finite.");

            }



            try

            {

                ExistingControllers existing = ExistingControllers.Instance;

                if (existing == null)

                {

                    return new UnitCommandResult(false, entityKeyId, "AttackMove", targetX, targetZ,

                        "ExistingControllers instance is not available.");

                }



                EntityController entity = FindEntityByKey(existing, entityKeyId.Trim());

                if (entity == null || entity.gameObject == null)

                {

                    return new UnitCommandResult(false, entityKeyId, "AttackMove", targetX, targetZ,

                        "Entity '" + entityKeyId + "' not found or destroyed.");

                }



                PlayerUnitController puc = ResolvePlayerUnitController();

                if (puc != null)

                {

                    InternalSelect(puc, entity.gameObject);

                }



                PlayerUnitController.AttackMove_Static(new Vector3(targetX, 0f, targetZ));

                return new UnitCommandResult(true, entityKeyId, "AttackMove", targetX, targetZ,

                    "Order AttackMove to (" + targetX + ", " + targetZ + ") dispatched.");

            }

            catch (Exception ex)

            {

                return new UnitCommandResult(false, entityKeyId, "AttackMove", targetX, targetZ,

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

                float clamped;
                if (!GuiControlPolicy.TryValidateTimeScale(timeScale, out clamped))
                {
                    return new GameSpeedCommandResult(false, prev, prev,
                        "Game time scale must be finite.");
                }

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

            if (float.IsNaN(targetX) || float.IsInfinity(targetX) ||
                float.IsNaN(targetZ) || float.IsInfinity(targetZ))
            {
                return new EntitySpawnResult(false, string.Empty, entityId ?? string.Empty, side ?? string.Empty,
                    targetX, 0f, targetZ, 0f, "Spawn coordinates must be finite.");
            }

            if (string.IsNullOrWhiteSpace(entityId))

            {

                return new EntitySpawnResult(false, string.Empty, string.Empty, string.Empty, targetX, 0f, targetZ, 0f, "Entity ID cannot be empty.");

            }



            try

            {

                string tag;
                if (string.Equals(side, Tags.Ai, StringComparison.OrdinalIgnoreCase))
                {
                    tag = Tags.Ai;
                }
                else if (string.Equals(side, Tags.Player, StringComparison.OrdinalIgnoreCase))
                {
                    tag = Tags.Player;
                }
                else
                {
                    return new EntitySpawnResult(false, string.Empty, entityId.Trim(), side ?? string.Empty,
                        targetX, 0f, targetZ, 0f, "Side must be 'AI' or 'Player'.");
                }

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



                string keyId = entity.UniqueEntityId > 0
                    ? entity.UniqueEntityId.ToString()
                    : entity.EntityId ?? entity.name ?? entityId;

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

            float validatedX;
            float validatedZ;
            if (!GuiControlPolicy.TryValidateCameraFloat(targetX, out validatedX) ||
                !GuiControlPolicy.TryValidateCameraFloat(targetZ, out validatedZ))
            {
                return new CameraCommandResult(false, "MoveCamera", targetX, targetZ, 0f,
                    "Camera target coordinates must be finite.");
            }
            targetX = validatedX;
            targetZ = validatedZ;

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

            float validatedFov;
            if (!GuiControlPolicy.TryValidateCameraFloat(fieldOfView, out validatedFov))
            {
                return new CameraCommandResult(false, "SetCameraZoom", 0f, 0f, fieldOfView,
                    "Camera field of view must be finite.");
            }
            fieldOfView = validatedFov;

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

            if (float.IsNaN(sliderValue) || float.IsInfinity(sliderValue))
            {
                return new AudioCommandResult(false, channel ?? string.Empty, sliderValue,
                    "Audio slider value must be finite.");
            }

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

                    null, new[] { typeof(UnityEngine.Audio.AudioMixer), typeof(float) }, null);



                if (m != null)

                {

                    m.Invoke(null, new object[] { mixer, clamped });

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





        /// <summary>
        /// Grants combat experience (XP) to a unit, advancing veterancy ranks when thresholds are met.
        /// </summary>
        public static VeterancyCommandResult TryGrantEntityCombatXp(string entityKeyId, int xpAmount)
        {
            if (string.IsNullOrWhiteSpace(entityKeyId) || xpAmount <= 0)
            {
                return new VeterancyCommandResult(false, entityKeyId, 0, 0,
                    "Invalid entity key or XP amount must be positive.");
            }

            try
            {
                ExistingControllers existing = ExistingControllers.Instance;
                if (existing == null)
                {
                    return new VeterancyCommandResult(false, entityKeyId, 0, 0,
                        "ExistingControllers instance is not available.");
                }

                EntityController unit = FindEntityByKey(existing, entityKeyId.Trim());
                if (unit == null || unit.gameObject == null)
                {
                    return new VeterancyCommandResult(false, entityKeyId, 0, 0,
                        "Entity '" + entityKeyId + "' not found or destroyed.");
                }

                int resultingXp, resultingRank;
                bool success = RuntimeUnitMechanicsDirector.TryGrantCombatXp(unit, xpAmount, out resultingXp, out resultingRank);
                if (success)
                {
                    return new VeterancyCommandResult(true, entityKeyId, resultingRank, resultingXp,
                        "Granted " + xpAmount + " combat XP to unit '" + entityKeyId + "' (Rank: " + resultingRank + ", XP: " + resultingXp + ").");
                }

                return new VeterancyCommandResult(false, entityKeyId, 0, 0,
                    "Failed granting combat XP to unit '" + entityKeyId + "'.");
            }
            catch (Exception ex)
            {
                return new VeterancyCommandResult(false, entityKeyId, 0, 0,
                    "Error granting combat XP: " + ex.Message);
            }
        }

        /// <summary>
        /// Sets a unit's veterancy rank level directly, invoking rankup bonuses.
        /// </summary>
        public static VeterancyCommandResult TrySetEntityVeterancyLevel(string entityKeyId, int targetLevel)
        {
            if (string.IsNullOrWhiteSpace(entityKeyId) || targetLevel < 0)
            {
                return new VeterancyCommandResult(false, entityKeyId, 0, 0,
                    "Invalid entity key or target level must be non-negative.");
            }

            try
            {
                ExistingControllers existing = ExistingControllers.Instance;
                if (existing == null)
                {
                    return new VeterancyCommandResult(false, entityKeyId, 0, 0,
                        "ExistingControllers instance is not available.");
                }

                EntityController unit = FindEntityByKey(existing, entityKeyId.Trim());
                if (unit == null || unit.gameObject == null)
                {
                    return new VeterancyCommandResult(false, entityKeyId, 0, 0,
                        "Entity '" + entityKeyId + "' not found or destroyed.");
                }

                int resultingRank;
                bool success = RuntimeUnitMechanicsDirector.TrySetUnitVeterancyRank(unit, targetLevel, out resultingRank);
                int currentXp = RuntimeUnitMechanicsDirector.GetCombatXpForEntity(unit);
                if (success)
                {
                    return new VeterancyCommandResult(true, entityKeyId, resultingRank, currentXp,
                        "Set veterancy level of unit '" + entityKeyId + "' to Rank " + resultingRank + ".");
                }

                return new VeterancyCommandResult(false, entityKeyId, 0, 0,
                    "Failed setting veterancy level of unit '" + entityKeyId + "'.");
            }
            catch (Exception ex)
            {
                return new VeterancyCommandResult(false, entityKeyId, 0, 0,
                    "Error setting veterancy level: " + ex.Message);
            }
        }

        /// <summary>
        /// Dispatches a dynamic command aura pulse around a captain entity, linking squad subordinates and applying tactical aura buffs.
        /// </summary>
        public static CommandAuraResult TryDispatchCommandAuraPulse(string captainKeyId, string auraType, float radius, float durationSeconds)
        {
            if (float.IsNaN(radius) || float.IsInfinity(radius) ||
                float.IsNaN(durationSeconds) || float.IsInfinity(durationSeconds))
            {
                return new CommandAuraResult(false, captainKeyId ?? string.Empty, auraType ?? string.Empty, 0,
                    radius, durationSeconds, "Command aura radius and duration must be finite.");
            }

            if (string.IsNullOrWhiteSpace(captainKeyId))
            {
                return new CommandAuraResult(false, captainKeyId, auraType, 0, radius, durationSeconds,
                    "Captain entity key must not be empty.");
            }

            try
            {
                ExistingControllers existing = ExistingControllers.Instance;
                if (existing == null)
                {
                    return new CommandAuraResult(false, captainKeyId, auraType, 0, radius, durationSeconds,
                        "ExistingControllers instance is not available.");
                }

                EntityController captain = FindEntityByKey(existing, captainKeyId.Trim());
                if (captain == null || captain.gameObject == null)
                {
                    return new CommandAuraResult(false, captainKeyId, auraType, 0, radius, durationSeconds,
                        "Captain entity '" + captainKeyId + "' not found or destroyed.");
                }

                int linkedCount;
                bool success = RuntimeUnitMechanicsDirector.TryDispatchDynamicCommandAura(
                    captain, auraType, radius, durationSeconds, out linkedCount);

                float clampedRadius = Mathf.Clamp(radius, 3.0f, 50.0f);
                float clampedDuration = Mathf.Clamp(durationSeconds, 1.0f, 60.0f);
                string aura = string.IsNullOrWhiteSpace(auraType) ? "Inspire" : auraType.Trim();

                if (success)
                {
                    return new CommandAuraResult(true, captainKeyId, aura, linkedCount, clampedRadius, clampedDuration,
                        "Dispatched " + aura + " command aura pulse from captain '" + captainKeyId + "' linking " + linkedCount + " squad units.");
                }

                return new CommandAuraResult(false, captainKeyId, aura, 0, clampedRadius, clampedDuration,
                    "Failed dispatching command aura from captain '" + captainKeyId + "'.");
            }
            catch (Exception ex)
            {
                return new CommandAuraResult(false, captainKeyId, auraType, 0, radius, durationSeconds,
                    "Error dispatching command aura: " + ex.Message);
            }
        }

        /// <summary>
        /// Clears command aura links and subordinate captain bonds for a captain entity.
        /// </summary>
        public static CommandAuraResult TryClearCommandAuraLinks(string captainKeyId)
        {
            if (string.IsNullOrWhiteSpace(captainKeyId))
            {
                return new CommandAuraResult(false, captainKeyId, "", 0, 0f, 0f,
                    "Captain entity key must not be empty.");
            }

            try
            {
                ExistingControllers existing = ExistingControllers.Instance;
                if (existing == null)
                {
                    return new CommandAuraResult(false, captainKeyId, "", 0, 0f, 0f,
                        "ExistingControllers instance is not available.");
                }

                EntityController captain = FindEntityByKey(existing, captainKeyId.Trim());
                if (captain == null || captain.gameObject == null)
                {
                    return new CommandAuraResult(false, captainKeyId, "", 0, 0f, 0f,
                        "Captain entity '" + captainKeyId + "' not found or destroyed.");
                }

                int clearedCount;
                bool success = RuntimeUnitMechanicsDirector.TryClearDynamicCommandAura(captain, out clearedCount);
                if (success)
                {
                    return new CommandAuraResult(true, captainKeyId, "", clearedCount, 0f, 0f,
                        "Cleared command aura links for captain '" + captainKeyId + "' (" + clearedCount + " followers unlinked).");
                }

                return new CommandAuraResult(false, captainKeyId, "", 0, 0f, 0f,
                    "Failed clearing command aura links for captain '" + captainKeyId + "'.");
            }
            catch (Exception ex)
            {
                return new CommandAuraResult(false, captainKeyId, "", 0, 0f, 0f,
                    "Error clearing command aura links: " + ex.Message);
            }
        }

        /// <summary>
        /// Spawns a battlefield environmental hazard zone (Firestorm, AcidPool, EmpField, CryoMist, NaniteField).
        /// Enforces trigger collider and amortized tick rate (>= 0.5s).
        /// </summary>
        public static HazardCommandResult TrySpawnHazardZone(
            string hazardType,
            float x,
            float z,
            float radius,
            float durationSeconds,
            float tickDamage)
        {
            string sanitizedType = string.IsNullOrWhiteSpace(hazardType) ? "Firestorm" : hazardType.Trim();
            if (float.IsNaN(x) || float.IsInfinity(x) ||
                float.IsNaN(z) || float.IsInfinity(z) ||
                float.IsNaN(radius) || float.IsInfinity(radius) ||
                float.IsNaN(durationSeconds) || float.IsInfinity(durationSeconds) ||
                float.IsNaN(tickDamage) || float.IsInfinity(tickDamage) ||
                radius <= 0f || durationSeconds <= 0f || tickDamage < 0f)
            {
                return new HazardCommandResult(
                    false,
                    "",
                    sanitizedType,
                    x,
                    z,
                    radius,
                    durationSeconds,
                    tickDamage,
                    "Hazard coordinates, radius, duration, and damage must be finite; radius and duration must be positive and damage cannot be negative.");
            }

            try
            {
                string zoneId;
                bool success = RuntimeHazardManager.TrySpawnHazardZone(
                    hazardType, x, z, radius, durationSeconds, tickDamage, out zoneId);

                float clampedRadius = Mathf.Clamp(radius, RuntimeHazardManager.MinimumRadius, RuntimeHazardManager.MaximumRadius);
                float clampedDuration = Mathf.Clamp(durationSeconds, RuntimeHazardManager.MinimumDuration, RuntimeHazardManager.MaximumDuration);
                float clampedDamage = Mathf.Max(0f, tickDamage);

                if (success)
                {
                    return new HazardCommandResult(
                        true,
                        zoneId,
                        sanitizedType,
                        x,
                        z,
                        clampedRadius,
                        clampedDuration,
                        clampedDamage,
                        "Spawned hazard zone '" + zoneId + "' (" + sanitizedType + ") at (" + x + ", " + z + ") with radius " + clampedRadius + "m.");
                }

                return new HazardCommandResult(
                    false,
                    "",
                    sanitizedType,
                    x,
                    z,
                    clampedRadius,
                    clampedDuration,
                    clampedDamage,
                    "Failed spawning hazard zone.");
            }
            catch (Exception ex)
            {
                return new HazardCommandResult(
                    false,
                    "",
                    hazardType ?? "Firestorm",
                    x,
                    z,
                    radius,
                    durationSeconds,
                    tickDamage,
                    "Error spawning hazard zone: " + ex.Message);
            }
        }

        /// <summary>
        /// Clears all active battlefield environmental hazard zones and their trigger colliders.
        /// </summary>
        public static HazardCommandResult TryClearHazardZones()
        {
            try
            {
                int cleared = RuntimeHazardManager.ClearAll();
                return new HazardCommandResult(
                    true,
                    "",
                    "All",
                    0f,
                    0f,
                    0f,
                    0f,
                    0f,
                    "Cleared " + cleared + " active hazard zones.");
            }
            catch (Exception ex)
            {
                return new HazardCommandResult(
                    false,
                    "",
                    "All",
                    0f,
                    0f,
                    0f,
                    0f,
                    0f,
                    "Error clearing hazard zones: " + ex.Message);
            }
        }

        public static string GetActiveUnitVariant(string keyId)
        {
            if (string.IsNullOrWhiteSpace(keyId)) return string.Empty;
            lock (_activeUnitVariants)
            {
                string variant;
                return _activeUnitVariants.TryGetValue(keyId.Trim(), out variant) ? variant : string.Empty;
            }
        }

        public static IReadOnlyList<UnitLoadoutDiagnosticRow> GetActiveUnitLoadoutRows(
            int maxRows, out int omittedCount, out int totalCount)
        {
            var rows = new List<UnitLoadoutDiagnosticRow>();
            totalCount = 0;
            omittedCount = 0;

            try
            {
                ExistingControllers existing = ExistingControllers.Instance;
                if (existing != null)
                {
                    var units = new List<EntityController>();
                    if (existing.PlayerUnits() != null) units.AddRange(existing.PlayerUnits());
                    if (existing.AiUnits() != null) units.AddRange(existing.AiUnits());

                    for (int i = 0; i < units.Count; i++)
                    {
                        EntityController unit = units[i];
                        if (unit == null || unit.IsDestroyed) continue;

                        totalCount++;
                        if (rows.Count < maxRows)
                        {
                            string keyId = unit.UniqueEntityId.ToString();
                            string variantId;
                            lock (_activeUnitVariants)
                            {
                                _activeUnitVariants.TryGetValue(keyId, out variantId);
                            }
                            string loadoutName = string.IsNullOrEmpty(variantId) ? "Standard" : variantId;

                            rows.Add(new UnitLoadoutDiagnosticRow(
                                keyId,
                                ResolveEntityIdString(unit),
                                variantId ?? string.Empty,
                                loadoutName,
                                unit.IsControlledByPlayer,
                                unit.MoveSpeed,
                                unit.WeaponRange,
                                unit.Damage1,
                                unit.Attack1Cooldown));
                        }
                    }
                }
            }
            catch { }

            omittedCount = Math.Max(0, totalCount - rows.Count);
            return rows.AsReadOnly();
        }

        public static IReadOnlyList<PrespawnPoolDiagnosticRow> GetPrespawnPoolRows(
            int maxRows, out int omittedCount, out int totalCount)
        {
            var rows = new List<PrespawnPoolDiagnosticRow>();
            totalCount = 0;
            omittedCount = 0;

            try
            {
                lock (_prespawnPoolQuotas)
                {
                    foreach (KeyValuePair<string, int> kv in _prespawnPoolQuotas)
                    {
                        totalCount++;
                        if (rows.Count < maxRows)
                        {
                            int warmed = 0;
                            _prespawnPoolWarmed.TryGetValue(kv.Key, out warmed);
                            bool isBuilding = kv.Key.IndexOf("factory", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                              kv.Key.IndexOf("building", StringComparison.OrdinalIgnoreCase) >= 0;
                            rows.Add(new PrespawnPoolDiagnosticRow(kv.Key, kv.Value, warmed, isBuilding));
                        }
                    }
                }

                Type entityFactory = Type.GetType("EntityFactory, Assembly-CSharp");
                if (entityFactory != null)
                {
                    FieldInfo toPrespawnField = entityFactory.GetField("toPrespawn", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                    FieldInfo prespawnedField = entityFactory.GetField("prespawned", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);

                    System.Collections.IDictionary toPrespawn = toPrespawnField?.GetValue(null) as System.Collections.IDictionary;
                    System.Collections.IDictionary prespawned = prespawnedField?.GetValue(null) as System.Collections.IDictionary;

                    if (toPrespawn != null)
                    {
                        foreach (System.Collections.DictionaryEntry de in toPrespawn)
                        {
                            string entityId = de.Key as string;
                            if (string.IsNullOrEmpty(entityId)) continue;

                            bool alreadyPresent = false;
                            for (int r = 0; r < rows.Count; r++)
                            {
                                if (string.Equals(rows[r].EntityId, entityId, StringComparison.OrdinalIgnoreCase))
                                {
                                    alreadyPresent = true;
                                    break;
                                }
                            }

                            if (!alreadyPresent)
                            {
                                totalCount++;
                                if (rows.Count < maxRows)
                                {
                                    int quota = 0;
                                    try { quota = Convert.ToInt32(de.Value); } catch { }
                                    int warmed = 0;
                                    if (prespawned != null && prespawned.Contains(entityId))
                                    {
                                        System.Collections.ICollection stack = prespawned[entityId] as System.Collections.ICollection;
                                        warmed = stack != null ? stack.Count : 0;
                                    }
                                    bool isBuilding = entityId.IndexOf("factory", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                      entityId.IndexOf("building", StringComparison.OrdinalIgnoreCase) >= 0;
                                    rows.Add(new PrespawnPoolDiagnosticRow(entityId, quota, warmed, isBuilding));
                                }
                            }
                        }
                    }
                }
            }
            catch { }

            omittedCount = Math.Max(0, totalCount - rows.Count);
            return rows.AsReadOnly();
        }

        public static string GetActiveAiDoctrine()
        {
            lock (_doctrineLock)
            {
                return _activeAiDoctrine;
            }
        }

        public static float GetAiAggressionMultiplier()
        {
            lock (_doctrineLock)
            {
                return _aiAggressionMultiplier;
            }
        }

        public static IReadOnlyList<AiDeckEntryDiagnosticRow> GetAiDeckEntries(
            int maxEntries, out int omittedCount, out int totalCount, out int profileCount)
        {
            var rows = new List<AiDeckEntryDiagnosticRow>();
            totalCount = 0;
            omittedCount = 0;
            profileCount = 1;

            try
            {
                lock (_injectedAiDeckEntries)
                {
                    for (int i = 0; i < _injectedAiDeckEntries.Count; i++)
                    {
                        totalCount++;
                        if (rows.Count < maxEntries)
                        {
                            rows.Add(_injectedAiDeckEntries[i]);
                        }
                    }
                }

                object manager = ResolveAiDirectorManager();
                if (manager != null)
                {
                    Type mgrType = manager.GetType();
                    FieldInfo activeBField = mgrType.GetField("activeBehaviour", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                        ?? mgrType.GetField("_activeBehaviour", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    object activeBehaviour = activeBField?.GetValue(manager);
                    if (activeBehaviour != null)
                    {
                        FieldInfo aiBasesField = activeBehaviour.GetType().GetField("aiBases", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        System.Collections.IList bases = aiBasesField?.GetValue(activeBehaviour) as System.Collections.IList;
                        if (bases != null)
                        {
                            profileCount = Math.Max(1, bases.Count);
                            for (int b = 0; b < bases.Count; b++)
                            {
                                object aiBase = bases[b];
                                if (aiBase == null) continue;

                                FieldInfo deckField = aiBase.GetType().GetField("entitiesInAiDeck", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                                System.Collections.IList deck = deckField?.GetValue(aiBase) as System.Collections.IList;
                                if (deck != null)
                                {
                                    for (int d = 0; d < deck.Count; d++)
                                    {
                                        object entry = deck[d];
                                        if (entry == null) continue;

                                        Type entryType = entry.GetType();
                                        string uId = entryType.GetField("entityId", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(entry) as string;
                                        int atStart = 0;
                                        int maxL = 0;
                                        int curL = 0;

                                        FieldInfo casF = entryType.GetField("countAtStart", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                                        if (casF != null) atStart = Convert.ToInt32(casF.GetValue(entry));

                                        FieldInfo mlF = entryType.GetField("maxLiving", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                                        if (mlF != null) maxL = Convert.ToInt32(mlF.GetValue(entry));

                                        FieldInfo clF = entryType.GetField("currentlyLiving", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                                        if (clF != null) curL = Convert.ToInt32(clF.GetValue(entry));

                                        bool alreadyInRows = false;
                                        for (int r = 0; r < rows.Count; r++)
                                        {
                                            if (rows[r].BaseIndex == b && string.Equals(rows[r].UnitId, uId, StringComparison.OrdinalIgnoreCase))
                                            {
                                                alreadyInRows = true;
                                                break;
                                            }
                                        }

                                        if (!alreadyInRows)
                                        {
                                            totalCount++;
                                            if (rows.Count < maxEntries)
                                            {
                                                rows.Add(new AiDeckEntryDiagnosticRow(b, uId, atStart, maxL, curL));
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch { }

            omittedCount = Math.Max(0, totalCount - rows.Count);
            return rows.AsReadOnly();
        }

        private static object ResolveAiDirectorManager()
        {
            try
            {
                Type type = typeof(AiBehaviourManager);
                FieldInfo instanceF = type.GetField("_instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                return instanceF?.GetValue(null) ?? UnityEngine.Object.FindObjectOfType<AiBehaviourManager>();
            }
            catch
            {
                return null;
            }
        }

        public static UnitVisualVariantResult TryApplyUnitVisualVariant(string entityKeyId, string variantId)
        {
            if (string.IsNullOrWhiteSpace(entityKeyId))
            {
                return new UnitVisualVariantResult(false, entityKeyId, variantId, string.Empty, "Entity key ID must not be empty.");
            }

            try
            {
                ExistingControllers existing = ExistingControllers.Instance;
                if (existing == null)
                {
                    return new UnitVisualVariantResult(false, entityKeyId, variantId, string.Empty, "ExistingControllers instance not available.");
                }

                EntityController unit = FindEntityByKey(existing, entityKeyId.Trim());
                if (unit == null || unit.gameObject == null || unit.IsDestroyed)
                {
                    return new UnitVisualVariantResult(false, entityKeyId, variantId, string.Empty, "Entity '" + entityKeyId + "' not found or destroyed.");
                }

                string key = entityKeyId.Trim();
                string unitId = ResolveEntityIdString(unit);

                RuntimeUnitVisualVariant targetVariant = null;
                bool isReset = string.IsNullOrWhiteSpace(variantId) ||
                               string.Equals(variantId, "default", StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(variantId, "primary", StringComparison.OrdinalIgnoreCase);

                if (isReset)
                {
                    if (!RuntimeUnitVisualVariantRegistry.TryGetPrimary(unitId, out targetVariant))
                    {
                        targetVariant = null;
                    }
                }
                else
                {
                    if (!RuntimeUnitVisualVariantRegistry.TryGet(unitId, variantId.Trim(), out targetVariant))
                    {
                        return new UnitVisualVariantResult(false, key, variantId, string.Empty,
                            "Visual variant '" + variantId + "' is not registered for unit '" + unitId + "'.");
                    }
                }

                RogueCommandExpansionLoader.Runtime.Visual.RuntimeVisualDirector.AttachIfApplicable(unit);
                RogueCommandExpansionLoader.Runtime.Visual.RuntimeVisualDirector director = unit.gameObject.GetComponent<RogueCommandExpansionLoader.Runtime.Visual.RuntimeVisualDirector>();
                if (director == null)
                {
                    return new UnitVisualVariantResult(false, key, variantId, targetVariant != null ? targetVariant.PresetName : string.Empty,
                        "Unit '" + unitId + "' does not possess an active RuntimeVisualDirector.");
                }

                if (!director.TryApplyVisualVariant(targetVariant))
                {
                    return new UnitVisualVariantResult(false, key, variantId,
                        targetVariant != null ? targetVariant.PresetName : string.Empty,
                        "Visual variant could not be applied because a declared module or canonical hardpoint is unavailable.",
                        director.ActiveVisualModuleCount);
                }
                string appliedId = targetVariant != null ? targetVariant.VariantId : "default";
                string preset = targetVariant != null ? targetVariant.PresetName : "default";
                string levelLabel = targetVariant != null && targetVariant.IsPrimary ? "Level 1 Primary Chassis" : "Level 2 Tactical Variant";

                return new UnitVisualVariantResult(true, key, appliedId, preset,
                    "Applied " + levelLabel + " [" + preset + "] to '" + key + "' with " +
                    director.ActiveVisualModuleCount + " mechanical module(s).",
                    director.ActiveVisualModuleCount);
            }
            catch (Exception ex)
            {
                return new UnitVisualVariantResult(false, entityKeyId, variantId, string.Empty,
                    "Failed to apply visual variant: " + ex.Message);
            }
        }

        public static string OrderApplyUnitVisualVariantJson(string entityKeyId, string variantId)
        {
            return AgentDiagnosticsFormatter.SerializeUnitVisualVariantResult(
                TryApplyUnitVisualVariant(entityKeyId, variantId));
        }

        public static string GetVisualVariantCensusJson(int maxUnits = 64)
        {
            return AgentDiagnosticsFormatter.SerializeVisualVariantCensus(maxUnits);
        }

        public static UnitLoadoutResult TryApplyUnitLoadout(string entityKeyId, string loadoutId)
        {
            if (string.IsNullOrWhiteSpace(entityKeyId))
            {
                return new UnitLoadoutResult(false, entityKeyId, loadoutId, "Entity key ID must not be empty.");
            }

            try
            {
                ExistingControllers existing = ExistingControllers.Instance;
                if (existing == null)
                {
                    return new UnitLoadoutResult(false, entityKeyId, loadoutId, "ExistingControllers instance is not available.");
                }

                EntityController unit = FindEntityByKey(existing, entityKeyId.Trim());
                if (unit == null || unit.gameObject == null || unit.IsDestroyed)
                {
                    return new UnitLoadoutResult(false, entityKeyId, loadoutId, "Entity '" + entityKeyId + "' not found or destroyed.");
                }

                string key = entityKeyId.Trim();
                string sanitized = string.IsNullOrWhiteSpace(loadoutId) ? "Standard" : loadoutId.Trim();
                string unitId = ResolveEntityIdString(unit);
                RuntimeUnitVariant declaredVariant;
                bool isDeclaredVariant = RuntimeUnitVariantRegistry.TryGet(unitId, sanitized, out declaredVariant);
                var changes = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
                var absoluteOverrides = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);

                if (isDeclaredVariant)
                {
                    sanitized = declaredVariant.VariantId;
                    if (string.Equals(declaredVariant.Loadout, "AP", StringComparison.OrdinalIgnoreCase))
                    {
                        changes["Damage"] = 0.30f;
                        changes["AttackCooldown"] = 0.20f;
                    }
                    else if (string.Equals(declaredVariant.Loadout, "HE", StringComparison.OrdinalIgnoreCase))
                    {
                        changes["Damage"] = 0.15f;
                        changes["EffectRadius"] = 0.40f;
                    }
                    else if (!string.IsNullOrWhiteSpace(declaredVariant.Loadout))
                    {
                        return new UnitLoadoutResult(false, key, sanitized,
                            "Declared variant '" + sanitized + "' has unsupported loadout '" + declaredVariant.Loadout + "'.");
                    }

                    foreach (RuntimeUnitVariantOverride balanceOverride in declaredVariant.BalanceOverrides)
                    {
                        string statName;
                        float absoluteValue;
                        string failure;
                        if (!TryResolveRuntimeVariantOverride(unit, balanceOverride, out statName, out absoluteValue, out failure))
                        {
                            return new UnitLoadoutResult(false, key, sanitized, failure);
                        }

                        absoluteOverrides[statName] = absoluteValue;
                        changes.Remove(statName);
                    }
                }
                else if (RuntimeUnitVariantRegistry.HasVariants(unitId) &&
                         !string.Equals(sanitized, "Standard", StringComparison.OrdinalIgnoreCase))
                {
                    return new UnitLoadoutResult(false, key, sanitized,
                        "Declared unit variant '" + sanitized + "' is not supported for entity '" + unitId + "'.");
                }
                else if (string.Equals(sanitized, "AP", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(sanitized, "ArmorPiercing", StringComparison.OrdinalIgnoreCase))
                {
                    sanitized = "AP";
                    changes["Damage"] = 0.30f;
                    changes["AttackCooldown"] = 0.20f;
                }
                else if (string.Equals(sanitized, "HE", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(sanitized, "HighExplosive", StringComparison.OrdinalIgnoreCase))
                {
                    sanitized = "HE";
                    changes["Damage"] = 0.15f;
                    changes["EffectRadius"] = 0.40f;
                }
                else if (string.Equals(sanitized, "Speed", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(sanitized, "Scout", StringComparison.OrdinalIgnoreCase))
                {
                    sanitized = "Speed";
                    changes["MoveSpeed"] = 0.25f;
                    changes["WeaponRange"] = 0.15f;
                }
                else if (string.Equals(sanitized, "Siege", StringComparison.OrdinalIgnoreCase))
                {
                    sanitized = "Siege";
                    changes["WeaponRange"] = 0.35f;
                    changes["MoveSpeed"] = -0.30f;
                    changes["Damage"] = 0.20f;
                }
                else if (string.Equals(sanitized, "Reinforced", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(sanitized, "Assault", StringComparison.OrdinalIgnoreCase))
                {
                    sanitized = "Reinforced";
                    changes["MaxHealth"] = 0.30f;
                    changes["ArmorProtection"] = 0.25f;
                    changes["MoveSpeed"] = -0.10f;
                }
                else if (string.Equals(sanitized, "Standard", StringComparison.OrdinalIgnoreCase))
                {
                    sanitized = "Standard";
                }
                else
                {
                    return new UnitLoadoutResult(false, key, sanitized,
                        "Unrecognized loadout '" + sanitized + "'.");
                }

                var previousStats = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
                lock (_activeUnitVariants)
                {
                    Dictionary<string, float> registeredStats;
                    if (_activeUnitLoadoutStats.TryGetValue(key, out registeredStats) && registeredStats != null)
                    {
                        foreach (KeyValuePair<string, float> registeredStat in registeredStats)
                        {
                            previousStats[registeredStat.Key] = registeredStat.Value;
                        }
                    }
                }

                foreach (KeyValuePair<string, float> previousStat in previousStats)
                {
                    RuntimeUnitMechanicsDirector.ApplyTimedStatChange(
                        unit,
                        previousStat.Key,
                        0f,
                        3600f,
                        UnitLoadoutOriginator(key, previousStat.Key),
                        true);
                }

                foreach (KeyValuePair<string, float> absoluteOverride in absoluteOverrides)
                {
                    float currentValue;
                    if (!TryReadUnitFloatProperty(unit, RuntimeVariantPropertyName(absoluteOverride.Key), out currentValue) ||
                        float.IsNaN(currentValue) || float.IsInfinity(currentValue) || currentValue <= 0f)
                    {
                        RestoreUnitLoadoutChanges(unit, key, previousStats);
                        return new UnitLoadoutResult(false, key, sanitized,
                            "Declared variant '" + sanitized + "' cannot apply '" + absoluteOverride.Key + "' because the current native stat is unavailable or non-positive.");
                    }

                    float relativeDelta = (absoluteOverride.Value / currentValue) - 1f;
                    if (float.IsNaN(relativeDelta) || float.IsInfinity(relativeDelta))
                    {
                        RestoreUnitLoadoutChanges(unit, key, previousStats);
                        return new UnitLoadoutResult(false, key, sanitized,
                            "Declared variant '" + sanitized + "' produced a non-finite modifier for '" + absoluteOverride.Key + "'.");
                    }

                    changes[absoluteOverride.Key] = relativeDelta;
                }

                foreach (KeyValuePair<string, float> change in changes)
                {
                    RuntimeUnitMechanicsDirector.ApplyTimedStatChange(
                        unit,
                        change.Key,
                        change.Value,
                        3600f,
                        UnitLoadoutOriginator(key, change.Key),
                        true);
                }

                lock (_activeUnitVariants)
                {
                    _activeUnitVariants[key] = sanitized;
                    _activeUnitLoadoutStats[key] = new Dictionary<string, float>(changes, StringComparer.OrdinalIgnoreCase);
                }

                return new UnitLoadoutResult(true, key, sanitized,
                    "Applied loadout '" + sanitized + "' to unit '" + entityKeyId + "'.");
            }
            catch (Exception ex)
            {
                return new UnitLoadoutResult(false, entityKeyId, loadoutId, "Error applying unit loadout: " + ex.Message);
            }
        }

        private static void RestoreUnitLoadoutChanges(
            EntityController unit,
            string key,
            IDictionary<string, float> previousStats)
        {
            if (unit == null || previousStats == null)
            {
                return;
            }

            foreach (KeyValuePair<string, float> previousStat in previousStats)
            {
                RuntimeUnitMechanicsDirector.ApplyTimedStatChange(
                    unit,
                    previousStat.Key,
                    previousStat.Value,
                    3600f,
                    UnitLoadoutOriginator(key, previousStat.Key),
                    true);
            }
        }

        private static bool TryResolveRuntimeVariantOverride(
            EntityController unit,
            RuntimeUnitVariantOverride balanceOverride,
            out string statName,
            out float absoluteValue,
            out string failure)
        {
            statName = string.Empty;
            absoluteValue = 0f;
            failure = "Declared variant balance override is invalid.";
            if (balanceOverride == null || string.IsNullOrWhiteSpace(balanceOverride.Name) ||
                !float.TryParse(
                    balanceOverride.Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out absoluteValue) ||
                float.IsNaN(absoluteValue) || float.IsInfinity(absoluteValue) || absoluteValue < 0f)
            {
                failure = "Declared variant balance override '" + (balanceOverride == null ? string.Empty : balanceOverride.Name) +
                    "' must contain a finite non-negative number.";
                return false;
            }

            string rawName = balanceOverride.Name.Trim();
            if (string.Equals(rawName, "damage", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(rawName, "damage1", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(rawName, "attackDamage", StringComparison.OrdinalIgnoreCase))
            {
                statName = "Damage";
            }
            else if (string.Equals(rawName, "attackCooldown", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(rawName, "attack1Cooldown", StringComparison.OrdinalIgnoreCase))
            {
                statName = "AttackCooldown";
            }
            else if (string.Equals(rawName, "moveSpeed", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(rawName, "speed", StringComparison.OrdinalIgnoreCase))
            {
                statName = "MoveSpeed";
            }
            else if (string.Equals(rawName, "weaponRange", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(rawName, "attackRange", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(rawName, "range", StringComparison.OrdinalIgnoreCase))
            {
                statName = "WeaponRange";
            }
            else if (string.Equals(rawName, "maxHealth", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(rawName, "health", StringComparison.OrdinalIgnoreCase))
            {
                statName = "MaxHealth";
            }
            else if (string.Equals(rawName, "maxArmor", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(rawName, "armor", StringComparison.OrdinalIgnoreCase))
            {
                statName = "MaxArmor";
            }
            else if (string.Equals(rawName, "armorProtection", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(rawName, "defense", StringComparison.OrdinalIgnoreCase))
            {
                statName = "ArmorProtection";
            }
            else if (string.Equals(rawName, "maxShield", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(rawName, "shield", StringComparison.OrdinalIgnoreCase))
            {
                statName = "MaxShield";
            }
            else if (string.Equals(rawName, "healAmount", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(rawName, "healAmount1", StringComparison.OrdinalIgnoreCase))
            {
                statName = "HealAmount";
            }
            else if (string.Equals(rawName, "sightRadius", StringComparison.OrdinalIgnoreCase))
            {
                statName = "SightRadius";
            }
            else if (string.Equals(rawName, "effectRadius", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(rawName, "effectRadius1", StringComparison.OrdinalIgnoreCase))
            {
                statName = "EffectRadius";
            }
            else if (string.Equals(rawName, "skillRange", StringComparison.OrdinalIgnoreCase))
            {
                statName = "SkillRange";
            }
            else
            {
                failure = "Declared variant balance override '" + rawName + "' is not supported for live unit gameplay.";
                return false;
            }

            float currentValue;
            if (unit == null || !TryReadUnitFloatProperty(unit, RuntimeVariantPropertyName(statName), out currentValue))
            {
                failure = "Declared variant balance override '" + rawName + "' is not available on the live EntityController.";
                return false;
            }

            return true;
        }

        private static string RuntimeVariantPropertyName(string statName)
        {
            if (string.Equals(statName, "Damage", StringComparison.OrdinalIgnoreCase)) return "Damage1";
            if (string.Equals(statName, "AttackCooldown", StringComparison.OrdinalIgnoreCase)) return "Attack1Cooldown";
            if (string.Equals(statName, "EffectRadius", StringComparison.OrdinalIgnoreCase)) return "EffectRadius1";
            if (string.Equals(statName, "HealAmount", StringComparison.OrdinalIgnoreCase)) return "HealAmount1";
            return statName;
        }

        private static bool TryReadUnitFloatProperty(EntityController unit, string propertyName, out float value)
        {
            value = 0f;
            if (unit == null || string.IsNullOrWhiteSpace(propertyName))
            {
                return false;
            }

            try
            {
                PropertyInfo property = unit.GetType().GetProperty(
                    propertyName,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                object rawValue = property == null ? null : property.GetValue(unit, null);
                if (rawValue == null)
                {
                    FieldInfo field = unit.GetType().GetField(
                        propertyName,
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    rawValue = field == null ? null : field.GetValue(unit);
                }

                if (rawValue == null)
                {
                    return false;
                }

                value = Convert.ToSingle(rawValue, System.Globalization.CultureInfo.InvariantCulture);
                return !float.IsNaN(value) && !float.IsInfinity(value);
            }
            catch
            {
                return false;
            }
        }

        private static string UnitLoadoutOriginator(string entityKeyId, string statName)
        {
            return "rcl:unit-loadout:" + entityKeyId + ":" + statName;
        }

        public static PrespawnWarmResult TryWarmPrespawnPool(string entityId, int count)
        {
            if (string.IsNullOrWhiteSpace(entityId))
            {
                return new PrespawnWarmResult(false, entityId, count, 0, "Entity ID must not be empty.");
            }

            int clampedCount = Mathf.Clamp(count, 1, 50);
            string sanitizedId = entityId.Trim();

            try
            {
                int actualWarmed = 0;
                bool prespawnResolved = false;

                Type entityFactory = Type.GetType("EntityFactory, Assembly-CSharp");
                if (entityFactory != null)
                {
                    MethodInfo prespawnM = entityFactory.GetMethod("PrespawnEntity",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                        null, new[] { typeof(string), typeof(int) }, null);
                    if (prespawnM != null)
                    {
                        prespawnResolved = true;
                        try
                        {
                            object res = prespawnM.Invoke(null, new object[] { sanitizedId, clampedCount });
                            if (res is bool success)
                            {
                                actualWarmed = success ? clampedCount : 0;
                            }
                        }
                        catch { }
                    }
                }

                // Record the desired quota locally regardless of live prespawn availability, so the
                // diagnostic census reflects the requested target even when the game assembly is absent.
                lock (_prespawnPoolQuotas)
                {
                    int currentQuota = 0;
                    _prespawnPoolQuotas.TryGetValue(sanitizedId, out currentQuota);
                    _prespawnPoolQuotas[sanitizedId] = Math.Max(currentQuota, clampedCount);

                    int currentWarmed = 0;
                    _prespawnPoolWarmed.TryGetValue(sanitizedId, out currentWarmed);
                    _prespawnPoolWarmed[sanitizedId] = Math.Max(currentWarmed, actualWarmed);
                }

                string message;
                if (!prespawnResolved)
                {
                    message = "Recorded warm intent for entity '" + sanitizedId + "' (target " + clampedCount +
                              "); live prespawn is unavailable in this environment (0 instances warmed).";
                }
                else if (actualWarmed > 0)
                {
                    message = "Warmed pre-spawn pool for entity '" + sanitizedId + "' with " + actualWarmed + " instances.";
                }
                else
                {
                    message = "Prespawn for entity '" + sanitizedId + "' was rejected (0 instances warmed).";
                }

                return new PrespawnWarmResult(true, sanitizedId, clampedCount, actualWarmed, message);
            }
            catch (Exception ex)
            {
                return new PrespawnWarmResult(false, sanitizedId, clampedCount, 0, "Error warming pre-spawn pool: " + ex.Message);
            }
        }

        public static AiDoctrineResult TrySetAiDoctrine(string doctrineName, float aggressionMultiplier)
        {
            if (float.IsNaN(aggressionMultiplier) || float.IsInfinity(aggressionMultiplier))
            {
                return new AiDoctrineResult(false, doctrineName ?? string.Empty, aggressionMultiplier,
                    "Aggression multiplier must be finite.");
            }

            if (string.IsNullOrWhiteSpace(doctrineName))
            {
                return new AiDoctrineResult(false, doctrineName, aggressionMultiplier, "Doctrine name must not be empty.");
            }

            string sanitized = doctrineName.Trim();
            float clampedMultiplier = Mathf.Clamp(aggressionMultiplier, 0.1f, 5.0f);

            lock (_doctrineLock)
            {
                _activeAiDoctrine = sanitized;
                _aiAggressionMultiplier = clampedMultiplier;
            }

            try
            {
                object manager = ResolveAiDirectorManager();
                if (manager != null)
                {
                    Type mgrType = manager.GetType();
                    FieldInfo tickField = _cachedTickDurationField ?? (_cachedTickDurationField = mgrType.GetField("tickDuration", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance));
                    if (tickField != null && clampedMultiplier > 0f)
                    {
                        float baseTick = 1.0f;
                        tickField.SetValue(manager, Math.Max(0.2f, baseTick / clampedMultiplier));
                    }
                }
            }
            catch { }

            return new AiDoctrineResult(true, sanitized, clampedMultiplier,
                "Configured active AI doctrine '" + sanitized + "' with aggression multiplier " + clampedMultiplier.ToString("0.0#", System.Globalization.CultureInfo.InvariantCulture) + ".");
        }

        public static AiDeckInjectionResult TryInjectAiDeckUnit(int baseIndex, string unitId, int countAtStart, int maxLiving)
        {
            if (baseIndex < 0)
            {
                return new AiDeckInjectionResult(false, baseIndex, unitId, countAtStart, maxLiving, "Base index must be non-negative.");
            }
            if (string.IsNullOrWhiteSpace(unitId))
            {
                return new AiDeckInjectionResult(false, baseIndex, unitId, countAtStart, maxLiving, "Unit ID must not be empty.");
            }

            string sanitizedId = unitId.Trim();
            int clampedStart = Mathf.Clamp(countAtStart, 0, 50);
            int clampedMax = Mathf.Clamp(maxLiving, 1, 50);

            try
            {
                bool injectedIntoGame = false;
                object manager = ResolveAiDirectorManager();
                if (manager != null)
                {
                    Type mgrType = manager.GetType();
                    FieldInfo activeBField = mgrType.GetField("activeBehaviour", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                        ?? mgrType.GetField("_activeBehaviour", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    object activeBehaviour = activeBField?.GetValue(manager);
                    if (activeBehaviour != null)
                    {
                        FieldInfo aiBasesField = activeBehaviour.GetType().GetField("aiBases", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        System.Collections.IList bases = aiBasesField?.GetValue(activeBehaviour) as System.Collections.IList;
                        if (bases != null && baseIndex < bases.Count)
                        {
                            object aiBase = bases[baseIndex];
                            if (aiBase != null)
                            {
                                FieldInfo deckField = aiBase.GetType().GetField("entitiesInAiDeck", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                                System.Collections.IList deck = deckField?.GetValue(aiBase) as System.Collections.IList;
                                if (deck != null)
                                {
                                    Type deckEntryType = deckField.FieldType.IsGenericType ? deckField.FieldType.GetGenericArguments()[0] : null;
                                    if (deckEntryType != null)
                                    {
                                        object existingEntry = null;
                                        for (int i = 0; i < deck.Count; i++)
                                        {
                                            object entry = deck[i];
                                            if (entry != null)
                                            {
                                                string existingId = entry.GetType().GetField("entityId", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(entry) as string;
                                                if (string.Equals(existingId, sanitizedId, StringComparison.OrdinalIgnoreCase))
                                                {
                                                    existingEntry = entry;
                                                    break;
                                                }
                                            }
                                        }

                                        if (existingEntry != null)
                                        {
                                            existingEntry.GetType().GetField("countAtStart", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(existingEntry, clampedStart);
                                            existingEntry.GetType().GetField("maxLiving", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(existingEntry, clampedMax);
                                        }
                                        else
                                        {
                                            object newEntry = Activator.CreateInstance(deckEntryType);
                                            newEntry.GetType().GetField("entityId", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(newEntry, sanitizedId);
                                            newEntry.GetType().GetField("countAtStart", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(newEntry, clampedStart);
                                            newEntry.GetType().GetField("maxLiving", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(newEntry, clampedMax);
                                            deck.Add(newEntry);
                                        }
                                        injectedIntoGame = true;
                                    }
                                }
                            }
                        }
                    }
                }

                lock (_injectedAiDeckEntries)
                {
                    bool replaced = false;
                    for (int i = 0; i < _injectedAiDeckEntries.Count; i++)
                    {
                        if (_injectedAiDeckEntries[i].BaseIndex == baseIndex &&
                            string.Equals(_injectedAiDeckEntries[i].UnitId, sanitizedId, StringComparison.OrdinalIgnoreCase))
                        {
                            _injectedAiDeckEntries[i] = new AiDeckEntryDiagnosticRow(baseIndex, sanitizedId, clampedStart, clampedMax, 0);
                            replaced = true;
                            break;
                        }
                    }

                    if (!replaced)
                    {
                        _injectedAiDeckEntries.Add(new AiDeckEntryDiagnosticRow(baseIndex, sanitizedId, clampedStart, clampedMax, 0));
                    }
                }

                string status = injectedIntoGame ? "injected into live AI Behaviour deck" : "recorded in runtime deck registry";
                return new AiDeckInjectionResult(true, baseIndex, sanitizedId, clampedStart, clampedMax,
                    "Unit '" + sanitizedId + "' " + status + " for base " + baseIndex + " (countAtStart: " + clampedStart + ", maxLiving: " + clampedMax + ").");
            }
            catch (Exception ex)
            {
                return new AiDeckInjectionResult(false, baseIndex, sanitizedId, clampedStart, clampedMax, "Error injecting AI deck unit: " + ex.Message);
            }
        }

        public static void ClearUnitLoadouts()
        {
            lock (_activeUnitVariants)
            {
                _activeUnitVariants.Clear();
                _activeUnitLoadoutStats.Clear();
            }
            lock (_prespawnPoolQuotas)
            {
                _prespawnPoolQuotas.Clear();
                _prespawnPoolWarmed.Clear();
            }
        }

        public static void ClearAiDoctrineAndDeckEntries()
        {
            lock (_doctrineLock)
            {
                _activeAiDoctrine = "Standard";
                _aiAggressionMultiplier = 1.0f;
            }
            lock (_injectedAiDeckEntries)
            {
                _injectedAiDeckEntries.Clear();
            }
        }

        /// <summary>
        /// Dispatches a native patrol route to a living unit. Clears the unit's existing native
        /// patrol positions, seeds them with the supplied waypoints, and issues a tagged
        /// <c>EntityCommand.Patrol(0)</c>. Uses the verified game identity (Assembly-CSharp.dll
        /// SHA-256 d2da384d...): <c>EntityController.ClearPatrolPositions</c>
        /// (0x06000888), <c>AddPatrolPosition(Vector3)</c> (0x06000889),
        /// <c>ArePatrolPositionsEmpty</c> (0x0600088a), and <c>EntityCommand.Patrol(int)</c>
        /// (0x06000697). Waypoints are supplied as parallel X/Z lists; the Y axis is resolved by
        /// the native pathing. Fails closed when the route is empty or seeding does not stick.
        /// </summary>
        public static UnitPatrolResult TryOrderUnitPatrol(string entityKeyId, IReadOnlyList<float> waypointX, IReadOnlyList<float> waypointZ)
        {
            if (string.IsNullOrWhiteSpace(entityKeyId))
            {
                return new UnitPatrolResult(false, entityKeyId, 0, "Entity key ID cannot be empty.");
            }

            if (waypointX == null || waypointZ == null || waypointX.Count != waypointZ.Count || waypointX.Count < 1)
            {
                return new UnitPatrolResult(false, entityKeyId, 0,
                    "Patrol requires at least one waypoint with matching X and Z coordinates.");
            }

            for (int i = 0; i < waypointX.Count; i++)
            {
                if (float.IsNaN(waypointX[i]) || float.IsInfinity(waypointX[i]) ||
                    float.IsNaN(waypointZ[i]) || float.IsInfinity(waypointZ[i]))
                {
                    return new UnitPatrolResult(false, entityKeyId, 0,
                        "Patrol waypoint coordinates must be finite.");
                }
            }

            try
            {
                ExistingControllers existing = ExistingControllers.Instance;
                if (existing == null)
                {
                    return new UnitPatrolResult(false, entityKeyId, 0, "ExistingControllers instance is not available.");
                }

                EntityController unit = FindEntityByKey(existing, entityKeyId.Trim());
                if (unit == null)
                {
                    return new UnitPatrolResult(false, entityKeyId, 0, "Unit '" + entityKeyId + "' not found or destroyed.");
                }

                unit.ClearPatrolPositions();
                for (int i = 0; i < waypointX.Count; i++)
                {
                    unit.AddPatrolPosition(new Vector3(waypointX[i], 0f, waypointZ[i]));
                }

                if (unit.ArePatrolPositionsEmpty())
                {
                    return new UnitPatrolResult(false, entityKeyId, 0,
                        "Native patrol positions were not seeded for unit '" + entityKeyId + "'.");
                }

                EntityCommand patrolCommand = EntityCommand.Patrol(0);
                patrolCommand.tag = "rcl-command-director";
                unit.ExecuteCommand(patrolCommand, EntityController.CommandProcessingType.ExecuteBeforeCurrentCommand);

                return new UnitPatrolResult(true, entityKeyId, waypointX.Count,
                    "Patrol route dispatched for unit '" + entityKeyId + "' with " + waypointX.Count + " waypoint(s).");
            }
            catch (Exception ex)
            {
                return new UnitPatrolResult(false, entityKeyId, 0, "Error dispatching unit patrol: " + ex.Message);
            }
        }

        /// <summary>
        /// Dispatches a single command type to every living unit in the provided key list.
        /// Supported command types: "Move", "Stop", "HoldPosition", "AttackMove". Each member is
        /// routed through the already-verified single-unit dispatch methods so the group command
        /// reuses the same fail-closed reflection and error handling. The result reports how many
        /// targeted units accepted the order versus how many were requested.
        /// </summary>
        public static GroupCommandResult TryOrderGroupCommand(IReadOnlyList<string> entityKeyIds, string commandType, float targetX, float targetZ)
        {
            if (entityKeyIds == null || entityKeyIds.Count == 0)
            {
                return new GroupCommandResult(false, string.Empty, 0, 0, "No entity keys provided for the group command.");
            }

            string normalized = string.IsNullOrWhiteSpace(commandType) ? string.Empty : commandType.Trim();
            bool isMove = string.Equals(normalized, "Move", StringComparison.OrdinalIgnoreCase);
            bool isStop = string.Equals(normalized, "Stop", StringComparison.OrdinalIgnoreCase);
            bool isHold = string.Equals(normalized, "HoldPosition", StringComparison.OrdinalIgnoreCase);
            bool isAttackMove = string.Equals(normalized, "AttackMove", StringComparison.OrdinalIgnoreCase);

            if (isMove || isAttackMove)
            {
                if (float.IsNaN(targetX) || float.IsInfinity(targetX) ||
                    float.IsNaN(targetZ) || float.IsInfinity(targetZ))
                {
                    return new GroupCommandResult(false, normalized, 0, 0,
                        "Group command target coordinates must be finite.");
                }
            }

            if (!isMove && !isStop && !isHold && !isAttackMove)
            {
                return new GroupCommandResult(false, normalized, 0, 0,
                    "Unsupported group command type '" + normalized + "'. Supported: Move, Stop, HoldPosition, AttackMove.");
            }

            int targetedCount = 0;
            int dispatchedCount = 0;
            string lastFailure = string.Empty;

            for (int i = 0; i < entityKeyIds.Count; i++)
            {
                string key = entityKeyIds[i];
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                targetedCount++;
                UnitCommandResult memberResult = isMove
                    ? TryOrderUnitMove(key.Trim(), targetX, targetZ)
                    : isStop
                        ? TryOrderStop(key.Trim())
                        : isHold
                            ? TryOrderHoldPosition(key.Trim())
                            : TryOrderAttackMove(key.Trim(), targetX, targetZ);

                if (memberResult.Success)
                {
                    dispatchedCount++;
                }
                else
                {
                    lastFailure = "Unit '" + key.Trim() + "': " + memberResult.Message;
                }
            }

            bool success = dispatchedCount > 0;
            string message;
            if (targetedCount == 0)
            {
                message = "Group command skipped: no valid entity keys were provided.";
            }
            else if (success && dispatchedCount == targetedCount)
            {
                message = "Group '" + normalized + "' dispatched to all " + dispatchedCount + " targeted unit(s).";
            }
            else if (success)
            {
                message = "Group '" + normalized + "' dispatched to " + dispatchedCount + " of " + targetedCount +
                          " targeted unit(s). Last failure: " + lastFailure;
            }
            else
            {
                message = "Group '" + normalized + "' failed for all " + targetedCount + " targeted unit(s). Last failure: " + lastFailure;
            }

            return new GroupCommandResult(success, normalized, targetedCount, dispatchedCount, message);
        }

        /// <summary>
        /// Dispatches a tactical formation movement to multiple units.
        /// Supported formations: Line, Wedge, Column, Box, Surround.
        /// Spacing is in meters (default 4.0, clamped 1.5 to 25.0).
        /// </summary>
        public static FormationOrderResult TryOrderFormationMove(
            IReadOnlyList<string> entityKeyIds,
            string formationType,
            float targetX,
            float targetZ,
            float spacing)
        {
            if (entityKeyIds == null || entityKeyIds.Count == 0)
            {
                return new FormationOrderResult(false, formationType ?? "Line", 0, 0, targetX, targetZ, spacing, "No entity keys provided for formation move.");
            }

            string normalized = string.IsNullOrWhiteSpace(formationType) ? "Line" : formationType.Trim();
            bool isLine = string.Equals(normalized, "Line", StringComparison.OrdinalIgnoreCase);
            bool isWedge = string.Equals(normalized, "Wedge", StringComparison.OrdinalIgnoreCase);
            bool isColumn = string.Equals(normalized, "Column", StringComparison.OrdinalIgnoreCase);
            bool isBox = string.Equals(normalized, "Box", StringComparison.OrdinalIgnoreCase);
            bool isSurround = string.Equals(normalized, "Surround", StringComparison.OrdinalIgnoreCase);

            if (!isLine && !isWedge && !isColumn && !isBox && !isSurround)
            {
                return new FormationOrderResult(false, normalized, 0, 0, targetX, targetZ, spacing,
                    "Unsupported formation type '" + normalized + "'. Supported: Line, Wedge, Column, Box, Surround.");
            }

            if (float.IsNaN(targetX) || float.IsInfinity(targetX) ||
                float.IsNaN(targetZ) || float.IsInfinity(targetZ) ||
                float.IsNaN(spacing) || float.IsInfinity(spacing))
            {
                return new FormationOrderResult(false, normalized, 0, 0, targetX, targetZ, spacing,
                    "Formation target coordinates and spacing must be finite.");
            }

            float effSpacing = Mathf.Clamp(spacing > 0.01f ? spacing : 4.0f, 1.5f, 25.0f);
            int n = entityKeyIds.Count;
            int targetedCount = 0;
            int dispatchedCount = 0;
            string lastError = string.Empty;

            for (int i = 0; i < n; i++)
            {
                string key = entityKeyIds[i];
                if (string.IsNullOrWhiteSpace(key)) continue;

                targetedCount++;
                float offX = 0f;
                float offZ = 0f;

                if (isLine)
                {
                    offX = (i - (n - 1) / 2.0f) * effSpacing;
                    offZ = 0f;
                }
                else if (isColumn)
                {
                    offX = 0f;
                    offZ = -(i - (n - 1) / 2.0f) * effSpacing;
                }
                else if (isWedge)
                {
                    if (i == 0)
                    {
                        offX = 0f;
                        offZ = 0f;
                    }
                    else
                    {
                        int rank = (i + 1) / 2;
                        float side = (i % 2 == 1) ? -1.0f : 1.0f;
                        offX = side * rank * effSpacing;
                        offZ = -rank * effSpacing;
                    }
                }
                else if (isBox)
                {
                    if (n == 1)
                    {
                        offX = 0f;
                        offZ = 0f;
                    }
                    else
                    {
                        int cols = (int)Math.Ceiling(Math.Sqrt(n));
                        int rows = (int)Math.Ceiling((double)n / cols);
                        int c = i % cols;
                        int r = i / cols;
                        offX = (c - (cols - 1) / 2.0f) * effSpacing;
                        offZ = -(r - (rows - 1) / 2.0f) * effSpacing;
                    }
                }
                else if (isSurround)
                {
                    float theta = (float)(2.0 * Math.PI * i / n);
                    float radius = Math.Max(effSpacing, (float)(effSpacing * n / (2.0 * Math.PI)));
                    offX = radius * (float)Math.Cos(theta);
                    offZ = radius * (float)Math.Sin(theta);
                }

                float posX = targetX + offX;
                float posZ = targetZ + offZ;

                UnitCommandResult res = TryOrderUnitMove(key.Trim(), posX, posZ);
                if (res.Success)
                {
                    dispatchedCount++;
                }
                else
                {
                    lastError = "Unit '" + key.Trim() + "': " + res.Message;
                }
            }

            bool success = dispatchedCount > 0;
            string msg;
            if (targetedCount == 0)
            {
                msg = "Formation move skipped: no valid entity keys provided.";
            }
            else if (success && dispatchedCount == targetedCount)
            {
                msg = "Dispatched " + normalized + " formation move to all " + dispatchedCount + " unit(s) at (" + targetX + ", " + targetZ + ").";
            }
            else if (success)
            {
                msg = "Dispatched " + normalized + " formation move to " + dispatchedCount + " of " + targetedCount + " unit(s). Last error: " + lastError;
            }
            else
            {
                msg = "Failed formation move for all " + targetedCount + " unit(s). Last error: " + lastError;
            }

            return new FormationOrderResult(success, normalized, targetedCount, dispatchedCount, targetX, targetZ, effSpacing, msg);
        }

        /// <summary>
        /// Assigns a tactical combat stance to a unit.
        /// Supported stances: Standard, Aggressive, Defensive, HoldFire, Kiting.
        /// </summary>
        public static UnitTacticalStanceResult TrySetUnitTacticalStance(string entityKeyId, string stanceName)
        {
            if (string.IsNullOrWhiteSpace(entityKeyId))
            {
                return new UnitTacticalStanceResult(false, string.Empty, stanceName ?? "Standard", 0, "Entity key ID cannot be empty.");
            }

            string normalized = string.IsNullOrWhiteSpace(stanceName) ? "Standard" : stanceName.Trim();
            bool isStandard = string.Equals(normalized, "Standard", StringComparison.OrdinalIgnoreCase);
            bool isAggressive = string.Equals(normalized, "Aggressive", StringComparison.OrdinalIgnoreCase);
            bool isDefensive = string.Equals(normalized, "Defensive", StringComparison.OrdinalIgnoreCase);
            bool isHoldFire = string.Equals(normalized, "HoldFire", StringComparison.OrdinalIgnoreCase);
            bool isKiting = string.Equals(normalized, "Kiting", StringComparison.OrdinalIgnoreCase);

            if (!isStandard && !isAggressive && !isDefensive && !isHoldFire && !isKiting)
            {
                return new UnitTacticalStanceResult(false, entityKeyId, normalized, 0,
                    "Unsupported tactical stance '" + normalized + "'. Supported: Standard, Aggressive, Defensive, HoldFire, Kiting.");
            }

            try
            {
                ExistingControllers existing = ExistingControllers.Instance;
                if (existing == null)
                {
                    return new UnitTacticalStanceResult(false, entityKeyId, normalized, 0, "ExistingControllers not ready.");
                }

                EntityController unit = FindEntityByKey(existing, entityKeyId.Trim());
                if (unit == null)
                {
                    return new UnitTacticalStanceResult(false, entityKeyId, normalized, 0, "Unit '" + entityKeyId + "' not found or destroyed.");
                }

                string key = unit.UniqueEntityId.ToString();
                lock (_activeTacticalStances)
                {
                    _activeTacticalStances[key] = normalized;
                }

                if (isHoldFire)
                {
                    TryOrderStop(key);
                }

                return new UnitTacticalStanceResult(true, key, normalized, 1,
                    "Tactical stance '" + normalized + "' assigned to unit '" + key + "'.");
            }
            catch (Exception ex)
            {
                return new UnitTacticalStanceResult(false, entityKeyId, normalized, 0, "Error setting tactical stance: " + ex.Message);
            }
        }

        /// <summary>
        /// Resets or removes the active tactical stance for a specific unit key.
        /// </summary>
        public static void ResetUnitTacticalStance(string entityKeyId)
        {
            if (string.IsNullOrWhiteSpace(entityKeyId))
            {
                return;
            }

            string key = entityKeyId.Trim();
            try
            {
                EntityController unit = FindEntityByKey(ExistingControllers.Instance, key);
                if (unit != null)
                {
                    key = unit.UniqueEntityId.ToString();
                }
            }
            catch
            {
            }

            lock (_activeTacticalStances)
            {
                _activeTacticalStances.Remove(key);
            }
        }

        public static string GetUnitTacticalStance(string entityKeyId)
        {
            if (string.IsNullOrWhiteSpace(entityKeyId))
            {
                return "Standard";
            }

            string key = entityKeyId.Trim();
            try
            {
                EntityController unit = FindEntityByKey(ExistingControllers.Instance, key);
                if (unit != null)
                {
                    key = unit.UniqueEntityId.ToString();
                }
            }
            catch
            {
            }

            lock (_activeTacticalStances)
            {
                string stance;
                return _activeTacticalStances.TryGetValue(key, out stance) && !string.IsNullOrEmpty(stance)
                    ? stance
                    : "Standard";
            }
        }

        /// <summary>
        /// Assigns a tactical combat stance to a group of units.
        /// </summary>
        public static UnitTacticalStanceResult TrySetGroupTacticalStance(IReadOnlyList<string> entityKeyIds, string stanceName)
        {
            if (entityKeyIds == null || entityKeyIds.Count == 0)
            {
                return new UnitTacticalStanceResult(false, string.Empty, stanceName ?? "Standard", 0, "No entity keys provided.");
            }

            int applied = 0;
            string lastErr = string.Empty;
            for (int i = 0; i < entityKeyIds.Count; i++)
            {
                string key = entityKeyIds[i];
                if (string.IsNullOrWhiteSpace(key)) continue;
                UnitTacticalStanceResult res = TrySetUnitTacticalStance(key.Trim(), stanceName);
                if (res.Success) applied++;
                else lastErr = res.Message;
            }

            bool success = applied > 0;
            string msg = success
                ? "Assigned stance '" + stanceName + "' to " + applied + " unit(s)."
                : "Failed assigning stance to group: " + lastErr;
            return new UnitTacticalStanceResult(success, "Group", stanceName, applied, msg);
        }

        /// <summary>
        /// Queries the tactical combat stance census across living units.
        /// Omission counters precede detail collections.
        /// </summary>
        public static TacticalStanceCensus GetTacticalStanceCensus(int maxTrackedUnits = 32)
        {
            var rows = new List<TacticalStanceDiagnosticRow>();
            int totalCount = 0;
            int omittedCount = 0;
            int aggCount = 0;
            int defCount = 0;
            int kiteCount = 0;
            int holdCount = 0;
            int stdCount = 0;

            try
            {
                ExistingControllers existing = ExistingControllers.Instance;
                if (existing != null)
                {
                    var units = new List<EntityController>();
                    if (existing.PlayerUnits() != null) units.AddRange(existing.PlayerUnits());
                    if (existing.AiUnits() != null) units.AddRange(existing.AiUnits());

                    for (int i = 0; i < units.Count; i++)
                    {
                        EntityController unit = units[i];
                        if (unit == null || unit.IsDestroyed || unit.DestroyHasBeenStarted) continue;

                        totalCount++;
                        string key = unit.UniqueEntityId.ToString();
                        string stance;
                        lock (_activeTacticalStances)
                        {
                            if (!_activeTacticalStances.TryGetValue(key, out stance) || string.IsNullOrEmpty(stance))
                            {
                                stance = "Standard";
                            }
                        }

                        if (string.Equals(stance, "Aggressive", StringComparison.OrdinalIgnoreCase)) aggCount++;
                        else if (string.Equals(stance, "Defensive", StringComparison.OrdinalIgnoreCase)) defCount++;
                        else if (string.Equals(stance, "Kiting", StringComparison.OrdinalIgnoreCase)) kiteCount++;
                        else if (string.Equals(stance, "HoldFire", StringComparison.OrdinalIgnoreCase)) holdCount++;
                        else stdCount++;

                        if (rows.Count < maxTrackedUnits)
                        {
                            rows.Add(new TacticalStanceDiagnosticRow(
                                key,
                                ResolveEntityIdString(unit),
                                stance,
                                unit.IsControlledByPlayer,
                                unit.MoveSpeed,
                                unit.WeaponRange));
                        }
                    }
                }
            }
            catch { }

            omittedCount = Math.Max(0, totalCount - rows.Count);
            return new TacticalStanceCensus(
                omittedCount, totalCount, aggCount, defCount, kiteCount, holdCount, stdCount, rows.AsReadOnly());
        }

        /// <summary>
        /// Applies an elite affix/trait to a unit.
        /// Supported affixes: Swift, Juggernaut, Overcharged, Vampiric, Bulwark, Volatile.
        /// </summary>
        public static UnitAffixResult TryApplyUnitAffix(string entityKeyId, string affixName)
        {
            if (string.IsNullOrWhiteSpace(entityKeyId))
            {
                return new UnitAffixResult(false, string.Empty, affixName ?? string.Empty, "Apply", 0, "Entity key ID cannot be empty.");
            }
            if (string.IsNullOrWhiteSpace(affixName))
            {
                return new UnitAffixResult(false, entityKeyId, string.Empty, "Apply", 0, "Affix name cannot be empty.");
            }

            string normalized = affixName.Trim();
            bool isSwift = string.Equals(normalized, "Swift", StringComparison.OrdinalIgnoreCase);
            bool isJuggernaut = string.Equals(normalized, "Juggernaut", StringComparison.OrdinalIgnoreCase);
            bool isOvercharged = string.Equals(normalized, "Overcharged", StringComparison.OrdinalIgnoreCase);
            bool isVampiric = string.Equals(normalized, "Vampiric", StringComparison.OrdinalIgnoreCase);
            bool isBulwark = string.Equals(normalized, "Bulwark", StringComparison.OrdinalIgnoreCase);
            bool isVolatile = string.Equals(normalized, "Volatile", StringComparison.OrdinalIgnoreCase);

            if (!isSwift && !isJuggernaut && !isOvercharged && !isVampiric && !isBulwark && !isVolatile)
            {
                return new UnitAffixResult(false, entityKeyId, normalized, "Apply", 0,
                    "Unsupported unit affix '" + normalized + "'. Supported: Swift, Juggernaut, Overcharged, Vampiric, Bulwark, Volatile.");
            }

            try
            {
                ExistingControllers existing = ExistingControllers.Instance;
                if (existing == null)
                {
                    return new UnitAffixResult(false, entityKeyId, normalized, "Apply", 0, "ExistingControllers not ready.");
                }

                EntityController unit = FindEntityByKey(existing, entityKeyId.Trim());
                if (unit == null)
                {
                    return new UnitAffixResult(false, entityKeyId, normalized, "Apply", 0, "Unit '" + entityKeyId + "' not found or destroyed.");
                }

                string key = unit.UniqueEntityId.ToString();
                int activeCount = 0;
                lock (_activeUnitAffixes)
                {
                    List<string> affixes;
                    if (!_activeUnitAffixes.TryGetValue(key, out affixes))
                    {
                        affixes = new List<string>();
                        _activeUnitAffixes[key] = affixes;
                    }

                    bool exists = false;
                    for (int i = 0; i < affixes.Count; i++)
                    {
                        if (string.Equals(affixes[i], normalized, StringComparison.OrdinalIgnoreCase))
                        {
                            exists = true;
                            break;
                        }
                    }
                    if (!exists)
                    {
                        affixes.Add(normalized);
                    }
                    activeCount = affixes.Count;
                }

                // Apply mechanics fail-closed
                if (isBulwark)
                {
                    unit.ChargeShield(2f, true);
                }
                else if (isJuggernaut)
                {
                    unit.Heal(25f, unit, true, true);
                }

                return new UnitAffixResult(true, key, normalized, "Apply", activeCount,
                    "Affix '" + normalized + "' applied to unit '" + key + "'. Active affixes: " + activeCount + ".");
            }
            catch (Exception ex)
            {
                return new UnitAffixResult(false, entityKeyId, normalized, "Apply", 0, "Error applying unit affix: " + ex.Message);
            }
        }

        /// <summary>
        /// Removes an active elite affix from a unit (or 'All' to clear all affixes).
        /// </summary>
        public static UnitAffixResult TryRemoveUnitAffix(string entityKeyId, string affixName)
        {
            if (string.IsNullOrWhiteSpace(entityKeyId))
            {
                return new UnitAffixResult(false, string.Empty, affixName ?? string.Empty, "Remove", 0, "Entity key ID cannot be empty.");
            }

            string normalized = string.IsNullOrWhiteSpace(affixName) ? "All" : affixName.Trim();
            bool removeAll = string.Equals(normalized, "All", StringComparison.OrdinalIgnoreCase);

            string key = entityKeyId.Trim();
            try
            {
                EntityController unit = FindEntityByKey(ExistingControllers.Instance, key);
                if (unit != null)
                {
                    key = unit.UniqueEntityId.ToString();
                }
            }
            catch
            {
                // Keep the supplied key so numeric runtime keys can still be removed fail-closed.
            }

            int activeCount = 0;
            bool found = false;

            lock (_activeUnitAffixes)
            {
                List<string> affixes;
                if (_activeUnitAffixes.TryGetValue(key, out affixes) && affixes != null)
                {
                    if (removeAll)
                    {
                        affixes.Clear();
                        _activeUnitAffixes.Remove(key);
                        found = true;
                    }
                    else
                    {
                        for (int i = affixes.Count - 1; i >= 0; i--)
                        {
                            if (string.Equals(affixes[i], normalized, StringComparison.OrdinalIgnoreCase))
                            {
                                affixes.RemoveAt(i);
                                found = true;
                            }
                        }
                        if (affixes.Count == 0)
                        {
                            _activeUnitAffixes.Remove(key);
                        }
                    }
                    activeCount = affixes.Count;
                }
            }

            if (!found)
            {
                return new UnitAffixResult(false, key, normalized, "Remove", activeCount,
                    "Affix '" + normalized + "' was not active on unit '" + key + "'.");
            }

            return new UnitAffixResult(true, key, normalized, "Remove", activeCount,
                "Removed affix '" + normalized + "' from unit '" + key + "'. Remaining affixes: " + activeCount + ".");
        }

        /// <summary>
        /// Queries the unit elite affixes census across living units.
        /// Omission counters precede detail collections.
        /// </summary>
        public static UnitAffixCensus GetUnitAffixCensus(int maxTrackedUnits = 32)
        {
            var rows = new List<UnitAffixDiagnosticRow>();
            int totalTrackedUnits = 0;
            int omittedCount = 0;
            int totalActiveAffixes = 0;

            try
            {
                ExistingControllers existing = ExistingControllers.Instance;
                if (existing != null)
                {
                    var units = new List<EntityController>();
                    if (existing.PlayerUnits() != null) units.AddRange(existing.PlayerUnits());
                    if (existing.AiUnits() != null) units.AddRange(existing.AiUnits());

                    for (int i = 0; i < units.Count; i++)
                    {
                        EntityController unit = units[i];
                        if (unit == null || unit.IsDestroyed || unit.DestroyHasBeenStarted) continue;

                        string key = unit.UniqueEntityId.ToString();
                        List<string> affixes = null;
                        lock (_activeUnitAffixes)
                        {
                            List<string> stored;
                            if (_activeUnitAffixes.TryGetValue(key, out stored) && stored != null && stored.Count > 0)
                            {
                                affixes = new List<string>(stored);
                            }
                        }

                        if (affixes != null && affixes.Count > 0)
                        {
                            totalTrackedUnits++;
                            totalActiveAffixes += affixes.Count;
                            if (rows.Count < maxTrackedUnits)
                            {
                                rows.Add(new UnitAffixDiagnosticRow(
                                    key,
                                    ResolveEntityIdString(unit),
                                    unit.IsControlledByPlayer,
                                    affixes.AsReadOnly()));
                            }
                        }
                    }
                }
            }
            catch { }

            omittedCount = Math.Max(0, totalTrackedUnits - rows.Count);
            return new UnitAffixCensus(omittedCount, totalTrackedUnits, totalActiveAffixes, rows.AsReadOnly());
        }

        /// <summary>
        /// Attempts to retrieve active unit affixes for the specified unit key in a thread-safe manner.
        /// </summary>
        public static bool TryGetActiveAffixes(string unitKey, out IReadOnlyList<string> affixes)
        {
            if (string.IsNullOrWhiteSpace(unitKey))
            {
                affixes = null;
                return false;
            }

            lock (_activeUnitAffixes)
            {
                List<string> list;
                if (_activeUnitAffixes.TryGetValue(unitKey.Trim(), out list) && list != null && list.Count > 0)
                {
                    affixes = list.AsReadOnly();
                    return true;
                }
            }

            affixes = null;
            return false;
        }

        /// <summary>
        /// Manually triggers and coordinates an AI assault wave with elevated aggression.
        /// Advances the attack wave counter and orders living AI combat units to attack-move towards player HQ.
        /// </summary>
        public static AiWaveDispatchResult TryDispatchAiAssaultWave(int waveNumber, float aggressionBoost, int targetBaseIndex = 0)
        {
            int wave = waveNumber > 0 ? waveNumber : 1;
            float effBoost = Mathf.Clamp(aggressionBoost > 0.1f ? aggressionBoost : 1.5f, 1.0f, 3.0f);

            try
            {
                // 1. Advance wave counter in AI Director
                TrySetAttackWaveNumber(wave);

                // 2. Adjust aggression in AI Doctrine
                TrySetAiDoctrine("Assault", effBoost);

                // 3. Command AI combat units to attack-move towards player HQ / center ground
                ExistingControllers existing = ExistingControllers.Instance;
                int dispatchedCount = 0;

                if (existing != null && existing.AiUnits() != null)
                {
                    float targetX = 0f;
                    float targetZ = 0f;

                    // Attempt to target player HQ if available
                    EntityController hq = existing.EngineeringHq();
                    if (hq != null && !hq.IsDestroyed)
                    {
                        targetX = hq.Position.x;
                        targetZ = hq.Position.z;
                    }

                    foreach (EntityController aiUnit in existing.AiUnits())
                    {
                        if (aiUnit == null || aiUnit.IsDestroyed || aiUnit.DestroyHasBeenStarted) continue;
                        if (!aiUnit.IsCombatUnit && !aiUnit.CanAttackAndIsAttackEnabled) continue;

                        string key = aiUnit.UniqueEntityId.ToString();
                        UnitCommandResult cmd = TryOrderAttackMove(key, targetX, targetZ);
                        if (cmd.Success)
                        {
                            dispatchedCount++;
                        }
                    }
                }

                return new AiWaveDispatchResult(
                    true,
                    wave,
                    effBoost,
                    dispatchedCount,
                    "AI Assault Wave " + wave + " dispatched to " + dispatchedCount + " unit(s) with aggression boost " + effBoost.ToString("0.0#", System.Globalization.CultureInfo.InvariantCulture) + ".");
            }
            catch (Exception ex)
            {
                return new AiWaveDispatchResult(false, wave, effBoost, 0, "Error dispatching AI assault wave: " + ex.Message);
            }
        }

        /// <summary>
        /// Clears active tactical stances and unit affixes (used for test teardown and match transitions).
        /// </summary>
        /// <summary>
        /// Queries the runtime visual census capturing geometry rendering, rig architecture, and animation states.
        /// </summary>
        public static VisualRenderCensus GetVisualRenderCensus(int maxTrackedUnits = 32)
        {
            return RuntimeVisualCensus.Current(maxTrackedUnits);
        }

        /// <summary>
        /// Inspects deep visual render evidence (renderers, shaders, mesh bounds, LODs), rig hierarchy (armature bones, canonical sockets, socket ambiguity), animation parameters (RCL_IsMoving, RCL_Fire, RCL_Death), and visual director state for a specific entity.
        /// </summary>
        public static DetailedEntityVisualDiagnostic InspectEntityVisual(string entityKeyId)
        {
            return RuntimeVisualCensus.InspectEntity(entityKeyId);
        }

        public static LoadedPrefabVisualAuditCensus GetLoadedPrefabVisualAuditCensus(int maxEntries = 64)
        {
            return RuntimeVisualCensus.GetLoadedPrefabAuditCensus(maxEntries);
        }

        private sealed class AiProfileUnitDefaultsRecord
        {
            public string ProfileId;
            public string Doctrine;
            public float AggressionMultiplier;
            public string DefaultUnitStance;
            public string DefaultUnitLoadout;
            public IReadOnlyList<string> StartingUnitAffixes;
            public IReadOnlyList<string> TargetUnitIds;
            public int StartingUnitVeterancyRank;
        }

        private static readonly object _aiProfileDefaultsGate = new object();
        private static readonly Dictionary<string, AiProfileUnitDefaultsRecord> _aiProfileUnitDefaults =
            new Dictionary<string, AiProfileUnitDefaultsRecord>(StringComparer.OrdinalIgnoreCase);
        private static string _activeAiProfileId = string.Empty;

        public static void RegisterAiProfileUnitDefaults(
            string profileId,
            string doctrine,
            float? aggressionMultiplier,
            string defaultUnitStance,
            string defaultUnitLoadout,
            IReadOnlyList<string> startingUnitAffixes,
            int? startingUnitVeterancyRank,
            bool setActive = false,
            IReadOnlyList<string> targetUnitIds = null)
        {
            if (string.IsNullOrWhiteSpace(profileId))
            {
                return;
            }

            string normalizedId = profileId.Trim();
            var affixes = new List<string>();
            if (startingUnitAffixes != null)
            {
                for (int i = 0; i < startingUnitAffixes.Count; i++)
                {
                    if (!string.IsNullOrWhiteSpace(startingUnitAffixes[i]))
                    {
                        affixes.Add(startingUnitAffixes[i].Trim());
                    }
                }
            }

            var targetIds = new List<string>();
            if (targetUnitIds != null)
            {
                for (int i = 0; i < targetUnitIds.Count; i++)
                {
                    if (!string.IsNullOrWhiteSpace(targetUnitIds[i]))
                    {
                        string targetId = targetUnitIds[i].Trim();
                        bool alreadyAdded = false;
                        for (int targetIndex = 0; targetIndex < targetIds.Count; targetIndex++)
                        {
                            if (String.Equals(targetIds[targetIndex], targetId, StringComparison.OrdinalIgnoreCase))
                            {
                                alreadyAdded = true;
                                break;
                            }
                        }
                        if (!alreadyAdded)
                        {
                            targetIds.Add(targetId);
                        }
                    }
                }
            }

            var record = new AiProfileUnitDefaultsRecord
            {
                ProfileId = normalizedId,
                Doctrine = string.IsNullOrWhiteSpace(doctrine) ? "Standard" : doctrine.Trim(),
                AggressionMultiplier = aggressionMultiplier.HasValue && !float.IsNaN(aggressionMultiplier.Value) && !float.IsInfinity(aggressionMultiplier.Value)
                    ? Mathf.Clamp(aggressionMultiplier.Value, 0.1f, 5.0f)
                    : 1.0f,
                DefaultUnitStance = string.IsNullOrWhiteSpace(defaultUnitStance) ? string.Empty : defaultUnitStance.Trim(),
                DefaultUnitLoadout = string.IsNullOrWhiteSpace(defaultUnitLoadout) ? string.Empty : defaultUnitLoadout.Trim(),
                StartingUnitAffixes = affixes.AsReadOnly(),
                TargetUnitIds = targetIds.AsReadOnly(),
                StartingUnitVeterancyRank = startingUnitVeterancyRank.HasValue
                    ? Mathf.Clamp(startingUnitVeterancyRank.Value, 0, 5)
                    : 0,
            };

            lock (_aiProfileDefaultsGate)
            {
                _aiProfileUnitDefaults[normalizedId] = record;
                if (setActive)
                {
                    _activeAiProfileId = normalizedId;
                }
            }
        }

        public static void ClearAiProfileUnitDefaults()
        {
            lock (_aiProfileDefaultsGate)
            {
                _aiProfileUnitDefaults.Clear();
                _activeAiProfileId = string.Empty;
            }
        }

        private static bool HasAiProfileDefaults(AiProfileUnitDefaultsRecord record)
        {
            return record != null &&
                (!string.IsNullOrEmpty(record.DefaultUnitStance) ||
                 !string.IsNullOrEmpty(record.DefaultUnitLoadout) ||
                 (record.StartingUnitAffixes != null && record.StartingUnitAffixes.Count > 0) ||
                 record.StartingUnitVeterancyRank > 0);
        }

        private static bool ProfileTargetsUnit(AiProfileUnitDefaultsRecord record, string unitId)
        {
            if (record == null || string.IsNullOrWhiteSpace(unitId) ||
                record.TargetUnitIds == null || record.TargetUnitIds.Count == 0)
            {
                return false;
            }

            for (int i = 0; i < record.TargetUnitIds.Count; i++)
            {
                if (String.Equals(record.TargetUnitIds[i], unitId, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        public static int ApplyAiProfileUnitDefaultsToEntity(object entityObj)
        {
            if (entityObj == null)
            {
                return 0;
            }

            try
            {
                EntityController unit = entityObj as EntityController;
                if (unit == null || unit.IsDestroyed || unit.DestroyHasBeenStarted || unit.IsControlledByPlayer)
                {
                    return 0;
                }

                string unitId = ResolveEntityIdString(unit);
                AiProfileUnitDefaultsRecord activeDefaults = null;
                lock (_aiProfileDefaultsGate)
                {
                    // Prefer the profile whose declared AI roster contains this product.
                    // The previous global "last profile wins" fallback applied one profile's
                    // stance/loadout to every AI unit when several scoped profiles were loaded.
                    foreach (AiProfileUnitDefaultsRecord candidate in _aiProfileUnitDefaults.Values)
                    {
                        if (ProfileTargetsUnit(candidate, unitId) &&
                            (activeDefaults == null || String.Equals(candidate.ProfileId, _activeAiProfileId, StringComparison.OrdinalIgnoreCase)))
                        {
                            activeDefaults = candidate;
                        }
                    }

                    if (activeDefaults == null && !string.IsNullOrEmpty(_activeAiProfileId))
                    {
                        AiProfileUnitDefaultsRecord activeCandidate;
                        if (_aiProfileUnitDefaults.TryGetValue(_activeAiProfileId, out activeCandidate) &&
                            (activeCandidate.TargetUnitIds == null || activeCandidate.TargetUnitIds.Count == 0))
                        {
                            activeDefaults = activeCandidate;
                        }
                    }

                    if (activeDefaults == null)
                    {
                        // Preserve the legacy API behavior for callers that do not provide
                        // roster metadata, but never use a scoped profile as a global fallback.
                        foreach (AiProfileUnitDefaultsRecord candidate in _aiProfileUnitDefaults.Values)
                        {
                            if ((candidate.TargetUnitIds == null || candidate.TargetUnitIds.Count == 0) &&
                                HasAiProfileDefaults(candidate))
                            {
                                activeDefaults = candidate;
                                break;
                            }
                        }
                    }
                }

                if (activeDefaults == null)
                {
                    return 0;
                }

                string key = unit.UniqueEntityId.ToString();
                UnitRuntimeMechanicsProfile unitProfile;
                RuntimeUnitMechanicsDirector.TryGetProfile(unitId, out unitProfile);

                bool modified = false;

                // Apply AI profile default stance if the unit has no explicit unit-level default stance and is still Standard
                if (!string.IsNullOrEmpty(activeDefaults.DefaultUnitStance) &&
                    (unitProfile == null || string.IsNullOrWhiteSpace(unitProfile.DefaultTacticalStance)))
                {
                    string currentStance = GetUnitTacticalStance(key);
                    if (string.Equals(currentStance, "Standard", StringComparison.OrdinalIgnoreCase))
                    {
                        if (TrySetUnitTacticalStance(key, activeDefaults.DefaultUnitStance).Success)
                        {
                            modified = true;
                        }
                    }
                }

                // Apply AI profile default loadout if the unit has no explicit unit-level default loadout
                if (!string.IsNullOrEmpty(activeDefaults.DefaultUnitLoadout) &&
                    (unitProfile == null || string.IsNullOrWhiteSpace(unitProfile.DefaultLoadout)))
                {
                    string currentVariant = GetActiveUnitVariant(key);
                    if (string.IsNullOrEmpty(currentVariant) || string.Equals(currentVariant, "Standard", StringComparison.OrdinalIgnoreCase))
                    {
                        if (TryApplyUnitLoadout(key, activeDefaults.DefaultUnitLoadout).Success)
                        {
                            modified = true;
                        }
                    }
                }

                // Apply AI profile starting affixes
                if (activeDefaults.StartingUnitAffixes != null && activeDefaults.StartingUnitAffixes.Count > 0)
                {
                    for (int i = 0; i < activeDefaults.StartingUnitAffixes.Count; i++)
                    {
                        string affix = activeDefaults.StartingUnitAffixes[i];
                        if (!string.IsNullOrWhiteSpace(affix))
                        {
                            if (TryApplyUnitAffix(key, affix).Success)
                            {
                                modified = true;
                            }
                        }
                    }
                }

                // Apply AI profile starting veterancy rank if higher than current rank
                if (activeDefaults.StartingUnitVeterancyRank > 0)
                {
                    int targetRank = activeDefaults.StartingUnitVeterancyRank;
                    if (unitProfile != null && unitProfile.StartingVeterancyRank.HasValue && unitProfile.StartingVeterancyRank.Value > targetRank)
                    {
                        targetRank = unitProfile.StartingVeterancyRank.Value;
                    }
                    int resultingRank;
                    if (RuntimeUnitMechanicsDirector.TrySetUnitVeterancyRank(unit, targetRank, out resultingRank))
                    {
                        modified = true;
                    }
                }

                return modified ? 1 : 0;
            }
            catch
            {
                return 0;
            }
        }

        public static int ApplyActiveAiUnitDefaultsToAiUnits()
        {
            int appliedCount = 0;
            try
            {
                ExistingControllers existing = ExistingControllers.Instance;
                if (existing != null && existing.AiUnits() != null)
                {
                    foreach (EntityController aiUnit in existing.AiUnits())
                    {
                        if (aiUnit == null || aiUnit.IsDestroyed || aiUnit.DestroyHasBeenStarted)
                        {
                            continue;
                        }
                        appliedCount += ApplyAiProfileUnitDefaultsToEntity(aiUnit);
                    }
                }
            }
            catch
            {
            }
            return appliedCount;
        }

        public static LoaderRuntimeAuditCensus GetLoaderRuntimeAuditCensus(
            LoaderDiagnosticsSnapshot snapshot,
            int maxUnits = 64,
            int maxAiProfiles = 32)
        {
            int capUnits = maxUnits < 1 ? 1 : (maxUnits > 256 ? 256 : maxUnits);
            int capProfiles = maxAiProfiles < 1 ? 1 : (maxAiProfiles > 128 ? 128 : maxAiProfiles);

            var livingCountsById = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            try
            {
                ExistingControllers existing = ExistingControllers.Instance;
                if (existing != null)
                {
                    var allEntities = new List<EntityController>();
                    if (existing.PlayerUnits() != null) allEntities.AddRange(existing.PlayerUnits());
                    if (existing.AiUnits() != null) allEntities.AddRange(existing.AiUnits());
                    if (existing.PlayerBuildings() != null) allEntities.AddRange(existing.PlayerBuildings());
                    if (existing.AiBuildings() != null) allEntities.AddRange(existing.AiBuildings());

                    for (int i = 0; i < allEntities.Count; i++)
                    {
                        EntityController entity = allEntities[i];
                        if (entity == null || entity.IsDestroyed || entity.DestroyHasBeenStarted) continue;
                        string eid = ResolveEntityIdString(entity);
                        if (!string.IsNullOrEmpty(eid))
                        {
                            int cur;
                            livingCountsById.TryGetValue(eid, out cur);
                            livingCountsById[eid] = cur + 1;
                        }
                    }
                }
            }
            catch { }

            var factoryMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (snapshot != null && snapshot.UnitFactories != null)
            {
                for (int i = 0; i < snapshot.UnitFactories.Count; i++)
                {
                    UnitFactoryDiagnosticReport uf = snapshot.UnitFactories[i];
                    if (uf != null && !string.IsNullOrEmpty(uf.UnitId))
                    {
                        factoryMap[uf.UnitId] = uf.FactoryId ?? string.Empty;
                    }
                }
            }

            var unitRows = new List<LoaderUnitAuditRow>();
            int totalUnits = snapshot != null && snapshot.Units != null ? snapshot.Units.Count : 0;
            int warmedPoolUnits = 0;

            if (snapshot != null && snapshot.Units != null)
            {
                for (int i = 0; i < snapshot.Units.Count; i++)
                {
                    UnitDiagnosticReport u = snapshot.Units[i];
                    if (u == null || string.IsNullOrEmpty(u.UnitId)) continue;

                    string factoryId;
                    factoryMap.TryGetValue(u.UnitId, out factoryId);
                    bool isBuilding = string.IsNullOrEmpty(factoryId);

                    UnitRuntimeMechanicsProfile mechProfile;
                    bool hasMech = RuntimeUnitMechanicsDirector.TryGetProfile(u.UnitId, out mechProfile) && mechProfile != null;
                    string defStance = hasMech && !string.IsNullOrEmpty(mechProfile.DefaultTacticalStance) ? mechProfile.DefaultTacticalStance : string.Empty;
                    string defLoadout = hasMech && !string.IsNullOrEmpty(mechProfile.DefaultLoadout) ? mechProfile.DefaultLoadout : string.Empty;
                    int startVet = hasMech && mechProfile.StartingVeterancyRank.HasValue ? mechProfile.StartingVeterancyRank.Value : 0;

                    LoadedPrefabVisualAuditRow auditRow;
                    bool audited = RuntimeVisualCensus.TryGetLoadedPrefabAudit(u.UnitId, out auditRow);
                    string rigType = audited && !string.IsNullOrEmpty(auditRow.RigType) ? auditRow.RigType : "StaticMesh";
                    bool ambig = audited && auditRow.SocketAmbiguityCount > 0;

                    int quota = 0;
                    int warmed = 0;
                    lock (_prespawnPoolQuotas)
                    {
                        _prespawnPoolQuotas.TryGetValue(u.UnitId, out quota);
                        _prespawnPoolWarmed.TryGetValue(u.UnitId, out warmed);
                    }
                    if (quota > 0 || warmed > 0)
                    {
                        warmedPoolUnits++;
                    }

                    int living;
                    livingCountsById.TryGetValue(u.UnitId, out living);

                    if (unitRows.Count < capUnits)
                    {
                        unitRows.Add(new LoaderUnitAuditRow(
                            u.UnitId,
                            u.PackId ?? string.Empty,
                            factoryId ?? string.Empty,
                            isBuilding,
                            hasMech,
                            u.VariantCount,
                            audited,
                            rigType,
                            ambig,
                            quota,
                            warmed,
                            living,
                            defStance,
                            defLoadout,
                            startVet));
                    }
                }
            }

            var aiRows = new List<LoaderAiProfileAuditRow>();
            int totalAiProfiles = snapshot != null && snapshot.AiProfiles != null ? snapshot.AiProfiles.Count : 0;
            int activeAiInstances = 0;

            if (snapshot != null && snapshot.AiProfiles != null)
            {
                for (int i = 0; i < snapshot.AiProfiles.Count; i++)
                {
                    AiProfileDiagnosticReport p = snapshot.AiProfiles[i];
                    if (p == null || string.IsNullOrEmpty(p.ProfileId)) continue;

                    activeAiInstances += Math.Max(0, p.ActiveAiInstances);

                    AiProfileUnitDefaultsRecord defRecord = null;
                    lock (_aiProfileDefaultsGate)
                    {
                        _aiProfileUnitDefaults.TryGetValue(p.ProfileId, out defRecord);
                    }

                    string doctrine = defRecord != null && !string.IsNullOrEmpty(defRecord.Doctrine)
                        ? defRecord.Doctrine
                        : GetActiveAiDoctrine();
                    float aggMult = defRecord != null
                        ? defRecord.AggressionMultiplier
                        : GetAiAggressionMultiplier();
                    string defStance = defRecord != null ? defRecord.DefaultUnitStance : string.Empty;
                    string defLoadout = defRecord != null ? defRecord.DefaultUnitLoadout : string.Empty;
                    IReadOnlyList<string> startAffixes = defRecord != null && defRecord.StartingUnitAffixes != null
                        ? defRecord.StartingUnitAffixes
                        : Array.Empty<string>();
                    int startVet = defRecord != null ? defRecord.StartingUnitVeterancyRank : 0;

                    if (aiRows.Count < capProfiles)
                    {
                        aiRows.Add(new LoaderAiProfileAuditRow(
                            p.ProfileId,
                            p.PackId ?? string.Empty,
                            p.PrefabAssetName ?? string.Empty,
                            p.ModuleIds != null ? p.ModuleIds.Count : 0,
                            p.ActiveAiInstances,
                            doctrine,
                            aggMult,
                            defStance,
                            defLoadout,
                            startAffixes,
                            startVet));
                    }
                }
            }

            int omittedUnits = Math.Max(0, totalUnits - unitRows.Count);
            int omittedProfiles = Math.Max(0, totalAiProfiles - aiRows.Count);

            return new LoaderRuntimeAuditCensus(
                omittedUnits,
                totalUnits,
                omittedProfiles,
                totalAiProfiles,
                RuntimeUnitMechanicsDirector.RegisteredProfileCount,
                RuntimeVisualCensus.AuditedPrefabCount,
                warmedPoolUnits,
                activeAiInstances,
                unitRows.AsReadOnly(),
                aiRows.AsReadOnly());
        }

        public static LoaderReconciliationResult TryReconcileLoaderRuntimeState(LoaderDiagnosticsSnapshot snapshot)
        {
            int reconciledUnits = 0;
            int warmedPools = 0;
            int attachedMechanics = 0;
            int attachedVisuals = 0;
            int appliedAiDefaults = 0;
            int reconciledAiProfiles = 0;

            try
            {
                // 1. Warm pre-spawn pools for all registered units in snapshot & mechanics profiles
                var targetUnits = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (snapshot != null && snapshot.Units != null)
                {
                    for (int i = 0; i < snapshot.Units.Count; i++)
                    {
                        UnitDiagnosticReport u = snapshot.Units[i];
                        if (u != null && !string.IsNullOrWhiteSpace(u.UnitId))
                        {
                            targetUnits.Add(u.UnitId.Trim());
                        }
                    }
                }
                IReadOnlyList<string> mechIds = RuntimeUnitMechanicsDirector.RegisteredProfileIds;
                if (mechIds != null)
                {
                    for (int i = 0; i < mechIds.Count; i++)
                    {
                        if (!string.IsNullOrWhiteSpace(mechIds[i]))
                        {
                            targetUnits.Add(mechIds[i].Trim());
                        }
                    }
                }

                foreach (string unitId in targetUnits)
                {
                    reconciledUnits++;
                    int quota = 0;
                    lock (_prespawnPoolQuotas)
                    {
                        _prespawnPoolQuotas.TryGetValue(unitId, out quota);
                    }
                    int targetWarm = quota > 0 ? quota : 4;
                    if (TryWarmPrespawnPool(unitId, targetWarm).Success)
                    {
                        warmedPools++;
                    }
                }

                // 2. Reconcile active AI profiles and doctrine
                AiProfileUnitDefaultsRecord activeAiRecord = null;
                lock (_aiProfileDefaultsGate)
                {
                    reconciledAiProfiles = _aiProfileUnitDefaults.Count;
                    if (!string.IsNullOrEmpty(_activeAiProfileId))
                    {
                        _aiProfileUnitDefaults.TryGetValue(_activeAiProfileId, out activeAiRecord);
                    }
                }
                if (snapshot != null && snapshot.AiProfiles != null)
                {
                    reconciledAiProfiles = Math.Max(reconciledAiProfiles, snapshot.AiProfiles.Count);
                }
                if (activeAiRecord != null && !string.IsNullOrEmpty(activeAiRecord.Doctrine))
                {
                    TrySetAiDoctrine(activeAiRecord.Doctrine, activeAiRecord.AggressionMultiplier);
                }

                // 3. Reconcile living entities on the battlefield (VisualDirector + UnitMechanicsDirector + AI squad defaults)
                ExistingControllers existing = ExistingControllers.Instance;
                if (existing != null)
                {
                    var entities = new List<EntityController>();
                    if (existing.PlayerUnits() != null) entities.AddRange(existing.PlayerUnits());
                    if (existing.AiUnits() != null) entities.AddRange(existing.AiUnits());
                    if (existing.PlayerBuildings() != null) entities.AddRange(existing.PlayerBuildings());
                    if (existing.AiBuildings() != null) entities.AddRange(existing.AiBuildings());

                    for (int i = 0; i < entities.Count; i++)
                    {
                        EntityController entity = entities[i];
                        if (entity == null || entity.IsDestroyed || entity.DestroyHasBeenStarted)
                        {
                            continue;
                        }

                        try
                        {
                            Type visualDirType = Type.GetType("RogueCommandExpansionLoader.Runtime.Visual.RuntimeVisualDirector, RogueCommandExpansionLoader", false);
                            if (visualDirType != null)
                            {
                                MethodInfo attachMethod = visualDirType.GetMethod("AttachIfApplicable", BindingFlags.Public | BindingFlags.Static);
                                if (attachMethod != null)
                                {
                                    attachMethod.Invoke(null, new object[] { entity });
                                }
                                if (entity.gameObject != null)
                                {
                                    MethodInfo getCompMethod = typeof(GameObject).GetMethod("GetComponent", new[] { typeof(Type) });
                                    object comp = getCompMethod != null
                                        ? getCompMethod.Invoke(entity.gameObject, new object[] { visualDirType })
                                        : entity.gameObject.GetComponent(visualDirType.Name);
                                    if (comp != null)
                                    {
                                        attachedVisuals++;
                                    }
                                }
                            }
                        }
                        catch { }

                        try
                        {
                            RuntimeUnitMechanicsDirector.AttachIfApplicable(entity);
                            if (entity.gameObject != null)
                            {
                                RuntimeUnitMechanicsDirector mechDir = entity.gameObject.GetComponent<RuntimeUnitMechanicsDirector>();
                                if (mechDir != null)
                                {
                                    mechDir.TickMechanics();
                                    attachedMechanics++;
                                }
                            }
                        }
                        catch { }

                        if (!entity.IsControlledByPlayer)
                        {
                            appliedAiDefaults += ApplyAiProfileUnitDefaultsToEntity(entity);
                        }
                    }
                }

                string msg = "Reconciled " + reconciledUnits + " unit(s), warmed " + warmedPools +
                             " pool(s), synchronized " + attachedMechanics + " mechanics director(s), " +
                             attachedVisuals + " visual director(s), and applied AI squad defaults to " +
                             appliedAiDefaults + " AI unit(s) across " + reconciledAiProfiles + " AI profile(s).";
                return new LoaderReconciliationResult(
                    true,
                    reconciledUnits,
                    warmedPools,
                    attachedMechanics,
                    attachedVisuals,
                    appliedAiDefaults,
                    reconciledAiProfiles,
                    msg);
            }
            catch (Exception ex)
            {
                return new LoaderReconciliationResult(
                    false,
                    reconciledUnits,
                    warmedPools,
                    attachedMechanics,
                    attachedVisuals,
                    appliedAiDefaults,
                    reconciledAiProfiles,
                    "Error reconciling loader runtime state: " + ex.Message);
            }
        }

        public static void ClearTacticalStancesAndAffixes()
        {
            lock (_activeTacticalStances)
            {
                _activeTacticalStances.Clear();
            }
            lock (_activeUnitAffixes)
            {
                _activeUnitAffixes.Clear();
            }
            lock (_commandLoopGate)
            {
                _lastUnitCommandKind.Clear();
                _lastUnitCommandProcessingType.Clear();
                _lastUnitCommandTag.Clear();
                _unitCommandStepCounts.Clear();
                _factoryProductionCounts.Clear();
                _lastPlacementGhostEntityId = string.Empty;
            }
            ClearAiProfileUnitDefaults();
        }

        public static void OnBattlefieldRegistryReset()
        {
            lock (_activeTacticalStances)
            {
                _activeTacticalStances.Clear();
            }
            lock (_activeUnitAffixes)
            {
                _activeUnitAffixes.Clear();
            }
            lock (_activeUnitVariants)
            {
                _activeUnitVariants.Clear();
                _activeUnitLoadoutStats.Clear();
            }
            lock (_commandLoopGate)
            {
                _lastUnitCommandKind.Clear();
                _lastUnitCommandProcessingType.Clear();
                _lastUnitCommandTag.Clear();
                _unitCommandStepCounts.Clear();
                _factoryProductionCounts.Clear();
                _lastPlacementGhostEntityId = string.Empty;
            }
            Modules.CompositeAiRuntimeBehaviour.NotifyBattlefieldRegistryReset();
        }

        public static void OnEntityDestroyed(string entityKeyId)
        {
            if (string.IsNullOrWhiteSpace(entityKeyId)) return;
            string key = entityKeyId.Trim();
            lock (_activeTacticalStances)
            {
                _activeTacticalStances.Remove(key);
            }
            lock (_activeUnitAffixes)
            {
                _activeUnitAffixes.Remove(key);
            }
            lock (_activeUnitVariants)
            {
                _activeUnitVariants.Remove(key);
                _activeUnitLoadoutStats.Remove(key);
            }
            lock (_commandLoopGate)
            {
                _lastUnitCommandKind.Remove(key);
                _lastUnitCommandProcessingType.Remove(key);
                _lastUnitCommandTag.Remove(key);
                _unitCommandStepCounts.Remove(key);
                _factoryProductionCounts.Remove(key);
            }
            int parsedKey;
            if (int.TryParse(key, out parsedKey) && parsedKey > 0)
            {
                Modules.CompositeAiRuntimeBehaviour.NotifyUnitLeaseInvalidated(parsedKey);
            }
        }

        public static void OnPlacementGhostCreated(string entityId, GameObject ghost)
        {
            lock (_commandLoopGate)
            {
                _lastPlacementGhostEntityId = string.IsNullOrWhiteSpace(entityId) ? string.Empty : entityId.Trim();
            }
        }

        public static string GetLastPlacementGhostEntityId()
        {
            lock (_commandLoopGate)
            {
                return _lastPlacementGhostEntityId ?? string.Empty;
            }
        }

        public static void OnFactoryProduced(object factoryObj, bool instantProduction, bool forFree, bool doNotTriggerEvent)
        {
            if (factoryObj == null) return;
            int keyInt = RuntimeUnitMechanicsDirector.GetEntityTrackingKey(factoryObj);
            string factoryId = string.Empty;
            try
            {
                PropertyInfo prop = factoryObj.GetType().GetProperty("EntityId") ?? factoryObj.GetType().GetProperty("entityId");
                if (prop != null) factoryId = prop.GetValue(factoryObj, null) as string ?? string.Empty;
                if (string.IsNullOrWhiteSpace(factoryId))
                {
                    FieldInfo field = factoryObj.GetType().GetField("entityId") ?? factoryObj.GetType().GetField("EntityId");
                    if (field != null) factoryId = field.GetValue(factoryObj) as string ?? string.Empty;
                }
            }
            catch { }

            lock (_commandLoopGate)
            {
                if (keyInt != 0)
                {
                    string key = keyInt.ToString();
                    int current;
                    _factoryProductionCounts.TryGetValue(key, out current);
                    _factoryProductionCounts[key] = current + 1;
                }

                if (!string.IsNullOrWhiteSpace(factoryId))
                {
                    string fKey = factoryId.Trim();
                    if (keyInt == 0 || !string.Equals(fKey, keyInt.ToString(), StringComparison.OrdinalIgnoreCase))
                    {
                        int currentById;
                        _factoryProductionCounts.TryGetValue(fKey, out currentById);
                        _factoryProductionCounts[fKey] = currentById + 1;
                    }
                }
            }
        }

        public static int GetFactoryProducedCount(string entityKeyId)
        {
            if (string.IsNullOrWhiteSpace(entityKeyId)) return 0;
            lock (_commandLoopGate)
            {
                int count;
                return _factoryProductionCounts.TryGetValue(entityKeyId.Trim(), out count) ? count : 0;
            }
        }

        public static int GetFactoryProducedCountByEntityId(string entityId)
        {
            return GetFactoryProducedCount(entityId);
        }

        public static void OnEntityCommandExecuted(object entityObj, string commandKind, string processingType, string commandTag = "")
        {
            if (entityObj == null) return;
            int keyInt = RuntimeUnitMechanicsDirector.GetEntityTrackingKey(entityObj);
            if (keyInt == 0) return;
            string key = keyInt.ToString();
            string cleanTag = string.IsNullOrWhiteSpace(commandTag) ? string.Empty : commandTag.Trim();
            lock (_commandLoopGate)
            {
                _lastUnitCommandKind[key] = string.IsNullOrWhiteSpace(commandKind) ? "Unknown" : commandKind.Trim();
                _lastUnitCommandProcessingType[key] = string.IsNullOrWhiteSpace(processingType) ? "ExecuteAndCancelCurrentCommand" : processingType.Trim();
                _lastUnitCommandTag[key] = cleanTag;
            }
            Modules.CompositeAiRuntimeBehaviour.NotifyUnitCommandExecuted(keyInt, cleanTag);
        }

        public static void OnEntityCommandChainStep(object entityObj)
        {
            if (entityObj == null) return;
            int keyInt = RuntimeUnitMechanicsDirector.GetEntityTrackingKey(entityObj);
            if (keyInt == 0) return;
            string key = keyInt.ToString();
            lock (_commandLoopGate)
            {
                int steps;
                _unitCommandStepCounts.TryGetValue(key, out steps);
                _unitCommandStepCounts[key] = steps + 1;
            }
        }

        public static void OnEntityCommandReset(object entityObj)
        {
            if (entityObj == null) return;
            int keyInt = RuntimeUnitMechanicsDirector.GetEntityTrackingKey(entityObj);
            if (keyInt == 0) return;
            string key = keyInt.ToString();
            lock (_commandLoopGate)
            {
                _lastUnitCommandKind[key] = "DoNothing";
                _lastUnitCommandTag.Remove(key);
            }
            Modules.CompositeAiRuntimeBehaviour.NotifyUnitLeaseInvalidated(keyInt);
        }

        public static void OnEntityCommandChainDeleted(object entityObj)
        {
            if (entityObj == null) return;
            int keyInt = RuntimeUnitMechanicsDirector.GetEntityTrackingKey(entityObj);
            if (keyInt == 0) return;
            string key = keyInt.ToString();
            lock (_commandLoopGate)
            {
                _lastUnitCommandKind[key] = "DoNothing";
                _lastUnitCommandProcessingType[key] = "Cleared";
                _lastUnitCommandTag.Remove(key);
            }
            Modules.CompositeAiRuntimeBehaviour.NotifyUnitLeaseInvalidated(keyInt);
        }

        public static string GetLastUnitCommandKind(string entityKeyId)
        {
            if (string.IsNullOrWhiteSpace(entityKeyId)) return string.Empty;
            lock (_commandLoopGate)
            {
                string kind;
                return _lastUnitCommandKind.TryGetValue(entityKeyId.Trim(), out kind) ? kind : string.Empty;
            }
        }

        public static string GetLastUnitCommandProcessingType(string entityKeyId)
        {
            if (string.IsNullOrWhiteSpace(entityKeyId)) return string.Empty;
            lock (_commandLoopGate)
            {
                string proc;
                return _lastUnitCommandProcessingType.TryGetValue(entityKeyId.Trim(), out proc) ? proc : string.Empty;
            }
        }

        public static string GetLastUnitCommandTag(string entityKeyId)
        {
            if (string.IsNullOrWhiteSpace(entityKeyId)) return string.Empty;
            lock (_commandLoopGate)
            {
                string tag;
                return _lastUnitCommandTag.TryGetValue(entityKeyId.Trim(), out tag) ? tag : string.Empty;
            }
        }

        public static int GetUnitCommandStepCount(string entityKeyId)
        {
            if (string.IsNullOrWhiteSpace(entityKeyId)) return 0;
            lock (_commandLoopGate)
            {
                int steps;
                return _unitCommandStepCounts.TryGetValue(entityKeyId.Trim(), out steps) ? steps : 0;
            }
        }

        public static int GetLiveMechanicsDirectorCount()
        {
            int count = 0;
            try
            {
                ExistingControllers existing = ExistingControllers.Instance;
                if (existing != null)
                {
                    List<EntityController> units = new List<EntityController>();
                    if (existing.PlayerUnits() != null) units.AddRange(existing.PlayerUnits());
                    if (existing.AiUnits() != null) units.AddRange(existing.AiUnits());
                    for (int i = 0; i < units.Count; i++)
                    {
                        if (units[i] != null && !units[i].IsDestroyed && units[i].GetComponent<RuntimeUnitMechanicsDirector>() != null)
                            count++;
                    }
                }
            }
            catch { }
            return count;
        }

        public static int GetLiveVisualDirectorCount()
        {
            int count = 0;
            try
            {
                ExistingControllers existing = ExistingControllers.Instance;
                if (existing != null)
                {
                    List<EntityController> units = new List<EntityController>();
                    if (existing.PlayerUnits() != null) units.AddRange(existing.PlayerUnits());
                    if (existing.AiUnits() != null) units.AddRange(existing.AiUnits());
                    for (int i = 0; i < units.Count; i++)
                    {
                        if (units[i] != null && !units[i].IsDestroyed)
                        {
                            if (FindRuntimeVisualDirector(units[i]) != null)
                            {
                                count++;
                            }
                        }
                    }
                }
            }
            catch { }
            return count;
        }

        public static int GetLiveJammedTurretCount()
        {
            int count = 0;
            try
            {
                ExistingControllers existing = ExistingControllers.Instance;
                if (existing != null)
                {
                    HashSet<EntityController> pUnits = existing.PlayerUnits();
                    HashSet<EntityController> aUnits = existing.AiUnits();
                    int cap = (pUnits != null ? pUnits.Count : 0) + (aUnits != null ? aUnits.Count : 0);
                    List<EntityController> units = new List<EntityController>(cap);
                    if (pUnits != null) units.AddRange(pUnits);
                    if (aUnits != null) units.AddRange(aUnits);
                    for (int i = 0; i < units.Count; i++)
                    {
                        if (units[i] != null && !units[i].IsDestroyed)
                        {
                            Component vd = FindRuntimeVisualDirector(units[i]);
                            if (vd != null)
                            {
                                if (vd is Visual.RuntimeVisualDirector rvd)
                                {
                                    if (rvd.IsTurretJammed) count++;
                                }
                                else
                                {
                                    PropertyInfo p = vd.GetType().GetProperty("IsTurretJammed");
                                    if (p != null && (bool)p.GetValue(vd, null)) count++;
                                }
                            }
                        }
                    }
                }
            }
            catch { }
            return count;
        }

        public static int GetLiveRegeneratingShieldCount()
        {
            int count = 0;
            try
            {
                ExistingControllers existing = ExistingControllers.Instance;
                if (existing != null)
                {
                    HashSet<EntityController> pUnits = existing.PlayerUnits();
                    HashSet<EntityController> aUnits = existing.AiUnits();
                    int cap = (pUnits != null ? pUnits.Count : 0) + (aUnits != null ? aUnits.Count : 0);
                    List<EntityController> units = new List<EntityController>(cap);
                    if (pUnits != null) units.AddRange(pUnits);
                    if (aUnits != null) units.AddRange(aUnits);
                    for (int i = 0; i < units.Count; i++)
                    {
                        if (units[i] != null && !units[i].IsDestroyed && units[i].MaxShield > 0f && units[i].CurrentShield < units[i].MaxShield)
                        {
                            string uid = ResolveEntityIdString(units[i]);
                            UnitRuntimeMechanicsProfile profile;
                            if (!string.IsNullOrEmpty(uid) && RuntimeUnitMechanicsDirector.TryGetProfile(uid, out profile) && profile?.ShieldRegen != null)
                                count++;
                        }
                    }
                }
            }
            catch { }
            return count;
        }

        public static UnitMechanicsCensus GetUnitMechanicsCensus(int maxUnits = 64)
        {
            int cap = Math.Max(1, Math.Min(512, maxUnits));
            var rows = new List<UnitMechanicsCensusRow>();

            int totalTrackedUnits = 0;
            int totalMechanicsInstances = 0;
            int activeHealerCount = 0;
            int activeAuraCount = 0;
            int activeShieldRegenCount = 0;

            try
            {
                ExistingControllers existing = ExistingControllers.Instance;
                if (existing != null)
                {
                    HashSet<EntityController> pUnits = existing.PlayerUnits();
                    HashSet<EntityController> aUnits = existing.AiUnits();
                    int capUnits = (pUnits != null ? pUnits.Count : 0) + (aUnits != null ? aUnits.Count : 0);
                    List<EntityController> allUnits = new List<EntityController>(capUnits);
                    if (pUnits != null) allUnits.AddRange(pUnits);
                    if (aUnits != null) allUnits.AddRange(aUnits);

                    for (int i = 0; i < allUnits.Count; i++)
                    {
                        EntityController unit = allUnits[i];
                        if (unit == null || unit.gameObject == null || unit.IsDestroyed) continue;

                        totalTrackedUnits++;
                        string key = unit.UniqueEntityId.ToString();
                        string unitId = ResolveEntityIdString(unit);

                        UnitRuntimeMechanicsProfile profile = null;
                        bool hasProfile = !string.IsNullOrEmpty(unitId) && RuntimeUnitMechanicsDirector.TryGetProfile(unitId, out profile) && profile != null;
                        if (hasProfile)
                        {
                            totalMechanicsInstances++;
                        }

                        bool hasHealing = profile?.Healing != null && (profile.Healing.healAmount > 0f || profile.Healing.repairAmount > 0f);
                        bool hasAura = profile?.Aura != null && profile.Aura.auraRadius > 0f;
                        bool hasShieldRegen = profile?.ShieldRegen != null && profile.ShieldRegen.rechargeRate > 0f;

                        if (hasHealing) activeHealerCount++;
                        if (hasAura) activeAuraCount++;
                        if (hasShieldRegen) activeShieldRegenCount++;

                        if (rows.Count < cap)
                        {
                            float hp = unit.MaxHealth > 0.001f ? Mathf.Clamp01(unit.CurrentHealth / unit.MaxHealth) : 1f;
                            float sh = unit.MaxShield > 0.001f ? Mathf.Clamp01(unit.CurrentShield / unit.MaxShield) : 0f;
                            string stance = GetUnitTacticalStance(key);
                            string loadout = GetActiveUnitVariant(key);
                            int rank = unit.CurrentRank;
                            int xp = RuntimeUnitMechanicsDirector.GetCombatXpForEntity(unit);

                            List<string> affixesList;
                            IReadOnlyList<string> affixes = _activeUnitAffixes.TryGetValue(key, out affixesList)
                                ? affixesList.AsReadOnly()
                                : Array.Empty<string>();

                            float crit = profile?.CriticalHitChance ?? 0f;
                            float minRange = profile?.MinAttackRange ?? 0f;

                            int statusCount = 0;
                            string statusSummary = string.Empty;
                            try
                            {
                                var activeEffects = new List<string>();
                                StatusEffect[] allEffects = _allStatusEffects;
                                for (int s = 0; s < allEffects.Length; s++)
                                {
                                    if (unit.HasActiveStatusEffect(allEffects[s]))
                                    {
                                        activeEffects.Add(allEffects[s].ToString());
                                    }
                                }
                                statusCount = activeEffects.Count;
                                if (statusCount > 0)
                                {
                                    statusSummary = string.Join(",", activeEffects);
                                }
                            }
                            catch
                            {
                                // Fallback
                            }

                            bool hasVd = false;
                            bool isJammed = false;
                            float recoil = 0f;
                            float door = 0f;

                            Component vd = FindRuntimeVisualDirector(unit);
                            if (vd != null)
                            {
                                hasVd = true;
                                if (vd is Visual.RuntimeVisualDirector rvd)
                                {
                                    isJammed = rvd.IsTurretJammed;
                                    recoil = rvd.CurrentRecoilOffset;
                                    door = rvd.DoorOpenProgress;
                                }
                                else
                                {
                                    Type vt = vd.GetType();
                                    PropertyInfo pj = vt.GetProperty("IsTurretJammed");
                                    if (pj != null) isJammed = (bool)pj.GetValue(vd, null);
                                    PropertyInfo pr = vt.GetProperty("CurrentRecoilOffset");
                                    if (pr != null) recoil = (float)pr.GetValue(vd, null);
                                    PropertyInfo pd = vt.GetProperty("DoorOpenProgress");
                                    if (pd != null) door = (float)pd.GetValue(vd, null);
                                }
                            }

                            rows.Add(new UnitMechanicsCensusRow
                            {
                                EntityKeyId = key,
                                EntityId = unitId,
                                IsControlledByPlayer = unit.IsControlledByPlayer,
                                HealthRatio = hp,
                                ShieldRatio = sh,
                                TacticalStance = stance,
                                LoadoutVariant = loadout,
                                VeterancyRank = rank,
                                CombatXp = xp,
                                Affixes = affixes,
                                HasHealing = hasHealing,
                                HasAura = hasAura,
                                HasShieldRegen = hasShieldRegen,
                                CriticalHitChance = crit,
                                MinAttackRange = minRange,
                                ActiveStatusEffectsCount = statusCount,
                                ActiveStatusEffectsSummary = statusSummary,
                                HasVisualDirector = hasVd,
                                IsVisualTurretJammed = isJammed,
                                VisualRecoilOffset = recoil,
                                VisualDoorProgress = door,
                            });
                        }
                    }
                }
            }
            catch { }

            return new UnitMechanicsCensus
            {
                OmittedUnitCount = Math.Max(0, totalTrackedUnits - rows.Count),
                TotalTrackedUnits = totalTrackedUnits,
                TotalMechanicsInstancesCount = totalMechanicsInstances,
                ActiveHealerCount = activeHealerCount,
                ActiveAuraCount = activeAuraCount,
                ActiveShieldRegenCount = activeShieldRegenCount,
                Rows = rows.AsReadOnly(),
            };
        }

        public static Component FindRuntimeVisualDirector(EntityController unit)
        {
            if (unit == null || unit.gameObject == null)
            {
                return null;
            }

            try
            {
                Visual.RuntimeVisualDirector direct = unit.GetComponent<Visual.RuntimeVisualDirector>();
                if (direct != null) return direct;

                direct = unit.gameObject.GetComponentInChildren<Visual.RuntimeVisualDirector>(true);
                if (direct != null) return direct;

                Component[] comps = unit.GetComponents<Component>();
                for (int i = 0; i < comps.Length; i++)
                {
                    if (comps[i] != null && comps[i].GetType().Name == "RuntimeVisualDirector")
                    {
                        return comps[i];
                    }
                }

                Component[] childComps = unit.gameObject.GetComponentsInChildren<Component>(true);
                for (int i = 0; i < childComps.Length; i++)
                {
                    if (childComps[i] != null && childComps[i].GetType().Name == "RuntimeVisualDirector")
                    {
                        return childComps[i];
                    }
                }
            }
            catch
            {
            }

            return null;
        }

        public static VisualFeedbackResult TryOrderUnitVisualFeedback(string entityKeyId, string feedbackType, float magnitude = 0.22f)
        {
            if (string.IsNullOrWhiteSpace(entityKeyId))
            {
                return new VisualFeedbackResult { Success = false, EntityKeyId = entityKeyId, FeedbackType = feedbackType, Message = "Entity key ID cannot be empty." };
            }
            if (string.IsNullOrWhiteSpace(feedbackType))
            {
                return new VisualFeedbackResult { Success = false, EntityKeyId = entityKeyId, FeedbackType = feedbackType, Message = "Feedback type cannot be empty." };
            }

            if (float.IsNaN(magnitude) || float.IsInfinity(magnitude))
            {
                return new VisualFeedbackResult
                {
                    Success = false,
                    EntityKeyId = entityKeyId,
                    FeedbackType = feedbackType,
                    Message = "Visual feedback magnitude must be finite."
                };
            }

            try
            {
                ExistingControllers existing = ExistingControllers.Instance;
                if (existing == null)
                {
                    return new VisualFeedbackResult { Success = false, EntityKeyId = entityKeyId, FeedbackType = feedbackType, Message = "ExistingControllers is unavailable." };
                }

                EntityController unit = FindEntityByKey(existing, entityKeyId.Trim());
                if (unit == null || unit.gameObject == null || unit.IsDestroyed)
                {
                    return new VisualFeedbackResult { Success = false, EntityKeyId = entityKeyId, FeedbackType = feedbackType, Message = "Entity not found or destroyed: " + entityKeyId };
                }

                Component visualDirector = FindRuntimeVisualDirector(unit);

                if (visualDirector == null)
                {
                    return new VisualFeedbackResult { Success = false, EntityKeyId = entityKeyId, FeedbackType = feedbackType, Message = "RuntimeVisualDirector is not attached to entity: " + entityKeyId };
                }

                Type vdType = visualDirector.GetType();
                string normFeedback = feedbackType.Trim();
                if (string.Equals(normFeedback, "WeaponRecoil", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(normFeedback, "CritImpact", StringComparison.OrdinalIgnoreCase))
                {
                    MethodInfo m = vdType.GetMethod("TriggerWeaponFeedback", BindingFlags.Public | BindingFlags.Instance);
                    if (m != null)
                    {
                        float recoil = Mathf.Clamp(magnitude, 0.05f, 1.0f);
                        m.Invoke(visualDirector, new object[] { recoil });
                        return new VisualFeedbackResult { Success = true, EntityKeyId = entityKeyId, FeedbackType = normFeedback, Message = "Weapon recoil/impact feedback triggered with magnitude " + recoil };
                    }
                }
                else if (string.Equals(normFeedback, "JamTurret", StringComparison.OrdinalIgnoreCase))
                {
                    MethodInfo m = vdType.GetMethod("SetTurretJammedOverride", BindingFlags.Public | BindingFlags.Instance);
                    if (m != null)
                    {
                        m.Invoke(visualDirector, new object[] { (bool?)true });
                        return new VisualFeedbackResult { Success = true, EntityKeyId = entityKeyId, FeedbackType = normFeedback, Message = "Turret jammed override activated." };
                    }
                }
                else if (string.Equals(normFeedback, "UnjamTurret", StringComparison.OrdinalIgnoreCase))
                {
                    MethodInfo m = vdType.GetMethod("SetTurretJammedOverride", BindingFlags.Public | BindingFlags.Instance);
                    if (m != null)
                    {
                        m.Invoke(visualDirector, new object[] { (bool?)false });
                        return new VisualFeedbackResult { Success = true, EntityKeyId = entityKeyId, FeedbackType = normFeedback, Message = "Turret jammed override cleared." };
                    }
                }
                else if (string.Equals(normFeedback, "OpenBayDoors", StringComparison.OrdinalIgnoreCase))
                {
                    MethodInfo m = vdType.GetMethod("SetDoorOpenOverride", BindingFlags.Public | BindingFlags.Instance);
                    if (m != null)
                    {
                        m.Invoke(visualDirector, new object[] { (float?)1.0f });
                        return new VisualFeedbackResult { Success = true, EntityKeyId = entityKeyId, FeedbackType = normFeedback, Message = "Bay doors open override set to 1.0." };
                    }
                }
                else if (string.Equals(normFeedback, "CloseBayDoors", StringComparison.OrdinalIgnoreCase))
                {
                    MethodInfo m = vdType.GetMethod("SetDoorOpenOverride", BindingFlags.Public | BindingFlags.Instance);
                    if (m != null)
                    {
                        m.Invoke(visualDirector, new object[] { (float?)0.0f });
                        return new VisualFeedbackResult { Success = true, EntityKeyId = entityKeyId, FeedbackType = normFeedback, Message = "Bay doors open override cleared to 0.0." };
                    }
                }
                else if (string.Equals(normFeedback, "FootstepPulse", StringComparison.OrdinalIgnoreCase))
                {
                    MethodInfo m = vdType.GetMethod("TriggerFootstepFeedback", BindingFlags.Public | BindingFlags.Instance);
                    if (m != null)
                    {
                        m.Invoke(visualDirector, new object[] { null });
                        return new VisualFeedbackResult { Success = true, EntityKeyId = entityKeyId, FeedbackType = normFeedback, Message = "Footstep ground pulse triggered." };
                    }
                }

                return new VisualFeedbackResult { Success = false, EntityKeyId = entityKeyId, FeedbackType = feedbackType, Message = "Unsupported visual feedback type: " + feedbackType };
            }
            catch (Exception ex)
            {
                return new VisualFeedbackResult { Success = false, EntityKeyId = entityKeyId, FeedbackType = feedbackType, Message = "Error executing visual feedback: " + ex.Message };
            }
        }

        public static VisualFeedbackResult TryOrderUnitAttachment(string entityKeyId, string attachmentType, bool active)
        {
            if (string.IsNullOrWhiteSpace(entityKeyId))
            {
                return new VisualFeedbackResult { Success = false, EntityKeyId = entityKeyId, FeedbackType = "Attachment", Message = "Entity key ID cannot be empty." };
            }
            if (string.IsNullOrWhiteSpace(attachmentType))
            {
                return new VisualFeedbackResult { Success = false, EntityKeyId = entityKeyId, FeedbackType = "Attachment", Message = "Attachment type cannot be empty." };
            }

            try
            {
                ExistingControllers existing = ExistingControllers.Instance;
                if (existing == null)
                {
                    return new VisualFeedbackResult { Success = false, EntityKeyId = entityKeyId, FeedbackType = "Attachment", Message = "ExistingControllers is unavailable." };
                }

                EntityController unit = FindEntityByKey(existing, entityKeyId.Trim());
                if (unit == null || unit.gameObject == null || unit.IsDestroyed)
                {
                    return new VisualFeedbackResult { Success = false, EntityKeyId = entityKeyId, FeedbackType = "Attachment", Message = "Entity not found or destroyed: " + entityKeyId };
                }

                Component visualDirector = FindRuntimeVisualDirector(unit);

                if (visualDirector == null)
                {
                    return new VisualFeedbackResult { Success = false, EntityKeyId = entityKeyId, FeedbackType = "Attachment", Message = "RuntimeVisualDirector is not attached to entity: " + entityKeyId };
                }

                Type vdType = visualDirector.GetType();
                MethodInfo m = vdType.GetMethod("TrySetAttachmentState", BindingFlags.Public | BindingFlags.Instance);
                if (m != null)
                {
                    bool set = (bool)m.Invoke(visualDirector, new object[] { attachmentType.Trim(), active });
                    if (set)
                    {
                        return new VisualFeedbackResult { Success = true, EntityKeyId = entityKeyId, FeedbackType = "Attachment:" + attachmentType, Message = "Attachment '" + attachmentType + "' state set to " + active };
                    }
                    return new VisualFeedbackResult { Success = false, EntityKeyId = entityKeyId, FeedbackType = "Attachment:" + attachmentType, Message = "Attachment hardpoint '" + attachmentType + "' not found on unit visual hierarchy." };
                }

                return new VisualFeedbackResult { Success = false, EntityKeyId = entityKeyId, FeedbackType = "Attachment", Message = "TrySetAttachmentState method not found on RuntimeVisualDirector." };
            }
            catch (Exception ex)
            {
                return new VisualFeedbackResult { Success = false, EntityKeyId = entityKeyId, FeedbackType = "Attachment", Message = "Error setting attachment state: " + ex.Message };
            }
        }
}
}
