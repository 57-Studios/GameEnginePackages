using System;
using System.Collections.Generic;
using System.Text;
using Studios57.DeformableSnow.Testing;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Studios57.DeformableSnow.Editor
{
    /// <summary>
    /// Everything the Setup tab needs. Kept as a plain serializable class so the window can
    /// persist it across assembly reloads and so tests can build one without a window.
    /// </summary>
    [Serializable]
    public sealed class DeformableSnowSetupSettings
    {
        /// <summary>Terrain the snow sits on. When set, the dimensions are fitted to it.</summary>
        public Terrain Terrain;

        /// <summary>Explicit terrain selection. Each gets a volume in its own scene.</summary>
        public Terrain[] Terrains = Array.Empty<Terrain>();

        /// <summary>Derive centre, size and depth from the terrain instead of using the literals below.</summary>
        public bool AutoFit = true;

        /// <summary>
        /// Target world size of one heightfield texel. This is the real constraint on footprint:
        /// a footprint must span several texels to resolve, so the volume extent follows from
        /// this and the resolution rather than being an arbitrary number.
        ///
        /// 8 mm at 1024 reproduces the validated 8 m volume (7.8 mm per texel) exactly.
        /// </summary>
        public float TargetTexelSize = 0.008f;

        public Vector2 Centre = new Vector2(118f, 192f);
        public float VolumeSize = 8f;
        public int Resolution = 1024;
        public float SnowDepth = 0.06f;
        public float ContactGain = 2.5f;
        public float RefillPerSecond = 0.02f;
        public float ContactBand = 0.06f;

        public float PatrolHalfLength = 3.4f;
        public float PatrolHalfWidth = 1.2f;
        public float WalkSpeed = 1.1f;
        public float StepFrequency = 1.8f;
        public float StepLength = 0.3f;
        public float StepLift = 0.14f;
        public float LiftSharpness = 0.4f;
        public float GroundClearance = 0.02f;

        public bool ResetAppearance;
        public bool SaveScene = true;
    }

    /// <summary>Everything derived from a terrain, so the window can show it before building.</summary>
    public struct TerrainFit
    {
        public Vector2 Centre;
        public float VolumeSize;
        public float MinHeight;
        public float MaxHeight;
        public float SurfaceHeight;
        public float CaptureDepth;
        public float TexelSize;
        public string Warning;
        public bool Valid;
    }

    /// <summary>
    /// Builds and clears scene-local DeformableSnow rigs. All scene mutation lives here
    /// so the window stays presentation-only and the EditMode tests can drive the same path.
    /// </summary>
    public static class DeformableSnowSetup
    {
        public const string RootName = "DeformableSnow";
        private const string MaterialRelativePath = "Materials/PackedSlush.mat";
        private const string AssetsMaterialPath = "Assets/57Studios/DeformableSnow/" + MaterialRelativePath;
        private const string SurfaceShaderName = "57Studios/DeformableSnow/Layer";
        private const string UndoLabel = "Set up DeformableSnow";

        /// <summary>Matches the shader properties written by the old Phase2Setup script.</summary>
        public static readonly Color DefaultSnowColour = new Color(0.62f, 0.68f, 0.76f, 1f);
        public static readonly Color DefaultCompressedColour = new Color(0.40f, 0.44f, 0.47f, 1f);
        public static readonly Color DefaultWetColour = new Color(0.56f, 0.64f, 0.70f, 1f);

        /// <summary>
        /// Absolute AssetDatabase path of the appearance material. Resolves through the package
        /// containing this editor assembly, so local file packages and Git packages both work.
        /// </summary>
        public static string MaterialAssetPath
        {
            get
            {
                var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(DeformableSnowSetup).Assembly);
                if (package != null)
                    return package.assetPath + "/" + MaterialRelativePath;

                // Asset-package installs live under Assets/ instead of Packages/.
                return AssetsMaterialPath;
            }
        }

        private static string MaterialFolder =>
            System.IO.Path.GetDirectoryName(MaterialAssetPath)?.Replace('\\', '/');

        /// <summary>
        /// Same predicate every other path in the feature uses: an unparented Terrain with
        /// real data that is large enough to be the scene's main terrain.
        /// </summary>
        public static Terrain FindMainTerrain(Scene scene)
        {
            if (!scene.IsValid())
                return null;

            Terrain best = null;
            float bestArea = 0f;
            foreach (Terrain candidate in UnityEngine.Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None))
            {
                if (candidate == null || candidate.terrainData == null)
                    continue;
                if (candidate.transform.parent != null)
                    continue;
                if (candidate.gameObject.scene != scene)
                    continue;

                Vector3 size = candidate.terrainData.size;
                if (size.x <= 100f)
                    continue;
                float area = size.x * size.z;
                if (area > bestArea)
                {
                    bestArea = area;
                    best = candidate;
                }
            }
            return best;
        }

        /// <summary>Human-readable reason setup cannot run, or null when it can.</summary>
        public static string CheckScene(Scene scene, out Terrain terrain)
        {
            terrain = FindMainTerrain(scene);
            if (!scene.IsValid())
                return "No valid scene is open.";
            if (terrain == null)
                return "This scene has no main Terrain (unparented, at least 100 m wide). "
                    + "Open a scene with terrain and run setup there.";
            if (Shader.Find(SurfaceShaderName) == null)
                return "The surface shader '" + SurfaceShaderName + "' is not available. Recompile first.";
            return null;
        }

        internal static string ResolveTerrains(DeformableSnowSetupSettings settings, Scene fallbackScene,
            List<Terrain> targets)
        {
            targets.Clear();
            if (settings.Terrains != null)
            {
                foreach (Terrain terrain in settings.Terrains)
                {
                    if (terrain != null && !targets.Contains(terrain))
                        targets.Add(terrain);
                }
            }

            if (targets.Count == 0 && settings.Terrain != null)
                targets.Add(settings.Terrain);

            if (targets.Count == 0)
            {
                string problem = CheckScene(fallbackScene, out Terrain terrain);
                if (problem != null)
                    return problem;
                targets.Add(terrain);
            }

            foreach (Terrain terrain in targets)
            {
                Scene scene = terrain.gameObject.scene;
                if (!scene.IsValid() || !scene.isLoaded || EditorSceneManager.IsPreviewScene(scene))
                    return "Choose a Terrain from a loaded scene: " + terrain.name + ".";
                if (terrain.terrainData == null)
                    return "The chosen Terrain has no TerrainData: " + terrain.name + ".";
            }

            return Shader.Find(SurfaceShaderName) == null
                ? "The surface shader '" + SurfaceShaderName + "' is not available. Recompile first."
                : null;
        }

        /// <summary>
        /// Resolves everything that should follow from the terrain rather than from a hand-typed
        /// number: how big the volume is and how deep the capture slab must be.
        ///
        /// The footprint is derived from a target texel size, because that is the property that
        /// actually matters: a footprint is only resolvable if it spans several heightfield
        /// texels. A 3.9 cm texel (the old 40 m / 1024 config) makes a 14 cm foot span fewer
        /// than four texels, which is why the approved 40 m volume was never workable at
        /// footprint scale.
        ///
        /// The centre is only moved when it falls outside the terrain. Choosing a terrain should
        /// change the dimensions, not silently relocate a site the user already validated.
        /// </summary>
        public static TerrainFit FitToTerrain(Terrain terrain, DeformableSnowSetupSettings settings)
            => FitToTerrain(terrain, settings, true, true);

        internal static TerrainFit GetPlacement(Terrain terrain, DeformableSnowSetupSettings settings, bool multiple)
            => FitToTerrain(terrain, settings, settings.AutoFit, settings.AutoFit || multiple);

        private static TerrainFit FitToTerrain(Terrain terrain, DeformableSnowSetupSettings settings,
            bool autoFit, bool fitCentre)
        {
            var fit = new TerrainFit();
            if (terrain == null || terrain.terrainData == null)
            {
                fit.Warning = "No terrain to fit to.";
                return fit;
            }

            Vector3 origin = terrain.transform.position;
            Vector3 size = terrain.terrainData.size;

            // Centre: keep the configured one if it is inside this terrain, otherwise move to
            // the terrain's middle so a freshly slotted terrain still produces a usable rig.
            Vector2 centre = settings.Centre;
            bool inside = centre.x >= origin.x && centre.x <= origin.x + size.x
                && centre.y >= origin.z && centre.y <= origin.z + size.z;
            if (fitCentre && !inside)
                centre = new Vector2(origin.x + size.x * 0.5f, origin.z + size.z * 0.5f);
            float volumeSize = settings.VolumeSize;
            if (autoFit)
            {
                float maxSize = Mathf.Min(size.x, size.z);
                float texel = Mathf.Max(0.002f, settings.TargetTexelSize);
                volumeSize = Mathf.Clamp(Mathf.Round(texel * settings.Resolution), Mathf.Min(1f, maxSize), maxSize);
            }

            float minHeight;
            float maxHeight;
            SampleHeightRange(terrain, centre, volumeSize, out minHeight, out maxHeight);

            fit.Valid = true;
            fit.Centre = centre;
            fit.VolumeSize = volumeSize;
            fit.MinHeight = minHeight;
            fit.MaxHeight = maxHeight;
            fit.SurfaceHeight = minHeight - 0.02f;
            // The slab has to clear the terrain's height spread inside the footprint, plus a
            // margin for anything standing on it.
            fit.CaptureDepth = (maxHeight - minHeight) + 0.5f;
            fit.TexelSize = volumeSize / Mathf.Max(1, settings.Resolution);

            // The volume must sit entirely on the terrain. A rig whose volume hangs over the
            // edge captures nothing there, so refuse it here rather than building something
            // that cannot work.
            float half = volumeSize * 0.5f;
            bool fitsX = centre.x - half >= origin.x && centre.x + half <= origin.x + size.x;
            bool fitsZ = centre.y - half >= origin.z && centre.y + half <= origin.z + size.z;
            if (fitCentre && (!fitsX || !fitsZ || volumeSize < 0.5f))
            {
                fit.Valid = false;
                fit.Warning = "A " + volumeSize.ToString("F0") + " m volume does not fit on '" + terrain.name
                    + "' at " + centre.ToString("F0") + ". That terrain is "
                    + size.x.ToString("F0") + " x " + size.z.ToString("F0") + " m. "
                    + "Adjust the volume size or move the centre inward.";
            }
            return fit;
        }

        /// <summary>Applies a terrain fit to the settings that <see cref="Run"/> consumes.</summary>
        public static void ApplyFit(DeformableSnowSetupSettings settings, TerrainFit fit)
        {
            if (settings == null || !fit.Valid)
                return;
            settings.Centre = fit.Centre;
            settings.VolumeSize = fit.VolumeSize;
        }

        /// <summary>
        /// Rebuilds one rig root per selected scene, with a volume for each selected terrain.
        /// </summary>
        public static string Run(DeformableSnowSetupSettings settings)
        {
            if (settings == null)
                settings = new DeformableSnowSetupSettings();

            var terrains = new List<Terrain>();
            string problem = ResolveTerrains(settings, EditorSceneManager.GetActiveScene(), terrains);
            if (problem != null)
                return problem;

            var placements = new TerrainFit[terrains.Count];
            for (int i = 0; i < terrains.Count; i++)
            {
                TerrainFit fit = GetPlacement(terrains[i], settings, terrains.Count > 1);
                if (!fit.Valid)
                    return "Cannot fit the volume to '" + terrains[i].name + "': " + fit.Warning;
                placements[i] = fit;
            }

            Shader shader = Shader.Find(SurfaceShaderName);
            var sb = new StringBuilder();
            var roots = new Dictionary<Scene, GameObject>();

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(UndoLabel);
            try
            {
                var terrainCounts = new Dictionary<Scene, int>();
                foreach (Terrain terrain in terrains)
                {
                    Scene scene = terrain.gameObject.scene;
                    terrainCounts.TryGetValue(scene, out int count);
                    terrainCounts[scene] = count + 1;
                }

                for (int i = 0; i < terrains.Count; i++)
                {
                    Terrain terrain = terrains[i];
                    Scene scene = terrain.gameObject.scene;
                    if (!roots.TryGetValue(scene, out GameObject root))
                    {
                        RemoveRigRoots(scene);
                        root = new GameObject(RootName);
                        SceneManager.MoveGameObjectToScene(root, scene);
                        Undo.RegisterCreatedObjectUndo(root, UndoLabel);
                        roots.Add(scene, root);
                    }

                    GameObject rig = root;
                    if (terrainCounts[scene] > 1)
                    {
                        rig = new GameObject(terrain.name);
                        SceneManager.MoveGameObjectToScene(rig, scene);
                        Undo.RegisterCreatedObjectUndo(rig, UndoLabel);
                        rig.transform.SetParent(root.transform, false);
                    }

                    TerrainFit fit = placements[i];
                    sb.AppendLine("Terrain: " + terrain.name + " (" + scene.name + ")");
                    BuildVolume(rig, settings, terrain, fit, sb);
                    SnowTestWalker walker = BuildWalker(rig, settings, terrain, fit.Centre, sb);
                    walker.ConfigureGait(settings.StepFrequency, settings.StepLength, settings.StepLift,
                        settings.LiftSharpness, settings.GroundClearance);
                    BuildDebugView(rig, sb);
                }
                BuildSurface(settings, shader, sb);
            }
            catch (Exception exception)
            {
                Undo.RevertAllDownToGroup(undoGroup);
                Debug.LogException(exception);
                return "Setup failed and was rolled back: " + exception.Message;
            }
            Undo.CollapseUndoOperations(undoGroup);

            foreach (Scene scene in roots.Keys)
                EditorSceneManager.MarkSceneDirty(scene);

            if (settings.SaveScene)
                AssetDatabase.SaveAssetIfDirty(AssetDatabase.LoadAssetAtPath<Material>(MaterialAssetPath));
            foreach (Scene scene in roots.Keys)
            {
                bool saved = settings.SaveScene && !string.IsNullOrEmpty(scene.path)
                    && EditorSceneManager.SaveScene(scene);
                sb.AppendLine("Scene: " + (string.IsNullOrEmpty(scene.path) ? scene.name : scene.path)
                    + (saved ? " (saved)" : " (unsaved changes)"));
            }

            if (terrains.Count == 1 && settings.AutoFit)
                ApplyFit(settings, placements[0]);
            sb.AppendLine("Press Ctrl+Z to undo the generated rigs.");
            return sb.ToString();
        }

        /// <summary>Removes the active scene's rig. The shared appearance material stays.</summary>
        public static string Clear()
        {
            Scene scene = EditorSceneManager.GetActiveScene();
            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Clear DeformableSnow");
            bool removed = RemoveRigRoots(scene);
            Undo.CollapseUndoOperations(undoGroup);

            if (!removed)
                return "No " + RootName + " root in the active scene.";
            EditorSceneManager.MarkSceneDirty(scene);
            return "Removed " + RootName + " from " + scene.name + ".";
        }

        private static bool RemoveRigRoots(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded)
                return false;

            bool removed = false;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name != RootName)
                    continue;
                Undo.DestroyObjectImmediate(root);
                removed = true;
            }
            return removed;
        }

        private static void SampleHeightRange(Terrain terrain, Vector2 centre, float size,
            out float minHeight, out float maxHeight)
        {
            minHeight = float.MaxValue;
            maxHeight = float.MinValue;
            const int steps = 4;
            for (int i = -steps; i <= steps; i++)
            {
                for (int j = -steps; j <= steps; j++)
                {
                    var point = new Vector3(centre.x + i * (size / (steps * 2f)), 0f,
                        centre.y + j * (size / (steps * 2f)));
                    float height = terrain.SampleHeight(point) + terrain.transform.position.y;
                    minHeight = Mathf.Min(minHeight, height);
                    maxHeight = Mathf.Max(maxHeight, height);
                }
            }
        }

        private static void BuildVolume(GameObject root, DeformableSnowSetupSettings settings, Terrain terrain,
            TerrainFit fit, StringBuilder sb)
        {
            var go = new GameObject("Snow Volume");
            Undo.RegisterCreatedObjectUndo(go, UndoLabel);
            go.transform.SetParent(root.transform, false);
            go.transform.position = new Vector3(fit.Centre.x, 0f, fit.Centre.y);

            var volume = Undo.AddComponent<DeformableSnowVolume>(go);
            volume.Configure(new Vector2(fit.VolumeSize, fit.VolumeSize), fit.SurfaceHeight,
                fit.CaptureDepth, settings.Resolution, settings.SnowDepth, settings.ContactGain,
                settings.RefillPerSecond, settings.ContactBand);
            volume.SetTerrain(terrain);

            sb.AppendLine("Volume: " + fit.VolumeSize.ToString("F0") + " m at "
                + fit.Centre.ToString("F1") + ", " + settings.Resolution + " texels, snow "
                + (settings.SnowDepth * 100f).ToString("F1") + " cm, contact band "
                + (settings.ContactBand * 100f).ToString("F1") + " cm.");
            sb.AppendLine("  surfaceY=" + fit.SurfaceHeight.ToString("F2") + " terrainSpread="
                + (fit.MaxHeight - fit.MinHeight).ToString("F2") + " m captureDepth=" + fit.CaptureDepth.ToString("F2") + " m");
        }

        private static SnowTestWalker BuildWalker(GameObject root, DeformableSnowSetupSettings settings,
            Terrain terrain, Vector2 centre, StringBuilder sb)
        {
            var go = new GameObject("Snow Test Walker");
            Undo.RegisterCreatedObjectUndo(go, UndoLabel);
            go.transform.SetParent(root.transform, false);
            go.transform.position = new Vector3(centre.x, 0f, centre.y);

            var walker = Undo.AddComponent<SnowTestWalker>(go);

            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Body";
            Undo.RegisterCreatedObjectUndo(body, UndoLabel);
            body.transform.SetParent(go.transform, false);
            // Phase1Polish used to correct these after the fact; the correct values live
            // here now so a single run produces a rig that looks right.
            body.transform.localPosition = new Vector3(0f, 0.62f, 0f);
            body.transform.localScale = new Vector3(0.34f, 0.5f, 0.34f);
            StripCollider(body);

            Transform left = MakeFoot(go.transform, "Foot L", -0.12f);
            Transform right = MakeFoot(go.transform, "Foot R", 0.12f);

            walker.Configure(terrain, left, right,
                new Vector3(-settings.PatrolHalfLength, 0f, -settings.PatrolHalfWidth),
                new Vector3(settings.PatrolHalfLength, 0f, settings.PatrolHalfWidth),
                settings.WalkSpeed);

            sb.AppendLine("Walker: patrols +- " + settings.PatrolHalfLength.ToString("F1")
                + " m at " + settings.WalkSpeed.ToString("F2") + " m/s, step "
                + settings.StepLength.ToString("F2") + " m at " + settings.StepFrequency.ToString("F1")
                + " Hz, lift " + (settings.StepLift * 100f).ToString("F0") + " cm.");
            return walker;
        }

        private static Transform MakeFoot(Transform parent, string name, float x)
        {
            GameObject foot = GameObject.CreatePrimitive(PrimitiveType.Cube);
            foot.name = name;
            Undo.RegisterCreatedObjectUndo(foot, UndoLabel);
            foot.transform.SetParent(parent, false);
            foot.transform.localPosition = new Vector3(x, 0.035f, 0f);
            foot.transform.localScale = new Vector3(0.14f, 0.07f, 0.3f);
            StripCollider(foot);
            Undo.AddComponent<DeformableSnowDeformer>(foot);
            return foot.transform;
        }

        private static void StripCollider(GameObject go)
        {
            var collider = go.GetComponent<Collider>();
            if (collider != null)
                Undo.DestroyObjectImmediate(collider);
        }

        private static void BuildDebugView(GameObject root, StringBuilder sb)
        {
            var go = new GameObject("Snow Debug View");
            Undo.RegisterCreatedObjectUndo(go, UndoLabel);
            go.transform.SetParent(root.transform, false);
            var view = Undo.AddComponent<DeformableSnowDebugView>(go);
            view.Configure(root.GetComponentInChildren<DeformableSnowVolume>(), false, 0);
            sb.AppendLine("Debug overlay: F1 shows it, F2 cycles height / capture / normal.");
        }

        /// <summary>
        /// Ensures the appearance material exists and points at the layer shader.
        ///
        /// There is no surface object any more: the snow is a screen-space pass, so the only
        /// thing setup owns here is the material the renderer feature reads its values from.
        /// The feature lives in the renderer asset rather than the scene, which is why setup
        /// cannot create it and instead reports what to enable.
        /// </summary>
        private static void BuildSurface(DeformableSnowSetupSettings settings, Shader shader, StringBuilder sb)
        {
            EnsureFolder(MaterialFolder);

            Material material = EnsureMaterial(shader, settings.ResetAppearance, sb);
            sb.AppendLine("Appearance material: " + AssetDatabase.GetAssetPath(material));
            sb.AppendLine("  Enable 'Deformable Snow Layer' in the active Renderer asset.");
        }

        private static Material EnsureMaterial(Shader shader, bool resetAppearance, StringBuilder sb)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialAssetPath);
            if (material == null)
            {
                material = new Material(shader) { name = "PackedSlush" };
                WriteAppearanceDefaults(material);
                AssetDatabase.CreateAsset(material, MaterialAssetPath);
                sb.AppendLine("Created material " + MaterialAssetPath);
                return material;
            }

            if (material.shader != shader)
                material.shader = shader;

            if (resetAppearance)
            {
                WriteAppearanceDefaults(material);
                EditorUtility.SetDirty(material);
                sb.AppendLine("Reset " + MaterialAssetPath + " to the default appearance.");
            }
            else
            {
                sb.AppendLine("Kept the existing " + MaterialAssetPath + " values.");
            }
            return material;
        }

        private static void WriteAppearanceDefaults(Material material)
        {
            material.SetColor("_SnowColor", DefaultSnowColour);
            material.SetColor("_CompressedColor", DefaultCompressedColour);
            material.SetColor("_WetColor", DefaultWetColour);
            material.SetFloat("_SnowThickness", 0.06f);
            material.SetFloat("_MinimumThickness", 0.012f);
            material.SetFloat("_CompressedThreshold", 0.05f);
            material.SetFloat("_CompressedSoftness", 0.25f);
            material.SetFloat("_WetSpecular", 1.6f);
            material.SetFloat("_WetShininess", 64f);
            material.SetFloat("_NormalStrength", 0.7f);
            material.SetFloat("_DetailStrength", 0.35f);
            material.SetFloat("_DetailScale", 0.62f);
            // Coverage: level ground holds snow, anything steeper sheds it. Grass billboards
            // face sideways and fall below the threshold, which is what keeps them from being
            // painted as flat snow.
            material.SetFloat("_SnowAmount", 1f);
            material.SetFloat("_SlopeThreshold", 0.62f);
            material.SetFloat("_SlopeSoftness", 0.22f);
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
