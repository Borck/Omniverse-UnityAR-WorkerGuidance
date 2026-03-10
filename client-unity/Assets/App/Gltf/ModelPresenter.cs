using System.IO;
using System.Collections.Generic;
using UnityEngine;

namespace Guidance.Runtime
{
    public sealed class ModelPresenter
    {
        private GameObject _activeModelRoot;
        private readonly List<IModelLoader> _loaders;

        public ModelPresenter()
        {
            _loaders = new List<IModelLoader>
            {
                new GltfFastModelLoader(),
                new PrimitiveFallbackModelLoader(),
            };
        }

        public void PresentModel(string modelFilePath, StepActivationDto activation)
        {
            ClearActiveModel();

            if (string.IsNullOrEmpty(modelFilePath) || !File.Exists(modelFilePath))
            {
                Debug.LogWarning($"[ModelPresenter] Model file missing: {modelFilePath}");
                return;
            }

            _activeModelRoot = new GameObject($"Model_{activation.PartId}_{activation.StepId}");

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

        public void ClearActiveModel()
        {
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
