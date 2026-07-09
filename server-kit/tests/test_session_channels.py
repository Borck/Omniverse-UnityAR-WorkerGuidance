import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "app"))

from session_channels import SessionChannels


def test_attach_snapshot_includes_zero_heartbeat_and_no_step_until_set():
    ch = SessionChannels()
    ch.attach("s1", "job-a", context="fake-ctx")
    assert ch.session_count() == 1
    assert ch.snapshot() == [("s1", "job-a", 0, "")]
    assert ch.get_context("s1") == "fake-ctx"


def test_touch_heartbeat_updates_snapshot():
    ch = SessionChannels()
    ch.attach("s1", "job-a")
    ch.touch_heartbeat("s1")
    _, _, hb_ms, _ = ch.snapshot()[0]
    assert hb_ms > 0


def test_set_current_step_updates_snapshot():
    ch = SessionChannels()
    ch.attach("s1", "job-a")
    ch.set_current_step("s1", "step-3")
    _, _, _, step_id = ch.snapshot()[0]
    assert step_id == "step-3"


def test_set_current_step_on_unknown_session_is_a_noop():
    ch = SessionChannels()
    ch.set_current_step("ghost", "step-1")  # must not raise
    assert ch.snapshot() == []


def test_session_ids_for_job_filters_correctly():
    ch = SessionChannels()
    ch.attach("s1", "job-a")
    ch.attach("s2", "job-b")
    ch.attach("s3", "job-a")
    assert sorted(ch.session_ids_for_job("job-a")) == ["s1", "s3"]


def test_touch_heartbeat_on_unknown_session_is_a_noop():
    ch = SessionChannels()
    ch.touch_heartbeat("ghost")  # must not raise
    assert ch.snapshot() == []


def test_detach_clears_context_and_heartbeat():
    ch = SessionChannels()
    ch.attach("s1", "job-a", context="fake-ctx")
    ch.touch_heartbeat("s1")
    ch.detach("s1")
    assert ch.session_count() == 0
    assert ch.get_context("s1") is None
    assert ch.snapshot() == []


def test_broadcast_to_job_only_targets_matching_job():
    ch = SessionChannels()
    q1 = ch.attach("s1", "job-a")
    q2 = ch.attach("s2", "job-b")
    notified = ch.broadcast_to_job("job-a", "msg")
    assert notified == 1
    assert q1.get_nowait() == "msg"
    assert q2.empty()
