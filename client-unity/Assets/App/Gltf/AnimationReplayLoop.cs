using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Guidance.Runtime
{
    /// <summary>
    /// Drives a legacy Animation component by MANUAL SAMPLING so the play / hold
    /// / replay cycle is fully deterministic.
    ///
    /// Why manual: relying on Animation.Play() + wrapMode + WaitForSeconds let
    /// Unity keep auto-advancing the clip during the "hold" wait, so the part
    /// never actually froze on its last frame. Here we never call Play(): we set
    /// the clip time ourselves and call Animation.Sample() every frame. During
    /// the hold we re-pin the final frame each frame, so nothing can drift it.
    ///
    /// Cycle: play once at <c>speed</c> -> hold last frame for <c>delaySeconds</c>
    /// -> rewind -> repeat.
    /// </summary>
    public sealed class AnimationReplayLoop : MonoBehaviour
    {
        private Animation _anim;
        private float _delaySeconds;
        private float _speed;
        private Coroutine _loop;

        public void Initialize(Animation anim, float delaySeconds, float speed)
        {
            _anim = anim;
            _delaySeconds = delaySeconds;
            _speed = Mathf.Max(0.01f, speed);
            TryStartLoop();
        }

        // Called from Initialize() AND OnEnable(). Initialize fails silently for
        // the very first GLB load -- AppBootstrap SetActive(false)'s the
        // AnimationRoot until Vuforia acquires the fixture, and StartCoroutine
        // on an inactive GameObject does nothing. OnEnable picks it up once the
        // parent is activated by tracking.
        private void TryStartLoop()
        {
            if (_anim == null || _loop != null) return;
            if (!isActiveAndEnabled) return;
            _loop = StartCoroutine(LoopRoutine());
        }

        private void OnEnable()
        {
            TryStartLoop();
        }

        private IEnumerator LoopRoutine()
        {
            if (_anim == null) yield break;

            // Take manual control: stop any auto-play so the only thing moving
            // the clip is our Sample() calls below.
            _anim.Stop();
            _anim.playAutomatically = false;

            var states = new List<AnimationState>();
            float maxLen = 0f;
            foreach (AnimationState s in _anim)
            {
                // Force ClampForever on the STATE (not just the component): the
                // clip's own wrapMode is usually Loop, and sampling at time ==
                // length under Loop wraps back to frame 0 -- which made the hold
                // freeze on the FIRST frame instead of the last. ClampForever
                // makes sampling at the end clamp to the final frame.
                s.wrapMode = WrapMode.ClampForever;
                states.Add(s);
                if (s.length > maxLen) maxLen = s.length;
            }

            if (states.Count == 0 || maxLen <= 0f)
            {
                Debug.LogWarning($"[AnimationReplayLoop] {name}: no playable clips (states={states.Count}, maxLen={maxLen:F3}s); aborting.");
                yield break;
            }

            Debug.Log($"[AnimationReplayLoop] {name}: clip={maxLen:F2}s, speed={_speed:F2}x, delay={_delaySeconds:F1}s (manual-sample)");

            while (_anim != null)
            {
                // ---- play phase: advance time ourselves at _speed ----
                float t = 0f;
                while (_anim != null && t < maxLen)
                {
                    Pin(states, t);
                    t += Time.deltaTime * _speed;
                    yield return null;
                }
                if (_anim == null) yield break;

                // ---- hold phase: re-pin the LAST frame every frame for the
                // delay, so Unity cannot auto-advance it back into motion ----
                float held = 0f;
                while (_anim != null && held < _delaySeconds)
                {
                    Pin(states, maxLen);
                    held += Time.deltaTime;
                    yield return null;
                }
            }
        }

        // Sample every clip at the given (per-clip clamped) time.
        private void Pin(List<AnimationState> states, float time)
        {
            foreach (var s in states)
            {
                s.enabled = true;
                s.weight = 1f;
                s.time = Mathf.Min(time, s.length);
            }
            _anim.Sample();
        }

        private void OnDisable()
        {
            if (_loop != null) StopCoroutine(_loop);
            _loop = null;
        }
    }
}
