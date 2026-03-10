from pathlib import Path
import sys

sys.path.append(str(Path(__file__).resolve().parents[1] / "app"))

from config import AppConfig


def test_app_config_from_env_reads_stage_uri(monkeypatch) -> None:
    monkeypatch.setenv("GUIDANCE_STAGE_URI", "omniverse://localhost/Projects/Assembly.usd")

    config = AppConfig.from_env()

    assert config.stage_uri == "omniverse://localhost/Projects/Assembly.usd"
