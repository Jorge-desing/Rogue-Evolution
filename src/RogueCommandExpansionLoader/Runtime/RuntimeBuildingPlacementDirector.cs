using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace RogueCommandExpansionLoader.Runtime
{
    /// <summary>
    /// Resolves a selected native blueprint and drives the game's placement
    /// coroutine without requiring a focused window or desktop mouse input.
    /// The commit path keeps PlaceBuildings' native validation and build flow.
    /// </summary>
    internal static class RuntimeBuildingPlacementDirector
    {
        private const BindingFlags InstanceMembers = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags StaticMembers = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private const int MaximumPlacementSearchRadiusCells = 12;
        private const int MaximumPlacementCellDiagnostics = 16;

        public static BuildingPlacementCommandResult TryPlaceCurrentBlueprint(float worldX, float worldZ, bool commit)
        {
            return TryPlaceCurrentBlueprint(worldX, worldZ, commit, includeCellDiagnostics: true);
        }

        public static BuildingPlacementCommandResult InspectActivePlacement()
        {
            string entityId = string.Empty;
            try
            {
                if (Navigator.CurrentSceneState != Navigator.SceneState.Game)
                {
                    return Failure(entityId, 0, 0, Vector3.zero, 0,
                        "Building placement inspection is available only during a live Game scene.");
                }

                PlaceBuildings placement = ReadStaticField<PlaceBuildings>(typeof(PlaceBuildings), "_instance");
                if (placement == null || !placement.isActiveAndEnabled || !PlaceBuildings.IsShowing_Static())
                {
                    return Failure(entityId, 0, 0, Vector3.zero, 0,
                        "The native building placement controller is not active.");
                }

                object blueprintValue = ReadInstanceField<object>(placement, "_blueprintInfo");
                if (!(blueprintValue is BlueprintInfo blueprint))
                {
                    return Failure(entityId, 0, 0, Vector3.zero, 0,
                        "Select a building blueprint through the native building menu first.");
                }

                entityId = blueprint.entityId ?? string.Empty;
                GameObject ghost = ReadInstanceField<GameObject>(placement, "_placementGhost");
                if (string.IsNullOrWhiteSpace(entityId) || ghost == null || !ghost.activeInHierarchy)
                {
                    return Failure(entityId, 0, 0, Vector3.zero, 0,
                        "The selected native building blueprint has no active placement ghost.");
                }

                Vector3 ghostPosition = ghost.transform.position;
                return TryPlaceCurrentBlueprint(ghostPosition.x, ghostPosition.z, commit: false);
            }
            catch (Exception exception)
            {
                return Failure(entityId, 0, 0, Vector3.zero, 0,
                    "Active building ghost inspection failed closed: " + exception.GetBaseException().Message);
            }
        }

        private static BuildingPlacementCommandResult TryPlaceCurrentBlueprint(
            float worldX, float worldZ, bool commit, bool includeCellDiagnostics)
        {
            string entityId = string.Empty;
            int cost = 0;
            int balance = 0;
            int cellCount = 0;
            Vector3 placementPosition = Vector3.zero;
            List<BuildingPlacementCellDiagnostic> cellDiagnostics = null;

            try
            {
                if (float.IsNaN(worldX) || float.IsInfinity(worldX) || float.IsNaN(worldZ) || float.IsInfinity(worldZ))
                {
                    return Failure(entityId, cost, balance, placementPosition, cellCount,
                        "World coordinates must be finite numbers.");
                }

                if (Navigator.CurrentSceneState != Navigator.SceneState.Game)
                {
                    return Failure(entityId, cost, balance, placementPosition, cellCount,
                        "Building placement is available only during a live Game scene.");
                }

                PlaceBuildings placement = ReadStaticField<PlaceBuildings>(typeof(PlaceBuildings), "_instance");
                if (placement == null || !placement.isActiveAndEnabled || !PlaceBuildings.IsShowing_Static())
                {
                    return Failure(entityId, cost, balance, placementPosition, cellCount,
                        "The native building placement controller is not active.");
                }

                object blueprintValue = ReadInstanceField<object>(placement, "_blueprintInfo");
                if (!(blueprintValue is BlueprintInfo blueprint))
                {
                    return Failure(entityId, cost, balance, placementPosition, cellCount,
                        "Select a building blueprint through the native building menu first.");
                }

                entityId = blueprint.entityId ?? string.Empty;
                if (string.IsNullOrWhiteSpace(entityId))
                {
                    return Failure(entityId, cost, balance, placementPosition, cellCount,
                        "The active native blueprint has no entity ID.");
                }

                int buildingSize = ReadInstanceField<int>(placement, "_buildingSize");
                if (buildingSize < 1 || buildingSize > 2)
                {
                    return Failure(entityId, cost, balance, placementPosition, cellCount,
                        "The selected blueprint has an unsupported native grid size.");
                }

                WorldGrid grid = ReadInstanceField<WorldGrid>(placement, "_grid");
                GameObject ghost = ReadInstanceField<GameObject>(placement, "_placementGhost");
                List<string> entitiesToKeepDistanceFrom = ReadInstanceField<List<string>>(
                    placement, "_entitiesToBeFarEnoughAway");
                int maxBuildDistanceFromRefinery = ReadInstanceField<int>(placement, "_maxBuildDistanceFromRefinery");
                List<string> entitiesInProgress = ReadInstanceField<List<string>>(placement, "_entityIdsInProgress");

                if (grid == null || ghost == null || !ghost.activeInHierarchy ||
                    entitiesToKeepDistanceFrom == null || entitiesInProgress == null)
                {
                    return Failure(entityId, cost, balance, placementPosition, cellCount,
                        "The native placement state is incomplete; no building was queued.");
                }

                List<Vector2Int> cells = grid.CellsInSquareAround(new Vector3(worldX, 0f, worldZ), buildingSize);
                cellCount = cells != null ? cells.Count : 0;
                BuildingPlacementHelper.UnitsAllowedType allowedUnits =
                    GameBalancingStore.UnitTypeBuildingsCanBeDroppedOn;
                if (includeCellDiagnostics)
                {
                    cellDiagnostics = DescribePlacementCells(grid, cells, allowedUnits);
                }
                if (cells == null || cells.Count != buildingSize * buildingSize)
                {
                    return Failure(entityId, cost, balance, placementPosition, cellCount,
                        "The requested location crosses the map boundary.", cellDiagnostics);
                }

                Rect rect = grid.WorldRectAroundCells(cells);
                placementPosition = grid.HeightMapCorrected(
                    new Vector3(rect.center.x, 0f, rect.center.y), treatRampsAsOneLayerHigher: true);

                cost = EntityBalancingStore.Cost(entityId);
                balance = Bank.ActualBalance("Player");
                if (ShowBuildingMenu.HasCapacityReached(entityId))
                {
                    return Failure(entityId, cost, balance, placementPosition, cellCount,
                        "The native building capacity for this blueprint is full.", cellDiagnostics);
                }

                if (balance < cost)
                {
                    return Failure(entityId, cost, balance, placementPosition, cellCount,
                        "The Player account does not have enough credits for this blueprint.", cellDiagnostics);
                }

                if (!BuildingPlacementHelper.IsPlacementValid(cells, allowedUnits))
                {
                    return Failure(entityId, cost, balance, placementPosition, cellCount,
                        "The native grid rejected this location because it is occupied or not buildable.", cellDiagnostics);
                }

                foreach (Vector2Int cell in cells)
                {
                    if (Game.ShaderTextures.SecondaryFogOfWarOccludes(grid.Grid2World(cell.x, cell.y)))
                    {
                        return Failure(entityId, cost, balance, placementPosition, cellCount,
                            "The native placement rules reject cells hidden by secondary fog of war.", cellDiagnostics);
                    }
                }

                if (Game.Engineer != "ReachEngineer" && !EntityBalancingStore.HasRole(entityId, UnitRole.Refinery) &&
                    !grid.IsPlayerRefineryWithinRadiusAroundCells(cells, maxBuildDistanceFromRefinery))
                {
                    return Failure(entityId, cost, balance, placementPosition, cellCount,
                        "The native refinery-distance rule rejects this location.", cellDiagnostics);
                }

                int minimumDistanceFromBuilderBase = EntityBalancingStore.SkillRange("PCXBuilderBase");
                if (grid.IsBuildingWithinRadiusAroundCells(
                    entitiesToKeepDistanceFrom, cells, minimumDistanceFromBuilderBase))
                {
                    return Failure(entityId, cost, balance, placementPosition, cellCount,
                        "The native builder-base spacing rule rejects this location.", cellDiagnostics);
                }

                if (!commit)
                {
                    return new BuildingPlacementCommandResult(
                        true, false, entityId, cost, balance,
                        placementPosition.x, placementPosition.y, placementPosition.z,
                        cellCount, "Native placement checks passed; no game state was changed.", cellDiagnostics);
                }

                if (ExistingControllers.Instance == null || FirstEngineer(ExistingControllers.Instance) == null)
                {
                    return Failure(entityId, cost, balance, placementPosition, cellCount,
                        "The native placement coroutine requires a living player Engineer.");
                }

                MethodInfo instantiate = typeof(PlaceBuildings).GetMethod(
                    "InstantiateBuildingWithEffect", InstanceMembers, null,
                    new[] { typeof(BlueprintInfo), typeof(Vector3), typeof(List<Vector2Int>), typeof(GameObject) }, null);
                MethodInfo reset = typeof(PlaceBuildings).GetMethod(
                    "ResetPlacement", InstanceMembers, null, new[] { typeof(bool) }, null);
                if (instantiate == null || instantiate.ReturnType != typeof(IEnumerator) || reset == null)
                {
                    return Failure(entityId, cost, balance, placementPosition, cellCount,
                        "The native placement method signatures do not match the verified game build.");
                }

                IEnumerator routine = instantiate.Invoke(
                    placement, new object[] { blueprint, placementPosition, cells, ghost }) as IEnumerator;
                if (routine == null)
                {
                    return Failure(entityId, cost, balance, placementPosition, cellCount,
                        "The native placement coroutine could not be created.");
                }

                entitiesInProgress.Add(entityId);
                Coroutine running;
                try
                {
                    running = placement.StartCoroutine(routine);
                }
                catch (Exception exception)
                {
                    entitiesInProgress.Remove(entityId);
                    return Failure(entityId, cost, balance, placementPosition, cellCount,
                        "Starting the native placement coroutine failed; inspect the production census before retrying. " +
                        exception.GetBaseException().Message);
                }

                if (running == null)
                {
                    entitiesInProgress.Remove(entityId);
                    return Failure(entityId, cost, balance, placementPosition, cellCount,
                        "Unity did not start the native placement coroutine.");
                }

                string message = "The native PlaceBuildings coroutine started after all placement checks passed.";
                try
                {
                    reset.Invoke(placement, new object[] { true });
                }
                catch (Exception exception)
                {
                    message += " Placement queued, but native menu cleanup reported: " +
                        exception.GetBaseException().Message;
                }

                return new BuildingPlacementCommandResult(
                    true, true, entityId, cost, balance,
                    placementPosition.x, placementPosition.y, placementPosition.z,
                    cellCount, message, cellDiagnostics);
            }
            catch (Exception exception)
            {
                return Failure(entityId, cost, balance, placementPosition, cellCount,
                    "Native placement preflight failed closed: " + exception.GetBaseException().Message);
            }
        }

        public static BuildingPlacementSearchResult FindNearbyPlacement(
            float originX, float originZ, int radiusCells = 8)
        {
            if (float.IsNaN(originX) || float.IsInfinity(originX) ||
                float.IsNaN(originZ) || float.IsInfinity(originZ))
            {
                return new BuildingPlacementSearchResult(
                    false, originX, originZ, radiusCells, 0, default(BuildingPlacementCommandResult),
                    "Search origin must contain finite world coordinates.");
            }

            if (radiusCells < 0 || radiusCells > MaximumPlacementSearchRadiusCells)
            {
                return new BuildingPlacementSearchResult(
                    false, originX, originZ, radiusCells, 0, default(BuildingPlacementCommandResult),
                    "Search radius must be between 0 and " + MaximumPlacementSearchRadiusCells + " grid cells.");
            }

            int candidatesChecked = 0;
            BuildingPlacementCommandResult lastCandidate = default(BuildingPlacementCommandResult);
            BuildingPlacementCommandResult originCandidate = default(BuildingPlacementCommandResult);
            for (int distance = 0; distance <= radiusCells; distance++)
            {
                for (int offsetX = -distance; offsetX <= distance; offsetX++)
                {
                    int offsetZMagnitude = distance - Math.Abs(offsetX);
                    int negativeOffsetZ = -offsetZMagnitude;
                    BuildingPlacementCommandResult candidate = CheckSearchCandidate(
                        originX, originZ, offsetX, negativeOffsetZ, ref candidatesChecked,
                        includeCellDiagnostics: candidatesChecked == 0);
                    if (candidatesChecked == 1)
                    {
                        originCandidate = candidate;
                    }
                    if (candidate.Success)
                    {
                        candidate = TryPlaceCurrentBlueprint(
                            originX + offsetX * WorldGrid.CellSize,
                            originZ + negativeOffsetZ * WorldGrid.CellSize,
                            commit: false);
                        return candidate.Success
                            ? SearchFound(originX, originZ, radiusCells, candidatesChecked, candidate)
                            : SearchNotFound(originX, originZ, radiusCells, candidatesChecked, candidate,
                                "Placement state changed during the read-only search; rerun the preflight. " +
                                candidate.Message);
                    }

                    lastCandidate = candidate;
                    if (!IsLocationSpecificRejection(candidate.Message))
                    {
                        return SearchNotFound(
                            originX, originZ, radiusCells, candidatesChecked, candidate,
                            "Nearby placement search stopped because the active placement state cannot be searched. " +
                            candidate.Message);
                    }

                    if (offsetZMagnitude > 0)
                    {
                        candidate = CheckSearchCandidate(
                            originX, originZ, offsetX, offsetZMagnitude, ref candidatesChecked,
                            includeCellDiagnostics: false);
                        if (candidate.Success)
                        {
                            candidate = TryPlaceCurrentBlueprint(
                                originX + offsetX * WorldGrid.CellSize,
                                originZ + offsetZMagnitude * WorldGrid.CellSize,
                                commit: false);
                            return candidate.Success
                                ? SearchFound(originX, originZ, radiusCells, candidatesChecked, candidate)
                                : SearchNotFound(originX, originZ, radiusCells, candidatesChecked, candidate,
                                    "Placement state changed during the read-only search; rerun the preflight. " +
                                    candidate.Message);
                        }

                        lastCandidate = candidate;
                        if (!IsLocationSpecificRejection(candidate.Message))
                        {
                            return SearchNotFound(
                                originX, originZ, radiusCells, candidatesChecked, candidate,
                                "Nearby placement search stopped because the active placement state cannot be searched. " +
                                candidate.Message);
                        }
                    }
                }
            }

            return SearchNotFound(
                originX, originZ, radiusCells, candidatesChecked,
                originCandidate.EntityId.Length > 0 ? originCandidate : lastCandidate,
                "No location passed native placement checks within the requested radius. " +
                (originCandidate.EntityId.Length > 0 ? originCandidate.Message : lastCandidate.Message));
        }

        private static BuildingPlacementCommandResult CheckSearchCandidate(
            float originX, float originZ, int offsetX, int offsetZ, ref int candidatesChecked,
            bool includeCellDiagnostics)
        {
            candidatesChecked++;
            float step = WorldGrid.CellSize;
            return TryPlaceCurrentBlueprint(
                originX + offsetX * step,
                originZ + offsetZ * step,
                commit: false,
                includeCellDiagnostics: includeCellDiagnostics);
        }

        private static BuildingPlacementSearchResult SearchFound(
            float originX, float originZ, int radiusCells, int candidatesChecked,
            BuildingPlacementCommandResult candidate)
        {
            string message = "A nearby location passed native placement checks; no game state was changed.";
            var placement = new BuildingPlacementCommandResult(
                candidate.Success, false, candidate.EntityId, candidate.Cost, candidate.BalanceBefore,
                candidate.WorldX, candidate.WorldY, candidate.WorldZ, candidate.CellCount,
                message, candidate.CellDiagnostics, candidate.OmittedCellDiagnosticCount);
            return new BuildingPlacementSearchResult(
                true, originX, originZ, radiusCells, candidatesChecked, placement, message);
        }

        private static BuildingPlacementSearchResult SearchNotFound(
            float originX, float originZ, int radiusCells, int candidatesChecked,
            BuildingPlacementCommandResult candidate, string message)
        {
            return new BuildingPlacementSearchResult(
                false, originX, originZ, radiusCells, candidatesChecked, candidate, message);
        }

        private static bool IsLocationSpecificRejection(string message)
        {
            return string.Equals(message, "The requested location crosses the map boundary.", StringComparison.Ordinal) ||
                string.Equals(message,
                    "The native grid rejected this location because it is occupied or not buildable.", StringComparison.Ordinal) ||
                string.Equals(message,
                    "The native placement rules reject cells hidden by secondary fog of war.", StringComparison.Ordinal) ||
                string.Equals(message,
                    "The native refinery-distance rule rejects this location.", StringComparison.Ordinal) ||
                string.Equals(message,
                    "The native builder-base spacing rule rejects this location.", StringComparison.Ordinal);
        }

        private static List<BuildingPlacementCellDiagnostic> DescribePlacementCells(
            WorldGrid grid, List<Vector2Int> cells, BuildingPlacementHelper.UnitsAllowedType allowedUnits)
        {
            var diagnostics = new List<BuildingPlacementCellDiagnostic>(
                Math.Min(cells != null ? cells.Count : 0, MaximumPlacementCellDiagnostics));
            if (grid == null || cells == null || cells.Count == 0)
            {
                return diagnostics;
            }

            string footprintHeightLayer = string.Empty;
            if (grid.IsWithinGrid(cells[0]))
            {
                footprintHeightLayer = grid.GetHeightLayer(cells[0]).ToString();
            }

            int count = Math.Min(cells.Count, MaximumPlacementCellDiagnostics);
            for (int index = 0; index < count; index++)
            {
                Vector2Int cell = cells[index];
                bool withinGrid = grid.IsWithinGrid(cell);
                string worldObject = withinGrid ? grid.Identify(cell).ToString() : string.Empty;
                string heightLayer = withinGrid ? grid.GetHeightLayer(cell).ToString() : string.Empty;
                bool ground = withinGrid && grid.IsGround(cell);
                bool obstacle = withinGrid && grid.IsObstacle(cell);
                bool building = withinGrid && grid.IsBuilding(cell);
                bool ramp = withinGrid && grid.IsRamp(cell);
                bool crystal = withinGrid && grid.IsCrystalOnCell(cell);
                bool multiLayer = withinGrid && grid.IsMultiLayerCell(cell);
                bool buildingInProgress = PlaceBuildings.AreBuildingInProgressCellsContaining_Static(cell);
                bool unit = false;
                bool unitBlocksPlacement = false;
                bool secondaryFogOccluded = false;
                bool nativeObjectBlocked = false;
                bool sameHeightLayerAsFootprint = withinGrid &&
                    string.Equals(heightLayer, footprintHeightLayer, StringComparison.Ordinal);
                if (withinGrid)
                {
                    var singleCell = new List<Vector2Int>(1) { cell };
                    unit = grid.IsUnitOnCells(singleCell);
                    bool aiUnit = grid.IsAiUnitOnCells(singleCell);
                    unitBlocksPlacement = allowedUnits == BuildingPlacementHelper.UnitsAllowedType.None
                        ? unit
                        : allowedUnits == BuildingPlacementHelper.UnitsAllowedType.OnlyPlayerUnits && aiUnit;
                    nativeObjectBlocked = grid.IsBuildingOrRampOrObstacleOnCells(singleCell);
                    secondaryFogOccluded = Game.ShaderTextures.SecondaryFogOfWarOccludes(
                        grid.Grid2World(cell.x, cell.y));
                }

                var reasons = new List<string>(8);
                if (!withinGrid) reasons.Add("outsideGrid");
                if (withinGrid && heightLayer == HeightLayer.Nothing.ToString()) reasons.Add("noGroundLayer");
                if (multiLayer) reasons.Add("multiLayer");
                if (buildingInProgress) reasons.Add("buildingInProgress");
                if (nativeObjectBlocked) reasons.Add("occupiedByBuildingRampOrObstacle");
                if (crystal) reasons.Add("crystal");
                if (unitBlocksPlacement) reasons.Add("unit");
                if (withinGrid && !sameHeightLayerAsFootprint) reasons.Add("differentHeightLayer");
                if (secondaryFogOccluded) reasons.Add("secondaryFog");
                if (!ground && withinGrid && !nativeObjectBlocked && !crystal && !ramp &&
                    heightLayer != HeightLayer.Nothing.ToString())
                {
                    reasons.Add("nonGroundWorldObject:" + worldObject);
                }

                diagnostics.Add(new BuildingPlacementCellDiagnostic(
                    cell.x, cell.y, worldObject, heightLayer, withinGrid, ground, obstacle, building, ramp,
                    crystal, unit, unitBlocksPlacement, multiLayer, buildingInProgress,
                    secondaryFogOccluded, sameHeightLayerAsFootprint, reasons));
            }

            return diagnostics;
        }

        private static EntityController FirstEngineer(ExistingControllers controllers)
        {
            if (controllers == null)
            {
                return null;
            }

            HashSet<EntityController> engineers = controllers.Engineers();
            if (engineers == null)
            {
                return null;
            }

            foreach (EntityController engineer in engineers)
            {
                if (engineer != null && !engineer.IsDestroyed && !engineer.DestroyHasBeenStarted)
                {
                    return engineer;
                }
            }

            return null;
        }

        private static T ReadStaticField<T>(Type type, string name)
        {
            FieldInfo field = type.GetField(name, StaticMembers);
            if (field == null || !field.IsStatic)
            {
                throw new InvalidOperationException("Required static field '" + type.FullName + "." + name + "' was not found.");
            }

            object value = field.GetValue(null);
            if (value == null)
            {
                return default(T);
            }

            if (!(value is T typed))
            {
                throw new InvalidOperationException("Static field '" + type.FullName + "." + name + "' has an unexpected type.");
            }

            return typed;
        }

        private static T ReadInstanceField<T>(object instance, string name)
        {
            FieldInfo field = instance.GetType().GetField(name, InstanceMembers);
            if (field == null || field.IsStatic)
            {
                throw new InvalidOperationException("Required instance field '" + instance.GetType().FullName + "." + name + "' was not found.");
            }

            object value = field.GetValue(instance);
            if (value == null)
            {
                return default(T);
            }

            if (!(value is T typed))
            {
                throw new InvalidOperationException("Instance field '" + instance.GetType().FullName + "." + name + "' has an unexpected type.");
            }

            return typed;
        }

        private static BuildingPlacementCommandResult Failure(
            string entityId, int cost, int balance, Vector3 position, int cellCount, string message,
            IReadOnlyList<BuildingPlacementCellDiagnostic> cellDiagnostics = null)
        {
            return new BuildingPlacementCommandResult(
                false, false, entityId, cost, balance,
                position.x, position.y, position.z, cellCount, message, cellDiagnostics);
        }
    }
}
