using UnityEngine;

namespace Guidance.Runtime
{
    /// <summary>
    /// Branded intro overlay shown at app start. Centered logo + italic tagline,
    /// over a soft vertical-gradient background. Immediate-mode (matches the HUD):
    /// eases in, holds, fades out, then disables itself. This is the *app* splash —
    /// it appears after the Unity engine splash (removed separately in Player
    /// Settings, which needs a Unity Pro/Plus license).
    /// </summary>
    public sealed class SplashScreen : MonoBehaviour
    {
        [Header("Text")]
        [Tooltip("Show the title text. Off by default — the logo already carries the name.")]
        [SerializeField] private bool showTitle = false;
        [SerializeField] private string title = "Project Direkt";
        [SerializeField] private string subtitle = "Made by BTU";

        [Header("Timing (seconds)")]
        [SerializeField] private float fadeInSeconds = 0.8f;
        [SerializeField] private float displaySeconds = 2.5f;
        [SerializeField] private float fadeOutSeconds = 0.8f;

        [Header("Style")]
        [Tooltip("Top of the background gradient (lighter looks more premium).")]
        [SerializeField] private Color backgroundTopColor = new Color(0.14f, 0.16f, 0.20f, 1f);
        [Tooltip("Bottom of the background gradient.")]
        [SerializeField] private Color backgroundBottomColor = new Color(0.05f, 0.06f, 0.08f, 1f);
        [SerializeField] private Color titleColor = Color.white;
        [Tooltip("Accent used for the subtitle and the divider line.")]
        [SerializeField] private Color accentColor = new Color(0.35f, 0.75f, 1f, 1f);
        [Tooltip("Logo drawn in the center. Use a PNG with a TRANSPARENT background (Texture Type 'Default', Alpha Is Transparency on) so it blends into the gradient.")]
        [SerializeField] private Texture2D logo;
        [Tooltip("Logo height as a fraction of screen height.")]
        [Range(0.1f, 0.95f)]
        [SerializeField] private float logoHeightFraction = 0.7f;
        [Tooltip("Show ONLY the centered logo (no divider/title/subtitle). Use when the text is baked into the image.")]
        [SerializeField] private bool logoOnly = true;

        private float _elapsed;
        private bool _done;
        private Texture2D _solid;
        private Texture2D _gradient;

        private void Awake()
        {
            _solid = new Texture2D(1, 1);
            _solid.SetPixel(0, 0, Color.white);
            _solid.Apply();

            _gradient = BuildVerticalGradient(backgroundTopColor, backgroundBottomColor);
        }

        private void Update()
        {
            if (_done) return;
            _elapsed += Time.unscaledDeltaTime;   // unaffected by timeScale/pause
            if (_elapsed >= fadeInSeconds + displaySeconds + fadeOutSeconds)
            {
                _done = true;
                gameObject.SetActive(false);
            }
        }

        private void OnGUI()
        {
            if (_done) return;

            GUI.depth = -1000;                    // draw on top of the HUD
            float alpha = ComputeAlpha();
            float intro = EaseOutCubic(Mathf.Clamp01(_elapsed / Mathf.Max(0.0001f, fadeInSeconds)));
            float w = Screen.width;
            float h = Screen.height;
            float cx = w * 0.5f;
            var prev = GUI.color;

            // Gradient background.
            GUI.color = new Color(1f, 1f, 1f, alpha);
            GUI.DrawTexture(new Rect(0, 0, w, h), _gradient, ScaleMode.StretchToFill);

            // Centered group eases up + scales in slightly.
            float rise = (1f - intro) * h * 0.03f;
            float scale = Mathf.Lerp(0.94f, 1f, intro);

            // Logo-only mode: just the centered image (text is baked into it).
            if (logoOnly && logo != null)
            {
                float s = h * logoHeightFraction * scale;
                GUI.color = new Color(1f, 1f, 1f, alpha);
                GUI.DrawTexture(new Rect(cx - s * 0.5f, h * 0.5f - s * 0.5f - rise, s, s), logo, ScaleMode.ScaleToFit);
                GUI.color = prev;
                return;
            }

            float heroCenterY = h * 0.42f - rise;
            float dividerY;

            if (logo != null)
            {
                float s = h * logoHeightFraction * scale;
                GUI.color = new Color(1f, 1f, 1f, alpha);
                GUI.DrawTexture(new Rect(cx - s * 0.5f, heroCenterY - s * 0.5f, s, s), logo, ScaleMode.ScaleToFit);
                dividerY = heroCenterY + s * 0.5f + h * 0.045f;
            }
            else if (showTitle)
            {
                var titleStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = Mathf.RoundToInt(h * 0.075f * scale),
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter,
                    wordWrap = false,
                };
                var titleRect = new Rect(0, heroCenterY - h * 0.06f, w, h * 0.12f);
                DrawWithShadow(titleRect, title, titleStyle, titleColor, alpha);
                dividerY = heroCenterY + h * 0.07f;
            }
            else
            {
                dividerY = heroCenterY + h * 0.06f;
            }

            // Optional title under the logo (only if both a logo and showTitle are set).
            if (logo != null && showTitle)
            {
                var underStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = Mathf.RoundToInt(h * 0.055f),
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter,
                    wordWrap = false,
                };
                DrawWithShadow(new Rect(0, dividerY, w, h * 0.09f), title, underStyle, titleColor, alpha);
                dividerY += h * 0.09f;
            }

            // Accent divider.
            float lineW = w * 0.16f;
            GUI.color = new Color(accentColor.r, accentColor.g, accentColor.b, alpha * 0.9f);
            GUI.DrawTexture(new Rect(cx - lineW * 0.5f, dividerY, lineW, Mathf.Max(2f, h * 0.003f)), _solid);

            // Italic metallic subtitle (chrome look, matching the logo).
            var subStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.RoundToInt(h * 0.038f),
                fontStyle = FontStyle.Italic,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = false,
            };
            DrawMetallicText(new Rect(0, dividerY + h * 0.018f, w, h * 0.08f), subtitle, subStyle, alpha);

            GUI.color = prev;
        }

        private float ComputeAlpha()
        {
            if (_elapsed < fadeInSeconds)
                return EaseOutCubic(Mathf.Clamp01(_elapsed / Mathf.Max(0.0001f, fadeInSeconds)));
            if (_elapsed < fadeInSeconds + displaySeconds)
                return 1f;
            float t = _elapsed - fadeInSeconds - displaySeconds;
            return 1f - EaseOutCubic(Mathf.Clamp01(t / Mathf.Max(0.0001f, fadeOutSeconds)));
        }

        private static float EaseOutCubic(float x) => 1f - Mathf.Pow(1f - x, 3f);

        private void DrawWithShadow(Rect rect, string text, GUIStyle style, Color color, float alpha)
        {
            float off = Mathf.Max(1.5f, Screen.height * 0.0025f);
            GUI.color = new Color(0f, 0f, 0f, 0.55f * alpha);
            GUI.Label(new Rect(rect.x + off, rect.y + off, rect.width, rect.height), text, style);
            GUI.color = new Color(color.r, color.g, color.b, alpha);
            GUI.Label(rect, text, style);
        }

        // Faux brushed-metal text: layered labels create a top-lit chrome look —
        // drop shadow, dark lower edge, mid-silver base, bright top highlight.
        private static readonly Color _metalShadow = new Color(0f, 0f, 0f, 0.6f);
        private static readonly Color _metalDark = new Color(0.40f, 0.42f, 0.47f, 1f);
        private static readonly Color _metalBase = new Color(0.74f, 0.77f, 0.82f, 1f);
        private static readonly Color _metalHighlight = new Color(0.96f, 0.98f, 1f, 1f);

        private void DrawMetallicText(Rect rect, string text, GUIStyle style, float alpha)
        {
            float s = Mathf.Max(1f, Screen.height * 0.0016f);

            GUI.color = new Color(_metalShadow.r, _metalShadow.g, _metalShadow.b, _metalShadow.a * alpha);
            GUI.Label(new Rect(rect.x + s * 2f, rect.y + s * 2f, rect.width, rect.height), text, style);

            GUI.color = new Color(_metalDark.r, _metalDark.g, _metalDark.b, alpha);
            GUI.Label(new Rect(rect.x, rect.y + s, rect.width, rect.height), text, style);

            GUI.color = new Color(_metalBase.r, _metalBase.g, _metalBase.b, alpha);
            GUI.Label(rect, text, style);

            GUI.color = new Color(_metalHighlight.r, _metalHighlight.g, _metalHighlight.b, alpha * 0.9f);
            GUI.Label(new Rect(rect.x, rect.y - s, rect.width, rect.height), text, style);
        }

        private static Texture2D BuildVerticalGradient(Color top, Color bottom)
        {
            const int height = 256;
            var tex = new Texture2D(1, height) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < height; y++)
            {
                // Row 0 renders at the bottom of the screen, so bottom color at y=0.
                float t = y / (float)(height - 1);
                tex.SetPixel(0, y, Color.Lerp(bottom, top, t));
            }
            tex.Apply();
            return tex;
        }

        private void OnDestroy()
        {
            if (_solid != null) Destroy(_solid);
            if (_gradient != null) Destroy(_gradient);
        }
    }
}
