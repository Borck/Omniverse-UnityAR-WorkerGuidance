using System.Threading;
using UnityEngine;

namespace Guidance.Runtime
{
    /// <summary>
    /// First-launch (or anytime) server endpoint screen. Three ways to connect:
    ///
    ///   1. Type the host + ports manually (survives APK reinstalls via PlayerPrefs).
    ///   2. Auto-discover over UDP (only on flat LANs without NAT).
    ///   3. Scan two QR codes — one for gRPC (grpc://host:port), one for FastAPI
    ///      (http://host:port). The two codes are independent, so gRPC and FastAPI
    ///      may run on different machines. Scanning both auto-connects.
    ///
    /// The VUZIX often lives behind a hotspot/NAT that blocks UDP broadcast, so the
    /// QR path is the most reliable way to hand the glasses a fresh set of endpoints.
    /// </summary>
    public sealed class ServerConfigPanel : MonoBehaviour
    {
        public const string PrefHost = "guidance.serverHost";          // gRPC host
        public const string PrefGrpcPort = "guidance.grpcPort";
        public const string PrefHttpHost = "guidance.httpHost";        // FastAPI host (may differ)
        public const string PrefHttpPort = "guidance.httpPort";
        public const string PrefHttpScheme = "guidance.httpScheme";    // "http" | "https"
        public const string PrefServiceTag = "guidance.serviceTag";

        [SerializeField] private int defaultGrpcPort = 50051;
        [SerializeField] private int defaultHttpPort = 8080;
        [SerializeField] private int discoveryPort = 45454;
        [SerializeField] private float discoveryTimeoutSeconds = 3f;
        [Tooltip("Vuforia QR scanner. If empty, one is found/added at runtime.")]
        [SerializeField] private QrEndpointScanner qrScanner;

        private AppBootstrap _bootstrap;
        private bool _visible;
        private string _host = "";      // gRPC host (also default HTTP host for manual entry)
        private string _httpHost = "";  // FastAPI host, when scanned separately
        private string _grpcPort = "";
        private string _httpPort = "";
        private bool _httpTls;
        private string _status = "";
        private bool _discovering;
        private CancellationTokenSource _discoveryCts;

        // QR scan state
        private bool _qrScanning;
        private bool _grpcScanned;
        private bool _httpScanned;
        private bool _qrHooked;

        public void Show(AppBootstrap bootstrap, string fallbackHost, int fallbackGrpcPort, int fallbackHttpPort)
        {
            _bootstrap = bootstrap;

            _host = PlayerPrefs.GetString(PrefHost, fallbackHost ?? "");
            _httpHost = PlayerPrefs.GetString(PrefHttpHost, "");
            _grpcPort = PlayerPrefs.GetInt(PrefGrpcPort, fallbackGrpcPort > 0 ? fallbackGrpcPort : defaultGrpcPort).ToString();
            _httpPort = PlayerPrefs.GetInt(PrefHttpPort, fallbackHttpPort > 0 ? fallbackHttpPort : defaultHttpPort).ToString();
            _httpTls = PlayerPrefs.GetString(PrefHttpScheme, "http") == "https";
            _status = "";
            _visible = true;
        }

        private void EnsureScanner()
        {
            if (qrScanner == null) qrScanner = FindFirstObjectByType<QrEndpointScanner>();
            if (qrScanner == null) qrScanner = gameObject.AddComponent<QrEndpointScanner>();
        }

        private void OnGUI()
        {
            if (!_visible) return;

            ImguiTheme.Begin();

            const float w = 760f;
            const float h = 760f;
            var rect = new Rect((ImguiTheme.VirtualWidth - w) / 2f, (ImguiTheme.VirtualHeight - h) / 2f, w, h);

            GUILayout.BeginArea(rect, GUI.skin.box);
            GUILayout.FlexibleSpace();

            GUILayout.Label("<b>Server Configuration</b>");
            GUILayout.Space(8);

            GUILayout.Label("Server IP / Host");
            _host = GUILayout.TextField(_host ?? "", GUILayout.Height(ImguiTheme.ControlHeight));

            GUILayout.Space(ImguiTheme.ControlHeight * 0.4f);

            GUILayout.BeginHorizontal();
            GUILayout.BeginVertical();
            GUILayout.Label("gRPC port");
            _grpcPort = GUILayout.TextField(_grpcPort ?? "", GUILayout.Height(ImguiTheme.ControlHeight));
            GUILayout.EndVertical();
            GUILayout.Space(8);
            GUILayout.BeginVertical();
            GUILayout.Label("HTTP port");
            _httpPort = GUILayout.TextField(_httpPort ?? "", GUILayout.Height(ImguiTheme.ControlHeight));
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();

            GUILayout.Space(ImguiTheme.ControlHeight * 0.4f);

            // --- QR scan (two codes: gRPC + FastAPI) ---
            GUI.enabled = !_discovering;
            if (!_qrScanning)
            {
                if (GUILayout.Button("Scan QR codes (Camera)", GUILayout.Height(ImguiTheme.ControlHeight)))
                    StartQrScan();
            }
            else
            {
                GUILayout.Label($"Scanning…  gRPC {(_grpcScanned ? "✓" : "–")}   FastAPI {(_httpScanned ? "✓" : "–")}");
                if (GUILayout.Button("Cancel scan", GUILayout.Height(ImguiTheme.ControlHeight)))
                    CancelQrScan();
            }
            GUI.enabled = true;

            GUILayout.Space(6);

            GUI.enabled = !_discovering && !_qrScanning;
            if (GUILayout.Button(_discovering ? "Searching..." : "Auto-discover (UDP)", GUILayout.Height(ImguiTheme.ControlHeight)))
                StartCoroutine(RunDiscovery());
            GUI.enabled = true;

            GUILayout.Space(6);

            GUI.enabled = !_discovering && !_qrScanning && !string.IsNullOrWhiteSpace(_host);
            if (GUILayout.Button("Save & Continue", GUILayout.Height(ImguiTheme.ControlHeight)))
                SaveAndContinue();
            GUI.enabled = true;

            if (!string.IsNullOrEmpty(_status))
            {
                GUILayout.Space(6);
                GUILayout.Label(_status);
            }

            GUILayout.FlexibleSpace();
            GUILayout.EndArea();

            ImguiTheme.End();
        }

        // ---------------- QR scanning ----------------

        private void StartQrScan()
        {
            EnsureScanner();
            if (qrScanner == null || !qrScanner.IsAvailable)
            {
                _status = "QR scanner unavailable (add a Vuforia Barcode object to the scene).";
                return;
            }

            _grpcScanned = false;
            _httpScanned = false;
            _qrScanning = true;
            _status = "Point the glasses at the gRPC and FastAPI QR codes.";

            if (!_qrHooked)
            {
                qrScanner.BarcodeScanned += OnBarcodeScanned;
                _qrHooked = true;
            }
            qrScanner.BeginScan();
        }

        private void CancelQrScan()
        {
            _qrScanning = false;
            StopScannerHook();
            _status = "Scan cancelled.";
        }

        private void StopScannerHook()
        {
            if (qrScanner != null)
            {
                qrScanner.EndScan();
                if (_qrHooked) qrScanner.BarcodeScanned -= OnBarcodeScanned;
            }
            _qrHooked = false;
        }

        // Vuforia raises this repeatedly while a code is in view; we capture each
        // service once and auto-connect as soon as both are in hand.
        private void OnBarcodeScanned(string raw)
        {
            if (!_qrScanning) return;

            var ep = EndpointQrPayload.Parse(raw);
            if (!ep.IsValid) return; // not one of our codes; ignore quietly

            if (ep.Service == EndpointService.Grpc && !_grpcScanned)
            {
                _host = ep.Host;
                _grpcPort = ep.Port.ToString();
                _grpcScanned = true;
            }
            else if (ep.Service == EndpointService.Http && !_httpScanned)
            {
                _httpHost = ep.Host;
                _httpPort = ep.Port.ToString();
                _httpTls = ep.UseTls;
                _httpScanned = true;
            }

            _status = $"Captured  gRPC {(_grpcScanned ? "✓" : "–")}   FastAPI {(_httpScanned ? "✓" : "–")}";

            if (_grpcScanned && _httpScanned)
            {
                _qrScanning = false;
                StopScannerHook();
                SaveAndContinue(); // auto-connect
            }
        }

        // ---------------- UDP discovery ----------------

        private System.Collections.IEnumerator RunDiscovery()
        {
            _discovering = true;
            _status = $"Listening on UDP {discoveryPort}...";

            _discoveryCts?.Cancel();
            _discoveryCts = new CancellationTokenSource();
            var task = DiscoveryClient.DiscoverAsync(discoveryPort, "", discoveryTimeoutSeconds, _discoveryCts.Token);
            yield return new WaitUntil(() => task.IsCompleted);

            if (task.Status == System.Threading.Tasks.TaskStatus.RanToCompletion && task.Result != null)
            {
                _host = task.Result.Host;
                _httpHost = ""; // same host for both on discovery
                _grpcPort = task.Result.GrpcPort.ToString();
                _httpPort = task.Result.HttpPort.ToString();
                _httpTls = false;
                _status = $"Found: {task.Result.Host} (tag '{task.Result.Tag}')";
            }
            else
            {
                _status = "No beacon received. Type the IP or scan the QR codes.";
            }
            _discovering = false;
        }

        // ---------------- confirm ----------------

        private void SaveAndContinue()
        {
            if (!int.TryParse(_grpcPort, out var grpc) || grpc <= 0) grpc = defaultGrpcPort;
            if (!int.TryParse(_httpPort, out var http) || http <= 0) http = defaultHttpPort;

            var grpcHost = (_host ?? "").Trim();
            var httpHost = string.IsNullOrWhiteSpace(_httpHost) ? grpcHost : _httpHost.Trim();

            _visible = false;
            StopScannerHook();
            // AppBootstrap owns persistence (PlayerPrefs) so the saved and applied
            // endpoints never drift apart.
            _bootstrap.OnServerConfigConfirmed(grpcHost, grpc, httpHost, http, _httpTls);
        }

        private void OnDisable()
        {
            StopScannerHook();
        }

        private void OnDestroy()
        {
            _discoveryCts?.Cancel();
            _discoveryCts?.Dispose();
            StopScannerHook();
        }
    }
}
