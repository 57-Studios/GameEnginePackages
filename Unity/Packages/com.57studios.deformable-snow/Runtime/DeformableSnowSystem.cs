using System.Collections.Generic;

namespace Studios57.DeformableSnow
{
    /// <summary>
    /// Central registry of snow volumes and deformers. Deliberately tiny: it exists so a
    /// volume can find the set of renderers that are allowed to write into its heightfield
    /// without scanning the scene, and so editor tooling can enumerate live volumes.
    /// Phase 3 replaces the linear scan with a spatial lookup; keep callers API-compatible.
    /// </summary>
    internal static class DeformableSnowSystem
    {
        private static readonly List<DeformableSnowVolume> Volumes = new List<DeformableSnowVolume>();
        private static readonly List<DeformableSnowDeformer> Deformers = new List<DeformableSnowDeformer>();

        internal static IReadOnlyList<DeformableSnowVolume> ActiveVolumes => Volumes;
        internal static IReadOnlyList<DeformableSnowDeformer> ActiveDeformers => Deformers;

        internal static void Register(DeformableSnowVolume volume)
        {
            if (volume != null && !Volumes.Contains(volume))
                Volumes.Add(volume);
        }

        internal static void Unregister(DeformableSnowVolume volume)
        {
            Volumes.Remove(volume);
        }

        internal static void Register(DeformableSnowDeformer deformer)
        {
            if (deformer != null && !Deformers.Contains(deformer))
                Deformers.Add(deformer);
        }

        internal static void Unregister(DeformableSnowDeformer deformer)
        {
            Deformers.Remove(deformer);
        }
    }
}
