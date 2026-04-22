using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Guidance.Runtime
{
    /// <summary>
    /// Optional glTFast-backed loader resolved through reflection at runtime.
    /// </summary>
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

        /// <summary>
        /// Synchronous wrapper kept for editor tests only. Production code should use
        /// <see cref="LoadModelAsync"/> to avoid blocking the main thread.
        /// </summary>
        public void LoadModel(string modelFilePath, Transform parent, Action<string> onError)
        {
            try
            {
                LoadModelAsync(modelFilePath, parent, CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();
            }
            catch (Exception ex)
            {
                onError?.Invoke($"glTFast loader failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Asynchronously loads and instantiates the glTF/GLB model.
        /// Awaits glTFast tasks natively to avoid blocking any thread.
        /// </summary>
        public async Task LoadModelAsync(string modelFilePath, Transform parent, CancellationToken ct)
        {
            if (_gltfImportType == null)
            {
                throw new InvalidOperationException("glTFast package was not found at runtime");
            }

            var importInstance = Activator.CreateInstance(_gltfImportType)
                ?? throw new InvalidOperationException("Unable to create GLTFast.GltfImport instance");

            var loadMethod = FindLoadMethod()
                ?? throw new InvalidOperationException("No supported glTFast Load(...) method found");

            var modelUri = new Uri(modelFilePath).AbsoluteUri;
            await InvokeLoadAsync(importInstance, loadMethod, modelUri);

            ct.ThrowIfCancellationRequested();

            var instantiateMethod = FindInstantiateMethod()
                ?? throw new InvalidOperationException("No supported glTFast InstantiateMainSceneAsync(...) method found");

            await InvokeInstantiateAsync(importInstance, instantiateMethod, parent);

            Debug.Log($"[GltfFastModelLoader] Loaded model via glTFast: {modelFilePath}");
        }

        private MethodInfo FindLoadMethod()
        {
            return _gltfImportType.GetMethod("Load", new[] { typeof(string) });
        }

        private MethodInfo FindInstantiateMethod()
        {
            return _gltfImportType.GetMethod("InstantiateMainSceneAsync", new[] { typeof(Transform) })
                ?? _gltfImportType.GetMethod("InstantiateMainSceneAsync", Type.EmptyTypes);
        }

        private static async Task InvokeLoadAsync(object importInstance, MethodInfo loadMethod, string modelUri)
        {
            var result = loadMethod.Invoke(importInstance, new object[] { modelUri });
            if (result is Task task)
            {
                await task;
                if (task.GetType().IsGenericType)
                {
                    var resultProp = task.GetType().GetProperty("Result");
                    var taskResult = resultProp?.GetValue(task);
                    if (taskResult is bool success && !success)
                    {
                        throw new InvalidOperationException($"glTFast Load() returned false");
                    }
                }
            }
        }

        private static async Task InvokeInstantiateAsync(object importInstance, MethodInfo instantiateMethod, Transform parent)
        {
            var parameters = instantiateMethod.GetParameters();
            object result;

            if (parameters.Length == 1)
            {
                result = instantiateMethod.Invoke(importInstance, new object[] { parent });
            }
            else
            {
                result = instantiateMethod.Invoke(importInstance, Array.Empty<object>());
            }

            if (result is Task task)
            {
                await task;
                if (task.GetType().IsGenericType)
                {
                    var resultProp = task.GetType().GetProperty("Result");
                    var taskResult = resultProp?.GetValue(task);
                    if (taskResult is bool success && !success)
                    {
                        throw new InvalidOperationException("glTFast InstantiateMainSceneAsync() returned false");
                    }
                }
            }
        }
    }
}
