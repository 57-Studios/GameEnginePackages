using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Studios57.TerrainScatter.Editor.Tests
{
    public sealed class TerrainScatterTests
    {
        private const string TestPrefabFolder = "Assets/TestArtifacts/TerrainScatter";

        private GameObject terrainObject;
        private TerrainData data;
        private TerrainLayer selectedLayer;
        private TerrainLayer otherLayer;
        private readonly List<string> prefabPaths = new List<string>();

        [OneTimeTearDown]
        public void CleanupTestArtifacts()
        {
            if (AssetDatabase.IsValidFolder(TestPrefabFolder))
                AssetDatabase.DeleteAsset(TestPrefabFolder);
        }

        [TearDown]
        public void TearDown()
        {
            if (terrainObject != null)
            {
                TerrainScatterGenerator.Clear(terrainObject.GetComponent<Terrain>());
                Object.DestroyImmediate(terrainObject);
            }
            if (data != null) Object.DestroyImmediate(data);
            if (selectedLayer != null) Object.DestroyImmediate(selectedLayer);
            if (otherLayer != null) Object.DestroyImmediate(otherLayer);
            foreach (string path in prefabPaths) AssetDatabase.DeleteAsset(path);
            prefabPaths.Clear();
        }

        [Test]
        public void Scatter_UsesDominantLayerAndReplacesItsOwnBatch()
        {
            Terrain terrain = CreateTerrain();
            GameObject first = CreatePrefab("A");
            GameObject second = CreatePrefab("B");
            var settings = new ScatterSettings
            {
                Terrain = terrain,
                Layer = selectedLayer,
                Prefabs = new[] { first, second },
                Count = 30,
                Seed = 17,
                RotationPercent = new Vector3(0f, 100f, 0f),
                ScalePercent = new Vector3(10f, 10f, 10f)
            };

            Assert.AreEqual(30, TerrainScatterGenerator.Scatter(settings, out string message), message);
            TerrainScatterBatch[] batches = Object.FindObjectsByType<TerrainScatterBatch>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            Assert.AreEqual(1, batches.Length);
            Assert.AreEqual(30, batches[0].transform.childCount);
            foreach (Transform child in batches[0].transform)
            {
                Assert.Less(child.position.x, 8f); // Right half is painted with the other layer.
                Assert.That(child.position.y, Is.EqualTo(terrain.SampleHeight(child.position) + terrain.transform.position.y).Within(0.01f));
                Assert.That(child.localScale.y, Is.InRange(0.9f, 1.1f));
            }

            settings.Count = 5;
            Assert.AreEqual(5, TerrainScatterGenerator.Scatter(settings, out message), message);
            batches = Object.FindObjectsByType<TerrainScatterBatch>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            Assert.AreEqual(1, batches.Length);
            Assert.AreEqual(5, batches[0].transform.childCount);
            Assert.IsTrue(TerrainScatterGenerator.Clear(terrain));
            Assert.AreEqual(0, Object.FindObjectsByType<TerrainScatterBatch>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length);
        }

        [Test]
        public void Scatter_KeepsPreviousBatchIfLayerHasNoEligibleCells()
        {
            Terrain terrain = CreateTerrain();
            GameObject prefab = CreatePrefab("Only");
            var settings = new ScatterSettings { Terrain = terrain, Layer = selectedLayer, Prefabs = new[] { prefab }, Count = 3, Seed = 9 };
            Assert.AreEqual(3, TerrainScatterGenerator.Scatter(settings, out string message), message);
            TerrainScatterBatch previous = Object.FindAnyObjectByType<TerrainScatterBatch>();

            var maps = new float[16, 16, 2];
            for (int y = 0; y < 16; y++)
                for (int x = 0; x < 16; x++) maps[y, x, 1] = 1f;
            data.SetAlphamaps(0, 0, maps);

            Assert.AreEqual(0, TerrainScatterGenerator.Scatter(settings, out message));
            Assert.AreSame(previous, Object.FindAnyObjectByType<TerrainScatterBatch>());
        }

        private Terrain CreateTerrain()
        {
            data = new TerrainData { heightmapResolution = 33, alphamapResolution = 16, size = new Vector3(16f, 2f, 16f) };
            selectedLayer = new TerrainLayer();
            otherLayer = new TerrainLayer();
            data.terrainLayers = new[] { selectedLayer, otherLayer };
            var maps = new float[16, 16, 2];
            for (int y = 0; y < 16; y++)
                for (int x = 0; x < 16; x++) maps[y, x, x < 8 ? 0 : 1] = 1f;
            data.SetAlphamaps(0, 0, maps);
            terrainObject = Terrain.CreateTerrainGameObject(data);
            return terrainObject.GetComponent<Terrain>();
        }

        private GameObject CreatePrefab(string suffix)
        {
            EnsureFolder(TestPrefabFolder);
            string path = AssetDatabase.GenerateUniqueAssetPath(TestPrefabFolder + "/ScatterTest" + suffix + ".prefab");
            var source = new GameObject("ScatterTest" + suffix);
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(source, path);
            Object.DestroyImmediate(source);
            prefabPaths.Add(path);
            return prefab;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;

            string parent = System.IO.Path.GetDirectoryName(path)?.Replace('\\', '/');
            string name = System.IO.Path.GetFileName(path);
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
                EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }
    }
}
