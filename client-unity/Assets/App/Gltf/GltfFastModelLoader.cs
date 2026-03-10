using System;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace Guidance.Runtime
{
    // Optional glTFast path without compile-time dependency. Uses reflection if glTFast is present.
    public sealed class GltfFastModelLoader : IModelLoader
    {
        private readonly Type _gltfImportType;

        public GltfFastModelLoader()
        {
            _gltfImportType = Type.GetType("GLTFast.GltfImport, glTFast")
                ?? Type.GetType("GLTFast.GltfImport, glTFast.Runtime");
        }

        public bool CanLoad(string modelFilePath)
        {
            if (_gltfImportType == null)
            {
                return false;
            }

            var ext = Path.GetExtension(modelFilePath)?.ToLowerInvariant();
            return ext == ".glb" || ext == ".gltf";
        }

        public void LoadModel(string modelFilePath, Transform parent, Action<string> onError)
        {
            try
            {
                // Best-effort placeholder: create marker object and report loader availability.
                // Full async import wiring should be added once glTFast package is pinned in Packages/manifest.json.
                var marker = new GameObject("glTFast_Import_Pending");
                marker.transform.SetParent(parent, false);
                Debug.Log($"[GltfFastModelLoader] glTFast detected. Placeholder load path active for: {modelFilePath}");
            }
            catch (Exception ex)
            {
                onError?.Invoke($"glTFast loader failed: {ex.Message}");
            }
        }
    }
}
