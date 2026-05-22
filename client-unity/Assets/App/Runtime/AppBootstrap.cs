using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Guidance.V1;

namespace Guidance.Runtime
{
    /// <summary>
    /// Main runtime orchestrator that wires session flow, asset presentation, tracking, and HUD actions.
    /// </summary>
    public sealed class AppBootstrap : MonoBehaviour
    {
        [SerializeField] private bool useNativeGrpcTransport = true;
        [SerializeField] private string grpcTarget = "172.20.10.2:50051";
        [SerializeField] private string httpBridgeBaseUrl = "172.20.10.2:8080";
        [SerializeField] private string desiredJobId = "demonstrator-26-02-25";
        [SerializeField] private bool enableRuntimeAssetPipeline = true;
        [SerializeField] private bool useHologramShader = true;
        [SerializeField] private GameObject fixtureOverlayPrefab;
        [SerializeField] private VuforiaTrackingBridge vuforiaTrackingBridge;
        [SerializeField] private bool autoConfirmStepAfterAssetReady = false;
        [SerializeField] private float autoConfirmDelaySeconds = 0.5f;
        [SerializeField] private float heartbeatIntervalSeconds = 5f;
        [SerializeField] private float reconnectMinIntervalSeconds = 2f;
        [SerializeField] private float reconnectMaxIntervalSeconds = 20f;
        [SerializeField] private float reconnectBackoffMultiplier = 1.8f;

        [SerializeField] private SessionStatusPanel statusPanel;
        [SerializeField] private TrackingDirectionHint trackingDirectionHint;
        [SerializeField] private Transform imageTargetAnchor;

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
        private bool _vuforiaTargetLoaded;
        private Transform _activeObserverTransform;
        private readonly List<StepActivationDto> _stepHistory = new List<StepActivationDto>();

        private void Awake()
        {
            System.AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
            _runtime = AppRuntimeContext.CreateDefault(
                useNativeGrpcTransport: useNativeGrpcTransport,
                grpcTarget: grpcTarget,
                httpBridgeBaseUrl: httpBridgeBaseUrl,
                supportsDraco: true,
                desiredJobId: desiredJobId
            );

            if (vuforiaTrackingBridge == null)
            {
                vuforiaTrackingBridge = FindFirstObjectByType<VuforiaTrackingBridge>();
            }

            _runtime.StepCoordinator.StateChanged += OnStepStateChanged;
            _runtime.SessionClient.StepActivated += OnSessionStepActivated;
            _runtime.SessionClient.ConnectionStateChanged += OnSessionConnectionStateChanged;
            _runtime.SessionClient.WorkflowCompleted += OnSessionWorkflowCompleted;
        }

        private void Start()
        {
            HologramApplier.Enabled = useHologramShader;
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
                statusPanel.SetTransportMode(useNativeGrpcTransport ? "gRPC :50051" : "HTTP Bridge :8080");
            }
        }

        private void Update()
        {
            if (_runtime == null) return;

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

        private void OnStepStateChanged(StepCoordinatorState previous, StepCoordinatorState current, string reason)
        {
            Debug.Log($"[AppBootstrap] Step state changed {previous} -> {current} ({reason})");
            if (statusPanel != null) statusPanel.SetStepState(current);
        }

        private void OnSessionStepActivated(StepActivationDto activation)
        {
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

        private void OnSessionWorkflowCompleted()
        {
            Debug.Log("[AppBootstrap] Workflow complete — all steps done.");
            _runtime.StepCoordinator.RegisterFault("workflow-complete");
            if (statusPanel != null)
            {
                statusPanel.SetActiveStep("-", "-");
                statusPanel.SetInstruction("-");
                statusPanel.SetWarning("Alle Schritte abgeschlossen! Workflow komplett.");
            }
        }

        private void OnSessionConnectionStateChanged(SessionConnectionState state)
        {
            Debug.Log($"[AppBootstrap] Session connection state: {state}");
            if (statusPanel != null) statusPanel.SetConnectionState(state);

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
                    _runtime.SessionClient.SendStepCompleted(_pendingCompletionJobId, _pendingCompletionStepId, _pendingCompletionAtUnixMs);
                    _pendingCompletionJobId = string.Empty;
                    _pendingCompletionStepId = string.Empty;
                    _pendingCompletionAtUnixMs = 0;
                }
            }
        }

        public void ConfirmActiveStep()
        {
            if (_lastActivation == null) return;

            if (_runtime.SessionClient.ConnectionState != SessionConnectionState.Connected)
            {
                EnterFrozenStepMode();
                return;
            }

            if (!_runtime.StepCoordinator.ConfirmStepCompleted()) return;

            var completedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            _runtime.SessionClient.SendStepCompleted(_lastActivation.JobId, _lastActivation.StepId, completedAt);
            _runtime.TargetManager.DeactivateTarget();
            _runtime.ModelPresenter.ClearActiveModel();
            _lastActivation = null;

            if (statusPanel != null)
            {
                statusPanel.SetActiveStep("-", "-");
                statusPanel.SetInstruction("-");
            }

            _isFrozenStepMode = false;
        }

        public void ReplayActiveStep()
        {
            if (_lastActivation == null) return;

            if (!string.IsNullOrEmpty(_lastModelPath) && File.Exists(_lastModelPath))
            {
                _runtime.TargetManager.ActivateTarget(_lastActivation.TargetId, _lastTargetVersion, _lastTargetPayloadPath);
                _loadCancellation?.Cancel();
                _loadCancellation?.Dispose();
                _loadCancellation = new CancellationTokenSource();
                _ = _runtime.ModelPresenter.PresentModelAsync(_lastModelPath, _lastActivation, _loadCancellation.Token, _activeObserverTransform);
                if (statusPanel != null) statusPanel.SetWarning(string.Empty);
                return;
            }

            StartCoroutine(ResolveAndPresentStepAsset(_lastActivation));
        }

        public void PreviousStep()
        {
            if (_stepHistory.Count < 2)
            {
                if (statusPanel != null) statusPanel.SetWarning("No previous step available.");
                return;
            }

            _stepHistory.RemoveAt(_stepHistory.Count - 1);
            var previousActivation = _stepHistory[^1];

            _runtime.ModelPresenter.ClearActiveModel();
            _runtime.TargetManager.DeactivateTarget();
            _lastActivation = previousActivation;

            if (statusPanel != null)
            {
                statusPanel.SetActiveStep(previousActivation.StepId, previousActivation.PartId);
                statusPanel.SetInstruction(previousActivation.DisplayName);
                statusPanel.SetWarning(string.Empty);
            }

            StartCoroutine(ResolveAndPresentStepAsset(previousActivation));
        }

        public void ShowHelp()
        {
            if (statusPanel != null) statusPanel.SetWarning("Richte das Geraet auf das Zielbild aus und druecke Confirm/Next nach dem Schritt.");
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
            Debug.Log($"[AppBootstrap] Diagnostics exported: {path}");
            if (statusPanel != null) statusPanel.SetWarning("Diagnostics exportiert (siehe Console)");
        }

        private IEnumerator ResolveAndPresentStepAsset(StepActivationDto activation)
        {
            ResolvedStepAssetBundle resolvedBundle = null;
            string resolveError = null;

            yield return _runtime.ManifestClient.ResolveStepAssetWithNext(
                activation.JobId, activation.StepId,
                onResolved: value => resolvedBundle = value,
                onError: error => resolveError = error
            );

            if (!string.IsNullOrEmpty(resolveError))
            {
                _runtime.TelemetryClient.TrackFault("MANIFEST_RESOLVE", resolveError);
                _runtime.StepCoordinator.RegisterFault(resolveError);
                if (statusPanel != null) statusPanel.SetWarning(resolveError);
                yield break;
            }

            if (resolvedBundle == null || resolvedBundle.Current == null)
            {
                _runtime.TelemetryClient.TrackFault("MANIFEST_RESOLVE", "Step asset resolve returned null");
                _runtime.StepCoordinator.RegisterFault("Step asset resolve returned null");
                if (statusPanel != null) statusPanel.SetWarning("Step asset resolve returned null");
                yield break;
            }

            var resolved = resolvedBundle.Current;
            var fileName = ExtractFileName(resolved.GlbUrl, activation.StepId);
            var glbCached = _runtime.AssetCache.TryGetCachedFile(resolved.AssetVersion, fileName, out _);
            if (glbCached) _runtime.TelemetryClient.TrackAssetCacheHit(resolved.AssetVersion, fileName);
            statusPanel?.SetPipelineStatus(glbCached ? "loading from cache..." : "downloading from server...");

            string modelPath = null;
            string downloadError = null;
            yield return _runtime.AssetCache.GetOrDownloadFile(
                resolved.GlbUrl, resolved.AssetVersion, fileName,
                onReady: path => modelPath = path,
                onError: error => downloadError = error
            );

            if (!string.IsNullOrEmpty(downloadError))
            {
                _runtime.TelemetryClient.TrackFault("ASSET_DOWNLOAD", downloadError);
                _runtime.StepCoordinator.RegisterFault(downloadError);
                if (statusPanel != null) statusPanel.SetWarning(downloadError);
                yield break;
            }

            _runtime.TelemetryClient.TrackAssetDownloaded(resolved.AssetVersion, fileName);

            var targetDatFileName = ExtractFileName(resolved.TargetUrl, activation.StepId, defaultExtension: "dat");
            string targetDatPath = null;
            string targetXmlPath = null;
            string targetPayloadError = null;
            var targetCached = _runtime.TargetPayloadCache.TryGetCachedFile(resolved.TargetVersion, targetDatFileName, out _);
            statusPanel?.SetTargetStatus(targetCached ? "loading from cache..." : "downloading from server...");

            yield return _runtime.TargetPayloadCache.GetOrDownloadTargetPair(
                resolved.TargetUrl, resolved.TargetVersion, targetDatFileName,
                onReady: (xml, dat) => { targetXmlPath = xml; targetDatPath = dat; },
                onError: error => targetPayloadError = error
            );

            if (!string.IsNullOrEmpty(targetPayloadError))
            {
                _runtime.TelemetryClient.TrackFault("TARGET_DOWNLOAD", targetPayloadError);
                _runtime.StepCoordinator.RegisterFault(targetPayloadError);
                if (statusPanel != null) statusPanel.SetWarning(targetPayloadError);
                yield break;
            }

            if (!string.IsNullOrEmpty(targetDatPath))
            {
                statusPanel?.SetTargetStatus(targetCached ? "ready (from cache)" : "ready (from server)");
                _runtime.TargetManager.ActivateTarget(activation.TargetId, resolved.TargetVersion, targetDatPath);
            }
            else
            {
                statusPanel?.SetTargetStatus("no target (skipped)");
            }

            // ====================================================================
            // VUFORIA LOADING BLOCK (Fixed target injection)
            // ====================================================================
            if (!string.IsNullOrEmpty(targetDatPath) && !_vuforiaTargetLoaded && string.Equals(activation.AnchorType, "model-target", StringComparison.OrdinalIgnoreCase))
            {
                string vuforiaError = null;

#if VUFORIA_ENGINE
                yield return VuforiaModelTargetLoader.LoadModelTargetDatabaseAsync(
                    targetDatPath,
                    activation.TargetId, // <--- Passing the dynamic target name here!
                    onLoaded: observer =>
                    {
                        if (vuforiaTrackingBridge != null)
                        {
                            vuforiaTrackingBridge.AssignObserver(observer);
                        }
                        _activeObserverTransform = observer != null ? observer.transform : null;
                        _vuforiaTargetLoaded = true;
                        statusPanel?.SetTargetStatus(observer != null ? "ACTIVE in Vuforia (from FastAPI)" : "ERROR: Vuforia returned null observer");

                        if (observer != null && fixtureOverlayPrefab != null)
                        {
                            var overlay = observer.gameObject.AddComponent<FixtureOverlay>();
                            overlay.Initialize(fixtureOverlayPrefab, observer);
                        }
                    },
                    onError: err => vuforiaError = err
                );
#else
                Debug.LogWarning("Vuforia Engine is not enabled. Skipping Model Target load.");
                _vuforiaTargetLoaded = true; 
#endif

                if (!string.IsNullOrEmpty(vuforiaError))
                {
                    _runtime.TelemetryClient.TrackFault("VUFORIA_LOAD", vuforiaError);
                    _runtime.StepCoordinator.RegisterFault(vuforiaError);
                    if (statusPanel != null) statusPanel.SetWarning(vuforiaError);
                    yield break;
                }
            }
            else if (string.Equals(activation.AnchorType, "image-target", StringComparison.OrdinalIgnoreCase)
                  || string.Equals(activation.AnchorType, "ImageTarget", StringComparison.OrdinalIgnoreCase))
            {
                _activeObserverTransform = imageTargetAnchor;
#if VUFORIA_ENGINE
                if (imageTargetAnchor != null && vuforiaTrackingBridge != null)
                {
                    var imgObserver = imageTargetAnchor.GetComponentInParent<Vuforia.ObserverBehaviour>();
                    if (imgObserver != null)
                        vuforiaTrackingBridge.AssignObserver(imgObserver);
                }
#endif
                Debug.Log($"[AppBootstrap] Image target anchor assigned for step {activation.StepId}");
            }
            // ====================================================================

            _loadCancellation?.Cancel();
            _loadCancellation?.Dispose();
            _loadCancellation = new CancellationTokenSource();
            var loadToken = _loadCancellation.Token;

            var observerTransform = _activeObserverTransform != null && _activeObserverTransform.gameObject != null
                ? _activeObserverTransform : null;
            Task loadTask = _runtime.ModelPresenter.PresentModelAsync(modelPath, activation, loadToken, observerTransform);
            yield return new WaitUntil(() => loadTask.IsCompleted);

            if (loadTask.IsFaulted)
            {
                var err = loadTask.Exception?.GetBaseException().Message ?? "Unknown load error";
                _runtime.TelemetryClient.TrackFault("MODEL_LOAD", err);
                _runtime.StepCoordinator.RegisterFault(err);
                if (statusPanel != null) statusPanel.SetWarning(err);
                yield break;
            }

            statusPanel?.SetPipelineStatus("ready");
            _lastModelPath = modelPath ?? string.Empty;
            _lastTargetPayloadPath = targetDatPath ?? string.Empty;
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
            if (string.IsNullOrEmpty(url)) return $"step_{stepId}.{defaultExtension}";
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                var absolute = Path.GetFileName(uri.AbsolutePath);
                if (!string.IsNullOrEmpty(absolute)) return absolute;
            }
            var simple = Path.GetFileName(url);
            return string.IsNullOrEmpty(simple) ? $"step_{stepId}.{defaultExtension}" : simple;
        }

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
            if (trackingDirectionHint == null || _runtime == null) return;

            var hasHint = !string.IsNullOrEmpty(_runtime.TargetManager.ActiveTargetId) && !_runtime.TargetManager.IsTrackingAcquired;
            var angle = _runtime.TargetManager.GetTrackingHintSignedAngleDegrees(Camera.main);
            trackingDirectionHint.SetHint(angle, hasHint);
        }

        private IEnumerator PrefetchNextStepAssets(ResolvedStepAsset next, string currentStepId)
        {
            if (next == null) yield break;

            var nextGlbFile = ExtractFileName(next.GlbUrl, currentStepId + "_next", "glb");
            if (!_runtime.AssetCache.TryGetCachedFile(next.AssetVersion, nextGlbFile, out _))
            {
                string prefetchError = null;
                yield return _runtime.AssetCache.GetOrDownloadFile(
                    next.GlbUrl, next.AssetVersion, nextGlbFile,
                    onReady: _ => { }, onError: error => prefetchError = error
                );
                if (!string.IsNullOrEmpty(prefetchError)) _runtime.TelemetryClient.TrackFault("PREFETCH_NEXT_ASSET", prefetchError);
            }

            var nextTargetFile = ExtractFileName(next.TargetUrl, currentStepId + "_next", "dat");
            if (!_runtime.TargetPayloadCache.TryGetCachedFile(next.TargetVersion, nextTargetFile, out _))
            {
                string prefetchTargetError = null;
                yield return _runtime.TargetPayloadCache.GetOrDownloadFile(
                    next.TargetUrl, next.TargetVersion, nextTargetFile,
                    onReady: _ => { }, onError: error => prefetchTargetError = error
                );
                if (!string.IsNullOrEmpty(prefetchTargetError)) _runtime.TelemetryClient.TrackFault("PREFETCH_NEXT_TARGET", prefetchTargetError);
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
            if (statusPanel != null) statusPanel.SetWarning("Netzwerk unterbrochen: Schritt eingefroren bis Reconnect.");
        }

        private void OnDestroy()
        {
            Debug.Log("[AppBootstrap] Shutting down session safely...");
            if (_runtime != null && _runtime.SessionClient != null)
            {
                try
                {
                    _runtime.SessionClient.Disconnect();
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"Error during shutdown: {e.Message}");
                }
            }
        }

        private void OnApplicationQuit()
        {
            OnDestroy();
        }
    }
}
