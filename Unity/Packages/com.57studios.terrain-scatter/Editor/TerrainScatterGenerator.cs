using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Studios57.TerrainScatter.Editor
{
    public sealed class ScatterSettings
    {
        public Terrain Terrain;
        public TerrainLayer Layer;
        public IList<GameObject> Prefabs;
        public int Count;
        public Vector3 ScalePercent;
        public Vector3 RotationPercent;
        public float MinSlope;
        public float MaxSlope = 90f;
        public float MinSpacing;
        public int Seed;
    }

    public static class TerrainScatterGenerator
    {
        private struct Cell
        {
            public int X;
            public int Y;

            public Cell(int x, int y) { X = x; Y = y; }
        }

        private struct Placement
        {
            public Vector3 Position;
            public int PrefabIndex;
            public Vector3 ScaleFactors;
            public Vector3 RotationDegrees;
        }

        public static bool IsDominant(float[,,] weights, int y, int x, int layer)
        {
            float selected = weights[y, x, layer];
            if (selected <= 0f) return false;
            for (int i = 0; i < weights.GetLength(2); i++)
                if (weights[y, x, i] > selected) return false;
            return true;
        }

        public static float RandomScaleFactor(System.Random random, float percent)
        {
            float spread = Mathf.Clamp(percent, 0f, 100f) / 100f;
            return Mathf.Max(0.01f, 1f + ((float)random.NextDouble() * 2f - 1f) * spread);
        }

        public static float RandomRotationDegrees(System.Random random, float percent)
        {
            return ((float)random.NextDouble() * 2f - 1f) * Mathf.Clamp(percent, 0f, 100f) * 1.8f;
        }

        public static int Scatter(ScatterSettings settings, out string message)
        {
            string error = Validate(settings);
            if (error != null) { message = error; return 0; }

            Terrain terrain = settings.Terrain;
            TerrainData data = terrain.terrainData;
            int layer = Array.IndexOf(data.terrainLayers, settings.Layer);
            List<Cell> cells = CollectCells(data, layer);
            if (cells.Count == 0)
            {
                message = "The chosen layer is not dominant anywhere on this terrain. The previous batch was kept.";
                return 0;
            }

            var random = new System.Random(settings.Seed);
            var placements = new List<Placement>(settings.Count);
            var grid = new Dictionary<Vector2Int, List<Vector2>>();
            float spacing = Mathf.Max(0f, settings.MinSpacing);
            int attempts = Mathf.Min(2000000, Math.Max(1000, settings.Count * 100));
            Vector3 origin = terrain.GetPosition();
            Vector3 size = data.size;
            int holes = data.holesResolution;

            for (int attempt = 0; attempt < attempts && placements.Count < settings.Count; attempt++)
            {
                Cell cell = cells[random.Next(cells.Count)];
                float u = (cell.X + (float)random.NextDouble()) / data.alphamapWidth;
                float v = (cell.Y + (float)random.NextDouble()) / data.alphamapHeight;
                int holeX = Mathf.Clamp((int)(u * holes), 0, holes - 1);
                int holeY = Mathf.Clamp((int)(v * holes), 0, holes - 1);
                if (data.IsHole(holeX, holeY)) continue;
                float slope = data.GetSteepness(u, v);
                if (slope < settings.MinSlope || slope > settings.MaxSlope) continue;

                Vector2 flat = new Vector2(origin.x + u * size.x, origin.z + v * size.z);
                if (spacing > 0f && !HasRoom(flat, spacing, grid)) continue;
                var placement = new Placement
                {
                    Position = new Vector3(flat.x, origin.y + data.GetInterpolatedHeight(u, v), flat.y),
                    PrefabIndex = random.Next(settings.Prefabs.Count),
                    ScaleFactors = new Vector3(RandomScaleFactor(random, settings.ScalePercent.x), RandomScaleFactor(random, settings.ScalePercent.y), RandomScaleFactor(random, settings.ScalePercent.z)),
                    RotationDegrees = new Vector3(RandomRotationDegrees(random, settings.RotationPercent.x), RandomRotationDegrees(random, settings.RotationPercent.y), RandomRotationDegrees(random, settings.RotationPercent.z))
                };
                placements.Add(placement);
                if (spacing > 0f) AddToGrid(flat, spacing, grid);
            }

            if (placements.Count == 0)
            {
                message = "No positions met the slope and spacing limits. The previous batch was kept.";
                return 0;
            }

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Scatter 57Studios prefabs");
            try
            {
                TerrainScatterBatch previous = FindBatch(terrain);
                if (previous != null) Undo.DestroyObjectImmediate(previous.gameObject);

                var root = new GameObject("57Studios Scatter - " + terrain.name);
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, terrain.gameObject.scene);
                Undo.RegisterCreatedObjectUndo(root, "Scatter 57Studios prefabs");
                root.AddComponent<TerrainScatterBatch>().SetTerrain(terrain);

                foreach (Placement placement in placements)
                {
                    GameObject prefab = settings.Prefabs[placement.PrefabIndex];
                    var instance = PrefabUtility.InstantiatePrefab(prefab, terrain.gameObject.scene) as GameObject;
                    if (instance == null) throw new InvalidOperationException("Could not instantiate " + prefab.name);
                    Undo.RegisterCreatedObjectUndo(instance, "Scatter 57Studios prefabs");
                    instance.transform.SetParent(root.transform, true);
                    instance.transform.position = placement.Position;
                    instance.transform.rotation = prefab.transform.rotation * Quaternion.Euler(placement.RotationDegrees);
                    instance.transform.localScale = Vector3.Scale(prefab.transform.localScale, placement.ScaleFactors);
                }

                Undo.CollapseUndoOperations(undoGroup);
                message = placements.Count == settings.Count
                    ? "Scattered " + placements.Count + " prefabs."
                    : "Scattered " + placements.Count + " of " + settings.Count + " prefabs; no more positions met the filters within the attempt limit.";
                return placements.Count;
            }
            catch (Exception exception)
            {
                Undo.RevertAllDownToGroup(undoGroup);
                message = "Scatter failed and the previous batch was restored: " + exception.Message;
                return 0;
            }
        }

        public static bool Clear(Terrain terrain)
        {
            TerrainScatterBatch batch = FindBatch(terrain);
            if (batch == null) return false;
            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Clear 57Studios scattered prefabs");
            Undo.DestroyObjectImmediate(batch.gameObject);
            Undo.CollapseUndoOperations(undoGroup);
            return true;
        }

        private static string Validate(ScatterSettings settings)
        {
            if (settings == null || settings.Terrain == null || settings.Terrain.terrainData == null) return "Choose a Terrain with Terrain Data.";
            if (!settings.Terrain.gameObject.scene.IsValid()) return "Choose a Terrain in a loaded scene.";
            if (settings.Layer == null || Array.IndexOf(settings.Terrain.terrainData.terrainLayers, settings.Layer) < 0) return "Choose a layer painted on the selected Terrain.";
            if (settings.Prefabs == null || settings.Prefabs.Count == 0) return "Add at least one prefab asset.";
            foreach (GameObject prefab in settings.Prefabs)
                if (prefab == null || !PrefabUtility.IsPartOfPrefabAsset(prefab)) return "All prefab entries must be project prefab assets.";
            if (settings.Count < 1 || settings.Count > 100000) return "Count must be between 1 and 100,000.";
            if (settings.MinSlope < 0f || settings.MaxSlope > 90f || settings.MinSlope > settings.MaxSlope) return "Slope range must be between 0° and 90° and in ascending order.";
            if (settings.MinSpacing < 0f) return "Minimum spacing cannot be negative.";
            return null;
        }

        private static List<Cell> CollectCells(TerrainData data, int layer)
        {
            var cells = new List<Cell>();
            int width = data.alphamapWidth;
            int height = data.alphamapHeight;
            for (int row = 0; row < height; row += 64)
            {
                int rows = Mathf.Min(64, height - row);
                float[,,] weights = data.GetAlphamaps(0, row, width, rows);
                for (int y = 0; y < rows; y++)
                    for (int x = 0; x < width; x++)
                        if (IsDominant(weights, y, x, layer)) cells.Add(new Cell(x, row + y));
            }
            return cells;
        }

        private static TerrainScatterBatch FindBatch(Terrain terrain)
        {
            if (terrain == null) return null;
            foreach (TerrainScatterBatch batch in UnityEngine.Object.FindObjectsByType<TerrainScatterBatch>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (batch.Terrain == terrain && batch.gameObject.scene == terrain.gameObject.scene) return batch;
            return null;
        }

        private static Vector2Int GridKey(Vector2 point, float spacing)
        {
            return new Vector2Int(Mathf.FloorToInt(point.x / spacing), Mathf.FloorToInt(point.y / spacing));
        }

        private static bool HasRoom(Vector2 point, float spacing, Dictionary<Vector2Int, List<Vector2>> grid)
        {
            Vector2Int key = GridKey(point, spacing);
            for (int y = -1; y <= 1; y++)
                for (int x = -1; x <= 1; x++)
                    if (grid.TryGetValue(key + new Vector2Int(x, y), out List<Vector2> neighbors))
                        foreach (Vector2 other in neighbors)
                            if ((point - other).sqrMagnitude < spacing * spacing) return false;
            return true;
        }

        private static void AddToGrid(Vector2 point, float spacing, Dictionary<Vector2Int, List<Vector2>> grid)
        {
            Vector2Int key = GridKey(point, spacing);
            if (!grid.TryGetValue(key, out List<Vector2> points))
            {
                points = new List<Vector2>();
                grid.Add(key, points);
            }
            points.Add(point);
        }
    }
}
