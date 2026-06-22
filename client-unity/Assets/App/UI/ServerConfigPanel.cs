using System.Threading;
using UnityEngine;

namespace Guidance.Runtime
{
    /// <summary>
    /// First-launch (or anytime) server endpoint screen.
    ///
    /// The VUZIX often lives behind a hotspot/NAT that blocks UDP broadcast, so
    /// auto-discovery isn't always available. This panel lets the operator type
    /// the server IP once; the value survives APK reinstalls via PlayerPrefs.
    /// The Auto-discover button is still offered for the networks where it works.
    /// </summary>
    public sealed class ServerConfigPanel : MonoBehaviour
    {
        public const string PrefHost = "guidance.serverHost";
        public const string PrefGrpcPort = "guidance.grpcPort";
        public const string PrefHttpPort = "guidance.httpPort";
        public const string PrefServiceTag = "guidance.serviceTag";

        [SerializeField] private int defaultGrpcPort = 50051;
        [SerializeField] private int defaultHttpPort = 8080;
        [SerializeField] private int discoveryPort = 45454;
        [SerializeField] private float discoveryTimeoutSeconds = 3f;

        private AppBootstrap _bootstrap;
        private bool _visible;
        private string _host = "";
        private string _grpcPort = "";
        private string _httpPort = "";
        private string _serviceTag = "";
        private string _status = "";
        private bool _discovering;
        private CancellationTokenSource _discoveryCts;

        public void Show(AppBootstrap bootstrap, string fallbackHost, int fallbackGrpcPort, int fallbackHttpPort)
        {
            _bootstrap = bootstrap;

            _host = PlayerPrefs.GetString(PrefHost, fallbackHost ?? "");
            _grpcPort = PlayerPrefs.GetInt(PrefGrpcPort, fallbackGrpcPort > 0 ? fallbackGrpcPort : defaultGrpcPort).ToString();
            _httpPort = PlayerPrefs.GetInt(PrefHttpPort, fallbackHttpPort > 0 ? fallbackHttpPort : defaultHttpPort).ToString();
            _serviceTag = PlayerPrefs.GetString(PrefServiceTag, "");
            _status = "";
            _visible = true;
        }

        private void OnGUI()
        {
            if (!_visible) return;

            ImguiTheme.Begin();

            const float w = 760f;
            const float h = 760f;
            var rect = new Rect((ImguiTheme.VirtualWidth - w) / 2f, (ImguiTheme.VirtualHeight - h) / 2f, w, h);

            GUILayout.BeginArea(rect, GUI.skin.box);

            // The content is shorter than the 760 px box, so center it vertically with
            // FlexibleSpace top + bottom. This pulls the IP field down off the top edge
            // and into the middle of the M4000's narrow FOV, where it is actually
            // visible (the earlier clipping was vertical, not horizontal).
            GUILayout.FlexibleSpace();

            GUILayout.Label("<b>Server Configuration</b>");
            GUILayout.Space(8);

            GUILayout.Label("Server IP / Host");
            _host = GUILayout.TextField(_host ?? "", GUILayout.Height(ImguiTheme.ControlHeight));

            // Ports kept, with modest separation above and below.
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

            // Service tag filter field removed (not required). _serviceTag stays ""
            // so auto-discover just matches any beacon, and SaveAndContinue still
            // persists it.
            GUILayout.Space(ImguiTheme.ControlHeight * 0.4f);

            GUI.enabled = !_discovering;
            if (GUILayout.Button(_discovering ? "Searching..." : "Auto-discover (UDP)", GUILayout.Height(ImguiTheme.ControlHeight)))
            {
                StartCoroutine(RunDiscovery());
            }
            GUI.enabled = true;

            GUILayout.Space(6);

            GUI.enabled = !_discovering && !string.IsNullOrWhiteSpace(_host);
            if (GUILayout.Button("Save & Continue", GUILayout.Height(ImguiTheme.ControlHeight)))
            {
                SaveAndContinue();
            }
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

        private System.Collections.IEnumerator RunDiscovery()
        {
            _discovering = true;
            _status = $"Listening on UDP {discoveryPort}...";

            _discoveryCts?.Cancel();
            _discoveryCts = new CancellationTokenSource();
            var task = DiscoveryClient.DiscoverAsync(discoveryPort, _serviceTag ?? "", discoveryTimeoutSeconds, _discoveryCts.Token);
            yield return new WaitUntil(() => task.IsCompleted);

            if (task.Status == System.Threading.Tasks.TaskStatus.RanToCompletion && task.Result != null)
            {
                _host = task.Result.Host;
                _grpcPort = task.Result.GrpcPort.ToString();
                _httpPort = task.Result.HttpPort.ToString();
                _serviceTag = task.Result.Tag ?? "";
                _status = $"Found: {task.Result.Host} (tag '{task.Result.Tag}')";
            }
            else
            {
                _status = "No beacon received. Type the IP manually.";
            }
            _discovering = false;
        }

        private void SaveAndContinue()
        {
            if (!int.TryParse(_grpcPort, out var grpc) || grpc <= 0) grpc = defaultGrpcPort;
            if (!int.TryParse(_httpPort, out var http) || http <= 0) http = defaultHttpPort;

            PlayerPrefs.SetString(PrefHost, _host.Trim());
            PlayerPrefs.SetInt(PrefGrpcPort, grpc);
            PlayerPrefs.SetInt(PrefHttpPort, http);
            PlayerPrefs.SetString(PrefServiceTag, _serviceTag?.Trim() ?? "");
            PlayerPrefs.Save();

            _visible = false;
            _bootstrap.OnServerConfigConfirmed(_host.Trim(), grpc, http);
        }

        private void OnDestroy()
        {
            _discoveryCts?.Cancel();
            _discoveryCts?.Dispose();
        }
    }
}
