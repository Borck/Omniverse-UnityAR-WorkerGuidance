using UnityEngine;

namespace Guidance.Runtime
{
    /// <summary>
    /// Swaps the materials on a freshly loaded GLB hierarchy for a shared hologram material.
    /// Toggle <see cref="Enabled"/> from AppBootstrap to disable without rebuilding.
    /// </summary>
    public static class HologramApplier
    {
        private const string ShaderResourceName = "Hologram";
        private static Material _sharedHologramMaterial;

        public static bool Enabled { get; set; } = true;

        public static void Apply(Transform root)
        {
            if (!Enabled || root == null) return;

            var material = GetSharedMaterial();
            if (material == null) return;

            foreach (var renderer in root.GetComponentsInChildren<Renderer>(includeInactive: true))
            {
                int count = renderer.sharedMaterials.Length;
                if (count == 0)
                {
                    renderer.sharedMaterial = material;
                    continue;
                }

                var swapped = new Material[count];
                for (int i = 0; i < count; i++) swapped[i] = material;
                renderer.sharedMaterials = swapped;
            }
        }

        private static Material GetSharedMaterial()
        {
            if (_sharedHologramMaterial != null) return _sharedHologramMaterial;

            var shader = Resources.Load<Shader>(ShaderResourceName);
            if (shader == null)
            {
                Debug.LogWarning($"[HologramApplier] Shader '{ShaderResourceName}' not found in Resources. Original GLB materials will be used.");
                return null;
            }

            _sharedHologramMaterial = new Material(shader) { name = "HologramShared" };
            return _sharedHologramMaterial;
        }
    }
}
