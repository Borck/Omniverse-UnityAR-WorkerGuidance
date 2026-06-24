using System;
using UnityEngine;

namespace Guidance.Runtime
{
    /// <summary>
    /// Compact left-edge control drawer. Idle state is just three small arrow
    /// tabs (Status / FOV / Eye) so it barely uses screen space. Tapping a tab
    /// slides the matching panel in from the left; tapping it again (or its tab)
    /// slides it back out. One panel open at a time. Panel bodies are drawn by
    /// the corresponding panels' DrawContent().
    ///
    /// Created and wired at runtime by AppBootstrap (no scene wiring needed).
    /// </summary>
    public sealed class ControlDrawer : MonoBehaviour
    {
        private enum Section { None = -1, Status = 0, Fov = 1, Eye = 2 }

        // Small tab column on the left edge.
        private const float TabX = 8f;
        private const float TabTop = 12f;
        private const float TabWidth = 92f;
        private const float TabHeight = 70f;
        private const float TabGap = 6f;
        private const int TabFontSize = 18;

        // Sliding panel.
        private const float PanelWidth = 540f;
        private const float PanelTop = 12f;
        private const float SlideSeconds = 0.15f;

        private static readonly string[] TabLabels = { "Status", "FOV", "Eye" };
        private static readonly float[] PanelHeights = { 470f, 250f, 290f };
        private static float PanelOpenX => TabX + TabWidth + 6f;

        private SessionStatusPanel _status;
        private FovTunerPanel _fov;
        private EyeOffsetPanel _eye;

        private readonly float[] _anim = { 0f, 0f, 0f };
        private Section _open = Section.None;
        private GUIStyle _tabStyle;

        public void Bind(SessionStatusPanel status, FovTunerPanel fov, EyeOffsetPanel eye)
        {
            _status = status;
            _fov = fov;
            _eye = eye;
        }

        private void Update()
        {
            float step = Time.unscaledDeltaTime / Mathf.Max(0.01f, SlideSeconds);
            for (int i = 0; i < _anim.Length; i++)
            {
                float target = ((int)_open == i) ? 1f : 0f;
                _anim[i] = Mathf.MoveTowards(_anim[i], target, step);
            }
        }

        private void OnGUI()
        {
            ImguiTheme.Begin();
            EnsureTabStyle();

            DrawSlidingPanel(); // behind the tabs
            DrawTabs();         // on top, always visible

            ImguiTheme.End();
        }

        // Draws exactly one panel: the open one sliding in, or (if just closed)
        // the last one sliding out. Avoids overlap during a switch.
        private void DrawSlidingPanel()
        {
            int index = -1;
            if (_open != Section.None)
            {
                index = (int)_open;
            }
            else
            {
                float max = 0.001f;
                for (int i = 0; i < _anim.Length; i++)
                    if (_anim[i] > max) { max = _anim[i]; index = i; }
            }
            if (index < 0) return;

            Action draw = DrawerContentFor(index);
            if (draw == null) return;

            float a = _anim[index];
            float x = Mathf.Lerp(PanelOpenX - PanelWidth, PanelOpenX, a); // slide in from behind the tabs
            var rect = new Rect(x, PanelTop, PanelWidth, PanelHeights[index]);

            GUILayout.BeginArea(rect, GUI.skin.box);
            draw();
            GUILayout.EndArea();
        }

        private void DrawTabs()
        {
            for (int i = 0; i < 3; i++)
            {
                var r = new Rect(TabX, TabTop + i * (TabHeight + TabGap), TabWidth, TabHeight);
                bool isOpen = (int)_open == i;
                string arrow = isOpen ? "◀" : "▶";
                if (GUI.Button(r, $"{arrow}\n{TabLabels[i]}", _tabStyle))
                {
                    if (isOpen)
                    {
                        _open = Section.None;
                    }
                    else
                    {
                        _open = (Section)i;
                        if (i == (int)Section.Eye) _eye?.ResetGuide();
                    }
                }
            }
        }

        private Action DrawerContentFor(int index)
        {
            switch (index)
            {
                case (int)Section.Status: return _status != null ? (Action)_status.DrawContent : null;
                case (int)Section.Fov:    return _fov    != null ? (Action)_fov.DrawContent    : null;
                case (int)Section.Eye:    return _eye    != null ? (Action)_eye.DrawContent    : null;
                default: return null;
            }
        }

        private void EnsureTabStyle()
        {
            if (_tabStyle != null) return;
            _tabStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = TabFontSize,
                alignment = TextAnchor.MiddleCenter,
                richText = true,
                wordWrap = true,
            };
        }
    }
}
