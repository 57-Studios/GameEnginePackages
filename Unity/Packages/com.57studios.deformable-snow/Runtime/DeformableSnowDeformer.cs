using System.Collections.Generic;
using UnityEngine;

namespace Studios57.DeformableSnow
{
    /// <summary>
    /// Marks a shape as allowed to press into snow. The volume captures the configured
    /// footprint box or the enabled renderers below this object.
    /// Put this only on the parts that should leave prints (feet, props), never on a whole
    /// character, or the ankle slab will record the torso as well.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DeformableSnowDeformer : MonoBehaviour
    {
        [Tooltip("Optional box shape for snow capture. Its physics state does not affect the footprint.")]
        [SerializeField] private BoxCollider footprint;
        private readonly List<Renderer> renderers = new List<Renderer>();

        public BoxCollider Footprint => footprint;
        public IReadOnlyList<Renderer> Renderers => renderers;

        public void SetFootprint(BoxCollider shape)
        {
            footprint = shape;
            Refresh();
        }

        private void OnEnable()
        {
            Refresh();
            DeformableSnowSystem.Register(this);
        }

        private void OnDisable()
        {
            DeformableSnowSystem.Unregister(this);
            renderers.Clear();
        }

        /// <summary>Rebuild the renderer list for a deformer that uses its hierarchy as the capture shape.</summary>
        public void Refresh()
        {
            renderers.Clear();
            if (footprint != null)
                return;

            GetComponentsInChildren(true, renderers);
            for (int i = renderers.Count - 1; i >= 0; i--)
            {
                if (renderers[i] == null)
                    renderers.RemoveAt(i);
            }
        }
    }
}
