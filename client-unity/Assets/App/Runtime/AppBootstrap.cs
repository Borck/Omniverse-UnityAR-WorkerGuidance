using UnityEngine;
using System;
using System.Collections;
using System.IO;

namespace Guidance.Runtime
{
    public sealed class AppBootstrap : MonoBehaviour
    {
        [SerializeField] private bool useNativeGrpcTransport = true;
        [SerializeField] private string grpcTarget = "localhost:50051";
        [SerializeField] private string httpBridgeBaseUrl = "http://localhost:8080";
        [SerializeField] private bool enableRuntimeAssetPipeline = true;
        [SerializeField] private bool autoConfirmStepAfterAssetReady = false;
        [SerializeField] private float autoConfirmDelaySeconds = 0.5f;
        [SerializeField] private float heartbeatIntervalSeconds = 5f;
        [SerializeField] private float reconnectIntervalSeconds = 3f;

        [SerializeField] private SessionStatusPanel statusPanel;

        private AppRuntimeContext _runtime;
        private StepActivationDto _lastActivation;
        private float _nextHeartbeatAt;
        private float _nextReconnectAt;

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
            _nextReconnectAt = Time.time + reconnectIntervalSeconds;

            if (statusPanel != null)
            {
                statusPanel.SetConnectionState(SessionConnectionState.Disconnected);
                statusPanel.SetStepState(StepCoordinatorState.Idle);
                statusPanel.SetActiveStep("-", "-");
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
                return;
            }

            if (now >= _nextReconnectAt)
            {
                _runtime.SessionClient.TryReconnect();
                _nextReconnectAt = now + reconnectIntervalSeconds;
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
            Debug.Log($"[AppBootstrap] Step activated from session: {activation.JobId}/{activation.StepId}");
            _runtime.StepCoordinator.ActivateStep(activation.JobId, activation.StepId);
            _runtime.TelemetryClient.TrackStepActivated(activation.JobId, activation.StepId, activation.PartId);
            _lastActivation = activation;

            if (statusPanel != null)
            {
                statusPanel.SetActiveStep(activation.StepId, activation.PartId);
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
        }

        public void ConfirmActiveStep()
        {
            if (_lastActivation == null)
            {
                return;
            }

            if (!_runtime.StepCoordinator.ConfirmStepCompleted())
            {
                return;
            }

            var completedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            _runtime.SessionClient.SendStepCompleted(_lastActivation.JobId, _lastActivation.StepId, completedAt);
            _runtime.TargetManager.DeactivateTarget();
            _runtime.ModelPresenter.ClearActiveModel();
            _lastActivation = null;

            if (statusPanel != null)
            {
                statusPanel.SetActiveStep("-", "-");
            }
        }

        private IEnumerator ResolveAndPresentStepAsset(StepActivationDto activation)
        {
            ResolvedStepAsset resolved = null;
            string resolveError = null;

            yield return _runtime.ManifestClient.ResolveStepAsset(
                activation.JobId,
                activation.StepId,
                onResolved: value => resolved = value,
                onError: error => resolveError = error
            );

            if (!string.IsNullOrEmpty(resolveError))
            {
                _runtime.TelemetryClient.TrackFault("MANIFEST_RESOLVE", resolveError);
                _runtime.StepCoordinator.RegisterFault(resolveError);
                yield break;
            }

            if (resolved == null)
            {
                _runtime.TelemetryClient.TrackFault("MANIFEST_RESOLVE", "Step asset resolve returned null");
                _runtime.StepCoordinator.RegisterFault("Step asset resolve returned null");
                yield break;
            }

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
                yield break;
            }

            _runtime.TelemetryClient.TrackAssetDownloaded(resolved.AssetVersion, fileName);
            _runtime.TargetManager.ActivateTarget(activation.TargetId, resolved.TargetVersion);
            _runtime.ModelPresenter.PresentModel(modelPath, activation);

            if (autoConfirmStepAfterAssetReady)
            {
                yield return new WaitForSeconds(autoConfirmDelaySeconds);
                ConfirmActiveStep();
            }
        }

        private static string ExtractFileName(string glbUrl, string stepId)
        {
            if (string.IsNullOrEmpty(glbUrl))
            {
                return $"step_{stepId}.glb";
            }

            if (Uri.TryCreate(glbUrl, UriKind.Absolute, out var uri))
            {
                var absolute = Path.GetFileName(uri.AbsolutePath);
                if (!string.IsNullOrEmpty(absolute))
                {
                    return absolute;
                }
            }

            var simple = Path.GetFileName(glbUrl);
            return string.IsNullOrEmpty(simple) ? $"step_{stepId}.glb" : simple;
        }
    }
}
