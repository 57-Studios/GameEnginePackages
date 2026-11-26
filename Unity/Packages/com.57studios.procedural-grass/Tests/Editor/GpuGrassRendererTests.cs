using NUnit.Framework;
using UnityEngine;

namespace Studios57.ProceduralGrass.Editor.Tests
{
    public sealed class GpuGrassRendererTests
    {
        [Test]
        public void Renderer_CanBeAddedToAGameObject()
        {
            var go = new GameObject("ProceduralGrassTest");
            try
            {
                var renderer = go.AddComponent<GpuGrassRenderer>();
                Assert.IsNotNull(renderer);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
