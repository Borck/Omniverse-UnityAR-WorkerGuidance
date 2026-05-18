using UnityEngine;

namespace Guidance.Runtime
{
    /// <summary>
    /// Lightweight composition root that wires runtime modules for the guidance app.
    /// </summary>
    public sealed class AppRuntimeContext
    {
        public SessionClient SessionClient { get; }
        public StepCoordinator StepCoordinator { get; }
        public AssetCache AssetCache { get; }
        public TargetPayloadCache TargetPayloadCache { get; }
        public TargetManager TargetManager { get; }
        public TelemetryClient TelemetryClient { get; }
        public DiagnosticsBundleExporter DiagnosticsExporter { get; }
        public StepAssetManifestClient ManifestClient { get; }
        public ModelPresenter ModelPresenter { get; }
        public GrpcAssetTransferClient GrpcAssetTransfer { get; }

        public AppRuntimeContext(
            SessionClient sessionClient,
            StepCoordinator stepCoordinator,
            AssetCache assetCache,
            TargetPayloadCache targetPayloadCache,
            TargetManager targetManager,
            TelemetryClient telemetryClient,
            DiagnosticsBundleExporter diagnosticsExporter,
            StepAssetManifestClient manifestClient,
            ModelPresenter modelPresenter,
            GrpcAssetTransferClient grpcAssetTransfer = null)
        {
            SessionClient = sessionClient;
            StepCoordinator = stepCoordinator;
            AssetCache = assetCache;
            TargetPayloadCache = targetPayloadCache;
            TargetManager = targetManager;
            TelemetryClient = telemetryClient;
            DiagnosticsExporter = diagnosticsExporter;
            ManifestClient = manifestClient;
            ModelPresenter = modelPresenter;
            GrpcAssetTransfer = grpcAssetTransfer;
        }

        /// <summary>
        /// Creates a default runtime graph with either native gRPC or HTTP bridge transport.
        /// </summary>
        public static AppRuntimeContext CreateDefault(
            bool useNativeGrpcTransport,
            string grpcTarget,
            string httpBridgeBaseUrl,
            bool supportsDraco,
            string desiredJobId = "")
        {
            var httpBase = httpBridgeBaseUrl.StartsWith("http://", System.StringComparison.OrdinalIgnoreCase)
                        || httpBridgeBaseUrl.StartsWith("https://", System.StringComparison.OrdinalIgnoreCase)
                ? httpBridgeBaseUrl
                : $"http://{httpBridgeBaseUrl}";

            ISessionTransport transport = useNativeGrpcTransport
                ? new GrpcSessionTransport(
                    target: grpcTarget,
                    deviceId: SystemInfo.deviceUniqueIdentifier,
                    appVersion: Application.version,
                    desiredJobId: desiredJobId
                )
                : new HttpBridgeSessionTransport(
                    baseUrl: httpBase,
                    deviceId: SystemInfo.deviceUniqueIdentifier,
                    appVersion: Application.version
                );

            var grpcAssetTransfer = useNativeGrpcTransport
                ? new GrpcAssetTransferClient(grpcTarget)
                : null;

            return new AppRuntimeContext(
                sessionClient: new SessionClient(supportsDraco: supportsDraco, transport: transport),
                stepCoordinator: new StepCoordinator(),
                assetCache: new AssetCache(),
                targetPayloadCache: new TargetPayloadCache(),
                targetManager: new TargetManager(),
                telemetryClient: new TelemetryClient(),
                diagnosticsExporter: new DiagnosticsBundleExporter(),
                manifestClient: new StepAssetManifestClient(httpBase),
                modelPresenter: new ModelPresenter(),
                grpcAssetTransfer: grpcAssetTransfer
            );
        }
    }
}
