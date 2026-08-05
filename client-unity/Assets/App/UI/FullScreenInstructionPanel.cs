using UnityEngine;

namespace Guidance.Runtime
{
    /// <summary>
    /// Full-screen, centered instruction overlay for the M4000 waveguide. Unlike
    /// the <see cref="SessionStatusPanel"/> (which lives inside the minimisable
    /// left-edge <see cref="ControlDrawer"/>), this draws the current step's
    /// instruction as large, word-wrapped text auto-sized to the biggest font that
    /// still fits a centred region of the screen — so the worker can read it with
    /// the control drawer collapsed.
    ///
    /// It only holds text + visibility; AppBootstrap decides WHEN it is shown:
    ///   • Preparation steps (text only)  -> shown continuously until the next step.
    ///   • Fitting steps (text + anim)    -> shown a few seconds, then hidden while
    ///     the animation plays, then shown again (see AppBootstrap's fitting cycle).
    ///
    /// Created and wired at runtime by AppBootstrap (no scene wiring needed), like
    /// the ControlDrawer.
    /// </summary>
    public sealed class FullScreenInstructionPanel : MonoBehaviour
    {
        [Tooltip("Fraction of the screen width the centred text block may use.")]
        [SerializeField, Range(0.1f, 1f)] private float widthFraction = 0.86f;
        [Tooltip("Fraction of the screen height the centred text block may use.")]
        [SerializeField, Range(0.1f, 1f)] private float heightFraction = 0.6f;
        [Tooltip("Largest font size to try. Auto-fit never exceeds this.")]
        [SerializeField] private int maxFontSize = 64;
        [Tooltip("Smallest font size to fall back to when text is very long.")]
        [SerializeField] private int minFontSize = 18;
        [Tooltip("Text colour. A warm/bright colour reads best on the additive waveguide (dark pixels are invisible there).")]
        [SerializeField] private Color textColor = new Color(1f, 0.92f, 0.35f);
        [Tooltip("Draw a dimmed backdrop behind the text (usually off for see-through glasses).")]
        [SerializeField] private bool dimBackground = false;
        [SerializeField, Range(0f, 1f)] private float backgroundDim = 0.35f;
        [Tooltip("Drawn on top of the control drawer when lower than the drawer's depth (IMGUI: lower depth = closer to the viewer).")]
        [SerializeField] private int guiDepth = -50;

        private string _text = string.Empty;
        private bool _visible;

        private GUIStyle _style;
        private Texture2D _bgTex;

        // Auto-fit cache: recompute the fitted font only when the text or the
        // screen dimensions change (CalcHeight over the font range is cheap but
        // there's no reason to run it every OnGUI pass).
        private string _fitText;
        private int _fitScreenW, _fitScreenH, _fitFont;

        public bool IsVisible => _visible;

        public void SetText(string text)
        {
            var t = text ?? string.Empty;
            if (t == _text) return;
            _text = t;
            _fitText = null; // invalidate fit cache
        }

        public void SetVisible(bool visible) => _visible = visible;
        public void Show() => _visible = true;
        public void Hide() => _visible = false;

        /// <summary>Convenience: set the text and show it in one call.</summary>
        public void ShowText(string text)
        {
            SetText(text);
            _visible = true;
        }

        private void OnGUI()
        {
            if (!_visible || string.IsNullOrEmpty(_text)) return;

            GUI.depth = guiDepth;
            EnsureStyle();

            float w = Screen.width * Mathf.Clamp01(widthFraction);
            float h = Screen.height * Mathf.Clamp01(heightFraction);
            var rect = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);

            if (dimBackground)
            {
                EnsureBgTex();
                var prev = GUI.color;
                GUI.color = new Color(0f, 0f, 0f, backgroundDim);
                GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), _bgTex);
                GUI.color = prev;
            }

            _style.fontSize = ComputeFittedFont(w, h);
            GUI.Label(rect, _text, _style);
        }

        // Largest font (<= maxFontSize) whose wrapped height fits within h.
        private int ComputeFittedFont(float w, float h)
        {
            if (_fitText == _text && _fitScreenW == Screen.width && _fitScreenH == Screen.height && _fitFont > 0)
                return _fitFont;

            var content = new GUIContent(_text);
            int best = Mathf.Max(1, minFontSize);
            for (int size = Mathf.Max(minFontSize, maxFontSize); size >= minFontSize; size--)
            {
                _style.fontSize = size;
                if (_style.CalcHeight(content, w) <= h)
                {
                    best = size;
                    break;
                }
            }

            _fitText = _text;
            _fitScreenW = Screen.width;
            _fitScreenH = Screen.height;
            _fitFont = best;
            return best;
        }

        private void EnsureStyle()
        {
            if (_style != null) return;
            _style = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true,
                richText = true,
                fontStyle = FontStyle.Bold,
            };
            _style.normal.textColor = textColor;
        }

        private void EnsureBgTex()
        {
            if (_bgTex != null) return;
            _bgTex = new Texture2D(1, 1);
            _bgTex.SetPixel(0, 0, Color.white);
            _bgTex.Apply();
        }
    }
}
