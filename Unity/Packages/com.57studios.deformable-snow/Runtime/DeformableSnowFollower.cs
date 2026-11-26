using UnityEngine;

namespace Studios57.DeformableSnow
{
    /// <summary>Creates a moving deformation volume for the footprint boxes beneath this character.</summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("57Studios/Deformable Snow Follower")]
    public sealed class DeformableSnowFollower : MonoBehaviour
    {
        [Tooltip("Layers containing the walkable terrain and road colliders. Leave character colliders out of this mask.")]
        [SerializeField] private LayerMask groundLayers = 1;

        private DeformableSnowVolume volume;

        private void OnEnable()
        {
            if (!Application.isPlaying)
                return;

            if (volume == null)
            {
                var child = new GameObject("Player Snow Footsteps");
                child.transform.SetParent(transform, false);
                volume = child.AddComponent<DeformableSnowVolume>();
                volume.Configure(new Vector2(16f, 16f), 0f, 5f, 1024, 0.06f, 12f, 0.02f);
                volume.Follow(transform, groundLayers);
            }
            volume.enabled = true;
        }

        private void OnDisable()
        {
            if (volume != null)
                volume.enabled = false;
        }

        private void OnDestroy()
        {
            if (volume != null)
                Destroy(volume.gameObject);
        }
    }
}
