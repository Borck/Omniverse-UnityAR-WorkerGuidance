using UnityEngine;

namespace Guidance.Runtime
{
    // Lightweight composition root for runtime modules until a full DI container is introduced.
    public sealed class AppRuntimeContext
    {
        public SessionClient SessionClient { get; }
        public StepCoordinator StepCoordinator { get; }
        public AssetCache AssetCache { get; }
        public TargetManager TargetManager { get; }
        public TelemetryClient TelemetryClient { get; }
        public StepAssetManifestClient ManifestClient { get; }
        public ModelPresenter ModelPresenter { get; }

        public AppRuntimeContext(
            SessionClient sessionClient,
            StepCoordinator stepCoordinator,
            AssetCache assetCache,
            TargetManager targetManager,
            TelemetryClient telemetryClient,
            StepAssetManifestClient manifestClient,
            ModelPresenter modelPresenter)
        {
            SessionClient = sessionClient;
            StepCoordinator = stepCoordinator;
            AssetCache = assetCache;
            TargetManager = targetManager;
            TelemetryClient = telemetryClient;
            ManifestClient = manifestClient;
            ModelPresenter = modelPresenter;
        }

        public static AppRuntimeContext CreateDefault(
            bool useNativeGrpcTransport,
            string grpcTarget,
            string httpBridgeBaseUrl,
            bool supportsDraco)
        {
            ISessionTransport transport = useNativeGrpcTransport
                ? new GrpcSessionTransport(
                    target: grpcTarget,
                    deviceId: SystemInfo.deviceUniqueIdentifier,
                    appVersion: Application.version
                )
                : new HttpBridgeSessionTransport(
                    baseUrl: httpBridgeBaseUrl,
                    deviceId: SystemInfo.deviceUniqueIdentifier,
                    appVersion: Application.version
                );

            return new AppRuntimeContext(
                sessionClient: new SessionClient(supportsDraco: supportsDraco, transport: transport),
                stepCoordinator: new StepCoordinator(),
                assetCache: new AssetCache(),
                targetManager: new TargetManager(),
                telemetryClient: new TelemetryClient(),
                manifestClient: new StepAssetManifestClient(httpBridgeBaseUrl),
                modelPresenter: new ModelPresenter()
            );
        }
    }
}
