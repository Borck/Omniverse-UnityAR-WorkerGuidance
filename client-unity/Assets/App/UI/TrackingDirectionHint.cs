using UnityEngine;

namespace Guidance.Runtime
{
    /// <summary>
    /// Displays a directional hint while tracking is lost.
    /// </summary>
    public sealed class TrackingDirectionHint : MonoBehaviour
    {
        [SerializeField] private bool visible = true;
        [SerializeField] private float minAngleToDisplay = 8f;

        private bool _hasHint;
        private float _signedAngleDeg;

        public void SetHint(float signedAngleDeg, bool hasHint)
        {
            _signedAngleDeg = signedAngleDeg;
            _hasHint = hasHint;
        }

        private void OnGUI()
        {
            if (!visible || !_hasHint)
            {
                return;
            }

            if (Mathf.Abs(_signedAngleDeg) < minAngleToDisplay)
            {
                return;
            }

            var direction = _signedAngleDeg > 0f ? "rechts" : "links";
            var magnitude = Mathf.RoundToInt(Mathf.Abs(_signedAngleDeg));
            GUILayout.BeginArea(new Rect(16, 148, 380, 60), GUI.skin.box);
            GUILayout.Label($"Tracking verloren: bitte {direction} drehen ({magnitude} Grad)");
            GUILayout.EndArea();
        }
    }
}
