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
        public const string PrefNucleusKey = "guidance.nucleusKey";

        [SerializeField] private int defaultGrpcPort = 50051;
        [SerializeField] private int defaultHttpPort = 8080;
        [SerializeField] private int discoveryPort = 45454;
        [SerializeField] private float discoveryTimeoutSeconds = 3f;
        [Tooltip("Nucleus selected by default on first launch. \"a\" = first server (BTU), \"b\" = second (Chesco). Persists per device once changed.")]
        [SerializeField] private string defaultNucleusKey = "a";

        private AppBootstrap _bootstrap;
        private bool _visible;
        private string _host = "";
        private string _grpcPort = "";
        private string _httpPort = "";
        private string _serviceTag = "";
        private string _status = "";
        private bool _discovering;
        private CancellationTokenSource _discoveryCts;

        private NucleusEndpointDto[] _nucleusEndpoints = System.Array.Empty<NucleusEndpointDto>();
        private string _activeNucleusKey = "";
        private string _nucleusStatus = "";
        private bool _nucleusBusy;

        public void Show(AppBootstrap bootstrap, string fallbackHost, int fallbackGrpcPort, int fallbackHttpPort)
        {
            _bootstrap = bootstrap;

            _host = PlayerPrefs.GetString(PrefHost, fallbackHost ?? "");
            _grpcPort = PlayerPrefs.GetInt(PrefGrpcPort, fallbackGrpcPort > 0 ? fallbackGrpcPort : defaultGrpcPort).ToString();
            _httpPort = PlayerPrefs.GetInt(PrefHttpPort, fallbackHttpPort > 0 ? fallbackHttpPort : defaultHttpPort).ToString();
            _serviceTag = PlayerPrefs.GetString(PrefServiceTag, "");
            _status = "";
            _visible = true;

            // Default to BTU ("a") on first launch so a key is pre-selected
            // without the operator having to tap one. Once they pick a Nucleus
            // it's saved to PlayerPrefs and that choice wins on later launches.
            _activeNucleusKey = PlayerPrefs.GetString(PrefNucleusKey, defaultNucleusKey);
            _nucleusEndpoints = System.Array.Empty<NucleusEndpointDto>();
            _nucleusStatus = "";
            LoadNucleusList();
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

            DrawNucleusSelector();

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
                LoadNucleusList();   // host changed — refresh the nucleus list
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

            // Best-effort: make sure the server's active Nucleus matches the
            // operator's choice before the job loads (covers the case where the
            // toggle was tapped while the server was briefly unreachable).
            if (!string.IsNullOrEmpty(_activeNucleusKey))
                NucleusClient.SetActive(BuildHttpBaseUrl(), _activeNucleusKey, null, null);

            _visible = false;
            _bootstrap.OnServerConfigConfirmed(_host.Trim(), grpc, http);
        }

        // ─── Nucleus server selection ────────────────────────────────────────

        private string BuildHttpBaseUrl()
        {
            var host = (_host ?? "").Trim();
            if (!int.TryParse(_httpPort, out var port) || port <= 0) port = defaultHttpPort;
            return $"http://{host}:{port}";
        }

        private void LoadNucleusList()
        {
            if (string.IsNullOrWhiteSpace(_host))
            {
                _nucleusStatus = "";
                return;
            }

            _nucleusBusy = true;
            _nucleusStatus = "Loading Nucleus servers...";
            NucleusClient.FetchList(
                BuildHttpBaseUrl(),
                resp =>
                {
                    _nucleusEndpoints = resp.endpoints ?? System.Array.Empty<NucleusEndpointDto>();
                    // Seed the selection from the server's active one if we don't
                    // have a saved/explicit choice yet.
                    if (string.IsNullOrEmpty(_activeNucleusKey))
                        _activeNucleusKey = resp.active ?? "";
                    // Show the active Nucleus immediately (e.g. "Active Nucleus: BTU")
                    // so the operator sees the default without having to tap a tab.
                    _nucleusStatus = string.IsNullOrEmpty(_activeNucleusKey)
                        ? ""
                        : $"Active Nucleus: {NameForKey(_activeNucleusKey)}";
                    _nucleusBusy = false;
                },
                err =>
                {
                    _nucleusStatus = $"Nucleus list unavailable: {err}";
                    _nucleusBusy = false;
                });
        }

        private void DrawNucleusSelector()
        {
            GUILayout.Label("Nucleus Server");

            GUILayout.BeginHorizontal();
            if (_nucleusEndpoints != null && _nucleusEndpoints.Length > 0)
            {
                foreach (var ep in _nucleusEndpoints)
                {
                    var isActive = ep.key == _activeNucleusKey;
                    var prevBg = GUI.backgroundColor;
                    if (isActive) GUI.backgroundColor = new Color(0.35f, 0.75f, 1f, 1f);

                    var label = string.IsNullOrEmpty(ep.name) ? ep.key : ep.name;
                    if (GUILayout.Button(isActive ? $"● {label}" : label, GUILayout.Height(ImguiTheme.ControlHeight)))
                        SelectNucleus(ep.key);

                    GUI.backgroundColor = prevBg;
                }
            }
            else
            {
                GUILayout.Label(_nucleusBusy ? "Loading..." : "(unavailable — check server IP)");
            }
            GUILayout.EndHorizontal();

            GUI.enabled = !_nucleusBusy && !string.IsNullOrWhiteSpace(_host);
            if (GUILayout.Button("Reload Nucleus list", GUILayout.Height(ImguiTheme.ControlHeight * 0.8f)))
                LoadNucleusList();
            GUI.enabled = true;

            if (!string.IsNullOrEmpty(_nucleusStatus))
                GUILayout.Label(_nucleusStatus);
        }

        private void SelectNucleus(string key)
        {
            _activeNucleusKey = key;
            PlayerPrefs.SetString(PrefNucleusKey, key);
            PlayerPrefs.Save();

            _nucleusBusy = true;
            _nucleusStatus = $"Switching to {NameForKey(key)}...";
            NucleusClient.SetActive(
                BuildHttpBaseUrl(),
                key,
                _ =>
                {
                    _nucleusStatus = $"Active Nucleus: {NameForKey(key)}";
                    _nucleusBusy = false;
                },
                err =>
                {
                    _nucleusStatus = $"Switch failed: {err}";
                    _nucleusBusy = false;
                });
        }

        private string NameForKey(string key)
        {
            if (_nucleusEndpoints != null)
                foreach (var ep in _nucleusEndpoints)
                    if (ep.key == key)
                        return string.IsNullOrEmpty(ep.name) ? ep.key : ep.name;
            return key;
        }

        private void OnDestroy()
        {
            _discoveryCts?.Cancel();
            _discoveryCts?.Dispose();
        }
    }
}
