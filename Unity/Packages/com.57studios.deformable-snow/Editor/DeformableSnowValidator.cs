using System.Text;
using Studios57.DeformableSnow.Testing;
using UnityEngine;
using UnityEngine.Rendering;

namespace Studios57.DeformableSnow.Editor
{
    /// <summary>
    /// Play-Mode proof that the rig tracks correctly. This exists because the checks that
    /// shipped before it were not sensitive to the one bug that mattered: a heightfield
    /// mirrored in world Z still reports a full-height peak, plausible coverage and a
    /// correct refill curve. Only correlation against the authored patrol line catches it,
    /// so that is what this does, alongside a direct read of the compression under each foot.
    /// </summary>
    public static class DeformableSnowValidator
    {
        public struct Report
        {
            public bool HasVolume;
            public bool Tracking;
            public bool DiscretePrints;
            public string Text;
        }

        private const int SamplesAlongPatrol = 1200;
        private const float PressedThreshold = 0.12f;

        public static Report Run()
        {
            var sb = new StringBuilder();
            var report = new Report();

            var volumes = Object.FindObjectsByType<DeformableSnowVolume>(FindObjectsSortMode.None);
            foreach (DeformableSnowVolume volume in volumes)
            {
                if (volume.HeightTexture == null)
                    continue;

                sb.AppendLine(volume.gameObject.scene.name + " / " + volume.transform.parent?.name
                    + " / " + volume.name);
                Report result = ValidateVolume(volume, sb);
                report.Tracking = result.Tracking && (!report.HasVolume || report.Tracking);
                report.DiscretePrints = result.DiscretePrints && (!report.HasVolume || report.DiscretePrints);
                report.HasVolume = true;
                sb.AppendLine();
            }

            if (!report.HasVolume)
                sb.AppendLine("No live snow volumes. Enter Play Mode so the heightfields are allocated, then validate.");
            report.Text = sb.ToString();
            return report;
        }

        private static Report ValidateVolume(DeformableSnowVolume volume, StringBuilder sb)
        {
            var report = new Report();

            var request = AsyncGPUReadback.Request(volume.HeightTexture);
            request.WaitForCompletion();
            if (request.hasError)
            {
                sb.AppendLine("GPU readback of the heightfield failed.");
                return report;
            }

            int resolution = volume.Resolution;
            var data = request.GetData<float>();
            Bounds bounds = volume.Bounds;

            AppendTrail(sb, data, resolution, bounds);
            SnowTestWalker walker = volume.transform.parent != null
                ? volume.transform.parent.GetComponentInChildren<SnowTestWalker>()
                : null;
            AppendFeet(sb, data, resolution, bounds, walker, out bool stancePressed);
            report.Tracking = AppendTracking(sb, data, resolution, bounds, walker);
            report.DiscretePrints = AppendGait(sb, data, resolution, bounds, walker);

            sb.AppendLine();
            sb.AppendLine("RESULT");
            sb.AppendLine("  tracking   : " + (report.Tracking ? "OK - prints follow the feet" : "WRONG - prints are not where the feet are"));
            sb.AppendLine("  stance foot: " + (stancePressed ? "leaves compression" : "no compression under the planted foot"));
            sb.AppendLine("  prints     : " + (report.DiscretePrints
                ? "discrete"
                : "not separable - read the gait section; a saturated rail is not a gait fault"));
            return report;
        }

        private static void AppendTrail(StringBuilder sb, Unity.Collections.NativeArray<float> data,
            int resolution, Bounds bounds)
        {
            double weight = 0, sumX = 0, sumZ = 0, sumXX = 0, sumZZ = 0, sumXZ = 0;
            float maxHeight = 0f;
            int pressed = 0;

            for (int y = 0; y < resolution; y++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    float h = data[y * resolution + x];
                    if (h > maxHeight) maxHeight = h;
                    if (h <= 0.05f) continue;

                    pressed++;
                    float worldX = bounds.min.x + (x + 0.5f) / resolution * bounds.size.x;
                    float worldZ = bounds.min.z + (y + 0.5f) / resolution * bounds.size.z;

                    double w = h;
                    weight += w; sumX += worldX * w; sumZ += worldZ * w;
                    sumXX += worldX * worldX * w; sumZZ += worldZ * worldZ * w; sumXZ += worldX * worldZ * w;
                }
            }

            sb.AppendLine("=== trail ===");
            sb.AppendLine("bounds " + bounds.min.ToString("F2") + " .. " + bounds.max.ToString("F2")
                + "  " + resolution + " texels");
            if (weight <= 0)
            {
                sb.AppendLine("no compression yet - walk the rig for a few seconds");
                return;
            }

            double cx = sumX / weight;
            double cz = sumZ / weight;
            sb.AppendLine("peak=" + maxHeight.ToString("F4")
                + "  pressed=" + (100.0 * pressed / (resolution * resolution)).ToString("F2") + "%"
                + "  centroid=(" + cx.ToString("F2") + ", " + cz.ToString("F2") + ")");
        }

        private static void AppendFeet(StringBuilder sb, Unity.Collections.NativeArray<float> data,
            int resolution, Bounds bounds, SnowTestWalker walker, out bool stancePressed)
        {
            stancePressed = false;
            if (walker == null)
            {
                sb.AppendLine("no test walker in the scene");
                return;
            }

            sb.AppendLine("=== under the feet ===");
            Transform left = walker.transform.Find("Foot L");
            Transform right = walker.transform.Find("Foot R");
            float leftHeight = FootHeight(left, data, resolution, bounds);
            float rightHeight = FootHeight(right, data, resolution, bounds);

            sb.AppendLine("Foot L compression " + leftHeight.ToString("F3")
                + "   Foot R compression " + rightHeight.ToString("F3"));
            sb.AppendLine("in a two-foot gait one foot is always in swing, so only the stance foot");
            sb.AppendLine("is expected to read high; a low value on the lifted foot is correct");

            // This is a single-instant spot check, and accumulation needs ~0.4 s to reach 1.0,
            // so a foot that has just landed reads low through no fault of the capture. Use a
            // low bar: the question is "does contact land under a foot at all", which is what a
            // mirrored or offset capture destroys. The trail axis check carries the precision.
            stancePressed = Mathf.Max(leftHeight, rightHeight) > 0.2f;
        }

        private static float FootHeight(Transform foot, Unity.Collections.NativeArray<float> data,
            int resolution, Bounds bounds)
        {
            if (foot == null) return 0f;
            Vector3 position = foot.position;

            // A foot is roughly 14 x 30 cm, several texels across at any sane volume size, so
            // read the peak over its footprint instead of one texel. A single sample flickers
            // between "planted" and "lifted" purely from which texel the heel happens to cover.
            int x = Mathf.Clamp((int)((position.x - bounds.min.x) / bounds.size.x * resolution), 0, resolution - 1);
            int y = Mathf.Clamp((int)((position.z - bounds.min.z) / bounds.size.z * resolution), 0, resolution - 1);

            int radius = Mathf.Clamp(Mathf.RoundToInt(0.05f / bounds.size.x * resolution), 1, 8);
            float best = 0f;
            for (int dy = -radius; dy <= radius; dy++)
            {
                int sy = Mathf.Clamp(y + dy, 0, resolution - 1);
                for (int dx = -radius; dx <= radius; dx++)
                {
                    int sx = Mathf.Clamp(x + dx, 0, resolution - 1);
                    float sample = data[sy * resolution + sx];
                    if (sample > best) best = sample;
                }
            }
            return best;
        }

        /// <summary>
        /// Compares the trail's principal axis against the walker's heading. A trail is a
        /// line, so antiparallel is still aligned; a pure axis mirror shows up as a large
        /// perpendicular component instead, which is exactly the Z-mirror signature.
        /// </summary>
        private static bool AppendTracking(StringBuilder sb, Unity.Collections.NativeArray<float> data,
            int resolution, Bounds bounds, SnowTestWalker walker)
        {
            if (walker == null) return false;

            double weight = 0, sumX = 0, sumZ = 0, sumXX = 0, sumZZ = 0, sumXZ = 0;
            for (int y = 0; y < resolution; y++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    float h = data[y * resolution + x];
                    if (h <= 0.05f) continue;
                    float worldX = bounds.min.x + (x + 0.5f) / resolution * bounds.size.x;
                    float worldZ = bounds.min.z + (y + 0.5f) / resolution * bounds.size.z;
                    double w = h;
                    weight += w; sumX += worldX * w; sumZ += worldZ * w;
                    sumXX += worldX * worldX * w; sumZZ += worldZ * worldZ * w; sumXZ += worldX * worldZ * w;
                }
            }
            if (weight <= 0) return false;

            double cx = sumX / weight, cz = sumZ / weight;
            double vxx = sumXX / weight - cx * cx;
            double vzz = sumZZ / weight - cz * cz;
            double vxz = sumXZ / weight - cx * cz;
            double angle = 0.5 * System.Math.Atan2(2 * vxz, vxx - vzz);
            double dirX = System.Math.Cos(angle);
            double dirZ = System.Math.Sin(angle);

            Vector3 forward = walker.transform.forward;
            double length = System.Math.Sqrt(forward.x * forward.x + forward.z * forward.z);
            if (length < 1e-5) return false;

            double dot = (dirX * forward.x + dirZ * forward.z) / length;
            double cross = (dirX * forward.z - dirZ * forward.x) / length;

            sb.AppendLine("=== tracking ===");
            sb.AppendLine("trail axis (" + dirX.ToString("F3") + ", " + dirZ.ToString("F3") + ")"
                + "  heading (" + forward.x.ToString("F3") + ", " + forward.z.ToString("F3") + ")");
            sb.AppendLine("dot=" + dot.ToString("F3") + " cross=" + cross.ToString("F3") + " -> "
                + (System.Math.Abs(cross) > 0.9 ? "PERPENDICULAR: the capture is mirrored on an axis"
                 : System.Math.Abs(dot) > 0.98 ? "COLLINEAR"
                 : "SKEWED"));

            if (System.Math.Abs(cross) > 0.9)
                sb.AppendLine("Check DeformableSnowVolume.captureFlipY; a mirrored Z axis looks exactly like this.");

            return System.Math.Abs(dot) > 0.98;
        }

        private static bool AppendGait(StringBuilder sb, Unity.Collections.NativeArray<float> data,
            int resolution, Bounds bounds, SnowTestWalker walker)
        {
            if (walker == null) return false;

            Transform rigRoot = walker.transform.parent;
            if (rigRoot == null) return false;

            // The gait profile is taken along the authored patrol, offset onto each foot
            // rail, because the feet straddle the centre line and the centre reads empty.
            Vector3 start = walker.PatrolStartWorld;
            Vector3 end = walker.PatrolEndWorld;
            float length = Vector3.Distance(start, end);
            if (length < 0.01f) return false;

            Vector3 direction = (end - start).normalized;
            var perpendicular = new Vector3(-direction.z, 0f, direction.x);

            sb.AppendLine("=== gait ===");
            int bestRuns = 0;
            float bestRunLength = 0f;
            float bestLateral = 0f;

            foreach (float lateral in new[] { -0.18f, -0.12f, 0.06f, 0.12f, 0.18f })
            {
                int runs = 0;
                int pressedSamples = 0;
                bool inRun = false;

                for (int i = 0; i < SamplesAlongPatrol; i++)
                {
                    float t = i / (float)(SamplesAlongPatrol - 1);
                    Vector3 point = Vector3.Lerp(start, end, t) + perpendicular * lateral;
                    int x = Mathf.Clamp((int)((point.x - bounds.min.x) / bounds.size.x * resolution), 0, resolution - 1);
                    int y = Mathf.Clamp((int)((point.z - bounds.min.z) / bounds.size.z * resolution), 0, resolution - 1);
                    bool isPressed = data[y * resolution + x] > PressedThreshold;

                    if (isPressed) pressedSamples++;
                    if (isPressed && !inRun) { inRun = true; runs++; }
                    else if (!isPressed) inRun = false;
                }

                float meanRun = runs > 0 ? pressedSamples * length / SamplesAlongPatrol / runs : 0f;
                sb.AppendLine("lateral " + lateral.ToString("F2").PadLeft(6)
                    + "  runs " + runs.ToString().PadLeft(3)
                    + "  mean run " + meanRun.ToString("F3") + " m");

                if (runs > bestRuns) { bestRuns = runs; bestRunLength = meanRun; bestLateral = lateral; }
            }

            if (bestRuns == 0)
            {
                sb.AppendLine("no prints found along the patrol");
                return false;
            }

            sb.AppendLine("best rail (lateral " + bestLateral.ToString("F2") + "): " + bestRuns + " runs, "
                + bestRunLength.ToString("F3") + " m mean");

            // One run spanning the whole patrol means the rail has filled in, which two quite
            // different things can cause. A saturated heightfield (the rig ping-pongs over the
            // same line until every texel is at 1.0) is not a gait fault, and reporting it as
            // merged prints would send the reader after the wrong problem.
            bool saturated = AverageHeightOnRail(data, resolution, bounds, start, end, perpendicular, bestLateral) > 0.85f;
            if (saturated)
            {
                sb.AppendLine("the rail is saturated: this rig walks the same line back and forth, so");
                sb.AppendLine("accumulation fills the gaps between prints given enough time. Restart Play");
                sb.AppendLine("Mode and validate within the first few seconds to judge print separation.");
            }
            else
            {
                sb.AppendLine("gaps between prints are still visible, so the runs above are real prints.");
            }

            bool discrete = bestRuns >= 3 && bestRunLength < length * 0.5f;
            return discrete;
        }

        /// <summary>Mean height across everything the rail sampled, 0-1.</summary>
        private static float AverageHeightOnRail(Unity.Collections.NativeArray<float> data, int resolution,
            Bounds bounds, Vector3 start, Vector3 end, Vector3 perpendicular, float lateral)
        {
            double sum = 0.0;
            int count = SamplesAlongPatrol;
            for (int i = 0; i < count; i++)
            {
                float t = i / (float)(count - 1);
                Vector3 point = Vector3.Lerp(start, end, t) + perpendicular * lateral;
                int x = Mathf.Clamp((int)((point.x - bounds.min.x) / bounds.size.x * resolution), 0, resolution - 1);
                int y = Mathf.Clamp((int)((point.z - bounds.min.z) / bounds.size.z * resolution), 0, resolution - 1);
                sum += data[y * resolution + x];
            }
            return (float)(sum / Mathf.Max(1, count));
        }
    }
}
