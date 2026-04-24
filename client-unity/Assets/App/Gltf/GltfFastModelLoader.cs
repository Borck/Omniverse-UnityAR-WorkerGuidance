using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using System.Linq;
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
                throw new InvalidOperationException("glTFast package was not found at runtime");

            var gltf = new GLTFast.GltfImport();

            bool ok = await gltf.Load(new Uri(modelFilePath).AbsoluteUri);
            if (!ok)
                throw new InvalidOperationException($"glTFast failed to load: {modelFilePath}");

            ct.ThrowIfCancellationRequested();

            await gltf.InstantiateMainSceneAsync(parent);

            PlayAnimationsIfPresent(gltf, parent?.gameObject);


            Debug.Log($"[GltfFastModelLoader] Loaded: {modelFilePath}");
        }

        // private MethodInfo FindLoadMethod()
        // {
        //     // Try exact single-string overload first (older glTFast)
        //     var method = _gltfImportType.GetMethod("Load", new[] { typeof(string) });
        //     if (method != null) return method;

        //     // Newer glTFast: Load(string, ImportSettings, CancellationToken) — find by first param type
        //     return _gltfImportType.GetMethods()
        //         .FirstOrDefault(m => m.Name == "Load"
        //             && m.GetParameters().Length >= 1
        //             && m.GetParameters()[0].ParameterType == typeof(string));
        // }

        // private MethodInfo FindInstantiateMethod()
        // {
        //     // Try exact overload first
        //     var method = _gltfImportType.GetMethod("InstantiateMainSceneAsync", new[] { typeof(Transform) })
        //         ?? _gltfImportType.GetMethod("InstantiateMainSceneAsync", Type.EmptyTypes);
        //     if (method != null) return method;

        //     // Fallback: any InstantiateMainSceneAsync
        //     return _gltfImportType.GetMethods()
        //         .FirstOrDefault(m => m.Name == "InstantiateMainSceneAsync");
        // }


        // private static async Task InvokeLoadAsync(object importInstance, MethodInfo loadMethod, string modelUri)
        // {
        //     var parameters = loadMethod.GetParameters();
        //     var args = new object[parameters.Length];
        //     args[0] = modelUri;
        //     for (int i = 1; i < parameters.Length; i++)
        //     {
        //         // Value types (e.g. CancellationToken) need default instance, not null
        //         var t = parameters[i].ParameterType;
        //         args[i] = t.IsValueType ? Activator.CreateInstance(t) : null;
        //     }

        //     var result = loadMethod.Invoke(importInstance, args);
        //     if (result is Task task)
        //     {
        //         await task;
        //         if (task.GetType().IsGenericType)
        //         {
        //             var resultProp = task.GetType().GetProperty("Result");
        //             var taskResult = resultProp?.GetValue(task);
        //             if (taskResult is bool success && !success)
        //                 throw new InvalidOperationException("glTFast Load() returned false");
        //         }
        //     }
        // }


        // private static async Task InvokeInstantiateAsync(object importInstance, MethodInfo instantiateMethod, Transform parent)
        // {
        //     var parameters = instantiateMethod.GetParameters();
        //     object result;

        //     if (parameters.Length == 1)
        //     {
        //         result = instantiateMethod.Invoke(importInstance, new object[] { parent });
        //     }
        //     else
        //     {
        //         result = instantiateMethod.Invoke(importInstance, Array.Empty<object>());
        //     }

        //     if (result is Task task)
        //     {
        //         await task;
        //         if (task.GetType().IsGenericType)
        //         {
        //             var resultProp = task.GetType().GetProperty("Result");
        //             var taskResult = resultProp?.GetValue(task);
        //             if (taskResult is bool success && !success)
        //             {
        //                 throw new InvalidOperationException("glTFast InstantiateMainSceneAsync() returned false");
        //             }
        //         }
        //     }
        // }
        private static void PlayAnimationsIfPresent(GLTFast.GltfImport gltf, GameObject root)
        {
            if (root == null) return;
            try
            {
                var clips = gltf.GetAnimationClips();
                if (clips == null || clips.Length == 0) return;
                var anim = root.GetComponent<Animation>() ?? root.AddComponent<Animation>();
                foreach (var clip in clips) { clip.legacy = true; anim.AddClip(clip, clip.name); }
                anim.Rewind(clips[0].name);
                anim.Play(clips[0].name);
                Debug.Log($"[GltfFastModelLoader] Playing: {clips[0].name}");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[GltfFastModelLoader] Animation setup failed: {ex.Message}");
            }
        }


    }


}
