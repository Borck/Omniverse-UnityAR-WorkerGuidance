using System;
using UnityEngine;
#if VUFORIA_ENGINE
using Vuforia;
#endif

namespace Guidance.Runtime
{
    /// <summary>
    /// Thin wrapper around Vuforia's built-in Barcode Scanner used to read the two
    /// connection QR codes (gRPC + FastAPI). Reuses the camera session Vuforia
    /// already owns for model-target tracking, so there is no camera contention
    /// (unlike a WebCamTexture + third-party decoder).
    ///
    /// NOTE: disabling Vuforia's video background does NOT stop the camera — the
    /// frames are still captured and processed, so barcode detection still works
    /// on optical-see-through glasses. Detection only needs the Vuforia engine to
    /// be running and the Barcode observer enabled.
    ///
    /// All Vuforia-specific code is behind <c>VUFORIA_ENGINE</c>. When Vuforia is
    /// not compiled in, <see cref="IsAvailable"/> is false and the component is an
    /// inert no-op — manual endpoint entry keeps working.
    ///
    /// One-time scene setup (in the Unity Editor):
    ///   1. GameObject → Vuforia Engine → Barcode  (adds a BarcodeBehaviour).
    ///   2. On that Barcode object, enable the "QR Code" observed type.
    ///   3. Assign that object to <see cref="barcode"/>, or leave it and this
    ///      component finds one in the scene at runtime.
    /// </summary>
    public sealed class QrEndpointScanner : MonoBehaviour
    {
        /// <summary>Raised with the raw decoded text every time a QR/barcode is observed.</summary>
        public event Action<string> BarcodeScanned;

        [Tooltip("Log per-scan diagnostics (status changes, decoded values) to the console. Off by default; the 'no Barcode object' / 'Vuforia not running' warnings always log regardless.")]
        [SerializeField] private bool verboseLogging = false;

#if VUFORIA_ENGINE
        [Tooltip("Scene Vuforia Barcode object (has a BarcodeBehaviour). If empty, one is found at runtime.")]
        [SerializeField] private BarcodeBehaviour barcode;
        private bool _subscribed;
        private bool _scanning;
        private string _lastEmitted;

        private void Awake()
        {
            if (barcode == null)
                barcode = FindFirstObjectByType<BarcodeBehaviour>();
        }
#endif

        /// <summary>True only when Vuforia is present and a BarcodeBehaviour is wired.</summary>
        public bool IsAvailable
        {
#if VUFORIA_ENGINE
            get => barcode != null;
#else
            get => false;
#endif
        }

        /// <summary>Start observing barcodes and forwarding their text via <see cref="BarcodeScanned"/>.</summary>
        public void BeginScan()
        {
#if VUFORIA_ENGINE
            if (barcode == null)
            {
                Debug.LogWarning("[QrEndpointScanner] BeginScan: no BarcodeBehaviour found. Add GameObject > Vuforia Engine > Barcode and enable the QR Code observed type.");
                return;
            }
            if (!_subscribed)
            {
                barcode.OnTargetStatusChanged += HandleTargetStatusChanged;
                _subscribed = true;
            }
            if (!barcode.gameObject.activeSelf) barcode.gameObject.SetActive(true);
            barcode.enabled = true;
            _scanning = true;
            _lastEmitted = null;

            bool engineRunning = VuforiaApplication.Instance != null && VuforiaApplication.Instance.IsRunning;
            if (verboseLogging)
                Debug.Log($"[QrEndpointScanner] BeginScan: barcode='{barcode.name}' active={barcode.gameObject.activeInHierarchy} enabled={barcode.enabled} vuforiaRunning={engineRunning}");
            if (!engineRunning)
                Debug.LogWarning("[QrEndpointScanner] Vuforia engine is NOT running yet — the camera won't be processing frames, so nothing will be detected. Ensure the AR Camera / VuforiaBehaviour is active on the configuration screen.");
#else
            Debug.LogWarning("[QrEndpointScanner] BeginScan: VUFORIA_ENGINE is not defined in this build.");
#endif
        }

        /// <summary>Stop observing barcodes.</summary>
        public void EndScan()
        {
#if VUFORIA_ENGINE
            _scanning = false;
            if (barcode == null) return;
            if (_subscribed)
            {
                barcode.OnTargetStatusChanged -= HandleTargetStatusChanged;
                _subscribed = false;
            }
            barcode.enabled = false;
#endif
        }

#if VUFORIA_ENGINE
        // Robust read path. Vuforia can surface a detected barcode either via a
        // status change on the placed BarcodeBehaviour OR on an internally-cloned
        // instance behaviour. So while scanning we also poll every BarcodeBehaviour
        // in the scene each frame and read InstanceData.Text — this catches the
        // cloned-instance case where the status event on the placed object never
        // fires. Cheap because it only runs during the short scan window.
        private void Update()
        {
            if (!_scanning) return;
            var all = FindObjectsByType<BarcodeBehaviour>(FindObjectsSortMode.None);
            foreach (var bb in all)
                TryEmit(bb, "poll");
        }

        private void HandleTargetStatusChanged(ObserverBehaviour behaviour, TargetStatus status)
        {
            if (verboseLogging)
                Debug.Log($"[QrEndpointScanner] status changed: {status.Status}");
            TryEmit(behaviour as BarcodeBehaviour, "event");
        }

        private void TryEmit(BarcodeBehaviour bb, string source)
        {
            var data = bb != null ? bb.InstanceData : null;
            var text = data != null ? data.Text : null;
            if (string.IsNullOrEmpty(text) || text == _lastEmitted) return;

            _lastEmitted = text;
            if (verboseLogging) Debug.Log($"[QrEndpointScanner] decoded ({source}): {text}");
            BarcodeScanned?.Invoke(text);
        }

        private void OnDestroy() => EndScan();
#endif
    }
}
