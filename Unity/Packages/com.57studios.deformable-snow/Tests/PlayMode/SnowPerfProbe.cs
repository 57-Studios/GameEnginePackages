using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Studios57.DeformableSnow.Testing
{
    /// <summary>
    /// Phase 1 measurement harness only. Samples <see cref="FrameTiming.gpuFrameTime"/> in
    /// alternating volume-on / volume-off phases and logs the medians, because polling the
    /// editor from outside produces more noise than the snow system costs.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SnowPerfProbe : MonoBehaviour
    {
        private static SnowPerfProbe instance;
        private static string lastReport = "not run";

        public static string LastReport => lastReport;

        public static string StartProbe(DeformableSnowVolume volume, float phaseSeconds = 4f)
        {
            if (volume == null)
                return "no volume";
            if (instance != null)
                return "already running";

            var go = new GameObject("Snow Perf Probe") { hideFlags = HideFlags.HideAndDontSave };
            DontDestroyOnLoad(go);
            instance = go.AddComponent<SnowPerfProbe>();
            instance.StartCoroutine(instance.Run(volume, Mathf.Max(1f, phaseSeconds)));
            return "started (" + phaseSeconds.ToString("F1") + " s per phase)";
        }

        /// <summary>
        /// Measures the screen-space layer instead of the compute chain. Toggling the volume
        /// only removes capture/blur/accumulate; the composite is fragment-bound and lives in a
        /// renderer asset, so it is toggled through a static switch on the feature instead.
        /// </summary>
        public static string StartLayerProbe(float phaseSeconds = 4f)
        {
            if (instance != null)
                return "already running";

            var go = new GameObject("Snow Perf Probe") { hideFlags = HideFlags.HideAndDontSave };
            DontDestroyOnLoad(go);
            instance = go.AddComponent<SnowPerfProbe>();
            instance.StartCoroutine(instance.RunLayer(Mathf.Max(1f, phaseSeconds)));
            return "started layer probe (" + phaseSeconds.ToString("F1") + " s per phase)";
        }

        private IEnumerator Run(DeformableSnowVolume volume, float phaseSeconds)
        {
            yield return Measure(phaseSeconds, on => volume.enabled = on, "volume");
        }

        private IEnumerator RunLayer(float phaseSeconds)
        {
            // The composite is a fragment pass in a renderer asset, so toggling the volume would
            // measure the wrong thing entirely. This removes the shading pass and the
            // DepthNormals prepass it requests.
            yield return Measure(phaseSeconds, on => DeformableSnowLayerFeature.SetLayerEnabled(on), "layer");
        }

        private IEnumerator Measure(float phaseSeconds, System.Action<bool> setEnabled, string label)
        {
            var timings = new FrameTiming[4];
            lastReport = "running...";

            yield return new WaitForSeconds(1f);

            // Short alternating blocks. The editor's GPU clock drifts over a run, so the
            // cost is taken from adjacent on/off pairs instead of whole phases.
            const int blocks = 10;
            var medians = new float[blocks];
            for (int block = 0; block < blocks; block++)
            {
                setEnabled(block % 2 == 0);
                yield return new WaitForSeconds(0.35f);

                var samples = new List<float>(512);
                float end = Time.realtimeSinceStartup + phaseSeconds;
                while (Time.realtimeSinceStartup < end)
                {
                    FrameTimingManager.CaptureFrameTimings();
                    uint count = FrameTimingManager.GetLatestTimings((uint)timings.Length, timings);
                    for (int i = 0; i < count; i++)
                    {
                        float gpu = (float)timings[i].gpuFrameTime;
                        if (gpu > 0f)
                            samples.Add(gpu);
                    }
                    yield return null;
                }

                medians[block] = Median(samples);
            }

            setEnabled(true);

            var sb = new StringBuilder();
            sb.AppendLine("SnowPerfProbe [" + label + "]  " + Screen.width + "x" + Screen.height
                + "  " + phaseSeconds.ToString("F1") + " s x " + blocks + " blocks");
            for (int block = 0; block < blocks; block++)
                sb.AppendLine((block % 2 == 0 ? "on  " : "off ") + medians[block].ToString("F3"));

            var deltas = new List<float>();
            for (int block = 0; block + 1 < blocks; block += 2)
                deltas.Add(medians[block] - medians[block + 1]);

            sb.AppendLine("pairs: " + string.Join(", ", deltas.ConvertAll(d => d.ToString("F3"))));
            sb.AppendLine("delta median=" + Median(deltas).ToString("F3") + " ms  mean="
                + (Sum(deltas) / Mathf.Max(1, deltas.Count)).ToString("F3") + " ms");

            lastReport = sb.ToString();
            Debug.Log("[SnowPerfProbe]\n" + lastReport);

            instance = null;
            Destroy(gameObject);
        }

        private static float Sum(List<float> values)
        {
            float total = 0f;
            for (int i = 0; i < values.Count; i++)
                total += values[i];
            return total;
        }

        private static float Median(List<float> samples)
        {
            if (samples.Count == 0)
                return 0f;
            samples.Sort();
            return samples[samples.Count / 2];
        }
    }
}

