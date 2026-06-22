using UnityEngine;

namespace Guidance.Runtime
{
    /// <summary>
    /// Centralised IMGUI sizing for the small Vuzix M4000 display.
    ///
    /// The whole runtime UI is immediate-mode (OnGUI/GUILayout), so there is no
    /// Canvas to scale. Earlier attempts used GUI.matrix scaling, but that
    /// stretches the font's existing glyph atlas and looks blurry. Instead we set
    /// a large NATIVE font size on the GUI skin so Unity rasterises the glyphs at
    /// that size directly -> crisp text. Panels enlarge their own rects to fit.
    ///
    /// Dials: <see cref="FontSize"/> (text size) and <see cref="ControlHeight"/>
    /// (button/field height). No matrix scaling, so layout uses the real screen.
    /// </summary>
    public static class ImguiTheme
    {
        // Native font size rasterised crisply by Unity. Raise for bigger text.
        public const int FontSize = 30;

        // Height for buttons / text fields so they fit the larger font.
        public const float ControlHeight = 52f;

        // No matrix scaling -> virtual screen == real screen.
        public static float VirtualWidth  => Screen.width;
        public static float VirtualHeight => Screen.height;

        /// <summary>Call at the very top of OnGUI (after the visibility guard).</summary>
        public static void Begin()
        {
            var skin = GUI.skin;

            skin.label.fontSize     = FontSize;
            skin.button.fontSize    = FontSize;
            skin.textField.fontSize = FontSize;
            skin.box.fontSize       = FontSize;
            skin.toggle.fontSize    = FontSize;

            skin.label.richText  = true;
            skin.box.richText    = true;
            skin.button.richText = true;

            // A little padding so larger glyphs aren't cramped in their controls.
            skin.button.padding    = new RectOffset(12, 12, 8, 8);
            skin.textField.padding = new RectOffset(10, 10, 8, 8);
        }

        /// <summary>Symmetry with Begin(); nothing to restore now.</summary>
        public static void End() { }
    }
}
