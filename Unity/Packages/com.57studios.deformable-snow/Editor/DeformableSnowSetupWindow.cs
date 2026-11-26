using System.Collections.Generic;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Studios57.DeformableSnow.Editor
{
    /// <summary>
    /// One-click authoring for the DeformableSnow rig: build it, tune the material, and
    /// prove the result tracks, without editing scripts or running CLI commands.
    ///
    /// Scene and prefab setup are handled by the authoring classes so they can run without the window.
    /// </summary>
    public sealed class DeformableSnowSetupWindow : EditorWindow
    {
        private static readonly string[] TabNames = { "Setup", "Appearance", "Validate", "Characters" };

        [SerializeField] private int tab;
        [SerializeField] private DeformableSnowSetupSettings settings = new DeformableSnowSetupSettings();
        [SerializeField] private List<DeformableSnowCharacterSettings> characters = new List<DeformableSnowCharacterSettings>();

        private Vector2 scroll;
        private string status;
        private MessageType statusType = MessageType.None;

        private Material material;
        private Color snowColour;
        private Color compressedColour;
        private Color wetColour;
        private float snowThickness;
        private float minimumThickness;
        private float compressedThreshold;
        private float compressedSoftness;
        private float wetSpecular;
        private float wetShininess;
        private float normalStrength;
        private float snowAmount;
        private float slopeThreshold;
        private float slopeSoftness;
        private float detailStrength;
        private float detailScale;
        private bool appearanceDirty;
        private string appearanceStatus;

        private string validationText;
        private string characterReport;
        private MessageType characterReportType;

        private readonly List<Terrain> targets = new List<Terrain>();
        private readonly Dictionary<Terrain, TerrainFit> fitCache = new Dictionary<Terrain, TerrainFit>();
        private SerializedObject window;
        private SerializedProperty terrainSlots;
        private int fitHash;

        [MenuItem("57Studios/Deformable Snow Setup")]
        public static void Open() => GetWindow<DeformableSnowSetupWindow>("Deformable Snow Setup");

        private void OnEnable()
        {
            if (settings == null)
                settings = new DeformableSnowSetupSettings();
            if ((settings.Terrains == null || settings.Terrains.Length == 0) && settings.Terrain != null)
                settings.Terrains = new[] { settings.Terrain };
            settings.Terrain = null;
            window = new SerializedObject(this);
            terrainSlots = window.FindProperty("settings.Terrains");
            LoadAppearance();
        }

        private void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);

            EditorGUILayout.LabelField("57Studios Deformable Snow", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            tab = GUILayout.Toolbar(tab, TabNames);
            EditorGUILayout.Space();

            switch (tab)
            {
                case 1: DrawAppearance(); break;
                case 2: DrawValidate(); break;
                case 3: DrawCharacters(); break;
                default: DrawSetup(); break;
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawCharacters()
        {
            EditorGUILayout.HelpBox("Assign a character prefab and pick its foot bones. Setup saves a footprint box on each selected bone. Adjust the generated BoxCollider in Prefab Mode to fit the sole.",
                MessageType.Info);

            for (int i = 0; i < characters.Count; i++)
            {
                var character = characters[i];
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        string label = character.Prefab != null ? character.Prefab.name : "Character " + (i + 1);
                        character.Expanded = EditorGUILayout.Foldout(character.Expanded, label, true);
                        if (GUILayout.Button("Remove", GUILayout.Width(68)))
                        {
                            characters.RemoveAt(i);
                            break;
                        }
                    }

                    if (character.Expanded)
                        DrawCharacter(character);
                }
            }

            if (GUILayout.Button("Add Character"))
                characters.Add(new DeformableSnowCharacterSettings());

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(characters.Count == 0 || EditorApplication.isPlayingOrWillChangePlaymode))
            {
                if (GUILayout.Button("Add Footprints to Prefabs", GUILayout.Height(30)))
                {
                    bool applied = DeformableSnowCharacterSetup.Apply(characters, out characterReport);
                    characterReportType = applied ? MessageType.Info : MessageType.Warning;
                }
            }
            if (!string.IsNullOrEmpty(characterReport))
                EditorGUILayout.HelpBox(characterReport, characterReportType);
        }

        private void DrawCharacter(DeformableSnowCharacterSettings character)
        {
            var prefab = (GameObject)EditorGUILayout.ObjectField("Character Prefab", character.Prefab, typeof(GameObject), false);
            if (prefab != character.Prefab)
            {
                character.Prefab = prefab;
                for (int i = 0; i < character.FootBones.Count; i++)
                    character.FootBones[i] = null;
            }

            string error = DeformableSnowCharacterSetup.CheckPrefab(prefab, out _);
            if (error != null && prefab != null)
                EditorGUILayout.HelpBox(error, MessageType.Warning);

            character.AddFollower = EditorGUILayout.ToggleLeft(new GUIContent("Add Following Snow Volume",
                "Enable for a player who should leave tracks while moving between terrains and roads."), character.AddFollower);
            EditorGUILayout.LabelField("Foot Bones", EditorStyles.boldLabel);
            for (int i = 0; i < character.FootBones.Count; i++)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.PrefixLabel("Foot " + (i + 1));
                    Transform bone = character.FootBones[i];
                    var label = new GUIContent(bone != null ? bone.name : "Select Bone",
                        bone != null && prefab != null ? AnimationUtility.CalculateTransformPath(bone, prefab.transform) : "");
                    using (new EditorGUI.DisabledScope(error != null))
                    {
                        if (GUILayout.Button(label, EditorStyles.popup))
                        {
                            int slot = i;
                            var picker = new FootBonePicker(prefab.transform, selected =>
                            {
                                if (character.Prefab == prefab && slot < character.FootBones.Count)
                                    character.FootBones[slot] = selected;
                                Repaint();
                            });
                            picker.Show(GUILayoutUtility.GetLastRect());
                        }
                    }
                    if (GUILayout.Button("−", GUILayout.Width(24)))
                    {
                        character.FootBones.RemoveAt(i);
                        break;
                    }
                }
            }
            if (GUILayout.Button("Add Foot Bone"))
                character.FootBones.Add(null);
        }

        private sealed class FootBonePicker : AdvancedDropdown
        {
            private sealed class BoneItem : AdvancedDropdownItem
            {
                public Transform Bone { get; }

                public BoneItem(string label, Transform bone) : base(label) => Bone = bone;
            }

            private readonly Transform root;
            private readonly System.Action<Transform> select;

            public FootBonePicker(Transform root, System.Action<Transform> select) : base(new AdvancedDropdownState())
            {
                this.root = root;
                this.select = select;
                minimumSize = new Vector2(420f, 280f);
            }

            protected override AdvancedDropdownItem BuildRoot()
            {
                var items = new AdvancedDropdownItem("Select Foot Bone");
                items.AddChild(new BoneItem("None", null));
                int id = 1;
                foreach (Transform bone in root.GetComponentsInChildren<Transform>(true))
                {
                    if (bone == root || (bone.TryGetComponent<DeformableSnowDeformer>(out var deformer) && deformer.Footprint != null))
                        continue;
                    string path = AnimationUtility.CalculateTransformPath(bone.parent, root);
                    string label = path.Length > 0 ? bone.name + "  (" + path + ")" : bone.name;
                    items.AddChild(new BoneItem(label, bone) { id = id++ });
                }
                return items;
            }

            protected override void ItemSelected(AdvancedDropdownItem item) => select(((BoneItem)item).Bone);
        }

        private void DrawSetup()
        {
            Scene scene = EditorSceneManager.GetActiveScene();
            window.Update();
            EditorGUILayout.PropertyField(terrainSlots, new GUIContent("Snow Terrains"), true);
            if (window.ApplyModifiedProperties())
                fitCache.Clear();
            EditorGUILayout.HelpBox("Add terrains from any open scene. If every slot is empty, setup uses the active scene's main terrain.",
                MessageType.None);

            string problem = DeformableSnowSetup.ResolveTerrains(settings, scene, targets);
            if (problem != null)
                EditorGUILayout.HelpBox(problem, MessageType.Warning);
            else
                EditorGUILayout.HelpBox("Setup replaces the snow rig in each listed scene. One Ctrl+Z undoes the setup.",
                    MessageType.Info);

            settings.AutoFit = EditorGUILayout.Toggle("Fit to Terrain", settings.AutoFit);
            if (settings.AutoFit)
                settings.TargetTexelSize = EditorGUILayout.FloatField("Target Texel (m)", settings.TargetTexelSize);

            settings.Resolution = EditorGUILayout.IntPopup("Heightfield", settings.Resolution,
                new[] { "256", "512", "1024", "2048" }, new[] { 256, 512, 1024, 2048 });
            settings.Centre = EditorGUILayout.Vector2Field("Preferred Centre (world XZ)", settings.Centre);
            if (!settings.AutoFit)
                settings.VolumeSize = EditorGUILayout.FloatField("Volume Size (m)", settings.VolumeSize);

            bool validPlacements = true;
            if (problem == null)
            {
                foreach (Terrain terrain in targets)
                    validPlacements &= DrawFitPreview(terrain);
            }

            settings.SnowDepth = EditorGUILayout.FloatField("Snow Depth (m)", settings.SnowDepth);
            settings.ContactBand = EditorGUILayout.FloatField("Contact Band (m)", settings.ContactBand);
            EditorGUILayout.HelpBox("Contact band is how far above the ground a foot still marks the snow. "
                + "It must stay well below the gait step lift or the swing foot smears the prints together.",
                MessageType.None);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Coverage", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Snow is a screen-space pass over the depth buffer, so it covers the "
                + "whole terrain without any mesh. Coverage follows the ground slope: level surfaces hold "
                + "snow and steep ones shed it, which also keeps grass billboards from being painted.",
                MessageType.None);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Test Rig", EditorStyles.boldLabel);
            settings.ContactGain = EditorGUILayout.FloatField("Contact Gain", settings.ContactGain);
            settings.RefillPerSecond = EditorGUILayout.FloatField("Refill (/s)", settings.RefillPerSecond);
            settings.WalkSpeed = EditorGUILayout.FloatField("Walk Speed (m/s)", settings.WalkSpeed);
            settings.StepFrequency = EditorGUILayout.FloatField("Step Frequency (Hz)", settings.StepFrequency);
            settings.StepLength = EditorGUILayout.FloatField("Step Length (m)", settings.StepLength);
            settings.StepLift = EditorGUILayout.FloatField("Step Lift (m)", settings.StepLift);
            settings.LiftSharpness = EditorGUILayout.Slider("Lift Sharpness", settings.LiftSharpness, 0.2f, 1f);

            EditorGUILayout.Space();
            if (GUILayout.Button("Use Scene View Pivot as Centre"))
            {
                if (SceneView.lastActiveSceneView != null)
                {
                    Vector3 pivot = SceneView.lastActiveSceneView.pivot;
                    settings.Centre = new Vector2(pivot.x, pivot.z);
                }
                else
                {
                    SetStatus("No scene view is open.", MessageType.Warning);
                }
            }

            settings.ResetAppearance = EditorGUILayout.Toggle("Reset appearance on setup", settings.ResetAppearance);
            settings.SaveScene = EditorGUILayout.Toggle(new GUIContent("Save target scenes",
                "Saves each target scene with an existing file, including its pending edits."), settings.SaveScene);

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(problem != null || !validPlacements || EditorApplication.isPlayingOrWillChangePlaymode))
            {
                if (GUILayout.Button("Set Up Rig", GUILayout.Height(30)))
                {
                    string report = DeformableSnowSetup.Run(settings);
                    SetStatus(report, problem == null ? MessageType.Info : MessageType.Warning);
                    LoadAppearance();
                }
            }

            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            {
                if (GUILayout.Button("Clear Rig in Active Scene", GUILayout.Height(22)))
                    SetStatus(DeformableSnowSetup.Clear(), MessageType.Info);
            }

            if (!string.IsNullOrEmpty(status))
            {
                EditorGUILayout.Space();
                EditorGUILayout.HelpBox(status, statusType);
            }
        }

        /// <summary>
        /// Shows exactly what the terrain will produce before anything is written, so the fit is
        /// reviewable rather than a surprise in the inspector. The result is cached because fitting
        /// samples the heightmap and OnGUI runs for every repaint and input event.
        /// </summary>
        private bool DrawFitPreview(Terrain terrain)
        {
            TerrainFit fit = CachedFit(terrain);
            EditorGUILayout.Space();
            Scene scene = terrain.gameObject.scene;
            EditorGUILayout.LabelField(terrain.name, EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Scene", string.IsNullOrEmpty(scene.path) ? scene.name + " (unsaved)" : scene.path);

            if (!fit.Valid)
            {
                EditorGUILayout.HelpBox(fit.Warning ?? "Cannot fit to this terrain.", MessageType.Warning);
                return false;
            }

            EditorGUILayout.LabelField("Centre", fit.Centre.ToString("F1"));
            EditorGUILayout.LabelField("Volume", fit.VolumeSize.ToString("F1") + " m  ("
                + fit.TexelSize.ToString("F3") + " m per texel)");
            EditorGUILayout.LabelField("Terrain Height", fit.MinHeight.ToString("F2") + " .. "
                + fit.MaxHeight.ToString("F2") + " m (spread " + (fit.MaxHeight - fit.MinHeight).ToString("F2") + " m)");
            EditorGUILayout.LabelField("Capture Depth", fit.CaptureDepth.ToString("F2") + " m");

            return true;
        }

        private TerrainFit CachedFit(Terrain terrain)
        {
            int hash = settings.TargetTexelSize.GetHashCode();
            hash = hash * 397 ^ settings.Resolution;
            hash = hash * 397 ^ settings.Centre.GetHashCode();
            hash = hash * 397 ^ settings.AutoFit.GetHashCode();
            hash = hash * 397 ^ settings.VolumeSize.GetHashCode();
            hash = hash * 397 ^ targets.Count;

            if (hash != fitHash)
            {
                fitCache.Clear();
                fitHash = hash;
            }
            if (!fitCache.TryGetValue(terrain, out TerrainFit fit))
            {
                fit = DeformableSnowSetup.GetPlacement(terrain, settings, targets.Count > 1);
                fitCache.Add(terrain, fit);
            }
            return fit;
        }

        private void DrawAppearance()
        {
            if (material == null)
            {
                EditorGUILayout.HelpBox("No material found at " + MaterialPathHint()
                    + ". Run Setup first, or create the material asset.", MessageType.Warning);
                if (GUILayout.Button("Reload")) LoadAppearance();
                return;
            }

            EditorGUILayout.LabelField("Material", material.name, EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Asset", AssetDatabase.GetAssetPath(material));
            EditorGUILayout.Space();

            snowColour = EditorGUILayout.ColorField("Snow Colour", snowColour);
            compressedColour = EditorGUILayout.ColorField("Compressed Colour", compressedColour);
            wetColour = EditorGUILayout.ColorField("Wet Tint", wetColour);
            EditorGUILayout.Space();

            snowThickness = EditorGUILayout.FloatField("Snow Thickness (m)", snowThickness);
            minimumThickness = EditorGUILayout.FloatField("Compressed Thickness (m)", minimumThickness);
            compressedThreshold = EditorGUILayout.Slider("Compressed Threshold", compressedThreshold, 0f, 1f);
            compressedSoftness = EditorGUILayout.Slider("Compressed Softness", compressedSoftness, 0.01f, 1f);
            wetSpecular = EditorGUILayout.FloatField("Wet Specular", wetSpecular);
            wetShininess = EditorGUILayout.FloatField("Wet Shininess", wetShininess);
            normalStrength = EditorGUILayout.FloatField("Normal Strength", normalStrength);
            EditorGUILayout.Space();

            EditorGUILayout.LabelField("Coverage", EditorStyles.boldLabel);
            snowAmount = EditorGUILayout.Slider("Snow Amount", snowAmount, 0f, 1f);
            slopeThreshold = EditorGUILayout.Slider("Slope Threshold", slopeThreshold, 0f, 1f);
            slopeSoftness = EditorGUILayout.Slider("Slope Softness", slopeSoftness, 0.01f, 1f);
            EditorGUILayout.HelpBox("Slope Threshold is the minimum up-facing component that keeps snow. "
                + "Raise it to shed snow from gentle slopes; lower it to cover more of the terrain.",
                MessageType.None);

            EditorGUILayout.Space();
            detailStrength = EditorGUILayout.FloatField("Detail Strength", detailStrength);
            detailScale = EditorGUILayout.FloatField("Detail Scale (1/m)", detailScale);

            appearanceDirty = GUI.changed;
            EditorGUILayout.Space();

            using (new EditorGUI.DisabledScope(!appearanceDirty))
            {
                if (GUILayout.Button("Apply to Material", GUILayout.Height(28)))
                {
                    ApplyAppearance();
                    appearanceStatus = "Applied to " + AssetDatabase.GetAssetPath(material) + ".";
                }
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Reload from Material")) LoadAppearance();
            if (GUILayout.Button("Open Material"))
            {
                Selection.activeObject = material;
                EditorGUIUtility.PingObject(material);
            }
            EditorGUILayout.EndHorizontal();

            if (GUILayout.Button("Reset to Defaults"))
            {
                snowColour = DeformableSnowSetup.DefaultSnowColour;
                compressedColour = DeformableSnowSetup.DefaultCompressedColour;
                wetColour = DeformableSnowSetup.DefaultWetColour;
                snowThickness = 0.06f;
                minimumThickness = 0.012f;
                compressedThreshold = 0.05f;
                compressedSoftness = 0.25f;
                wetSpecular = 1.6f;
                wetShininess = 64f;
                normalStrength = 0.7f;
                snowAmount = 1f;
                slopeThreshold = 0.62f;
                slopeSoftness = 0.22f;
                detailStrength = 0.35f;
                detailScale = 0.62f;
                appearanceDirty = true;
            }

            if (!string.IsNullOrEmpty(appearanceStatus))
            {
                EditorGUILayout.Space();
                EditorGUILayout.HelpBox(appearanceStatus, MessageType.None);
            }
        }

        private void DrawValidate()
        {
            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("Enter Play Mode, let the rig walk for a few seconds, "
                    + "then validate. The heightfield only exists while playing.", MessageType.Info);
            }

            using (new EditorGUI.DisabledScope(!Application.isPlaying))
            {
                if (GUILayout.Button("Validate Tracking", GUILayout.Height(28)))
                {
                    DeformableSnowValidator.Report report = DeformableSnowValidator.Run();
                    validationText = report.Text;
                    SetStatus(report.Tracking && report.DiscretePrints
                        ? "Tracking and prints are correct."
                        : "See the report below.", report.Tracking && report.DiscretePrints
                        ? MessageType.Info : MessageType.Warning);
                }
            }

            if (!string.IsNullOrEmpty(status))
            {
                EditorGUILayout.Space();
                EditorGUILayout.HelpBox(status, statusType);
            }

            if (string.IsNullOrEmpty(validationText))
                return;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Report", EditorStyles.boldLabel);
            EditorGUILayout.TextArea(validationText, GUILayout.MinHeight(280));
        }

        private static string MaterialPathHint()
        {
            return DeformableSnowSetup.MaterialAssetPath;
        }

        private void LoadAppearance()
        {
            material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPathHint());
            if (material == null)
                return;

            snowColour = material.GetColor("_SnowColor");
            compressedColour = material.GetColor("_CompressedColor");
            wetColour = material.GetColor("_WetColor");
            snowThickness = material.GetFloat("_SnowThickness");
            minimumThickness = material.GetFloat("_MinimumThickness");
            compressedThreshold = material.GetFloat("_CompressedThreshold");
            compressedSoftness = material.GetFloat("_CompressedSoftness");
            wetSpecular = material.GetFloat("_WetSpecular");
            wetShininess = material.GetFloat("_WetShininess");
            normalStrength = material.GetFloat("_NormalStrength");
            snowAmount = material.GetFloat("_SnowAmount");
            slopeThreshold = material.GetFloat("_SlopeThreshold");
            slopeSoftness = material.GetFloat("_SlopeSoftness");
            detailStrength = material.GetFloat("_DetailStrength");
            detailScale = material.GetFloat("_DetailScale");
            appearanceDirty = false;
            appearanceStatus = null;
        }

        private void ApplyAppearance()
        {
            if (material == null)
                return;

            material.SetColor("_SnowColor", snowColour);
            material.SetColor("_CompressedColor", compressedColour);
            material.SetColor("_WetColor", wetColour);
            material.SetFloat("_SnowThickness", snowThickness);
            material.SetFloat("_MinimumThickness", minimumThickness);
            material.SetFloat("_CompressedThreshold", compressedThreshold);
            material.SetFloat("_CompressedSoftness", compressedSoftness);
            material.SetFloat("_WetSpecular", wetSpecular);
            material.SetFloat("_WetShininess", wetShininess);
            material.SetFloat("_NormalStrength", normalStrength);
            material.SetFloat("_SnowAmount", snowAmount);
            material.SetFloat("_SlopeThreshold", slopeThreshold);
            material.SetFloat("_SlopeSoftness", slopeSoftness);
            material.SetFloat("_DetailStrength", detailStrength);
            material.SetFloat("_DetailScale", detailScale);
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssets();
            appearanceDirty = false;
        }

        private void SetStatus(string message, MessageType type)
        {
            status = message;
            statusType = type;
            Repaint();
        }
    }
}
