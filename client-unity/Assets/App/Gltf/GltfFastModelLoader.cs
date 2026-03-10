using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
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
                if (_gltfImportType == null)
                {
                    onError?.Invoke("glTFast package was not found at runtime");
                    return;
                }

                var importInstance = Activator.CreateInstance(_gltfImportType);
                if (importInstance == null)
                {
                    onError?.Invoke("Unable to create GLTFast.GltfImport instance");
                    return;
                }

                var loadMethod = FindLoadMethod();
                if (loadMethod == null)
                {
                    onError?.Invoke("No supported glTFast Load(...) method found");
                    return;
                }

                var modelUri = new Uri(modelFilePath).AbsoluteUri;
                var loadResult = InvokeLoad(importInstance, loadMethod, modelUri);
                if (!loadResult)
                {
                    onError?.Invoke($"glTFast failed loading model: {modelFilePath}");
                    return;
                }

                var instantiateMethod = FindInstantiateMethod();
                if (instantiateMethod == null)
                {
                    onError?.Invoke("No supported glTFast InstantiateMainSceneAsync(...) method found");
                    return;
                }

                var instantiateResult = InvokeInstantiate(importInstance, instantiateMethod, parent);
                if (!instantiateResult)
                {
                    onError?.Invoke("glTFast failed to instantiate main scene");
                    return;
                }

                Debug.Log($"[GltfFastModelLoader] Loaded model via glTFast: {modelFilePath}");
            }
            catch (Exception ex)
            {
                onError?.Invoke($"glTFast loader failed: {ex.Message}");
            }
        }

        private MethodInfo FindLoadMethod()
        {
            return _gltfImportType.GetMethod("Load", new[] { typeof(string) });
        }

        private MethodInfo FindInstantiateMethod()
        {
            // Most common glTFast signatures use parent Transform.
            return _gltfImportType.GetMethod("InstantiateMainSceneAsync", new[] { typeof(Transform) })
                ?? _gltfImportType.GetMethod("InstantiateMainSceneAsync", Type.EmptyTypes);
        }

        private static bool InvokeLoad(object importInstance, MethodInfo loadMethod, string modelUri)
        {
            var result = loadMethod.Invoke(importInstance, new object[] { modelUri });
            return CoerceToBool(result, true);
        }

        private static bool InvokeInstantiate(object importInstance, MethodInfo instantiateMethod, Transform parent)
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

            return CoerceToBool(result, true);
        }

        private static bool CoerceToBool(object value, bool defaultValue)
        {
            if (value == null)
            {
                return defaultValue;
            }

            if (value is bool b)
            {
                return b;
            }

            if (value is Task task)
            {
                task.GetAwaiter().GetResult();

                var taskType = task.GetType();
                if (!taskType.IsGenericType)
                {
                    return defaultValue;
                }

                var resultProperty = taskType.GetProperty("Result");
                if (resultProperty == null)
                {
                    return defaultValue;
                }

                var taskResult = resultProperty.GetValue(task);
                if (taskResult is bool taskBool)
                {
                    return taskBool;
                }

                return defaultValue;
            }

            return defaultValue;
        }
    }
}
