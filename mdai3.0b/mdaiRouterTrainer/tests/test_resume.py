import json
from pathlib import Path

from mdai_router_trainer.resume import ResumeStore


def test_resume_store_skips_completed_provider_task(tmp_path: Path) -> None:
    path = tmp_path / "checkpoint.json"
    store = ResumeStore(path)
    store.mark("001", "gemini", "COMPLETED")

    restored = ResumeStore(path)
    assert restored.should_skip("001", "gemini")
    assert not restored.should_skip("001", "groq")
    assert json.loads(path.read_text(encoding="utf-8"))["statuses"][0]["status"] == "COMPLETED"
