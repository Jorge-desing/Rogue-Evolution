using System;
using System.Reflection;
using UnityEngine;

namespace RogueCommandExpansionLoader.Runtime
{
    /// <summary>
    /// Pure, agent-readable telemetry of match terrain, map bounds, and grid layout.
    /// Decoupled from Unity types to support clean-room MCP telemetry and offline validation.
    /// </summary>
    public sealed class GridMetricsCensus
    {
        public float WorldWidth { get; }
        public float WorldHeight { get; }
        public int HorizontalCellCount { get; }
        public int VerticalCellCount { get; }
        public float CellSize { get; }
        public int PlayerStartX { get; }
        public int PlayerStartY { get; }
        public int AiStartX { get; }
        public int AiStartY { get; }
        public bool IsGridReady { get; }

        public GridMetricsCensus(
            float worldWidth,
            float worldHeight,
            int horizontalCellCount,
            int verticalCellCount,
            float cellSize,
            int playerStartX,
            int playerStartY,
            int aiStartX,
            int aiStartY,
            bool isGridReady)
        {
            WorldWidth = worldWidth;
            WorldHeight = worldHeight;
            HorizontalCellCount = horizontalCellCount;
            VerticalCellCount = verticalCellCount;
            CellSize = cellSize;
            PlayerStartX = playerStartX;
            PlayerStartY = playerStartY;
            AiStartX = aiStartX;
            AiStartY = aiStartY;
            IsGridReady = isGridReady;
        }

        public static GridMetricsCensus Empty { get; } =
            new GridMetricsCensus(0f, 0f, 0, 0, 0f, 0, 0, 0, 0, false);
    }

    /// <summary>
    /// Pure diagnostic evaluation for a specific (X, Z) coordinate against the battlefield grid.
    /// </summary>
    public readonly struct GridCellEvaluationResult
    {
        public GridCellEvaluationResult(
            bool isWithinWorld,
            float clampedX,
            float clampedZ,
            int cellX,
            int cellY,
            float surfaceHeight,
            bool isBorderCell,
            string message)
        {
            IsWithinWorld = isWithinWorld;
            ClampedX = clampedX;
            ClampedZ = clampedZ;
            CellX = cellX;
            CellY = cellY;
            SurfaceHeight = surfaceHeight;
            IsBorderCell = isBorderCell;
            Message = message ?? string.Empty;
        }

        public bool IsWithinWorld { get; }
        public float ClampedX { get; }
        public float ClampedZ { get; }
        public int CellX { get; }
        public int CellY { get; }
        public float SurfaceHeight { get; }
        public bool IsBorderCell { get; }
        public string Message { get; }
    }

    /// <summary>
    /// Diagnostic collector that inspects the live <see cref="WorldGrid"/>
    /// across game and test stubs without throwing.
    /// </summary>
    public static class RuntimeGridCensus
    {
        private static readonly BindingFlags Flags =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        public static GridMetricsCensus Current()
        {
            try
            {
                WorldGrid grid = WorldGrid.Instance;
                if (grid == null)
                {
                    return GridMetricsCensus.Empty;
                }

                float width = ReadFloat(grid, "WorldWidth");
                float height = ReadFloat(grid, "WorldHeight");
                int horizCount = ReadInt(grid, "HorizontalCellCount");
                int vertCount = ReadInt(grid, "VerticalCellCount");
                float cellSize = ReadFloat(grid, "CellSize");

                Vector2Int playerCell = ReadVector2Int(grid, "PlayerStartCell");
                Vector2Int aiCell = ReadVector2Int(grid, "AiStartCell");

                return new GridMetricsCensus(
                    width,
                    height,
                    horizCount,
                    vertCount,
                    cellSize,
                    playerCell.x,
                    playerCell.y,
                    aiCell.x,
                    aiCell.y,
                    true);
            }
            catch
            {
                return GridMetricsCensus.Empty;
            }
        }

        public static GridCellEvaluationResult EvaluatePosition(float x, float z)
        {
            try
            {
                WorldGrid grid = WorldGrid.Instance;
                if (grid == null)
                {
                    return new GridCellEvaluationResult(false, x, z, 0, 0, 0f, false, "WorldGrid instance not available.");
                }

                Vector3 pos = new Vector3(x, 0f, z);
                bool withinWorld = grid.IsWithinWorld(pos);
                Vector3 clamped = grid.ClampToWorld(pos);

                float cellSize = ReadFloat(grid, "CellSize");
                if (cellSize <= 0.01f)
                {
                    cellSize = 2f;
                }

                int cellX = Mathf.RoundToInt(clamped.x / cellSize);
                int cellY = Mathf.RoundToInt(clamped.z / cellSize);

                Vector2Int cell = new Vector2Int(cellX, cellY);
                bool isBorder = false;
                try
                {
                    isBorder = grid.IsBorderCell(cell);
                }
                catch
                {
                    // Degrade gracefully
                }

                float surfaceHeight = 0f;
                try
                {
                    surfaceHeight = grid.Height(cell);
                }
                catch
                {
                    // Degrade gracefully
                }

                string msg = withinWorld
                    ? "Coordinates within world bounds."
                    : "Coordinates out of bounds; clamped to edge.";

                return new GridCellEvaluationResult(
                    withinWorld,
                    clamped.x,
                    clamped.z,
                    cellX,
                    cellY,
                    surfaceHeight,
                    isBorder,
                    msg);
            }
            catch (Exception ex)
            {
                return new GridCellEvaluationResult(false, x, z, 0, 0, 0f, false, "Error evaluating position: " + ex.Message);
            }
        }

        private static float ReadFloat(object target, string name)
        {
            try
            {
                Type type = target.GetType();
                PropertyInfo prop = type.GetProperty(name, Flags);
                if (prop != null)
                {
                    object val = prop.GetValue(target, null);
                    if (val is float f) return f;
                    if (val is int i) return i;
                }
                FieldInfo field = type.GetField(name, Flags);
                if (field != null)
                {
                    object val = field.GetValue(target);
                    if (val is float f) return f;
                    if (val is int i) return i;
                }
            }
            catch { }
            return 0f;
        }

        private static int ReadInt(object target, string name)
        {
            try
            {
                Type type = target.GetType();
                PropertyInfo prop = type.GetProperty(name, Flags);
                if (prop != null)
                {
                    object val = prop.GetValue(target, null);
                    if (val is int i) return i;
                }
                FieldInfo field = type.GetField(name, Flags);
                if (field != null)
                {
                    object val = field.GetValue(target);
                    if (val is int i) return i;
                }
            }
            catch { }
            return 0;
        }

        private static Vector2Int ReadVector2Int(object target, string name)
        {
            try
            {
                Type type = target.GetType();
                PropertyInfo prop = type.GetProperty(name, Flags);
                if (prop != null && prop.PropertyType == typeof(Vector2Int))
                {
                    object val = prop.GetValue(target, null);
                    if (val is Vector2Int v) return v;
                }
                FieldInfo field = type.GetField(name, Flags);
                if (field != null && field.FieldType == typeof(Vector2Int))
                {
                    object val = field.GetValue(target);
                    if (val is Vector2Int v) return v;
                }
            }
            catch { }
            return new Vector2Int(0, 0);
        }
    }
}
