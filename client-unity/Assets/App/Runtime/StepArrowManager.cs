using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace App.Runtime
{
    public enum ArrowType { Round, Straight }

    [Serializable]
    public class ArrowPlacement
    {
        public ArrowType arrowType = ArrowType.Round;
        public bool animated = true;
        public Vector3 localPosition;
        public Vector3 localEulerAngles;
        public Vector3 localScale = Vector3.one;
    }

    [Serializable]
    public class StepArrowEntry
    {
        [Tooltip("Step number this applies to (1 = first step). Matched against the number parsed " +
                 "from the runtime stepId, so it survives stepId renames as long as the numbering is stable.")]
        public int stepNumber = 1;

        [Tooltip("Optional human-readable note (e.g. 'bottom plate'). Not used for matching.")]
        public string note;

        public ArrowPlacement[] arrows;
    }

    /// <summary>
    /// Spawns directional arrows as children of the model target each time a step activates.
    /// Entries are keyed by <see cref="StepArrowEntry.stepNumber"/> — the number is parsed out of
    /// the runtime stepId, so renaming a step (e.g. "step-001" → "step-001-bottomplate-v2") does
    /// not break the mapping as long as the leading number stays the same.
    /// Arrow prefabs should point in local +Z (forward).
    /// </summary>
    public class StepArrowManager : MonoBehaviour
    {
        [Header("Arrow Prefabs")]
        [SerializeField] private GameObject roundArrowPrefab;
        [SerializeField] private GameObject straightArrowPrefab;

        [Header("Per-step configuration")]
        [SerializeField] private List<StepArrowEntry> stepArrows = new();

        [Header("Authoring")]
        [Tooltip("Editor-only: while playing, continuously re-apply the Inspector position/rotation/scale " +
                 "to spawned arrows so you can tune placement live. Turn off for normal runs.")]
        [SerializeField] private bool livePreview = true;

        private readonly List<GameObject> _active = new();
        // Parallel to _active: the placement each spawned arrow was created from (for live preview).
        private readonly List<ArrowPlacement> _activePlacements = new();

        public void ShowArrowsForStep(string stepId, Transform modelTargetRoot)
        {
            ClearArrows();
            if (modelTargetRoot == null)
            {
                Debug.LogWarning($"[StepArrowManager] ShowArrowsForStep('{stepId}') skipped: modelTargetRoot is null.");
                return;
            }

            int number = ParseStepNumber(stepId);
            if (number < 0)
            {
                Debug.LogWarning($"[StepArrowManager] Could not parse a step number out of stepId '{stepId}'. No arrows shown.");
                return;
            }

            var entry = stepArrows.Find(e => e.stepNumber == number);
            if (entry?.arrows == null || entry.arrows.Length == 0)
            {
                Debug.LogWarning($"[StepArrowManager] No arrow entry configured for step number {number} (from stepId '{stepId}'). Configured steps: [{string.Join(", ", stepArrows.ConvertAll(e => e.stepNumber.ToString()))}]");
                return;
            }

            Debug.Log($"[StepArrowManager] Spawning {entry.arrows.Length} arrow(s) for step {number} ('{stepId}') under '{modelTargetRoot.name}'.");

            foreach (var p in entry.arrows)
            {
                var prefab = p.arrowType == ArrowType.Round ? roundArrowPrefab : straightArrowPrefab;
                if (prefab == null)
                {
                    Debug.LogWarning($"[StepArrowManager] {p.arrowType} arrow prefab is not assigned — skipping.");
                    continue;
                }

                var arrow = Instantiate(prefab, modelTargetRoot);
                arrow.transform.localPosition = p.localPosition;
                arrow.transform.localEulerAngles = p.localEulerAngles;
                arrow.transform.localScale = p.localScale;

                if (p.animated)
                {
                    // Procedural motion — independent of whether the FBX shipped animation data.
                    var bob = arrow.GetComponent<ArrowBob>();
                    if (bob == null) bob = arrow.AddComponent<ArrowBob>();
                    bob.BaseLocalPosition = p.localPosition;
                    bob.CaptureBaseScale(p.localScale);
                }
                else if (arrow.TryGetComponent<Animator>(out var anim))
                {
                    anim.enabled = false;
                }

                _active.Add(arrow);
                _activePlacements.Add(p);
            }
        }

        /// <summary>
        /// Extracts the first run of digits from a stepId. "step-001" → 1, "step-002-plate" → 2.
        /// Returns -1 when no number is present.
        /// </summary>
        private static int ParseStepNumber(string stepId)
        {
            if (string.IsNullOrEmpty(stepId)) return -1;
            var match = Regex.Match(stepId, @"\d+");
            return match.Success && int.TryParse(match.Value, out var n) ? n : -1;
        }

        public void ClearArrows()
        {
            foreach (var a in _active)
                if (a != null) Destroy(a);
            _active.Clear();
            _activePlacements.Clear();
        }

        public void SetVisible(bool visible)
        {
            foreach (var a in _active)
                if (a != null) a.SetActive(visible);
        }

#if UNITY_EDITOR
        // Live authoring: while playing in the editor, keep re-applying the configured
        // transform so tweaking the Inspector numbers moves the arrow in real time.
        private void Update()
        {
            if (!livePreview) return;
            for (int i = 0; i < _active.Count; i++)
            {
                var a = _active[i];
                if (a == null) continue;
                var p = _activePlacements[i];
                a.transform.localEulerAngles = p.localEulerAngles;

                // If the arrow bobs, feed the new base to it (so it keeps animating around the
                // tuned spot); otherwise drive the transform directly.
                if (a.TryGetComponent<ArrowBob>(out var bob))
                {
                    bob.BaseLocalPosition = p.localPosition;
                    bob.CaptureBaseScale(p.localScale);
                }
                else
                {
                    a.transform.localPosition = p.localPosition;
                    a.transform.localScale = p.localScale;
                }
            }
        }

        // Tuned base placement for step 1 (confirmed sitting on the fixture). Steps 2/3 nudge
        // sideways from this; 4-6 reuse it centred.
        private static ArrowPlacement BasePlacement() => new ArrowPlacement
        {
            arrowType = ArrowType.Straight,
            animated = true,
            localPosition = new Vector3(0f, 0.05f, 0f),
            localEulerAngles = new Vector3(350.34f, 329.40f, 187.90f),
            localScale = new Vector3(1.333f, 1.333f, 1.333f),
        };

        [ContextMenu("Configure steps 1-6")]
        private void ConfigureSteps()
        {
            // How far steps 2/3 shift sideways from centre, in fixture-local units (~metres).
            // 0.1 ≈ 4 inches. Flip the sign if right/left come out reversed for your fixture.
            const float sideShift = 0.1f;

            ArrowPlacement Shifted(float dx)
            {
                var p = BasePlacement();
                p.localPosition += new Vector3(dx, 0f, 0f);
                return p;
            }

            stepArrows = new List<StepArrowEntry>
            {
                new StepArrowEntry { stepNumber = 1, note = "centre",            arrows = new[] { BasePlacement() } },
                new StepArrowEntry { stepNumber = 2, note = "shifted right",     arrows = new[] { Shifted(+sideShift) } },
                new StepArrowEntry { stepNumber = 3, note = "shifted left",      arrows = new[] { Shifted(-sideShift) } },
                new StepArrowEntry { stepNumber = 4, note = "centre",            arrows = new[] { BasePlacement() } },
                new StepArrowEntry { stepNumber = 5, note = "centre",            arrows = new[] { BasePlacement() } },
                new StepArrowEntry { stepNumber = 6, note = "centre",            arrows = new[] { BasePlacement() } },
            };
            UnityEditor.EditorUtility.SetDirty(this);
        }
#endif
    }
}
