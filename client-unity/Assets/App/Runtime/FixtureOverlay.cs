using System.Collections;
using UnityEngine;

#if VUFORIA_ENGINE
using Vuforia;
#endif

namespace Guidance.Runtime
{
    /// <summary>
    /// Attaches a static fixture overlay (real-textured 3D model of the tracked machine)
    /// to a Vuforia ObserverBehaviour. Plays a slice-plane reveal animation when the
    /// target becomes tracked, reverses it on tracking loss with debounce.
    /// </summary>
    public sealed class FixtureOverlay : MonoBehaviour
    {
        [Tooltip("Uniform scale applied to the fixture overlay prefab when instantiated. Use 1 if the OBJ is already at real-world scale, smaller if it was modeled larger than the physical fixture.")]
        [SerializeField] private float overlayScale = 1f;

        private const float RevealDuration = 0.7f;
        private const float HideDebounceSeconds = 0.3f;
        private const string RevealShaderName = "FixtureReveal";
        private const string HologramShaderName = "Hologram";
        private const float InitialGlowIntensity = 2.5f;
        private static Material _steadyHologramMaterial;

        private GameObject _instance;
        private Renderer[] _renderers;
        private Material[][] _originalMaterials;
        private Material[][] _activeRevealMaterials;
        private Material _revealShaderTemplate;
        private Coroutine _animation;
        private bool _isVisible;
        private bool _enabled = true;

        /// <summary>
        /// Master switch — when false, the overlay stays hidden regardless of tracking.
        /// Driven by the HUD checkbox in AppBootstrap.
        /// </summary>
        public bool OverlayEnabled
        {
            get => _enabled;
            set
            {
                if (_enabled == value) return;
                _enabled = value;
                if (!_enabled)
                {
                    if (_animation != null) StopCoroutine(_animation);
                    _animation = null;
                    _isVisible = false;
                    if (_instance != null) _instance.SetActive(false);
                }
#if VUFORIA_ENGINE
                else if (_observer != null)
                {
                    UpdateVisibility(_observer.TargetStatus.Status);
                }
#endif
            }
        }

#if VUFORIA_ENGINE
        private ObserverBehaviour _observer;

        public void Initialize(GameObject prefab, ObserverBehaviour observer, Transform parentOverride = null)
        {
            if (prefab == null || observer == null) return;

            _observer = observer;
            var parent = parentOverride != null ? parentOverride : observer.transform;
            _instance = Object.Instantiate(prefab, parent);
            _instance.name = "FixtureOverlay";
            _instance.transform.localPosition = Vector3.zero;
            _instance.transform.localRotation = Quaternion.identity;
            _instance.transform.localScale = Vector3.one * overlayScale;
            _instance.SetActive(false);

            _renderers = _instance.GetComponentsInChildren<Renderer>(includeInactive: true);
            _originalMaterials = new Material[_renderers.Length][];
            for (int i = 0; i < _renderers.Length; i++)
            {
                _originalMaterials[i] = _renderers[i].sharedMaterials;
            }

            var shader = Resources.Load<Shader>(RevealShaderName);
            if (shader == null)
            {
                Debug.LogWarning($"[FixtureOverlay] Shader '{RevealShaderName}' not found in Resources. Reveal animation disabled — fixture will pop instantly.");
            }
            else
            {
                _revealShaderTemplate = new Material(shader) { name = "FixtureRevealTemplate" };
            }

            _observer.OnTargetStatusChanged += OnStatusChanged;
            UpdateVisibility(_observer.TargetStatus.Status);
        }

        private void OnDestroy()
        {
            if (_observer != null) _observer.OnTargetStatusChanged -= OnStatusChanged;
        }

        private void OnStatusChanged(ObserverBehaviour behaviour, TargetStatus status)
        {
            UpdateVisibility(status.Status);
        }

        private void UpdateVisibility(Status status)
        {
            if (!_enabled) return;
            bool tracked = status == Status.TRACKED || status == Status.EXTENDED_TRACKED;

            if (tracked && !_isVisible)
            {
                _isVisible = true;
                if (_animation != null) StopCoroutine(_animation);
                _animation = StartCoroutine(RevealCoroutine());
            }
            else if (!tracked && _isVisible)
            {
                _isVisible = false;
                if (_animation != null) StopCoroutine(_animation);
                _animation = StartCoroutine(HideCoroutine());
            }
        }

        private IEnumerator RevealCoroutine()
        {
            if (_instance == null) yield break;

            _instance.SetActive(true);
            ApplyRevealMaterial();
            SetGlowIntensity(InitialGlowIntensity);

            var bounds = ComputeBounds();
            float startY = bounds.min.y - 0.001f;
            float endY = bounds.max.y + 0.001f;

            float t = 0f;
            while (t < RevealDuration)
            {
                t += Time.deltaTime;
                float p = Mathf.Clamp01(t / RevealDuration);
                SetRevealY(Mathf.Lerp(startY, endY, p));
                yield return null;
            }

            // Initial reveal complete. Swap to a steady hologram skin (same shader as
            // the animated parts but with no pulse/heartbeat — the fixture should look
            // calm and consistent, not flashing.
            ApplySteadyHologramMaterial();
            _animation = null;
        }

        private IEnumerator HideCoroutine()
        {
            if (_instance == null) yield break;

            // Debounce: Vuforia flickers between TRACKED ↔ NO_POSE often.
            // If we get re-tracked within the debounce window, abort the hide.
            float waited = 0f;
            while (waited < HideDebounceSeconds)
            {
                waited += Time.deltaTime;
                if (_isVisible) yield break;
                yield return null;
            }

            ApplyRevealMaterial();

            var bounds = ComputeBounds();
            float startY = bounds.max.y + 0.001f;
            float endY = bounds.min.y - 0.001f;

            float t = 0f;
            while (t < RevealDuration)
            {
                t += Time.deltaTime;
                float p = Mathf.Clamp01(t / RevealDuration);
                SetRevealY(Mathf.Lerp(startY, endY, p));
                if (_isVisible) yield break; // re-acquired mid-hide
                yield return null;
            }

            _instance.SetActive(false);
            _animation = null;
        }

        private Bounds ComputeBounds()
        {
            Bounds bounds = default;
            bool initialized = false;
            foreach (var r in _renderers)
            {
                if (r == null) continue;
                if (!initialized)
                {
                    bounds = r.bounds;
                    initialized = true;
                }
                else
                {
                    bounds.Encapsulate(r.bounds);
                }
            }
            if (!initialized)
            {
                bounds = new Bounds(_instance.transform.position, Vector3.one);
            }
            return bounds;
        }

        private void ApplyRevealMaterial()
        {
            if (_revealShaderTemplate == null) return;

            // Destroy previous reveal materials to prevent GPU memory accumulation across steps.
            if (_activeRevealMaterials != null)
            {
                foreach (var mats in _activeRevealMaterials)
                    foreach (var m in mats)
                        if (m != null) Object.Destroy(m);
            }

            _activeRevealMaterials = new Material[_renderers.Length][];
            for (int i = 0; i < _renderers.Length; i++)
            {
                var origs = _originalMaterials[i];
                var swapped = new Material[origs.Length];
                for (int j = 0; j < origs.Length; j++)
                {
                    var mat = new Material(_revealShaderTemplate);
                    if (origs[j] != null && origs[j].HasProperty("_Color"))
                    {
                        mat.SetColor("_BaseColor", origs[j].color);
                    }
                    swapped[j] = mat;
                }
                _activeRevealMaterials[i] = swapped;
                _renderers[i].sharedMaterials = swapped;
            }
        }

        private void SetRevealY(float y)
        {
            if (_activeRevealMaterials == null) return;
            foreach (var mats in _activeRevealMaterials)
            {
                foreach (var m in mats)
                {
                    if (m != null) m.SetFloat("_RevealY", y);
                }
            }
        }

        private void SetGlowIntensity(float intensity)
        {
            if (_activeRevealMaterials == null) return;
            foreach (var mats in _activeRevealMaterials)
            {
                foreach (var m in mats)
                {
                    if (m != null) m.SetFloat("_GlowIntensity", intensity);
                }
            }
        }

        private void ApplySteadyHologramMaterial()
        {
            var material = GetSteadyHologramMaterial();
            if (material == null) return;

            for (int i = 0; i < _renderers.Length; i++)
            {
                int count = _renderers[i].sharedMaterials.Length;
                if (count == 0)
                {
                    _renderers[i].sharedMaterial = material;
                    continue;
                }

                var swapped = new Material[count];
                for (int j = 0; j < count; j++) swapped[j] = material;
                _renderers[i].sharedMaterials = swapped;
            }
            _activeRevealMaterials = null;
        }

        private static Material GetSteadyHologramMaterial()
        {
            if (_steadyHologramMaterial != null) return _steadyHologramMaterial;

            var shader = Resources.Load<Shader>(HologramShaderName);
            if (shader == null)
            {
                Debug.LogWarning($"[FixtureOverlay] Shader '{HologramShaderName}' not found in Resources.");
                return null;
            }

            _steadyHologramMaterial = new Material(shader) { name = "FixtureSteadyHologram" };
            // No pulse / no heartbeat — fixture stays calm and consistent.
            _steadyHologramMaterial.SetFloat("_PulseAmount", 0f);
            _steadyHologramMaterial.SetFloat("_PulseSpeed", 0f);

            // Distinct cool-cyan tint at lower opacity so the fixture overlay reads as
            // "the static reference object" vs the bright step animations. Set the two
            // common property names — Unity silently ignores the one the shader doesn't have.
            var fixtureTint = new Color(0.35f, 0.75f, 1.0f, 0.45f);
            if (_steadyHologramMaterial.HasProperty("_BaseColor"))
                _steadyHologramMaterial.SetColor("_BaseColor", fixtureTint);
            if (_steadyHologramMaterial.HasProperty("_Color"))
                _steadyHologramMaterial.SetColor("_Color", fixtureTint);
            // Lower the glow so it doesn't compete with the animated parts.
            if (_steadyHologramMaterial.HasProperty("_GlowIntensity"))
                _steadyHologramMaterial.SetFloat("_GlowIntensity", 0.8f);
            return _steadyHologramMaterial;
        }
#else
        public void Initialize(GameObject prefab, object observer)
        {
            Debug.LogWarning("[FixtureOverlay] Vuforia Engine not available — overlay disabled.");
        }
#endif
    }
}
