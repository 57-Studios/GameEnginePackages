using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Studios57.TerrainScatter.Editor
{
    public sealed class TerrainScatterWindow : EditorWindow
    {
        [SerializeField] private Terrain terrain;
        [SerializeField] private TerrainLayer layer;
        [SerializeField] private List<GameObject> prefabs = new List<GameObject>();
        [SerializeField] private int count = 100;
        [SerializeField] private Vector3 scalePercent = new Vector3(10f, 10f, 10f);
        [SerializeField] private Vector3 rotationPercent = new Vector3(0f, 100f, 0f);
        [SerializeField] private float minSlope;
        [SerializeField] private float maxSlope = 90f;
        [SerializeField] private float minSpacing;
        [SerializeField] private int seed = 12345;

        private Vector2 scroll;
        private string status;

        [MenuItem("57Studios/Terrain Prefab Scatter")]
        public static void Open() => GetWindow<TerrainScatterWindow>("Terrain Prefab Scatter");

        private void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.LabelField("57Studios Terrain Prefab Scatter", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            Terrain chosen = (Terrain)EditorGUILayout.ObjectField("Terrain", terrain, typeof(Terrain), true);
            if (chosen != terrain)
            {
                terrain = chosen;
                layer = null;
            }
            DrawLayerPicker();
            EditorGUILayout.Space();

            EditorGUILayout.LabelField("Prefab Assets", EditorStyles.boldLabel);
            for (int i = 0; i < prefabs.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                prefabs[i] = (GameObject)EditorGUILayout.ObjectField("Prefab " + (i + 1), prefabs[i], typeof(GameObject), false);
                if (GUILayout.Button("−", GUILayout.Width(26)))
                {
                    prefabs.RemoveAt(i);
                    i--;
                }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Add Prefab Slot")) prefabs.Add(null);
            if (GUILayout.Button("Add Selected Prefabs")) AddSelectedPrefabs();
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space();

            count = EditorGUILayout.IntField("Total Prefab Count", count);
            EditorGUILayout.LabelField("Scale / Height Variation (%)", EditorStyles.boldLabel);
            scalePercent = PercentFields(scalePercent);
            EditorGUILayout.LabelField("Rotation Variation (%)", EditorStyles.boldLabel);
            rotationPercent = PercentFields(rotationPercent);
            EditorGUILayout.HelpBox("Scale varies around each prefab's original XYZ scale. At 100%, rotation varies by up to 180° in either direction on each axis.", MessageType.Info);
            EditorGUILayout.Space();

            EditorGUILayout.LabelField("Placement Filters", EditorStyles.boldLabel);
            minSlope = EditorGUILayout.FloatField("Minimum Slope (°)", minSlope);
            maxSlope = EditorGUILayout.FloatField("Maximum Slope (°)", maxSlope);
            minSpacing = EditorGUILayout.FloatField("Minimum Spacing", minSpacing);
            seed = EditorGUILayout.IntField("Random Seed", seed);
            EditorGUILayout.Space();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Scatter", GUILayout.Height(30))) Scatter();
            if (GUILayout.Button("Clear This Terrain", GUILayout.Height(30))) Clear();
            EditorGUILayout.EndHorizontal();
            if (!string.IsNullOrEmpty(status)) EditorGUILayout.HelpBox(status, MessageType.None);
            EditorGUILayout.EndScrollView();
        }

        private void DrawLayerPicker()
        {
            TerrainLayer[] layers = terrain != null && terrain.terrainData != null ? terrain.terrainData.terrainLayers : null;
            if (layers == null || layers.Length == 0)
            {
                layer = null;
                EditorGUILayout.HelpBox("Choose a Terrain with painted Terrain Layers.", MessageType.Info);
                return;
            }

            var names = new string[layers.Length];
            for (int i = 0; i < layers.Length; i++) names[i] = layers[i] != null ? layers[i].name : "Missing Layer " + (i + 1);
            int selected = System.Array.IndexOf(layers, layer);
            int next = EditorGUILayout.Popup("Terrain Layer", Mathf.Max(0, selected), names);
            layer = layers[next];
        }

        private static Vector3 PercentFields(Vector3 value)
        {
            EditorGUILayout.BeginHorizontal();
            float labelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = 16f;
            value.x = EditorGUILayout.FloatField("X", value.x);
            value.y = EditorGUILayout.FloatField("Y", value.y);
            value.z = EditorGUILayout.FloatField("Z", value.z);
            EditorGUIUtility.labelWidth = labelWidth;
            EditorGUILayout.EndHorizontal();
            value.x = Mathf.Clamp(value.x, 0f, 100f);
            value.y = Mathf.Clamp(value.y, 0f, 100f);
            value.z = Mathf.Clamp(value.z, 0f, 100f);
            return value;
        }

        private void AddSelectedPrefabs()
        {
            foreach (Object selected in Selection.objects)
            {
                if (selected is GameObject candidate && PrefabUtility.IsPartOfPrefabAsset(candidate) && !prefabs.Contains(candidate))
                    prefabs.Add(candidate);
            }
        }

        private void Scatter()
        {
            var settings = new ScatterSettings
            {
                Terrain = terrain,
                Layer = layer,
                Prefabs = prefabs,
                Count = count,
                ScalePercent = scalePercent,
                RotationPercent = rotationPercent,
                MinSlope = minSlope,
                MaxSlope = maxSlope,
                MinSpacing = minSpacing,
                Seed = seed
            };
            int placed = TerrainScatterGenerator.Scatter(settings, out status);
            if (placed > 0) EditorSceneManager.MarkSceneDirty(terrain.gameObject.scene);
        }

        private void Clear()
        {
            if (terrain == null) { status = "Choose a Terrain first."; return; }
            bool cleared = TerrainScatterGenerator.Clear(terrain);
            status = cleared ? "Cleared the previous batch. Use Undo to restore it." : "No 57Studios scatter batch was found for this terrain.";
            if (cleared) EditorSceneManager.MarkSceneDirty(terrain.gameObject.scene);
        }
    }
}
