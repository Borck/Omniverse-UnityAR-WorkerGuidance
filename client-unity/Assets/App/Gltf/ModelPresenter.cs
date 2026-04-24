using System.IO;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Guidance.Runtime
{
    /// <summary>
    /// Owns one-active-model lifecycle and dispatches model loading to available loaders.
    /// </summary>
    public sealed class ModelPresenter
    {
        private GameObject _activeModelRoot;
        private CancellationTokenSource _loadCts;
        private readonly List<IModelLoader> _loaders;

        public ModelPresenter()
        {
            _loaders = new List<IModelLoader>
            {
                new GltfFastModelLoader(),
                new PrimitiveFallbackModelLoader(),
            };
        }

        /// <summary>
        /// Synchronous model presentation kept for replay paths. Production step activations
        /// should use <see cref="PresentModelAsync"/> to avoid blocking the main thread.
        /// </summary>
        public void PresentModel(string modelFilePath, StepActivationDto activation)
        {
            ClearActiveModel();

            if (string.IsNullOrEmpty(modelFilePath) || !File.Exists(modelFilePath))
            {
                Debug.LogWarning($"[ModelPresenter] Model file missing: {modelFilePath}");
                return;
            }

            _activeModelRoot = new GameObject($"Model_{activation.PartId}_{activation.StepId}");
            var cam = Camera.main;
            if (cam != null)
            {
                _activeModelRoot.transform.position = cam.transform.position + cam.transform.forward * 0.5f;
                _activeModelRoot.transform.rotation = Quaternion.LookRotation(-cam.transform.forward);
                _activeModelRoot.transform.localScale = Vector3.one * 0.9f; // Omniverse cm → meters
            }
            IModelLoader selectedLoader = null;
            foreach (var loader in _loaders)
            {
                if (loader.CanLoad(modelFilePath))
                {
                    selectedLoader = loader;
                    break;
                }
            }

            if (selectedLoader == null)
            {
                Debug.LogWarning($"[ModelPresenter] No model loader available for {modelFilePath}");
                return;
            }

            selectedLoader.LoadModel(
                modelFilePath,
                _activeModelRoot.transform,
                error => Debug.LogWarning($"[ModelPresenter] Loader error: {error}")
            );

            Debug.Log($"[ModelPresenter] Presented model for step {activation.StepId} from {modelFilePath}");
        }

        /// <summary>
        /// Asynchronously loads and presents the model. Cancels any in-flight load from
        /// the previous step activation before starting a new one.
        /// </summary>
        public async Task PresentModelAsync(string modelFilePath, StepActivationDto activation, CancellationToken ct)
        {
            ClearActiveModel();

            if (string.IsNullOrEmpty(modelFilePath) || !File.Exists(modelFilePath))
            {
                Debug.LogWarning($"[ModelPresenter] Model file missing: {modelFilePath}");
                return;
            }

            _activeModelRoot = new GameObject($"Model_{activation.PartId}_{activation.StepId}");
            var cam = Camera.main;
            if (cam != null)
            {
                _activeModelRoot.transform.position = cam.transform.position + cam.transform.forward * 0.5f;
                _activeModelRoot.transform.rotation = Quaternion.LookRotation(-cam.transform.forward);
                _activeModelRoot.transform.localScale = Vector3.one * 0.9f; // Omniverse cm → meters
            }

            IModelLoader selectedLoader = null;
            foreach (var loader in _loaders)
            {
                if (loader.CanLoad(modelFilePath))
                {
                    selectedLoader = loader;
                    break;
                }
            }

            if (selectedLoader == null)
            {
                Debug.LogWarning($"[ModelPresenter] No model loader available for {modelFilePath}");
                return;
            }

            try
            {
                await selectedLoader.LoadModelAsync(modelFilePath, _activeModelRoot.transform, ct);
                Debug.Log($"[ModelPresenter] Async-loaded model for step {activation.StepId} from {modelFilePath}");
            }
            catch (TaskCanceledException)
            {
                Debug.Log($"[ModelPresenter] Load cancelled for step {activation.StepId}");
                ClearActiveModel();
            }
        }

        public void ClearActiveModel()
        {
            _loadCts?.Cancel();
            _loadCts?.Dispose();
            _loadCts = null;

            if (_activeModelRoot == null)
            {
                return;
            }

            Object.Destroy(_activeModelRoot);
            _activeModelRoot = null;
            Debug.Log("[ModelPresenter] Cleared active model");
        }
    }
}
