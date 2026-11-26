using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Studios57.DeformableSnow.Editor.Tests
{
    /// <summary>
    /// Exercises the setup path that the Setup tab drives. Each test builds its own scene
    /// and keeps the editor's existing scenes open.
    /// </summary>
    public sealed class DeformableSnowSetupTests
    {
        private const string TestArtifactFolder = "Assets/TestArtifacts/DeformableSnow";

        private Scene originalScene;
        private Scene testScene;
        private string savedTestScenePath;
        private string baseScenePath;
        private readonly List<Scene> scenes = new List<Scene>();
        private readonly List<TerrainData> terrainData = new List<TerrainData>();

        [SetUp]
        public void SetUp()
        {
            originalScene = EditorSceneManager.GetActiveScene();

            // Unity refuses to create an additive scene while the active scene is an
            // untitled scene. Save an empty untitled scene to a temporary asset instead
            // of discarding anything; never save over a scene with user edits.
            if (string.IsNullOrEmpty(originalScene.path))
            {
                if (originalScene.isDirty)
                    Assert.Ignore("Save or close the untitled active scene before running these tests.");

                EnsureFolder(TestArtifactFolder);
                baseScenePath = AssetDatabase.GenerateUniqueAssetPath(TestArtifactFolder + "/SetupTestBase.unity");
                Assert.IsTrue(EditorSceneManager.SaveScene(originalScene, baseScenePath));
            }

            testScene = CreateScene();
            SceneManager.SetActiveScene(testScene);
        }

        [OneTimeTearDown]
        public void CleanupTestArtifacts()
        {
            if (AssetDatabase.IsValidFolder(TestArtifactFolder))
                AssetDatabase.DeleteAsset(TestArtifactFolder);
        }

        [TearDown]
        public void TearDown()
        {
            if (originalScene.IsValid() && originalScene.isLoaded)
                SceneManager.SetActiveScene(originalScene);
            foreach (Scene scene in scenes)
                EditorSceneManager.CloseScene(scene, true);
            foreach (TerrainData data in terrainData)
                Object.DestroyImmediate(data);
            if (!string.IsNullOrEmpty(savedTestScenePath))
                AssetDatabase.DeleteAsset(savedTestScenePath);
            savedTestScenePath = null;
            scenes.Clear();
            terrainData.Clear();

            if (!string.IsNullOrEmpty(baseScenePath))
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                AssetDatabase.DeleteAsset(baseScenePath);
                baseScenePath = null;
            }
        }

        private Scene CreateScene()
        {
            if (scenes.Count == 1)
            {
                // Unity allows only one untitled scene at a time.
                EnsureFolder(TestArtifactFolder);
                savedTestScenePath = AssetDatabase.GenerateUniqueAssetPath(TestArtifactFolder + "/SetupTestScene.unity");
                Assert.IsTrue(EditorSceneManager.SaveScene(scenes[0], savedTestScenePath));
            }
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            scenes.Add(scene);
            return scene;
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

        private int CountRoots() => CountRoots(testScene);

        private static int CountRoots(Scene scene)
        {
            int count = 0;
            foreach (GameObject root in scene.GetRootGameObjects())
                if (root.name == DeformableSnowSetup.RootName) count++;
            return count;
        }

        private static GameObject FindRig(Scene scene)
            => System.Array.Find(scene.GetRootGameObjects(), root => root.name == DeformableSnowSetup.RootName);

        private DeformableSnowSetupSettings SmallSettings()
        {
            var settings = new DeformableSnowSetupSettings
            {
                // AutoFit would override the small volume with a terrain-derived one, which is
                // not what these behavioural tests are about.
                AutoFit = false,
                VolumeSize = 4f,
                SaveScene = false
            };
            return settings;
        }

        private Terrain CreateMainTerrain() => CreateTerrain(testScene, Vector3.zero);

        private Terrain CreateTerrain(Scene scene, Vector3 position)
        {
            var data = new TerrainData { heightmapResolution = 33, size = new Vector3(200f, 40f, 200f) };
            terrainData.Add(data);
            GameObject go = Terrain.CreateTerrainGameObject(data);
            SceneManager.MoveGameObjectToScene(go, scene);
            go.transform.position = position;
            return go.GetComponent<Terrain>();
        }

        [Test]
        public void CheckScene_WithoutTerrain_ExplainsWhySetupCannotRun()
        {
            string problem = DeformableSnowSetup.CheckScene(testScene, out Terrain terrain);

            Assert.IsNotNull(problem, "A scene with no main terrain must refuse setup.");
            Assert.IsNull(terrain);
            StringAssert.Contains("main Terrain", problem);
        }

        [Test]
        public void Run_WithoutTerrain_WritesNothing()
        {
            string report = DeformableSnowSetup.Run(SmallSettings());

            StringAssert.Contains("main Terrain", report);
            Assert.AreEqual(0, CountRoots(), "Setup must not create anything when the guard fails.");
        }

        [Test]
        public void Run_WithTerrain_BuildsOneRigAndIsIdempotent()
        {
            CreateMainTerrain();
            Assert.IsNull(DeformableSnowSetup.CheckScene(testScene, out _));

            DeformableSnowSetup.Run(SmallSettings());
            Assert.AreEqual(1, CountRoots());

            DeformableSnowSetup.Run(SmallSettings());
            Assert.AreEqual(1, CountRoots(), "Running setup twice must leave exactly one rig.");

            Assert.IsNotNull(FindRig(testScene).GetComponentInChildren<DeformableSnowVolume>());
        }

        [Test]
        public void Run_WithTerrainSlots_BuildsOneVolumePerTileInItsOwnScene()
        {
            Scene otherScene = CreateScene();
            SceneManager.SetActiveScene(testScene);
            Terrain first = CreateMainTerrain();
            Terrain second = CreateTerrain(testScene, new Vector3(220f, 10f, 0f));
            Terrain third = CreateTerrain(otherScene, new Vector3(440f, 30f, 0f));
            var settings = new DeformableSnowSetupSettings
            {
                Terrains = new[] { first, null, second, third, second },
                SaveScene = false
            };

            DeformableSnowSetup.Run(settings);
            DeformableSnowSetup.Run(settings);
            Undo.PerformUndo();
            Undo.PerformRedo();

            Assert.AreEqual(1, CountRoots(testScene));
            Assert.AreEqual(1, CountRoots(otherScene));
            var ownVolumes = FindRig(testScene).GetComponentsInChildren<DeformableSnowVolume>();
            var otherVolumes = FindRig(otherScene).GetComponentsInChildren<DeformableSnowVolume>();
            Assert.AreEqual(2, ownVolumes.Length);
            Assert.AreEqual(1, otherVolumes.Length);
            Assert.AreEqual(29.98f, otherVolumes[0].SurfaceHeight, 0.001f);
            Assert.AreEqual(540f, otherVolumes[0].Bounds.center.x, 0.001f);
            Assert.AreSame(third, new SerializedObject(otherVolumes[0]).FindProperty("terrain").objectReferenceValue);

            DeformableSnowSetup.Clear();
            Assert.AreEqual(0, CountRoots(testScene));
            Assert.AreEqual(1, CountRoots(otherScene));
        }

        [Test]
        public void Run_WithInvalidSlot_DoesNotReplaceAnExistingRig()
        {
            Scene otherScene = CreateScene();
            SceneManager.SetActiveScene(testScene);
            Terrain first = CreateMainTerrain();
            DeformableSnowSetup.Run(SmallSettings());
            GameObject before = FindRig(testScene);
            Terrain invalid = CreateTerrain(otherScene, new Vector3(220f, 0f, 0f));
            invalid.terrainData = null;
            var settings = SmallSettings();
            settings.Terrains = new[] { first, invalid };

            StringAssert.Contains("no TerrainData", DeformableSnowSetup.Run(settings));
            Assert.AreSame(before, FindRig(testScene));
            Assert.AreEqual(0, CountRoots(otherScene));
        }

        [Test]
        public void Run_CreatesNoSnowGeometry()
        {
            CreateMainTerrain();

            DeformableSnowSetup.Run(SmallSettings());

            // The snow is a screen-space pass now, so a rig that owns a MeshFilter or a
            // renderer is the old mesh path leaking back in. That path duplicated the terrain
            // surface and every geometry bug the feature had came from keeping the two in
            // agreement, so its absence is what this test protects.
            var volume = FindRig(testScene).GetComponentInChildren<DeformableSnowVolume>();
            Assert.IsNotNull(volume);
            Assert.AreEqual(0, volume.GetComponentsInChildren<MeshFilter>(true).Length,
                "The volume must not own snow geometry. The test walker uses primitive meshes.");
        }

        [Test]
        public void Run_CreatesTheAppearanceMaterial()
        {
            CreateMainTerrain();

            DeformableSnowSetup.Run(SmallSettings());

            var material = AssetDatabase.LoadAssetAtPath<Material>(DeformableSnowSetup.MaterialAssetPath);
            Assert.IsNotNull(material, "The renderer feature reads its appearance from this material.");
            Assert.IsNotNull(material.shader, "The material must point at the layer shader.");
            StringAssert.Contains("DeformableSnow/Layer", material.shader.name);
        }

        [Test]
        public void Run_ThenUndo_RemovesTheWholeRig()
        {
            CreateMainTerrain();
            DeformableSnowSetup.Run(SmallSettings());
            Assert.AreEqual(1, CountRoots());

            Undo.PerformUndo();

            Assert.AreEqual(0, CountRoots(), "One undo should remove the entire rig.");
        }

        [Test]
        public void Clear_RemovesTheRigButLeavesTheMaterial()
        {
            CreateMainTerrain();
            DeformableSnowSetup.Run(SmallSettings());
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<Material>(DeformableSnowSetup.MaterialAssetPath));

            DeformableSnowSetup.Clear();

            Assert.AreEqual(0, CountRoots());
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<Material>(DeformableSnowSetup.MaterialAssetPath),
                "Clearing the scene must not delete shared assets.");
        }

        [Test]
        public void FitToTerrain_DerivesFootprintFromTexelSize()
        {
            Terrain terrain = CreateMainTerrain();
            var settings = new DeformableSnowSetupSettings { TargetTexelSize = 0.0078f, Resolution = 1024 };

            TerrainFit fit = DeformableSnowSetup.FitToTerrain(terrain, settings);

            Assert.IsTrue(fit.Valid);
            Assert.AreEqual(8f, fit.VolumeSize, 0.01f, "8 mm texels at 1024 should give an 8 m volume.");
            Assert.AreEqual(0.0078f, fit.TexelSize, 0.0005f);
            // A freshly created TerrainData is flat, so the spread is 0 and the slab is the
            // 0.5 m margin. Assert the relationship rather than a magnitude that depends on
            // whether the test terrain happens to have relief.
            Assert.AreEqual(fit.MaxHeight - fit.MinHeight + 0.5f, fit.CaptureDepth, 0.001f,
                "The slab must clear the height spread plus the margin.");
        }

        [Test]
        public void FitToTerrain_KeepsAnInTerrainCentre()
        {
            Terrain terrain = CreateMainTerrain();
            var settings = new DeformableSnowSetupSettings { Centre = new Vector2(60f, 70f) };

            TerrainFit fit = DeformableSnowSetup.FitToTerrain(terrain, settings);

            Assert.AreEqual(new Vector2(60f, 70f), fit.Centre,
                "Choosing a terrain must not relocate a site that is already inside it.");
        }

        [Test]
        public void FitToTerrain_MovesAnOutsideCentreIntoTheTerrain()
        {
            Terrain terrain = CreateMainTerrain();
            var settings = new DeformableSnowSetupSettings { Centre = new Vector2(9000f, 9000f) };

            TerrainFit fit = DeformableSnowSetup.FitToTerrain(terrain, settings);

            Assert.AreNotEqual(new Vector2(9000f, 9000f), fit.Centre,
                "A centre outside the terrain would capture nothing, so it must be corrected.");
        }

        [Test]
        public void FitToTerrain_WithoutTerrain_IsInvalid()
        {
            TerrainFit fit = DeformableSnowSetup.FitToTerrain(null, new DeformableSnowSetupSettings());

            Assert.IsFalse(fit.Valid);
            Assert.IsNotNull(fit.Warning);
        }

        [Test]
        public void FitToTerrain_RefusesAVolumeThatDoesNotFitOnTheTerrain()
        {
            Terrain terrain = CreateMainTerrain();
            // 200 m terrain, so a 51 m volume at the far edge hangs over the side.
            var settings = new DeformableSnowSetupSettings
            {
                TargetTexelSize = 0.05f,
                Centre = new Vector2(190f, 190f)
            };

            TerrainFit fit = DeformableSnowSetup.FitToTerrain(terrain, settings);

            Assert.IsFalse(fit.Valid, "A volume hanging off the terrain cannot capture anything there.");
            StringAssert.Contains("does not fit", fit.Warning);
        }

        [Test]
        public void Run_WithAnUnfittableVolume_WritesNothing()
        {
            Terrain terrain = CreateMainTerrain();
            var settings = new DeformableSnowSetupSettings
            {
                Terrain = terrain,
                AutoFit = true,
                TargetTexelSize = 0.05f,
                Centre = new Vector2(190f, 190f),
                SaveScene = false
            };

            string report = DeformableSnowSetup.Run(settings);

            StringAssert.Contains("Cannot fit", report);
            Assert.AreEqual(0, CountRoots(), "An unfittable volume must not leave a partial rig behind.");
        }

        [Test]
        public void Run_AutoFit_BuildsTheVolumeTheFitDescribes()
        {
            Terrain terrain = CreateMainTerrain();
            var settings = new DeformableSnowSetupSettings
            {
                Terrain = terrain,
                AutoFit = true,
                TargetTexelSize = 0.0078f,
                Resolution = 1024,
                SaveScene = false
            };

            // Ask the fit what it expects *before* Run mutates the settings, because AutoFit
            // overwrites the centre and volume size with the fitted values.
            TerrainFit expected = DeformableSnowSetup.FitToTerrain(terrain, settings);
            Assert.IsTrue(expected.Valid);
            DeformableSnowSetup.ApplyFit(settings, expected);

            string report = DeformableSnowSetup.Run(settings);

            StringAssert.DoesNotContain("failed", report.ToLowerInvariant());
            var volume = FindRig(testScene).GetComponentInChildren<DeformableSnowVolume>();
            Assert.IsNotNull(volume);
            Assert.AreEqual(expected.VolumeSize, volume.WorldSize.x, 0.01f,
                "The built volume must match the dimensions the fit previewed.");
            Assert.AreEqual(expected.Centre.x, volume.transform.position.x, 0.01f);
            Assert.AreEqual(expected.Centre.y, volume.transform.position.z, 0.01f);
        }
    }
}
