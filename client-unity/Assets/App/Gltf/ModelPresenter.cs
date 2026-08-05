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

            HologramApplier.Apply(_activeModelRoot.transform);
            Debug.Log($"[ModelPresenter] Presented model for step {activation.StepId} from {modelFilePath}");
        }

        /// <summary>
        /// Asynchronously loads and presents the model. Cancels any in-flight load from
        /// the previous step activation before starting a new one.
        /// </summary>
        /// <param name="parentTransform">
        /// Optional parent for the model root.  When a Vuforia <c>ObserverBehaviour</c>
        /// is provided here the model will move with the tracked physical object.
        /// Pass <c>null</c> to place the model at world origin (desktop test fallback).
        /// </param>
        public async Task PresentModelAsync(
            string modelFilePath,
            StepActivationDto activation,
            CancellationToken ct,
            Transform parentTransform = null)
        {
            ClearActiveModel();

            if (string.IsNullOrEmpty(modelFilePath) || !File.Exists(modelFilePath))
            {
                Debug.LogWarning($"[ModelPresenter] Model file missing: {modelFilePath}");
                return;
            }

            _activeModelRoot = new GameObject($"Model_{activation.PartId}_{activation.StepId}");

            if (parentTransform != null)
            {
                _activeModelRoot.transform.SetParent(parentTransform, worldPositionStays: false);
                _activeModelRoot.transform.localPosition = Vector3.zero;
                _activeModelRoot.transform.localRotation = Quaternion.identity;
                // No Unity-side scale correction. The GLBs are now authored at true
                // real-world metres at the Omniverse level (the source of truth), so we
                // trust the export for both size AND placement and leave the model root
                // at scale 1.0.
                //
                // History (do not reintroduce): 0.1 assumed an old export was ~10x too
                // big; 50/100 were stopgaps for a ~100x-too-small export. All obsolete
                // now that the source is fixed. If parts ever look wrong-sized again, fix
                // it in Omniverse (metersPerUnit / export scale), not here.
                _activeModelRoot.transform.localScale    = Vector3.one;
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

            var myRoot = _activeModelRoot;
            try
            {
                await selectedLoader.LoadModelAsync(modelFilePath, myRoot.transform, ct);
                if (_activeModelRoot == myRoot)
                {
                    HologramApplier.Apply(myRoot.transform);
                    Debug.Log($"[ModelPresenter] Async-loaded model for step {activation.StepId} from {modelFilePath}");
                }
            }
            catch (TaskCanceledException)
            {
                Debug.Log($"[ModelPresenter] Load cancelled for step {activation.StepId}");
                if (_activeModelRoot == myRoot)
                    ClearActiveModel();
            }
        }

        /// <summary>True while a model is loaded (whether shown or hidden).</summary>
        public bool HasActiveModel => _activeModelRoot != null;

        /// <summary>
        /// Show/hide the active model without destroying it. Toggling the model
        /// root's own GameObject is independent of the tracking gate (which toggles
        /// the parent AnimationRoot), and re-enabling it re-fires the replay-loop
        /// drivers' OnEnable so the animation restarts from frame 0 — used by the
        /// fitting-step text/animation cycle in AppBootstrap.
        /// </summary>
        public void SetActiveModelVisible(bool visible)
        {
            if (_activeModelRoot != null && _activeModelRoot.activeSelf != visible)
                _activeModelRoot.SetActive(visible);
        }

        /// <summary>
        /// Effective play duration (seconds) of the longest clip on the active
        /// model, at its authored playback speed — i.e. how long "one full play"
        /// of the motion takes. Returns 0 when there is no active model or no
        /// playable clip, so callers can fall back to a fixed duration.
        /// </summary>
        public float GetActiveAnimationPlaySeconds()
        {
            if (_activeModelRoot == null) return 0f;

            float maxSeconds = 0f;

            // Legacy Animation components (the primary path — see GltfFastModelLoader,
            // which drives these via AnimationReplayLoop). includeInactive: the model
            // may be hidden or under an inactive tracking gate when we query it.
            var animations = _activeModelRoot.GetComponentsInChildren<Animation>(true);
            foreach (var anim in animations)
            {
                foreach (AnimationState s in anim)
                {
                    float speed = Mathf.Abs(s.speed);
                    if (speed < 0.001f) speed = 1f;
                    float secs = s.length / speed;
                    if (secs > maxSeconds) maxSeconds = secs;
                }
            }

            // Mecanim Animator-driven GLBs (AnimatorReplayLoop). animator.speed may
            // read 0 while the loop is holding the last frame, so fall back to the
            // loader's authored playback speed (0.25x) in that case.
            var animators = _activeModelRoot.GetComponentsInChildren<Animator>(true);
            foreach (var animator in animators)
            {
                var controller = animator.runtimeAnimatorController;
                if (controller == null) continue;
                float speed = Mathf.Abs(animator.speed);
                if (speed < 0.001f) speed = 0.25f;
                foreach (var clip in controller.animationClips)
                {
                    if (clip == null) continue;
                    float secs = clip.length / speed;
                    if (secs > maxSeconds) maxSeconds = secs;
                }
            }

            return maxSeconds;
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
