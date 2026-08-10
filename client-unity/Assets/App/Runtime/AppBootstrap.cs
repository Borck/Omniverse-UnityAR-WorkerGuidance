using UnityEngine;
using UnityEngine.Rendering;
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
        [Tooltip("Seconds the instruction stays on screen before a step's animation appears. Text-only steps ignore this and show text only.")]
        [SerializeField] private float instructionHoldSeconds = 5f;

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

        [Header("Animation offset (applied only to step animations)")]
        [Tooltip("Local position applied to the spawned animations relative to the model-target observer. Use to nudge a step into perfect alignment.")]
        [SerializeField] private Vector3 animationOffsetLocalPosition = Vector3.zero;
        [Tooltip("Local euler rotation (deg) applied to the spawned animations.")]
        [SerializeField] private Vector3 animationOffsetLocalEulerAngles = Vector3.zero;

        [Header("Fixture overlay offset (applied only to the fixture hologram)")]
        [Tooltip("Local position applied to the fixture overlay relative to the model-target observer. Independent from the animation offset.")]
        [SerializeField] private Vector3 overlayOffsetLocalPosition = Vector3.zero;
        [Tooltip("Local euler rotation (deg) applied to the fixture overlay.")]
        [SerializeField] private Vector3 overlayOffsetLocalEulerAngles = Vector3.zero;

        [SerializeField] private SessionStatusPanel statusPanel;
        [Header("Full-screen instruction overlay")]
        [Tooltip("Optional scene-wired full-screen instruction overlay. If left empty, one is created at runtime.")]
        [SerializeField] private FullScreenInstructionPanel instructionOverlay;
        [Tooltip("Fitting steps: seconds the big instruction text is held before each animation play.")]
        [SerializeField] private float fittingTextHoldSeconds = 4f;
        [Tooltip("Fitting steps: fallback animation-play duration (seconds) used when the clip length can't be read.")]
        [SerializeField] private float fittingAnimationFallbackSeconds = 12f;
        [Tooltip("Fitting steps: seconds the finished animation stays visible at its END position before the instruction text returns.")]
        [SerializeField] private float fittingEndHoldSeconds = 3f;
        [SerializeField] private TrackingDirectionHint trackingDirectionHint;
        [SerializeField] private JobSelectorPanel jobSelectorPanel;
        [Header("Control drawer")]
        [Tooltip("Optional scene-wired FOV panel. If left empty, one is created at runtime.")]
        [SerializeField] private FovTunerPanel fovTunerPanel;
#if VUFORIA_ENGINE
        private Vuforia.ObserverBehaviour _modelTargetObserver;
#endif

        private AppRuntimeContext _runtime;
        private CameraFovOverride _fovOverride;
        private EyeOffsetCalibration _eyeOffset;
        private EyeOffsetPanel _eyeOffsetPanel;
        private ControlDrawer _drawer;
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
        private Transform _modelSpawnAnchor;
        private bool _vuforiaTargetLoaded;
        private Transform _activeObserverTransform;
        private readonly List<StepActivationDto> _stepHistory = new List<StepActivationDto>();
        private FixtureOverlay _activeFixtureOverlay;
        private Coroutine _fittingCycle;

        private void Awake()
        {
            System.AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

            if (vuforiaTrackingBridge == null)
                vuforiaTrackingBridge = FindFirstObjectByType<VuforiaTrackingBridge>();
        }

        private void Start()
        {
            HologramApplier.Enabled = useHologramShader;

            EnsureCameraFovOverride();
            EnsureEyeOffset();

            if (fovTunerPanel == null) fovTunerPanel = gameObject.AddComponent<FovTunerPanel>();
            _eyeOffsetPanel = gameObject.AddComponent<EyeOffsetPanel>();

            _drawer = gameObject.AddComponent<ControlDrawer>();
            _drawer.Bind(statusPanel, fovTunerPanel, _eyeOffsetPanel);
            _drawer.enabled = false; // shown once a job is running

            // Full-screen instruction overlay (drawn independently of the drawer,
            // so instructions stay readable with the drawer collapsed). Starts
            // hidden; step activation shows it.
            if (instructionOverlay == null)
                instructionOverlay = gameObject.AddComponent<FullScreenInstructionPanel>();
            instructionOverlay.Hide();

            StartCoroutine(StartupFlow());
        }

        // EyeOffsetCalibration lives on Camera.main and holds the persisted
        // camera->eye offset used for optical see-through parallax correction.
        private void EnsureEyeOffset()
        {
            var cam = Camera.main;
            if (cam == null) return;
            _eyeOffset = cam.GetComponent<EyeOffsetCalibration>();
            if (_eyeOffset == null) _eyeOffset = cam.gameObject.AddComponent<EyeOffsetCalibration>();
        }

        // CameraFovOverride lives on Camera.main and owns both the slider state
        // and the per-frame projection-matrix write (URP beginCameraRendering)
        // that actually performs the zoom. Attaching it here makes sure the
        // FOV tuner panel can always find it by Camera.main.GetComponent<>().
        private void EnsureCameraFovOverride()
        {
            var cam = Camera.main;
            if (cam == null) return;
            _fovOverride = cam.GetComponent<CameraFovOverride>();
            if (_fovOverride == null) _fovOverride = cam.gameObject.AddComponent<CameraFovOverride>();
        }

        // Feed the live tracked fixture anchor to the FOV override so its zoom
        // magnifies around the fixture (no off-axis drift) rather than around
        // the principal point. Null when no target is tracked -> override falls
        // back to principal-point magnification.
        private void LateUpdate()
        {
            ApplyEyeOffset();
            if (_fovOverride != null)
                _fovOverride.AnchorTransform = _activeObserverTransform;
            UpdateFixtureDistance();
        }

        // Live camera->fixture distance from Vuforia's tracked pose, pushed to
        // the status panel. Uses the observer transform (the clean tracked pose),
        // NOT the AnimationRoot (which carries the eye-offset shift). Reports a
        // negative value when there is no current track so the panel shows "--".
        private void UpdateFixtureDistance()
        {
            if (statusPanel == null) return;

            var cam = Camera.main;
            Transform observer = null;
#if VUFORIA_ENGINE
            if (_modelTargetObserver != null) observer = _modelTargetObserver.transform;
#endif
            if (observer == null && _activeObserverTransform != null)
                observer = _activeObserverTransform.parent; // AnimationRoot's parent == the observer

            bool tracked = _activeObserverTransform != null
                           && _activeObserverTransform.gameObject.activeSelf; // tracking gate

            if (cam != null && observer != null && tracked)
                statusPanel.SetFixtureDistance(Vector3.Distance(cam.transform.position, observer.position));
            else
                statusPanel.SetFixtureDistance(-1f);
        }

        // Optical see-through parallax correction (content-shift): shift the
        // tracked content by the calibrated camera->eye offset, expressed in world
        // space from the camera's current orientation. A fixed camera-space content
        // shift is equivalent to translating the rendering viewpoint, so the
        // correction is automatically distance-accurate (perspective handles near
        // vs far). AnimationRoot starts at the observer origin (localPosition 0); we
        // set its localPosition so its world position is observer + worldShift. A
        // zero OffsetMeters is a no-op, so an uncalibrated eye offset does nothing.
        private void ApplyEyeOffset()
        {
            if (_eyeOffset == null) return;
            if (_activeObserverTransform == null) return;
            var cam = Camera.main;
            if (cam == null) return;

            Vector3 worldShift = cam.transform.TransformVector(_eyeOffset.OffsetMeters);
            var parent = _activeObserverTransform.parent;
            _activeObserverTransform.localPosition = parent != null
                ? parent.InverseTransformVector(worldShift)
                : worldShift;
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

        public void OnServerConfigConfirmed(string grpcHost, int grpcPort, string httpHost, int httpPort, bool httpTls)
        {
            SaveAndApplyEndpoints(grpcHost, grpcPort, httpHost, httpPort, httpTls);
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
            var grpcHost = PlayerPrefs.GetString(ServerConfigPanel.PrefHost, "");
            if (string.IsNullOrEmpty(grpcHost)) return;

            var grpcPort = PlayerPrefs.GetInt(ServerConfigPanel.PrefGrpcPort, 50051);
            // gRPC and FastAPI may live on different hosts (separate QR codes); the
            // HTTP host falls back to the gRPC host when not set separately.
            var httpHost = PlayerPrefs.GetString(ServerConfigPanel.PrefHttpHost, grpcHost);
            if (string.IsNullOrEmpty(httpHost)) httpHost = grpcHost;
            var httpPort = PlayerPrefs.GetInt(ServerConfigPanel.PrefHttpPort, 8080);
            var httpScheme = PlayerPrefs.GetString(ServerConfigPanel.PrefHttpScheme, "http");

            grpcTarget = $"{grpcHost}:{grpcPort}";
            httpBridgeBaseUrl = $"{httpScheme}://{httpHost}:{httpPort}";
            Debug.Log($"[AppBootstrap] Using saved endpoints gRPC={grpcTarget} HTTP={httpBridgeBaseUrl}");
        }

        // Full form: independent gRPC and FastAPI hosts (used by the two-QR flow).
        private void SaveAndApplyEndpoints(string grpcHost, int grpcPort, string httpHost, int httpPort, bool httpTls)
        {
            if (string.IsNullOrEmpty(httpHost)) httpHost = grpcHost;
            var scheme = httpTls ? "https" : "http";

            grpcTarget = $"{grpcHost}:{grpcPort}";
            httpBridgeBaseUrl = $"{scheme}://{httpHost}:{httpPort}";

            PlayerPrefs.SetString(ServerConfigPanel.PrefHost, grpcHost);
            PlayerPrefs.SetInt(ServerConfigPanel.PrefGrpcPort, grpcPort);
            PlayerPrefs.SetString(ServerConfigPanel.PrefHttpHost, httpHost);
            PlayerPrefs.SetInt(ServerConfigPanel.PrefHttpPort, httpPort);
            PlayerPrefs.SetString(ServerConfigPanel.PrefHttpScheme, scheme);
            PlayerPrefs.Save();
        }

        // Back-compat: same host for both services (UDP auto-discovery).
        private void SaveAndApplyEndpoint(string host, int grpcPort, int httpPort)
            => SaveAndApplyEndpoints(host, grpcPort, host, httpPort, httpTls: false);

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
            if (_drawer != null) _drawer.enabled = true;
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

            // A new step supersedes the previous fitting text/animation cycle.
            StopFittingCycle();

            var instructionText = string.IsNullOrEmpty(activation.InstructionsShort)
                ? activation.DisplayName
                : activation.InstructionsShort;

            if (statusPanel != null)
            {
                statusPanel.SetActiveStep(activation.StepId, activation.PartId);
                statusPanel.SetInstruction(instructionText);
                statusPanel.SetWarning(string.Empty);
            }

            // Show the big centred instruction right away for every step. Whether
            // it stays up (preparation) or starts alternating with the animation
            // (fitting) is decided once the asset pipeline knows if there's a GLB.
            if (instructionOverlay != null)
                instructionOverlay.ShowText(instructionText);

            if (enableRuntimeAssetPipeline)
            {
                StartCoroutine(ResolveAndPresentStepAsset(activation));
            }
        }

        private void OnSessionWorkflowCompleted()
        {
            Debug.Log("[AppBootstrap] Workflow complete — all steps done.");
            _runtime.StepCoordinator.RegisterFault("workflow-complete");
            StopFittingCycle();
            if (statusPanel != null)
            {
                statusPanel.SetActiveStep("-", "-");
                statusPanel.SetInstruction("-");
                statusPanel.SetWarning("Alle Schritte abgeschlossen! Workflow komplett.");
            }
            if (instructionOverlay != null)
                instructionOverlay.ShowText("Alle Schritte abgeschlossen!");
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

            StopFittingCycle();
            if (instructionOverlay != null) instructionOverlay.Hide();

            if (_drawer != null) _drawer.enabled = false;

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
            _modelSpawnAnchor = null;
            _runtime = null;

            if (statusPanel != null)
            {
                statusPanel.SetConnectionState(SessionConnectionState.Disconnected);
                statusPanel.SetStepState(StepCoordinatorState.Idle);
                statusPanel.SetActiveStep("-", "-");
                statusPanel.SetInstruction("-");
                statusPanel.SetWarning(string.Empty);
                statusPanel.ClearImageTargetFound();
                statusPanel.SetReacquireHint(false);
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

            // Step confirmed: drop the big instruction and its cycle until the
            // server activates the next step.
            StopFittingCycle();
            if (instructionOverlay != null) instructionOverlay.Hide();

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

            var instructionText = string.IsNullOrEmpty(_lastActivation.InstructionsShort)
                ? _lastActivation.DisplayName
                : _lastActivation.InstructionsShort;

            StopFittingCycle();
            if (instructionOverlay != null) instructionOverlay.ShowText(instructionText);

            if (!string.IsNullOrEmpty(_lastModelPath) && File.Exists(_lastModelPath))
            {
                _runtime.TargetManager.ActivateTarget(_lastActivation.TargetId, _lastTargetVersion, _lastTargetPayloadPath);
                _loadCancellation?.Cancel();
                _loadCancellation?.Dispose();
                _loadCancellation = new CancellationTokenSource();
                _ = _runtime.ModelPresenter.PresentModelAsync(_lastModelPath, _lastActivation, _loadCancellation.Token, _activeObserverTransform);
                if (statusPanel != null) statusPanel.SetWarning(string.Empty);
                // Fitting step (has a model): restart the text/animation cycle. The
                // cycle tolerates the async load finishing a moment later.
                if (instructionOverlay != null)
                    _fittingCycle = StartCoroutine(RunFittingInstructionCycle(_loadCancellation.Token));
                return;
            }

            StartCoroutine(ResolveAndPresentStepAsset(_lastActivation));
        }

        public void PreviousStep()
        {
            if (_stepHistory.Count == 0)
            {
                if (statusPanel != null) statusPanel.SetWarning("No previous step available.");
                return;
            }

            StepActivationDto previousActivation;
            if (_lastActivation == null)
            {
                // Post-completion: just-finished step becomes active again.
                previousActivation = _stepHistory[^1];
            }
            else
            {
                if (_stepHistory.Count < 2)
                {
                    if (statusPanel != null) statusPanel.SetWarning("No previous step available.");
                    return;
                }
                _stepHistory.RemoveAt(_stepHistory.Count - 1);
                previousActivation = _stepHistory[^1];
            }

            _runtime.ModelPresenter.ClearActiveModel();
            _runtime.TargetManager.DeactivateTarget();
            _lastActivation = previousActivation;

            // A step change supersedes the previous fitting cycle; ResolveAndPresent
            // restarts one for the previous step if it's a fitting step.
            StopFittingCycle();

            var instructionText = string.IsNullOrEmpty(previousActivation.InstructionsShort)
                ? previousActivation.DisplayName
                : previousActivation.InstructionsShort;

            if (statusPanel != null)
            {
                statusPanel.SetActiveStep(previousActivation.StepId, previousActivation.PartId);
                statusPanel.SetInstruction(instructionText);
                statusPanel.SetWarning(string.Empty);
            }

            if (instructionOverlay != null)
                instructionOverlay.ShowText(instructionText);

            StartCoroutine(ResolveAndPresentStepAsset(previousActivation));
        }

        public bool IsFixtureOverlayVisible => showFixtureOverlay;

        public void SetFixtureOverlayVisible(bool visible)
        {
            showFixtureOverlay = visible;
            if (_activeFixtureOverlay != null)
                _activeFixtureOverlay.OverlayEnabled = visible;
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
            // Anchor the instruction-hold window at activation time, so an
            // animation step's model appears ~instructionHoldSeconds after the
            // worker first sees the instruction (asset download overlaps this).
            float presentAnimationAfter = Time.time + Mathf.Max(0f, instructionHoldSeconds);

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

            // Instruction-only substep (= "preparation" step): the manifest carries
            // no GLB (empty glbUrl). Show the instruction text only — skip asset
            // download, target activation, and model presentation. The big
            // full-screen instruction was already shown in OnSessionStepActivated
            // and, because no fitting cycle is started here, it stays on screen
            // until the next step. Backend Next/Previous still steps through these
            // entries normally.
            if (string.IsNullOrEmpty(resolved.GlbUrl))
            {
                _loadCancellation?.Cancel();
                StopFittingCycle();
                _runtime.ModelPresenter.ClearActiveModel();
                statusPanel?.SetPipelineStatus("instruction only");
                _lastModelPath = string.Empty;
                instructionOverlay?.Show(); // keep the preparation text up

                // Warm the next step's assets (typically the animation substep)
                // so it's ready by the time the backend advances.
                if (resolvedBundle.Next != null)
                    StartCoroutine(PrefetchNextStepAssets(resolvedBundle.Next, activation.StepId));
                yield break;
            }

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
                        // AnimationRoot is the identity gate. ModelAnchor + OverlayAnchor sit
                        // under it with independent offsets so the step animations and the
                        // fixture overlay can be aligned separately.
                        var animRoot = observer != null ? GetOrCreateAnimationRoot(observer.transform) : null;
                        _activeObserverTransform = animRoot;
                        _modelSpawnAnchor = animRoot != null ? GetOrCreateModelAnchor(animRoot) : null;
                        var overlayAnchor = animRoot != null ? GetOrCreateOverlayAnchor(animRoot) : null;
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
                            overlay.Initialize(fixtureOverlayPrefab, observer, overlayAnchor);
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
            // Image-target path removed — project is model-target only.
            // ====================================================================

            _loadCancellation?.Cancel();
            _loadCancellation?.Dispose();
            _loadCancellation = new CancellationTokenSource();
            var loadToken = _loadCancellation.Token;

            // Hold the instruction on screen before the animation appears. The
            // download/target work above overlapped this window, so the model
            // shows at ~instructionHoldSeconds from activation (or later if the
            // download ran long). Bail immediately if a newer step superseded us.
            //
            // With the full-screen overlay present, the fitting cycle owns the
            // text-then-animation timing (and the overlay already shows the text),
            // so load the model promptly and let the cycle decide when it appears —
            // otherwise the first play would be held twice (here + text phase).
            if (instructionOverlay == null)
            {
                while (Time.time < presentAnimationAfter)
                {
                    if (loadToken.IsCancellationRequested) yield break;
                    yield return null;
                }
            }

            var spawnAnchor = _modelSpawnAnchor != null && _modelSpawnAnchor.gameObject != null
                ? _modelSpawnAnchor
                : (_activeObserverTransform != null && _activeObserverTransform.gameObject != null
                    ? _activeObserverTransform : null);
            Task loadTask = _runtime.ModelPresenter.PresentModelAsync(modelPath, activation, loadToken, spawnAnchor);
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

            // Fitting step: the model carries an animation. Alternate the big
            // instruction text with the animation (text held, then one full play,
            // repeat) until the next step supersedes it.
            if (instructionOverlay != null)
            {
                StopFittingCycle();
                _fittingCycle = StartCoroutine(RunFittingInstructionCycle(loadToken));
            }

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

#if VUFORIA_ENGINE
        private void OnModelTargetStatusChanged(Vuforia.ObserverBehaviour behaviour, Vuforia.TargetStatus status)
        {
            if (_activeObserverTransform == null) return;

            // Fail-safe visibility (option A): show the hologram ONLY while the
            // pose is solid (TRACKED/LIMITED). On EXTENDED_TRACKED (device
            // tracker holding an out-of-view target — drifts, looks head-locked)
            // or NO_POSE, hide the whole AR subtree and prompt the worker to
            // re-aim. Hiding AnimationRoot (not the observer GameObject) keeps
            // Vuforia updating pose, so it re-locks instantly when the fixture
            // comes back into view. Policy shared with VuforiaTrackingBridge.
            var solid = VuforiaTrackingBridge.IsSolidPose(status.Status);
            if (_activeObserverTransform.gameObject.activeSelf != solid)
                _activeObserverTransform.gameObject.SetActive(solid);
            statusPanel?.SetImageTargetFound(solid);
            statusPanel?.SetReacquireHint(!solid);
        }
#endif

        /// <summary>
        /// Identity-transform child of the Vuforia model-target observer. Used as
        /// the tracking gate target (SetActive() toggles the whole AR subtree without
        /// disabling the observer GameObject, which would stop Vuforia from updating
        /// pose). Holds two independently-positioned siblings:
        ///   • ModelAnchor   — animationOffsetLocal* — parents the step animations.
        ///   • OverlayAnchor — overlayOffsetLocal*   — parents the fixture overlay.
        /// </summary>
        private Transform GetOrCreateAnimationRoot(Transform observerRoot)
        {
            if (observerRoot == null) return null;
            var root = GetOrCreateNamedChild(observerRoot, "AnimationRoot");
            root.localPosition = Vector3.zero;
            root.localRotation = Quaternion.identity;
            root.localScale = Vector3.one;
            return root;
        }

        private Transform GetOrCreateModelAnchor(Transform animationRoot)
        {
            if (animationRoot == null) return null;
            var t = GetOrCreateNamedChild(animationRoot, "ModelAnchor");
            t.localPosition = animationOffsetLocalPosition;
            t.localRotation = Quaternion.Euler(animationOffsetLocalEulerAngles);
            t.localScale = Vector3.one;
            return t;
        }

        private Transform GetOrCreateOverlayAnchor(Transform animationRoot)
        {
            if (animationRoot == null) return null;
            var t = GetOrCreateNamedChild(animationRoot, "OverlayAnchor");
            t.localPosition = overlayOffsetLocalPosition;
            t.localRotation = Quaternion.Euler(overlayOffsetLocalEulerAngles);
            t.localScale = Vector3.one;
            return t;
        }

        private static Transform GetOrCreateNamedChild(Transform parent, string name)
        {
            var existing = parent.Find(name);
            if (existing != null) return existing;
            var go = new GameObject(name);
            var t = go.transform;
            t.SetParent(parent, false);
            return t;
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

        private void StopFittingCycle()
        {
            if (_fittingCycle != null)
            {
                StopCoroutine(_fittingCycle);
                _fittingCycle = null;
            }
        }

        /// <summary>
        /// Fitting-step presentation loop: show the big instruction text for
        /// <see cref="fittingTextHoldSeconds"/> (model hidden), then hide the text
        /// and play the animation once fully (model shown), then repeat — until the
        /// step is superseded (token cancelled) or the coroutine is stopped.
        /// Preparation (text-only) steps never enter this loop; their text stays up.
        /// </summary>
        private IEnumerator RunFittingInstructionCycle(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                // ---- text phase: big instruction only, model hidden ----
                _runtime.ModelPresenter.SetActiveModelVisible(false);
                instructionOverlay?.Show();

                float held = 0f;
                while (held < fittingTextHoldSeconds)
                {
                    if (token.IsCancellationRequested) yield break;
                    held += Time.deltaTime;
                    yield return null;
                }

                // ---- animation phase: one full play, text hidden ----
                instructionOverlay?.Hide();
                // Re-enabling the model root re-fires the replay-loop drivers'
                // OnEnable, restarting the clip from frame 0.
                _runtime.ModelPresenter.SetActiveModelVisible(true);

                // Give the (re)started replay loop a couple of frames to settle so
                // the clip length reads back correctly.
                yield return null;
                yield return null;
                if (token.IsCancellationRequested) yield break;

                float playSeconds = _runtime.ModelPresenter.GetActiveAnimationPlaySeconds();
                if (playSeconds <= 0.01f) playSeconds = fittingAnimationFallbackSeconds;

                float played = 0f;
                while (played < playSeconds)
                {
                    if (token.IsCancellationRequested) yield break;
                    played += Time.deltaTime;
                    yield return null;
                }

                // ---- end-hold: keep the finished part visible at its final
                // position (the replay loop is holding the last frame) before the
                // instruction text returns, so the worker sees where it ends up.
                float endHeld = 0f;
                while (endHeld < fittingEndHoldSeconds)
                {
                    if (token.IsCancellationRequested) yield break;
                    endHeld += Time.deltaTime;
                    yield return null;
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
            if (statusPanel != null) statusPanel.SetWarning("Netzwerk unterbrochen: Schritt eingefroren bis Reconnect.");
        }

        private void OnDestroy()
        {
            Debug.Log("[AppBootstrap] Shutting down session safely...");
#if VUFORIA_ENGINE
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
