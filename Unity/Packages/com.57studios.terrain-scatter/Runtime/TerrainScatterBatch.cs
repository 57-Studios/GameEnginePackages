using UnityEngine;

namespace Studios57.TerrainScatter
{
    /// <summary>Identifies a batch owned by the terrain scatter editor tool.</summary>
    [DisallowMultipleComponent]
    public sealed class TerrainScatterBatch : MonoBehaviour
    {
        [SerializeField] private Terrain terrain;

        public Terrain Terrain => terrain;

        public void SetTerrain(Terrain value) => terrain = value;
    }
}
