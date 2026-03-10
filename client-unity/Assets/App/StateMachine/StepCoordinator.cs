using UnityEngine;
using System;

namespace Guidance.Runtime
{
    public enum StepCoordinatorState
    {
        Idle,
        StepReady,
        Tracking,
        Playing,
        Faulted
    }

    public sealed class StepCoordinator
    {
        public StepCoordinatorState CurrentState { get; private set; } = StepCoordinatorState.Idle;
        public string ActiveJobId { get; private set; } = string.Empty;
        public string ActiveStepId { get; private set; } = string.Empty;
        public string LastFaultMessage { get; private set; } = string.Empty;

        public event Action<StepCoordinatorState, StepCoordinatorState, string> StateChanged;

        public void Initialize()
        {
            CurrentState = StepCoordinatorState.Idle;
            ActiveJobId = string.Empty;
            ActiveStepId = string.Empty;
            LastFaultMessage = string.Empty;
            Debug.Log("[StepCoordinator] Initialized (Idle).");
        }

        public bool ActivateStep(string jobId, string stepId)
        {
            if (!TryTransition(StepCoordinatorState.StepReady, "activate-step"))
            {
                return false;
            }

            ActiveJobId = jobId;
            ActiveStepId = stepId;
            LastFaultMessage = string.Empty;
            return true;
        }

        public bool BeginTracking()
        {
            return TryTransition(StepCoordinatorState.Tracking, "tracking-acquired");
        }

        public bool StartPlayback()
        {
            return TryTransition(StepCoordinatorState.Playing, "playback-start");
        }

        public bool FinishPlayback()
        {
            return TryTransition(StepCoordinatorState.Tracking, "playback-finish");
        }

        public bool ConfirmStepCompleted()
        {
            if (!CanTransition(CurrentState, StepCoordinatorState.Idle))
            {
                Debug.LogWarning($"[StepCoordinator] Invalid completion transition {CurrentState} -> Idle");
                return false;
            }

            var previous = CurrentState;
            CurrentState = StepCoordinatorState.Idle;
            ActiveJobId = string.Empty;
            ActiveStepId = string.Empty;
            OnStateChanged(previous, CurrentState, "step-confirmed");
            return true;
        }

        public bool RegisterFault(string message)
        {
            if (!CanTransition(CurrentState, StepCoordinatorState.Faulted))
            {
                Debug.LogWarning($"[StepCoordinator] Invalid fault transition {CurrentState} -> Faulted");
                return false;
            }

            LastFaultMessage = message;
            return TryTransition(StepCoordinatorState.Faulted, "fault");
        }

        public bool RecoverFromFault()
        {
            if (!CanTransition(CurrentState, StepCoordinatorState.Idle))
            {
                Debug.LogWarning($"[StepCoordinator] Invalid recover transition {CurrentState} -> Idle");
                return false;
            }

            var previous = CurrentState;
            CurrentState = StepCoordinatorState.Idle;
            ActiveJobId = string.Empty;
            ActiveStepId = string.Empty;
            LastFaultMessage = string.Empty;
            OnStateChanged(previous, CurrentState, "fault-recovered");
            return true;
        }

        public bool NotifyTrackingLost()
        {
            return TryTransition(StepCoordinatorState.StepReady, "tracking-lost");
        }

        private bool TryTransition(StepCoordinatorState nextState, string reason)
        {
            if (!CanTransition(CurrentState, nextState))
            {
                Debug.LogWarning($"[StepCoordinator] Invalid transition {CurrentState} -> {nextState}");
                return false;
            }

            var previous = CurrentState;
            CurrentState = nextState;
            OnStateChanged(previous, nextState, reason);
            return true;
        }

        private void OnStateChanged(StepCoordinatorState previous, StepCoordinatorState current, string reason)
        {
            Debug.Log($"[StepCoordinator] {previous} -> {current} ({reason})");
            StateChanged?.Invoke(previous, current, reason);
        }

        private static bool CanTransition(StepCoordinatorState from, StepCoordinatorState to)
        {
            if (from == to)
            {
                return true;
            }

            switch (from)
            {
                case StepCoordinatorState.Idle:
                    return to == StepCoordinatorState.StepReady || to == StepCoordinatorState.Faulted;
                case StepCoordinatorState.StepReady:
                    return to == StepCoordinatorState.Tracking || to == StepCoordinatorState.Idle || to == StepCoordinatorState.Faulted;
                case StepCoordinatorState.Tracking:
                    return to == StepCoordinatorState.Playing || to == StepCoordinatorState.StepReady || to == StepCoordinatorState.Idle || to == StepCoordinatorState.Faulted;
                case StepCoordinatorState.Playing:
                    return to == StepCoordinatorState.Tracking || to == StepCoordinatorState.StepReady || to == StepCoordinatorState.Idle || to == StepCoordinatorState.Faulted;
                case StepCoordinatorState.Faulted:
                    return to == StepCoordinatorState.Idle;
                default:
                    return false;
            }
        }
    }
}
