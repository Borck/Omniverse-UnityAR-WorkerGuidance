using System.Collections;
using UnityEngine;

namespace Guidance.Runtime
{
    /// <summary>
    /// Mecanim equivalent of AnimationReplayLoop. Plays the Animator's current
    /// state once at the configured speed, holds the last frame for delaySeconds,
    /// then rewinds and replays. Used for GLBs whose animations are driven by
    /// an Animator instead of the legacy Animation component.
    ///
    /// Holding the last frame: we set Animator.speed = 0 after playing for the
    /// effective clip duration. This freezes whatever frame the animator is
    /// currently sampling -- which, if the state is set to loop in the GLB's
    /// Animator Controller, would be the end of the clip OR the start of the
    /// next iteration depending on frame timing. To avoid catching the auto-loop
    /// reset, we also call Animator.Play(stateHash, 0, 0.999f) + Update(0) to
    /// force-sample the near-final frame before pausing.
    /// </summary>
    public sealed class AnimatorReplayLoop : MonoBehaviour
    {
        private Animator _animator;
        private float _delaySeconds;
        private float _speed;
        private Coroutine _loop;

        public void Initialize(Animator animator, float delaySeconds, float speed)
        {
            _animator = animator;
            _delaySeconds = delaySeconds;
            _speed = speed;
            if (_loop != null) StopCoroutine(_loop);
            _loop = StartCoroutine(LoopRoutine());
        }

        private IEnumerator LoopRoutine()
        {
            if (_animator == null) yield break;

            // Wait one frame for the Animator to settle into its initial state
            // (GetCurrentAnimatorStateInfo can return zero length on the same
            // frame the component is enabled).
            yield return null;

            if (_animator == null) yield break;

            var stateInfo  = _animator.GetCurrentAnimatorStateInfo(0);
            float clipLen  = stateInfo.length;
            int stateHash  = stateInfo.fullPathHash;

            if (clipLen <= 0f || stateHash == 0)
            {
                Debug.LogWarning($"[AnimatorReplayLoop] {name}: no current state on layer 0 (length={clipLen:F3}s, hash={stateHash}); aborting.");
                yield break;
            }

            float effectiveDuration = clipLen / Mathf.Max(0.01f, Mathf.Abs(_speed));
            Debug.Log($"[AnimatorReplayLoop] {name}: clip={clipLen:F2}s, speed={_speed:F2}x -> effective {effectiveDuration:F2}s, delay {_delaySeconds:F1}s");

            while (_animator != null)
            {
                _animator.speed = _speed;
                _animator.Play(stateHash, 0, 0f);

                yield return new WaitForSeconds(effectiveDuration);

                if (_animator == null) yield break;

                // Force-position at the near-final frame, then pause. Doing this
                // BEFORE setting speed=0 makes the sample take effect; setting
                // speed=0 then locks it there for the delay.
                _animator.Play(stateHash, 0, 0.999f);
                _animator.Update(0f);
                _animator.speed = 0f;

                yield return new WaitForSeconds(_delaySeconds);
            }
        }

        private void OnDisable()
        {
            if (_loop != null) StopCoroutine(_loop);
            _loop = null;
        }
    }
}
