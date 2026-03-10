using NUnit.Framework;

namespace Guidance.Runtime.Tests.Editor
{
    public sealed class StepCoordinatorTests
    {
        [Test]
        public void ActivateStep_ThenTrackPlayFinishConfirm_ReturnsToIdleAndClearsActiveStep()
        {
            var coordinator = new StepCoordinator();
            coordinator.Initialize();

            Assert.IsTrue(coordinator.ActivateStep("job-1", "10"));
            Assert.AreEqual(StepCoordinatorState.StepReady, coordinator.CurrentState);
            Assert.AreEqual("job-1", coordinator.ActiveJobId);
            Assert.AreEqual("10", coordinator.ActiveStepId);

            Assert.IsTrue(coordinator.BeginTracking());
            Assert.AreEqual(StepCoordinatorState.Tracking, coordinator.CurrentState);

            Assert.IsTrue(coordinator.StartPlayback());
            Assert.AreEqual(StepCoordinatorState.Playing, coordinator.CurrentState);

            Assert.IsTrue(coordinator.FinishPlayback());
            Assert.AreEqual(StepCoordinatorState.Tracking, coordinator.CurrentState);

            Assert.IsTrue(coordinator.ConfirmStepCompleted());
            Assert.AreEqual(StepCoordinatorState.Idle, coordinator.CurrentState);
            Assert.AreEqual(string.Empty, coordinator.ActiveJobId);
            Assert.AreEqual(string.Empty, coordinator.ActiveStepId);
        }

        [Test]
        public void StartPlayback_FromIdle_FailsAndKeepsState()
        {
            var coordinator = new StepCoordinator();
            coordinator.Initialize();

            Assert.IsFalse(coordinator.StartPlayback());
            Assert.AreEqual(StepCoordinatorState.Idle, coordinator.CurrentState);
        }

        [Test]
        public void RegisterFault_ThenRecoverFromFault_ReturnsToIdleAndClearsFaultMessage()
        {
            var coordinator = new StepCoordinator();
            coordinator.Initialize();
            Assert.IsTrue(coordinator.ActivateStep("job-1", "20"));

            Assert.IsTrue(coordinator.RegisterFault("network-lost"));
            Assert.AreEqual(StepCoordinatorState.Faulted, coordinator.CurrentState);
            Assert.AreEqual("network-lost", coordinator.LastFaultMessage);

            Assert.IsTrue(coordinator.RecoverFromFault());
            Assert.AreEqual(StepCoordinatorState.Idle, coordinator.CurrentState);
            Assert.AreEqual(string.Empty, coordinator.LastFaultMessage);
            Assert.AreEqual(string.Empty, coordinator.ActiveJobId);
            Assert.AreEqual(string.Empty, coordinator.ActiveStepId);
        }

        [Test]
        public void RecoverFromFault_WhenNotFaulted_Fails()
        {
            var coordinator = new StepCoordinator();
            coordinator.Initialize();

            Assert.IsFalse(coordinator.RecoverFromFault());
            Assert.AreEqual(StepCoordinatorState.Idle, coordinator.CurrentState);
        }

        [Test]
        public void NotifyTrackingLost_FromTracking_ReturnsToStepReady()
        {
            var coordinator = new StepCoordinator();
            coordinator.Initialize();
            Assert.IsTrue(coordinator.ActivateStep("job-1", "30"));
            Assert.IsTrue(coordinator.BeginTracking());

            Assert.IsTrue(coordinator.NotifyTrackingLost());
            Assert.AreEqual(StepCoordinatorState.StepReady, coordinator.CurrentState);
            Assert.AreEqual("job-1", coordinator.ActiveJobId);
            Assert.AreEqual("30", coordinator.ActiveStepId);
        }
    }
}
