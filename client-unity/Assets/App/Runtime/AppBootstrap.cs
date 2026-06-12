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
        // useNativeGrpcTransport removed — gRPC is the only active transport.
        [SerializeField] private string grpcTarget = "172.20.10.2:50051";
        [SerializeField] private string httpBridgeBaseUrl = "172.20.10.2:8080"; // used only for HTTP asset/manifest fetching
        [SerializeField] private string desiredJobId = "demonstrator-26-02-25";
        [SerializeField] private bool enableRuntimeAssetPipeline = true;
        [SerializeField] private bool useHologramShader = true;
        [SerializeField] private GameObject fixtureOverlayPrefab;
        [Tooltip("Initial visibility of the fixture overlay. The HUD checkbox toggles it at runtime.")]
        [SerializeField] private bool showFixtureOverlay = true;
        [SerializeField] private VuforiaTrackingBridge vuforiaTrackingBridge;
        [SerializeField] private bool autoConfirmStepAfterAssetReady = false;
        [SerializeField] private float autoConfirmDelaySeconds = 0.5f;
        [SerializeField] private float heartbeatIntervalSeconds = 5f;
        [SerializeField] private float reconnectMinIntervalSeconds = 2f;
        [SerializeField] private float reconnectMaxIntervalSeconds = 20f;
        [SerializeField] private float reconnectBackoffMultiplier = 1.8f;

        [Header("Server endpoint")]
        [Tooltip("Shown at startup so the operator can type the server IP or auto-discover. Saved IP overrides Inspector defaults above.")]
        [SerializeField] private ServerConfigPanel serverConfigPanel;
        [Tooltip("If true, skip the config panel and try one UDP discovery attempt at startup. Only works on flat LANs without NAT.")]
        [SerializeField] private bool enableAutoDiscovery = false;
        [SerializeField] private int discoveryPort = 45454;
        [Tooltip("If set, only beacons whose 'tag' matches this string are accepted. Leave empty to accept any.")]
        [SerializeField] private string serviceTagFilter = "";
        [SerializeField] private float discoveryTimeoutSeconds = 3f;

        [SerializeField] private SessionStatusPanel statusPanel;
        [SerializeField] private TrackingDirectionHint trackingDirectionHint;
        [SerializeField] private Transform imageTargetAnchor;
        [SerializeField] private JobSelectorPanel jobSelectorPanel;
#if VUFORIA_ENGINE
        [SerializeField] private Vuforia.ObserverBehaviour imageTargetObserver;
        private Vuforia.ObserverBehaviour _modelTargetObserver;
#endif

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
        private FixtureOverlay _activeFixtureOverlay;

        private void Awake()
        {
            System.AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

            if (vuforiaTrackingBridge == null)
                vuforiaTrackingBridge = FindFirstObjectByType<VuforiaTrackingBridge>();

#if VUFORIA_ENGINE
            // Resolve while imageTargetAnchor is still active — GetComponentInParent
            // returns null on inactive objects, so this must run before Start() hides it.
            if (imageTargetAnchor != null && imageTargetObserver == null)
                imageTargetObserver = imageTargetAnchor.GetComponentInParent<Vuforia.ObserverBehaviour>();

#endif
        }

        private void Start()
        {
            if (imageTargetAnchor != null)
                imageTargetAnchor.gameObject.SetActive(false);

            HologramApplier.Enabled = useHologramShader;

            StartCoroutine(StartupFlow());
        }

        private IEnumerator StartupFlow()
        {
            ApplySavedEndpointPrefs();

            if (enableAutoDiscovery)
            {
                statusPanel?.SetWarning($"Searching for server (UDP {discoveryPort})...");

                var cts = new CancellationTokenSource();
                var task = DiscoveryClient.DiscoverAsync(discoveryPort, serviceTagFilter, discoveryTimeoutSeconds, cts.Token);
                yield return new WaitUntil(() => task.IsCompleted);

                if (task.Status == TaskStatus.RanToCompletion && task.Result != null)
                {
                    var result = task.Result;
                    SaveAndApplyEndpoint(result.Host, result.GrpcPort, result.HttpPort);
                    Debug.Log($"[AppBootstrap] Discovered server '{result.Tag}' at {result.Host}");
                    statusPanel?.SetWarning($"Server gefunden: {result.Host}");
                }
                else
                {
                    Debug.LogWarning($"[AppBootstrap] No discovery beacon received within {discoveryTimeoutSeconds:F1}s.");
                    statusPanel?.SetWarning("Kein Beacon — Server-IP eingeben.");
                }
            }

            if (serverConfigPanel != null)
            {
                ParseGrpcTarget(grpcTarget, out var prefHost, out var prefGrpc);
                ParseHttpBase(httpBridgeBaseUrl, out _, out var prefHttp);
                serverConfigPanel.Show(this, prefHost, prefGrpc, prefHttp);
                yield break;
            }

            ShowJobSelectorOrInitialize();
        }

        public void OnServerConfigConfirmed(string host, int grpcPort, int httpPort)
        {
            SaveAndApplyEndpoint(host, grpcPort, httpPort);
            ShowJobSelectorOrInitialize();
        }

        private void ShowJobSelectorOrInitialize()
        {
            if (jobSelectorPanel != null)
                jobSelectorPanel.Show(this);
            else
                InitializeWithJob(desiredJobId);
        }

        private void ApplySavedEndpointPrefs()
        {
            var savedHost = PlayerPrefs.GetString(ServerConfigPanel.PrefHost, "");
            if (string.IsNullOrEmpty(savedHost)) return;

            var savedGrpc = PlayerPrefs.GetInt(ServerConfigPanel.PrefGrpcPort, 50051);
            var savedHttp = PlayerPrefs.GetInt(ServerConfigPanel.PrefHttpPort, 8080);
            grpcTarget = $"{savedHost}:{savedGrpc}";
            httpBridgeBaseUrl = $"http://{savedHost}:{savedHttp}";
            Debug.Log($"[AppBootstrap] Using saved server endpoint {grpcTarget}");
        }

        private void SaveAndApplyEndpoint(string host, int grpcPort, int httpPort)
        {
            grpcTarget = $"{host}:{grpcPort}";
            httpBridgeBaseUrl = $"http://{host}:{httpPort}";
            PlayerPrefs.SetString(ServerConfigPanel.PrefHost, host);
            PlayerPrefs.SetInt(ServerConfigPanel.PrefGrpcPort, grpcPort);
            PlayerPrefs.SetInt(ServerConfigPanel.PrefHttpPort, httpPort);
            PlayerPrefs.Save();
        }

        private static void ParseGrpcTarget(string value, out string host, out int port)
        {
            host = value ?? "";
            port = 50051;
            if (string.IsNullOrEmpty(value)) return;
            var colon = value.LastIndexOf(':');
            if (colon > 0 && int.TryParse(value.Substring(colon + 1), out var parsed))
            {
                host = value.Substring(0, colon);
                port = parsed;
            }
        }

        private static void ParseHttpBase(string value, out string host, out int port)
        {
            host = "";
            port = 8080;
            if (string.IsNullOrEmpty(value)) return;
            var stripped = value;
            if (stripped.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) stripped = stripped.Substring(7);
            else if (stripped.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) stripped = stripped.Substring(8);
            var slash = stripped.IndexOf('/');
            if (slash >= 0) stripped = stripped.Substring(0, slash);
            var colon = stripped.LastIndexOf(':');
            if (colon > 0 && int.TryParse(stripped.Substring(colon + 1), out var parsed))
            {
                host = stripped.Substring(0, colon);
                port = parsed;
            }
            else
            {
                host = stripped;
            }
        }

        public void InitializeWithJob(string jobId)
        {
            desiredJobId = jobId;
            _runtime = AppRuntimeContext.CreateDefault(
                grpcTarget: grpcTarget,
                httpBridgeBaseUrl: httpBridgeBaseUrl,
                supportsDraco: true,
                desiredJobId: jobId
            );

            _runtime.StepCoordinator.StateChanged += OnStepStateChanged;
            _runtime.SessionClient.StepActivated += OnSessionStepActivated;
            _runtime.SessionClient.ConnectionStateChanged += OnSessionConnectionStateChanged;
            _runtime.SessionClient.WorkflowCompleted += OnSessionWorkflowCompleted;

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
                statusPanel.SetTransportMode("gRPC :50051");
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

        public void ReturnToJobSelector()
        {
            if (jobSelectorPanel == null) return;

            _loadCancellation?.Cancel();
            _loadCancellation?.Dispose();
            _loadCancellation = null;

            if (_runtime != null)
            {
                _runtime.ModelPresenter.ClearActiveModel();
                _runtime.TargetManager.DeactivateTarget();
                _runtime.SessionClient.Disconnect();
            }

#if VUFORIA_ENGINE
            if (imageTargetObserver != null)
                imageTargetObserver.OnTargetStatusChanged -= OnImageTargetStatusChanged;

            if (_modelTargetObserver != null)
            {
                _modelTargetObserver.OnTargetStatusChanged -= OnModelTargetStatusChanged;
                if (vuforiaTrackingBridge != null)
                    vuforiaTrackingBridge.AssignObserver(null);
                UnityEngine.Object.Destroy(_modelTargetObserver.gameObject);
                _modelTargetObserver = null;
            }
#endif

            _lastActivation = null;
            _stepHistory.Clear();
            _activeFixtureOverlay = null;
            _vuforiaTargetLoaded = false;
            _isFrozenStepMode = false;
            _pendingCompletionJobId = string.Empty;
            _pendingCompletionStepId = string.Empty;
            _lastModelPath = string.Empty;
            _lastTargetPayloadPath = string.Empty;
            _lastTargetVersion = string.Empty;
            _activeObserverTransform = null;
            _runtime = null;

            if (imageTargetAnchor != null)
                imageTargetAnchor.gameObject.SetActive(false);

            if (statusPanel != null)
            {
                statusPanel.SetConnectionState(SessionConnectionState.Disconnected);
                statusPanel.SetStepState(StepCoordinatorState.Idle);
                statusPanel.SetActiveStep("-", "-");
                statusPanel.SetInstruction("-");
                statusPanel.SetWarning(string.Empty);
                statusPanel.ClearImageTargetFound();
            }

            jobSelectorPanel.Show(this);
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

        public void SetFixtureOverlayVisible(bool visible)
        {
            showFixtureOverlay = visible;
            if (_activeFixtureOverlay != null)
                _activeFixtureOverlay.OverlayEnabled = visible;
        }

        private void OnGUI()
        {
            const float w = 220f;
            const float h = 32f;
            var rect = new Rect(Screen.width - w - 16f, 16f, w, h);

            GUILayout.BeginArea(rect, GUI.skin.box);
            var newValue = GUILayout.Toggle(showFixtureOverlay, " Show Fixture Overlay");
            if (newValue != showFixtureOverlay)
                SetFixtureOverlayVisible(newValue);
            GUILayout.EndArea();
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
                        // Single identity-transform child of the observer. Used only so we can
                        // SetActive() it for the tracking gate without disabling the observer
                        // GameObject itself (which would stop Vuforia from updating its pose).
                        _activeObserverTransform = observer != null ? GetOrCreateAnimationRoot(observer.transform) : null;
                        _vuforiaTargetLoaded = true;
#if VUFORIA_ENGINE
                        _modelTargetObserver = observer;
                        if (observer != null)
                        {
                            // Hide animations until the physical fixture is actually tracked.
                            // Vuforia keeps the observer transform active even when no pose is
                            // available, so without this gate the GLB renders at last-known /
                            // origin pose.
                            if (_activeObserverTransform != null)
                                _activeObserverTransform.gameObject.SetActive(false);

                            observer.OnTargetStatusChanged -= OnModelTargetStatusChanged;
                            observer.OnTargetStatusChanged += OnModelTargetStatusChanged;
                            OnModelTargetStatusChanged(observer, observer.TargetStatus);
                        }
#endif
                        statusPanel?.SetTargetStatus(observer != null ? "ACTIVE in Vuforia (from FastAPI)" : "ERROR: Vuforia returned null observer");

                        if (observer != null && fixtureOverlayPrefab != null)
                        {
                            var overlay = observer.gameObject.AddComponent<FixtureOverlay>();
                            overlay.Initialize(fixtureOverlayPrefab, observer, _activeObserverTransform);
                            overlay.OverlayEnabled = showFixtureOverlay;
                            _activeFixtureOverlay = overlay;
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
                if (imageTargetObserver != null)
                {
                    // Subscribe directly from AppBootstrap — VuforiaTrackingBridge is bypassed
                    // for image targets because its isActiveAndEnabled check can fail when the
                    // anchor is inactive.
                    imageTargetObserver.OnTargetStatusChanged -= OnImageTargetStatusChanged;
                    imageTargetObserver.OnTargetStatusChanged += OnImageTargetStatusChanged;

                    var s = imageTargetObserver.TargetStatus.Status;
                    var alreadyTracked = s == Vuforia.Status.TRACKED
                                      || s == Vuforia.Status.LIMITED;
                    if (imageTargetAnchor != null)
                        imageTargetAnchor.gameObject.SetActive(alreadyTracked);
                    statusPanel?.SetImageTargetFound(alreadyTracked);
                    Debug.Log($"[AppBootstrap] Subscribed to image target: {imageTargetObserver.name}, alreadyTracked={alreadyTracked}");
                }
                else
                {
                    Debug.LogWarning("[AppBootstrap] imageTargetObserver is null — check that imageTargetAnchor is a child of the ImageTarget GameObject.");
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

            if (imageTargetAnchor != null && IsImageTargetStep(_lastActivation))
            {
                imageTargetAnchor.gameObject.SetActive(trackingAcquired);
                statusPanel?.SetImageTargetFound(trackingAcquired);
            }

            if (trackingAcquired)
            {
                _runtime.StepCoordinator.BeginTracking();
                return;
            }
            _runtime.StepCoordinator.NotifyTrackingLost();
        }

#if VUFORIA_ENGINE
        private void OnModelTargetStatusChanged(Vuforia.ObserverBehaviour behaviour, Vuforia.TargetStatus status)
        {
            if (_activeObserverTransform == null) return;
            var tracked = status.Status == Vuforia.Status.TRACKED
                       || status.Status == Vuforia.Status.EXTENDED_TRACKED;
            if (_activeObserverTransform.gameObject.activeSelf != tracked)
                _activeObserverTransform.gameObject.SetActive(tracked);
            statusPanel?.SetImageTargetFound(tracked);
        }

        private void OnImageTargetStatusChanged(Vuforia.ObserverBehaviour behaviour, Vuforia.TargetStatus status)
        {
            if (!IsImageTargetStep(_lastActivation)) return;

            var tracked = status.Status == Vuforia.Status.TRACKED
                       || status.Status == Vuforia.Status.LIMITED;

            if (imageTargetAnchor != null)
                imageTargetAnchor.gameObject.SetActive(tracked);
            statusPanel?.SetImageTargetFound(tracked);
            Debug.Log($"[AppBootstrap] Image target tracking: {(tracked ? "FOUND" : "LOST")}");

            _runtime.TargetManager.UpdateTrackingPose(behaviour.transform.position, behaviour.transform.rotation, tracked);

            if (tracked) _runtime.StepCoordinator.BeginTracking();
            else _runtime.StepCoordinator.NotifyTrackingLost();
        }
#endif

        /// <summary>
        /// Identity-transform child of the Vuforia model-target observer. Both the
        /// fixture overlay and the per-step animations parent here. Exists only so
        /// the tracking gate can toggle visibility (SetActive) without disabling the
        /// observer GameObject itself (which would stop Vuforia from updating pose).
        /// No translation, rotation, or scale is applied — the source data is
        /// expected to be already aligned with the physical fixture.
        /// </summary>
        private Transform GetOrCreateAnimationRoot(Transform observerRoot)
        {
            if (observerRoot == null) return null;

            const string anchorName = "AnimationRoot";
            var existing = observerRoot.Find(anchorName);
            if (existing == null)
            {
                var go = new GameObject(anchorName);
                existing = go.transform;
                existing.SetParent(observerRoot, false);
            }

            existing.localPosition = Vector3.zero;
            existing.localRotation = Quaternion.identity;
            existing.localScale = Vector3.one;
            return existing;
        }

        private static bool IsImageTargetStep(StepActivationDto activation) =>
            activation != null &&
            (string.Equals(activation.AnchorType, "image-target", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(activation.AnchorType, "ImageTarget", StringComparison.OrdinalIgnoreCase));

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
#if VUFORIA_ENGINE
            if (imageTargetObserver != null)
                imageTargetObserver.OnTargetStatusChanged -= OnImageTargetStatusChanged;
            if (_modelTargetObserver != null)
                _modelTargetObserver.OnTargetStatusChanged -= OnModelTargetStatusChanged;
#endif
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
