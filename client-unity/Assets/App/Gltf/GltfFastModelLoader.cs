//using System;
//using System.IO;
//using System.Linq;
//using System.Reflection;
//using System.Threading;
//using System.Threading.Tasks;
//using UnityEngine;

//namespace Guidance.Runtime
//{
//    /// <summary>
//    /// Optional glTFast-backed loader resolved through reflection at runtime.
//    /// </summary>
//    public sealed class GltfFastModelLoader : IModelLoader
//    {
//        private readonly Type _gltfImportType;

//        public GltfFastModelLoader()
//        {
//            _gltfImportType = Type.GetType("GLTFast.GltfImport, glTFast")
//                ?? Type.GetType("GLTFast.GltfImport, glTFast.Runtime");
//        }

//        public bool CanLoad(string modelFilePath)
//        {
//            if (_gltfImportType == null)
//            {
//                return false;
//            }

//            var ext = Path.GetExtension(modelFilePath)?.ToLowerInvariant();
//            return ext == ".glb" || ext == ".gltf";
//        }

//        /// <summary>
//        /// Synchronous wrapper kept for editor tests only. Production code should use
//        /// <see cref="LoadModelAsync"/> to avoid blocking the main thread.
//        /// </summary>
//        public void LoadModel(string modelFilePath, Transform parent, Action<string> onError)
//        {
//            try
//            {
//                LoadModelAsync(modelFilePath, parent, CancellationToken.None)
//                    .GetAwaiter()
//                    .GetResult();
//            }
//            catch (Exception ex)
//            {
//                onError?.Invoke($"glTFast loader failed: {ex.Message}");
//            }
//        }

//        /// <summary>
//        /// Asynchronously loads and instantiates the glTF/GLB model.
//        /// Awaits glTFast tasks natively to avoid blocking any thread.
//        /// </summary>
//        public async Task LoadModelAsync(string modelFilePath, Transform parent, CancellationToken ct)
//        {
//            if (_gltfImportType == null)
//            {
//                throw new InvalidOperationException("glTFast package was not found at runtime");
//            }

//            var importInstance = CreateGltfImport()
//                ?? throw new InvalidOperationException("Unable to create GLTFast.GltfImport instance");

//            var loadMethod = FindLoadMethod()
//                ?? throw new InvalidOperationException("No supported glTFast Load(...) method found");

//            var modelUri = new Uri(modelFilePath).AbsoluteUri;
//            await InvokeLoadAsync(importInstance, loadMethod, modelUri);

//            ct.ThrowIfCancellationRequested();

//            var instantiateMethod = FindInstantiateMethod()
//                ?? throw new InvalidOperationException("No supported glTFast InstantiateMainSceneAsync(...) method found");

//            await InvokeInstantiateAsync(importInstance, instantiateMethod, parent);

//            Debug.Log($"[GltfFastModelLoader] Loaded model via glTFast: {modelFilePath}");
//        }

//        private object CreateGltfImport()
//        {
//            // Some glTFast versions removed the parameterless constructor.
//            // Try parameterless first, then fall back to the first constructor
//            // with nulls for every parameter (all args are nullable in glTFast).
//            try
//            {
//                return Activator.CreateInstance(_gltfImportType);
//            }
//            catch (MissingMethodException)
//            {
//            }

//            var ctor = _gltfImportType.GetConstructors()
//                .OrderBy(c => c.GetParameters().Length)
//                .FirstOrDefault();
//            if (ctor == null)
//            {
//                return null;
//            }
//            var args = new object[ctor.GetParameters().Length];
//            return ctor.Invoke(args);
//        }

//        private MethodInfo FindLoadMethod()
//        {
//            // glTFast 5.x: Load(string)
//            var m = _gltfImportType.GetMethod("Load", new[] { typeof(string) });
//            if (m != null) return m;

//            // glTFast 6.x+: Load(Uri)
//            m = _gltfImportType.GetMethod("Load", new[] { typeof(Uri) });
//            if (m != null) return m;

//            // LoadFile(string) variant seen in some forks
//            m = _gltfImportType.GetMethod("LoadFile", new[] { typeof(string) });
//            if (m != null) return m;

//            // Last resort: any public instance method named "Load*" whose first
//            // parameter accepts string or Uri (handles multi-param overloads).
//            foreach (var candidate in _gltfImportType.GetMethods(
//                         BindingFlags.Public | BindingFlags.Instance))
//            {
//                if (!candidate.Name.StartsWith("Load", StringComparison.Ordinal)) continue;
//                var prms = candidate.GetParameters();
//                if (prms.Length == 0) continue;
//                var firstType = prms[0].ParameterType;
//                if (firstType == typeof(string) || firstType == typeof(Uri))
//                    return candidate;
//            }

//            // Dump available methods to help diagnose version mismatches.
//            var allMethods = string.Join(", ", _gltfImportType
//                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
//                .Select(mm => $"{mm.Name}({string.Join(",", mm.GetParameters().Select(p => p.ParameterType.Name))})"));
//            Debug.LogWarning($"[GltfFastModelLoader] No Load method matched. Available public instance methods: {allMethods}");

//            return null;
//        }

//        private MethodInfo FindInstantiateMethod()
//        {
//            // Try exact single-Transform overload first (most common).
//            var m = _gltfImportType.GetMethod("InstantiateMainSceneAsync", new[] { typeof(Transform) });
//            if (m != null) return m;

//            // Parameterless overload.
//            m = _gltfImportType.GetMethod("InstantiateMainSceneAsync", Type.EmptyTypes);
//            if (m != null) return m;

//            // Multi-param overload: find any InstantiateMainSceneAsync where the first
//            // param is Transform (or there are no required params).
//            foreach (var candidate in _gltfImportType.GetMethods(
//                         BindingFlags.Public | BindingFlags.Instance))
//            {
//                if (candidate.Name != "InstantiateMainSceneAsync") continue;
//                var prms = candidate.GetParameters();
//                if (prms.Length == 0) return candidate;
//                if (prms[0].ParameterType == typeof(Transform)) return candidate;
//            }

//            return null;
//        }

//        private static async Task InvokeLoadAsync(object importInstance, MethodInfo loadMethod, string modelUri)
//        {
//            // Build argument list: first param is string or Uri; remaining params get defaults.
//            var prms = loadMethod.GetParameters();
//            var args = new object[prms.Length];
//            var firstType = prms.Length > 0 ? prms[0].ParameterType : typeof(string);
//            args[0] = firstType == typeof(Uri) ? (object)new Uri(modelUri) : modelUri;
//            // Leave the rest as null (all optional in glTFast).

//            var result = loadMethod.Invoke(importInstance, args);
//            if (result is Task task)
//            {
//                await task;
//                if (task.GetType().IsGenericType)
//                {
//                    var resultProp = task.GetType().GetProperty("Result");
//                    var taskResult = resultProp?.GetValue(task);
//                    if (taskResult is bool success && !success)
//                    {
//                        throw new InvalidOperationException("glTFast Load() returned false");
//                    }
//                }
//            }
//        }

//        private static async Task InvokeInstantiateAsync(object importInstance, MethodInfo instantiateMethod, Transform parent)
//        {
//            var prms = instantiateMethod.GetParameters();
//            object result;

//            if (prms.Length == 0)
//            {
//                result = instantiateMethod.Invoke(importInstance, Array.Empty<object>());
//            }
//            else
//            {
//                // Build argument list: pass parent for the Transform slot, null for everything else.
//                var args = new object[prms.Length];
//                for (int i = 0; i < prms.Length; i++)
//                {
//                    args[i] = prms[i].ParameterType == typeof(Transform) ? (object)parent : null;
//                }
//                result = instantiateMethod.Invoke(importInstance, args);
//            }

//            if (result is Task task)
//            {
//                await task;
//                if (task.GetType().IsGenericType)
//                {
//                    var resultProp = task.GetType().GetProperty("Result");
//                    var taskResult = resultProp?.GetValue(task);
//                    if (taskResult is bool success && !success)
//                    {
//                        throw new InvalidOperationException("glTFast InstantiateMainSceneAsync() returned false");
//                    }
//                }
//            }
//        }
//    }
//}



using System;
using System.IO;
using System.Linq;
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

        public async Task LoadModelAsync(string modelFilePath, Transform parent, CancellationToken ct)
        {
            if (_gltfImportType == null)
            {
                throw new InvalidOperationException("glTFast package was not found at runtime");
            }

            var importInstance = CreateGltfImport()
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

        private object CreateGltfImport()
        {
            try
            {
                return Activator.CreateInstance(_gltfImportType);
            }
            catch (MissingMethodException)
            {
            }

            var ctor = _gltfImportType.GetConstructors()
                .OrderBy(c => c.GetParameters().Length)
                .FirstOrDefault();
            if (ctor == null) return null;

            var args = new object[ctor.GetParameters().Length];
            return ctor.Invoke(args);
        }

        private MethodInfo FindLoadMethod()
        {
            var m = _gltfImportType.GetMethod("Load", new[] { typeof(string) });
            if (m != null) return m;

            m = _gltfImportType.GetMethod("Load", new[] { typeof(Uri) });
            if (m != null) return m;

            m = _gltfImportType.GetMethod("LoadFile", new[] { typeof(string) });
            if (m != null) return m;

            foreach (var candidate in _gltfImportType.GetMethods(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!candidate.Name.StartsWith("Load", StringComparison.Ordinal)) continue;
                var prms = candidate.GetParameters();
                if (prms.Length == 0) continue;
                var firstType = prms[0].ParameterType;
                if (firstType == typeof(string) || firstType == typeof(Uri))
                    return candidate;
            }
            return null;
        }

        private MethodInfo FindInstantiateMethod()
        {
            var m = _gltfImportType.GetMethod("InstantiateMainSceneAsync", new[] { typeof(Transform) });
            if (m != null) return m;

            m = _gltfImportType.GetMethod("InstantiateMainSceneAsync", Type.EmptyTypes);
            if (m != null) return m;

            foreach (var candidate in _gltfImportType.GetMethods(BindingFlags.Public | BindingFlags.Instance))
            {
                if (candidate.Name != "InstantiateMainSceneAsync") continue;
                var prms = candidate.GetParameters();
                if (prms.Length == 0) return candidate;
                if (prms[0].ParameterType == typeof(Transform)) return candidate;
            }
            return null;
        }

        private static async Task InvokeLoadAsync(object importInstance, MethodInfo loadMethod, string modelUri)
        {
            var prms = loadMethod.GetParameters();
            var args = new object[prms.Length];
            var firstType = prms.Length > 0 ? prms[0].ParameterType : typeof(string);
            args[0] = firstType == typeof(Uri) ? (object)new Uri(modelUri) : modelUri;

            var result = loadMethod.Invoke(importInstance, args);
            if (result is Task task)
            {
                await task;
                if (task.GetType().IsGenericType)
                {
                    var resultProp = task.GetType().GetProperty("Result");
                    var taskResult = resultProp?.GetValue(task);
                    if (taskResult is bool success && !success)
                    {
                        throw new InvalidOperationException("glTFast Load() returned false");
                    }
                }
            }
        }

        private static async Task InvokeInstantiateAsync(object importInstance, MethodInfo instantiateMethod, Transform parent)
        {
            // 1. Create the main anchor
            GameObject container = new GameObject("GLTF_Anchor_Container");
            if (parent != null) container.transform.SetParent(parent, false);

            // =========================================================================
            // FIX: Create an explicit Adjustment Node so you can move/rotate the model!
            // =========================================================================
            GameObject offsetNode = new GameObject("=== ADJUST_ME_TO_MOVE_ANIMATION ===");
            offsetNode.transform.SetParent(container.transform, false);

            var prms = instantiateMethod.GetParameters();
            object result;

            var activeScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            var rootsBefore = activeScene.GetRootGameObjects();

            if (prms.Length == 0)
            {
                result = instantiateMethod.Invoke(importInstance, Array.Empty<object>());
            }
            else
            {
                var args = new object[prms.Length];
                for (int i = 0; i < prms.Length; i++)
                {
                    // Pass the offset node to glTFast so it becomes the direct parent
                    args[i] = prms[i].ParameterType == typeof(Transform) ? (object)offsetNode.transform : null;
                }
                result = instantiateMethod.Invoke(importInstance, args);
            }

            if (result is Task task)
            {
                await task;
            }

            // Catch any models glTFast spawned outside the container
            var rootsAfter = activeScene.GetRootGameObjects();
            foreach (var newRoot in rootsAfter)
            {
                if (!rootsBefore.Contains(newRoot) && newRoot != container && newRoot != offsetNode)
                {
                    newRoot.transform.SetParent(offsetNode.transform, false);
                }
            }

            // Play Animations
            Animation[] animations = offsetNode.GetComponentsInChildren<Animation>();
            foreach (Animation anim in animations)
            {
                anim.wrapMode = WrapMode.Loop;
                anim.Play();
            }

            Animator[] animators = offsetNode.GetComponentsInChildren<Animator>();
            foreach (Animator animator in animators)
            {
                animator.enabled = true;
            }
        }
    
    }
}
