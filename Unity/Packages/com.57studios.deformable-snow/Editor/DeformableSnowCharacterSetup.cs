using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Studios57.DeformableSnow.Editor
{
    [Serializable]
    internal sealed class DeformableSnowCharacterSettings
    {
        public GameObject Prefab;
        public List<Transform> FootBones = new List<Transform> { null, null };
        public bool AddFollower;
        public bool Expanded = true;
    }

    internal static class DeformableSnowCharacterSetup
    {
        private const string FootprintPrefix = "Snow Footprint - ";
        private static readonly Vector3 DefaultSize = new Vector3(0.14f, 0.07f, 0.30f);
        private static readonly Vector3 DefaultOffset = new Vector3(0f, -0.07f, 0.06f);

        internal static string CheckPrefab(GameObject prefab, out string path)
        {
            path = AssetDatabase.GetAssetPath(prefab);
            if (prefab == null)
                return "Assign a character prefab.";
            if (PrefabUtility.GetPrefabAssetType(prefab) == PrefabAssetType.Model)
                return "Create a prefab variant of the imported model and assign the variant.";
            if (!path.StartsWith("Assets/", StringComparison.Ordinal) || !path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)
                || AssetDatabase.LoadAssetAtPath<GameObject>(path) != prefab)
                return "Assign the root of a prefab asset from this project's Assets folder.";
            if (PrefabStageUtility.GetCurrentPrefabStage()?.assetPath == path)
                return "Exit Prefab Mode for " + prefab.name + " before applying footprints.";
            return null;
        }

        internal static bool Apply(IReadOnlyList<DeformableSnowCharacterSettings> characters, out string report)
        {
            var targets = new Dictionary<string, (List<int[]> BonePaths, bool AddFollower)>();
            for (int i = 0; i < characters.Count; i++)
            {
                DeformableSnowCharacterSettings character = characters[i];
                string error = CheckPrefab(character.Prefab, out string path);
                if (error != null)
                {
                    report = $"Character {i + 1}. {error}";
                    return false;
                }
                if (targets.ContainsKey(path))
                {
                    report = character.Prefab.name + " is assigned more than once. Put its feet in the same character slot.";
                    return false;
                }
                if (!AssetDatabase.IsOpenForEdit(path))
                {
                    report = "The prefab is read-only: " + path;
                    return false;
                }
                if (character.FootBones.Count == 0)
                {
                    report = "Add at least one foot bone for " + character.Prefab.name + ".";
                    return false;
                }

                Transform root = character.Prefab.transform;
                var selectedBones = new HashSet<Transform>();
                var paths = new List<int[]>();
                foreach (Transform bone in character.FootBones)
                {
                    if (bone == null || bone == root || !bone.IsChildOf(root) || !selectedBones.Add(bone))
                    {
                        report = "Choose a distinct child bone for each foot slot on " + character.Prefab.name + ".";
                        return false;
                    }
                    paths.Add(GetBonePath(bone, root));
                }
                targets.Add(path, (paths, character.AddFollower));
            }

            if (targets.Count == 0)
            {
                report = "Add at least one character prefab.";
                return false;
            }

            bool success = true;
            var summary = new StringBuilder();
            foreach (var target in targets)
            {
                try
                {
                    var (paths, addFollower) = target.Value;
                    int added = AddFootprints(target.Key, paths, addFollower, out bool followerAdded);
                    summary.AppendLine($"{target.Key} — {added} added, {paths.Count - added} already set up."
                        + (followerAdded ? " Added a following snow volume." : ""));
                }
                catch (Exception exception)
                {
                    success = false;
                    summary.AppendLine(target.Key + " — " + exception.Message);
                }
            }
            report = summary.ToString();
            return success;
        }

        private static int[] GetBonePath(Transform bone, Transform root)
        {
            var indices = new Stack<int>();
            for (Transform step = bone; step != root; step = step.parent)
                indices.Push(step.GetSiblingIndex());
            return indices.ToArray();
        }

        private static int AddFootprints(string path, List<int[]> bonePaths, bool addFollower, out bool followerAdded)
        {
            GameObject contents = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var names = new HashSet<string>();
                foreach (Transform transform in contents.GetComponentsInChildren<Transform>(true))
                    names.Add(transform.name);

                int added = 0;
                foreach (int[] bonePath in bonePaths)
                {
                    Transform bone = contents.transform;
                    foreach (int index in bonePath)
                        bone = bone.GetChild(index);
                    if (AddFootprint(bone, contents.transform.rotation, names))
                        added++;
                }

                followerAdded = addFollower && !contents.TryGetComponent<DeformableSnowFollower>(out _);
                if (followerAdded)
                    contents.AddComponent<DeformableSnowFollower>();

                if (added > 0 || followerAdded)
                {
                    PrefabUtility.SaveAsPrefabAsset(contents, path, out bool saved);
                    if (!saved)
                        throw new InvalidOperationException("Unity could not save the prefab.");
                }
                return added;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        private static bool AddFootprint(Transform bone, Quaternion characterRotation, HashSet<string> names)
        {
            foreach (Transform child in bone)
            {
                if (child.TryGetComponent(out DeformableSnowDeformer deformer)
                    && deformer.Footprint != null && deformer.Footprint.transform == child)
                    return false;
            }

            string stem = FootprintPrefix + bone.name;
            string name = stem;
            for (int suffix = 2; !names.Add(name); suffix++)
                name = stem + " " + suffix;

            var probe = new GameObject(name);
            probe.transform.SetParent(bone, false);
            probe.transform.rotation = characterRotation;
            var box = probe.AddComponent<BoxCollider>();
            box.enabled = false;
            box.center = probe.transform.InverseTransformVector(characterRotation * DefaultOffset);
            box.size = new Vector3(
                DefaultSize.x / AxisScale(probe.transform, Vector3.right),
                DefaultSize.y / AxisScale(probe.transform, Vector3.up),
                DefaultSize.z / AxisScale(probe.transform, Vector3.forward));
            probe.AddComponent<DeformableSnowDeformer>().SetFootprint(box);
            return true;
        }

        private static float AxisScale(Transform transform, Vector3 axis)
            => Mathf.Max(0.0001f, transform.TransformVector(axis).magnitude);
    }
}
