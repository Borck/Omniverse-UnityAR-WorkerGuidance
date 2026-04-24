using UnityEngine;
using System;
using System.Collections;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
namespace Guidance.Runtime
{
    /// <summary>
    /// Main runtime orchestrator that wires session flow, asset presentation, tracking, and HUD actions.
    /// </summary>
    public sealed class AppBootstrap : MonoBehaviour
    {
        [SerializeField] private bool useNativeGrpcTransport = true;
        [SerializeField] private string grpcTarget = "localhost:50051";
        [SerializeField] private string httpBridgeBaseUrl = "http://localhost:8080";
        [SerializeField] private bool enableRuntimeAssetPipeline = true;
        [SerializeField] private bool autoConfirmStepAfterAssetReady = false;
        [SerializeField] private float autoConfirmDelaySeconds = 0.5f;
        [SerializeField] private float heartbeatIntervalSeconds = 5f;
        [SerializeField] private float reconnectMinIntervalSeconds = 2f;
        [SerializeField] private float reconnectMaxIntervalSeconds = 20f;
        [SerializeField] private float reconnectBackoffMultiplier = 1.8f;

        [SerializeField] private SessionStatusPanel statusPanel;
        [SerializeField] private TrackingDirectionHint trackingDirectionHint;

        private AppRuntimeContext _runtime;
        private StepActivationDto _lastActivation;
        private float _nextHeartbeatAt;
        private float _nextReconnectAt;
        private float _currentReconnectInterval;
        private bool _isFrozenStepMode;
        private string _pendingCompletionJobId = string.Empty;
        private string _pendingCompletionStepId = string.Empty;
        private long _pendingCompletionAtUnixMs;
        private string _lastModelPath = string.Empty;
        private string _lastTargetPayloadPath = string.Empty;
        private string _lastTargetVersion = string.Empty;
        private CancellationTokenSource _loadCancellation;
        private readonly List<StepActivationDto> _stepHistory = new List<StepActivationDto>();
        private string _lastHistoryModelPath = string.Empty;


        private void Awake()
        {
            _runtime = AppRuntimeContext.CreateDefault(
                useNativeGrpcTransport: useNativeGrpcTransport,
                grpcTarget: grpcTarget,
                httpBridgeBaseUrl: httpBridgeBaseUrl,
                supportsDraco: true
            );

            _runtime.StepCoordinator.StateChanged += OnStepStateChanged;
            _runtime.SessionClient.StepActivated += OnSessionStepActivated;
            _runtime.SessionClient.ConnectionStateChanged += OnSessionConnectionStateChanged;
        }

        private void Start()
        {
            _runtime.SessionClient.Initialize();
            _runtime.StepCoordinator.Initialize();
            _runtime.SessionClient.Connect();
            _nextHeartbeatAt = Time.time + heartbeatIntervalSeconds;
            _currentReconnectInterval = Mathf.Max(0.5f, reconnectMinIntervalSeconds);
            _nextReconnectAt = Time.time + _currentReconnectInterval;

            if (statusPanel != null)
            {
                statusPanel.SetConnectionState(SessionConnectionState.Disconnected);
                statusPanel.SetStepState(StepCoordinatorState.Idle);
                statusPanel.SetActiveStep("-", "-");
                statusPanel.SetInstruction("-");
                statusPanel.SetWarning(string.Empty);
            }
        }

        private void Update()
        {
            if (_runtime == null)
            {
                return;
            }

            var now = Time.time;
            if (_runtime.SessionClient.ConnectionState == SessionConnectionState.Connected)
            {
                if (now >= _nextHeartbeatAt)
                {
                    var unixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    _runtime.SessionClient.SendHeartbeat(unixMs);
                    _nextHeartbeatAt = now + heartbeatIntervalSeconds;
                }

                UpdateTrackingHint();
                return;
            }

            if (now >= _nextReconnectAt)
            {
                _runtime.SessionClient.TryReconnect();
                _nextReconnectAt = now + _currentReconnectInterval;
                _currentReconnectInterval = Mathf.Min(
                    reconnectMaxIntervalSeconds,
                    _currentReconnectInterval * Mathf.Max(1.1f, reconnectBackoffMultiplier)
                );
            }

            if (_lastActivation != null)
            {
                EnterFrozenStepMode();
            }
        }

        private void OnStepStateChanged(
            StepCoordinatorState previous,
            StepCoordinatorState current,
            string reason)
        {
            Debug.Log($"[AppBootstrap] Step state changed {previous} -> {current} ({reason})");
            if (statusPanel != null)
            {
                statusPanel.SetStepState(current);
            }
        }

        private void OnSessionStepActivated(StepActivationDto activation)
        {
            _runtime.ModelPresenter.ClearActiveModel();
            Debug.Log($"[AppBootstrap] Step activated from session: {activation.JobId}/{activation.StepId}");
            _runtime.StepCoordinator.ActivateStep(activation.JobId, activation.StepId);
            _runtime.TelemetryClient.TrackStepActivated(activation.JobId, activation.StepId, activation.PartId);
            _lastActivation = activation;
            _stepHistory.Add(activation);

            if (statusPanel != null)
            {
                statusPanel.SetActiveStep(activation.StepId, activation.PartId);
                statusPanel.SetInstruction(activation.DisplayName);
                statusPanel.SetWarning(string.Empty);
            }

            if (enableRuntimeAssetPipeline)
            {
                StartCoroutine(ResolveAndPresentStepAsset(activation));
            }
        }

        private void OnSessionConnectionStateChanged(SessionConnectionState state)
        {
            Debug.Log($"[AppBootstrap] Session connection state: {state}");
            if (statusPanel != null)
            {
                statusPanel.SetConnectionState(state);
            }

            if (state == SessionConnectionState.Connected)
            {
                _currentReconnectInterval = Mathf.Max(0.5f, reconnectMinIntervalSeconds);
                _nextReconnectAt = Time.time + _currentReconnectInterval;

                if (_isFrozenStepMode && statusPanel != null)
                {
                    statusPanel.SetWarning(string.Empty);
                }
                _isFrozenStepMode = false;

                if (!string.IsNullOrEmpty(_pendingCompletionJobId) && !string.IsNullOrEmpty(_pendingCompletionStepId))
                {
                    _runtime.SessionClient.SendStepCompleted(
                        _pendingCompletionJobId,
                        _pendingCompletionStepId,
                        _pendingCompletionAtUnixMs
                    );
                    _pendingCompletionJobId = string.Empty;
                    _pendingCompletionStepId = string.Empty;
                    _pendingCompletionAtUnixMs = 0;
                }
            }
        }

        /// <summary>
        /// Confirms the active step and notifies server progression when connected.
        /// </summary>
        public void ConfirmActiveStep()
        {
            if (_lastActivation == null)
            {
                return;
            }

            if (_runtime.SessionClient.ConnectionState != SessionConnectionState.Connected)
            {
                EnterFrozenStepMode();
                return;
            }

            if (!_runtime.StepCoordinator.ConfirmStepCompleted())
            {
                return;
            }

            var completedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            _runtime.SessionClient.SendStepCompleted(_lastActivation.JobId, _lastActivation.StepId, completedAt);
            // _runtime.TargetManager.DeactivateTarget();
            // _runtime.ModelPresenter.ClearActiveModel();
            // _lastActivation = null;

            // if (statusPanel != null)
            // {
            //     statusPanel.SetActiveStep("-", "-");
            //     statusPanel.SetInstruction("-");
            // }

            _isFrozenStepMode = false;
        }

        /// <summary>
        /// Replays currently active assets without forcing a fresh manifest resolve when possible.
        /// </summary>
        public void ReplayActiveStep()
        {
            if (_lastActivation == null)
            {
                return;
            }

            if (!string.IsNullOrEmpty(_lastModelPath) && File.Exists(_lastModelPath))
            {
                _runtime.TargetManager.ActivateTarget(
                    _lastActivation.TargetId,
                    _lastTargetVersion,
                    _lastTargetPayloadPath
                );
                _loadCancellation?.Cancel();
                _loadCancellation?.Dispose();
                _loadCancellation = new CancellationTokenSource();
                _ = _runtime.ModelPresenter.PresentModelAsync(_lastModelPath, _lastActivation, _loadCancellation.Token);
                // _runtime.ModelPresenter.PresentModel(_lastModelPath, _lastActivation);
                if (statusPanel != null)
                {
                    statusPanel.SetWarning(string.Empty);
                }
                return;
            }

            StartCoroutine(ResolveAndPresentStepAsset(_lastActivation));
        }

        public void PreviousStep()
        {
            // Need at least 2 entries: current + one before it
            if (_stepHistory.Count < 2)
            {
                if (statusPanel != null)
                    statusPanel.SetWarning("No previous step available.");
                return;
            }

            // Remove current step from history, peek at previous
            _stepHistory.RemoveAt(_stepHistory.Count - 1);
            var previousActivation = _stepHistory[_stepHistory.Count - 1];

            // Try loading from cache first
            var previousFileName = $"part_{previousActivation.PartId}_{previousActivation.AssetVersion?.Substring(7, 8)}.glb";
            if (_runtime.AssetCache.TryGetCachedFile(previousActivation.AssetVersion, previousFileName, out var cachedPath)
                && File.Exists(cachedPath))
            {
                _runtime.ModelPresenter.ClearActiveModel();
                _lastActivation = previousActivation;
                _loadCancellation?.Cancel();
                _loadCancellation?.Dispose();
                _loadCancellation = new CancellationTokenSource();
                _ = _runtime.ModelPresenter.PresentModelAsync(cachedPath, previousActivation, _loadCancellation.Token);
                if (statusPanel != null)
                {
                    statusPanel.SetActiveStep(previousActivation.StepId, previousActivation.PartId);
                    statusPanel.SetInstruction(previousActivation.DisplayName);
                    statusPanel.SetWarning(string.Empty);
                }
            }
            else
            {
                // Not cached — re-resolve from manifest
                _lastActivation = previousActivation;
                StartCoroutine(ResolveAndPresentStepAsset(previousActivation));
                if (statusPanel != null)
                {
                    statusPanel.SetActiveStep(previousActivation.StepId, previousActivation.PartId);
                    statusPanel.SetInstruction(previousActivation.DisplayName);
                }
            }
        }


        public void ShowHelp()
        {
            if (statusPanel != null)
            {
                statusPanel.SetWarning("Richte das Geraet auf das Zielbild aus und druecke Confirm/Next nach dem Schritt.");
            }
        }

        public void ExportDiagnosticsBundle()
        {
            var snapshot = new RuntimeDiagnosticsSnapshot
            {
                generatedAtUtc = DateTime.UtcNow.ToString("o"),
                unityVersion = Application.unityVersion,
                deviceModel = SystemInfo.deviceModel,
                operatingSystem = SystemInfo.operatingSystem,
                connectionState = _runtime.SessionClient.ConnectionState.ToString(),
                stepState = _runtime.StepCoordinator.CurrentState.ToString(),
                activeStepId = _lastActivation?.StepId ?? string.Empty,
                activePartId = _lastActivation?.PartId ?? string.Empty,
                activeTargetId = _runtime.TargetManager.ActiveTargetId,
                activeTargetVersion = _runtime.TargetManager.ActiveTargetVersion,
                trackingAcquired = _runtime.TargetManager.IsTrackingAcquired,
            };

            var path = _runtime.DiagnosticsExporter.Export(snapshot);
            _runtime.TelemetryClient.TrackFault("DIAGNOSTICS_EXPORT", $"Diagnostics exported: {path}");
            if (statusPanel != null)
            {
                statusPanel.SetWarning($"Diagnostics exportiert: {path}");
            }
        }

        private IEnumerator ResolveAndPresentStepAsset(StepActivationDto activation)
        {
            ResolvedStepAssetBundle resolvedBundle = null;
            string resolveError = null;

            yield return _runtime.ManifestClient.ResolveStepAssetWithNext(
                activation.JobId,
                activation.StepId,
                onResolved: value => resolvedBundle = value,
                onError: error => resolveError = error
            );

            if (!string.IsNullOrEmpty(resolveError))
            {
                _runtime.TelemetryClient.TrackFault("MANIFEST_RESOLVE", resolveError);
                _runtime.StepCoordinator.RegisterFault(resolveError);
                if (statusPanel != null)
                {
                    statusPanel.SetWarning(resolveError);
                }
                yield break;
            }

            if (resolvedBundle == null || resolvedBundle.Current == null)
            {
                _runtime.TelemetryClient.TrackFault("MANIFEST_RESOLVE", "Step asset resolve returned null");
                _runtime.StepCoordinator.RegisterFault("Step asset resolve returned null");
                if (statusPanel != null)
                {
                    statusPanel.SetWarning("Step asset resolve returned null");
                }
                yield break;
            }

            var resolved = resolvedBundle.Current;

            var fileName = ExtractFileName(resolved.GlbUrl, activation.StepId);
            if (_runtime.AssetCache.TryGetCachedFile(resolved.AssetVersion, fileName, out _))
            {
                _runtime.TelemetryClient.TrackAssetCacheHit(resolved.AssetVersion, fileName);
            }

            string modelPath = null;
            string downloadError = null;
            yield return _runtime.AssetCache.GetOrDownloadFile(
                resolved.GlbUrl,
                resolved.AssetVersion,
                fileName,
                onReady: path => modelPath = path,
                onError: error => downloadError = error
            );

            if (!string.IsNullOrEmpty(downloadError))
            {
                _runtime.TelemetryClient.TrackFault("ASSET_DOWNLOAD", downloadError);
                _runtime.StepCoordinator.RegisterFault(downloadError);
                if (statusPanel != null)
                {
                    statusPanel.SetWarning(downloadError);
                }
                yield break;
            }

            _runtime.TelemetryClient.TrackAssetDownloaded(resolved.AssetVersion, fileName);

            var targetFileName = ExtractFileName(resolved.TargetUrl, activation.StepId, defaultExtension: "dat");
            string targetPayloadPath = null;
            string targetPayloadError = null;

            if (_runtime.GrpcAssetTransfer != null && !string.IsNullOrEmpty(resolved.TargetVersion))
            {
                var targetOutputPath = _runtime.TargetPayloadCache.GetCachePath(resolved.TargetVersion, targetFileName);
                if (!_runtime.TargetPayloadCache.TryGetCachedFile(resolved.TargetVersion, targetFileName, out _))
                {
                    yield return _runtime.GrpcAssetTransfer.StreamTargetAsync(
                        activation.JobId,
                        activation.StepId,
                        resolved.TargetVersion,
                        targetOutputPath,
                        onReady: path => targetPayloadPath = path,
                        onError: error => targetPayloadError = error
                    );
                }
                else
                {
                    targetPayloadPath = targetOutputPath;
                }
            }
            else
            {
                yield return _runtime.TargetPayloadCache.GetOrDownloadFile(
                    resolved.TargetUrl,
                    resolved.TargetVersion,
                    targetFileName,
                    onReady: path => targetPayloadPath = path,
                    onError: error => targetPayloadError = error
                );
            }

            if (!string.IsNullOrEmpty(targetPayloadError))
            {
                _runtime.TelemetryClient.TrackFault("TARGET_DOWNLOAD", targetPayloadError);
                _runtime.StepCoordinator.RegisterFault(targetPayloadError);
                if (statusPanel != null)
                {
                    statusPanel.SetWarning(targetPayloadError);
                }
                yield break;
            }

            _runtime.TargetManager.ActivateTarget(activation.TargetId, resolved.TargetVersion, targetPayloadPath);

            // Cancel any previous in-flight model load and start a fresh async load.
            _loadCancellation?.Cancel();
            _loadCancellation?.Dispose();
            _loadCancellation = new CancellationTokenSource();
            var loadToken = _loadCancellation.Token;

            Task loadTask = _runtime.ModelPresenter.PresentModelAsync(modelPath, activation, loadToken);
            yield return new WaitUntil(() => loadTask.IsCompleted);

            if (loadTask.IsFaulted)
            {
                var err = loadTask.Exception?.GetBaseException().Message ?? "Unknown load error";
                _runtime.TelemetryClient.TrackFault("MODEL_LOAD", err);
                _runtime.StepCoordinator.RegisterFault(err);
                if (statusPanel != null) statusPanel.SetWarning(err);
                yield break;
            }
            _lastModelPath = modelPath ?? string.Empty;
            _lastTargetPayloadPath = targetPayloadPath ?? string.Empty;
            _lastTargetVersion = resolved.TargetVersion ?? string.Empty;

            if (resolvedBundle.Next != null)
            {
                StartCoroutine(PrefetchNextStepAssets(resolvedBundle.Next, activation.StepId));
            }

            if (autoConfirmStepAfterAssetReady)
            {
                yield return new WaitForSeconds(autoConfirmDelaySeconds);
                ConfirmActiveStep();
            }
        }

        private static string ExtractFileName(string glbUrl, string stepId)
        {
            return ExtractFileName(glbUrl, stepId, "glb");
        }

        private static string ExtractFileName(string url, string stepId, string defaultExtension)
        {
            if (string.IsNullOrEmpty(url))
            {
                return $"step_{stepId}.{defaultExtension}";
            }

            if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                var absolute = Path.GetFileName(uri.AbsolutePath);
                if (!string.IsNullOrEmpty(absolute))
                {
                    return absolute;
                }
            }

            var simple = Path.GetFileName(url);
            return string.IsNullOrEmpty(simple) ? $"step_{stepId}.{defaultExtension}" : simple;
        }

        // Hook for future Vuforia target callbacks (3DModel-target v1).
        /// <summary>
        /// Updates runtime tracking state from Vuforia or test injection callbacks.
        /// </summary>
        public void OnTargetTrackingUpdated(Vector3 observedPosition, Quaternion observedRotation, bool trackingAcquired)
        {
            _runtime.TargetManager.UpdateTrackingPose(observedPosition, observedRotation, trackingAcquired);

            if (trackingAcquired)
            {
                _runtime.StepCoordinator.BeginTracking();
                return;
            }

            _runtime.StepCoordinator.NotifyTrackingLost();
        }

        private void UpdateTrackingHint()
        {
            if (trackingDirectionHint == null || _runtime == null)
            {
                return;
            }

            var hasHint = !string.IsNullOrEmpty(_runtime.TargetManager.ActiveTargetId)
                && !_runtime.TargetManager.IsTrackingAcquired;
            var angle = _runtime.TargetManager.GetTrackingHintSignedAngleDegrees(Camera.main);
            trackingDirectionHint.SetHint(angle, hasHint);
        }

        private IEnumerator PrefetchNextStepAssets(ResolvedStepAsset next, string currentStepId)
        {
            if (next == null)
            {
                yield break;
            }

            var nextGlbFile = ExtractFileName(next.GlbUrl, currentStepId + "_next", "glb");
            if (!_runtime.AssetCache.TryGetCachedFile(next.AssetVersion, nextGlbFile, out _))
            {
                string prefetchError = null;
                yield return _runtime.AssetCache.GetOrDownloadFile(
                    next.GlbUrl,
                    next.AssetVersion,
                    nextGlbFile,
                    onReady: _ => { },
                    onError: error => prefetchError = error
                );

                if (!string.IsNullOrEmpty(prefetchError))
                {
                    _runtime.TelemetryClient.TrackFault("PREFETCH_NEXT_ASSET", prefetchError);
                }
            }

            var nextTargetFile = ExtractFileName(next.TargetUrl, currentStepId + "_next", "dat");
            if (!_runtime.TargetPayloadCache.TryGetCachedFile(next.TargetVersion, nextTargetFile, out _))
            {
                string prefetchTargetError = null;
                yield return _runtime.TargetPayloadCache.GetOrDownloadFile(
                    next.TargetUrl,
                    next.TargetVersion,
                    nextTargetFile,
                    onReady: _ => { },
                    onError: error => prefetchTargetError = error
                );

                if (!string.IsNullOrEmpty(prefetchTargetError))
                {
                    _runtime.TelemetryClient.TrackFault("PREFETCH_NEXT_TARGET", prefetchTargetError);
                }
            }
        }

        private void EnterFrozenStepMode()
        {
            _isFrozenStepMode = true;

            if (_lastActivation != null)
            {
                _pendingCompletionJobId = _lastActivation.JobId;
                _pendingCompletionStepId = _lastActivation.StepId;
                _pendingCompletionAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            }

            if (statusPanel != null)
            {
                statusPanel.SetWarning("Netzwerk unterbrochen: Schritt eingefroren bis Reconnect.");
            }
        }
    }
}
