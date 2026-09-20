import json
from pathlib import Path

from mdai_router_trainer.catalog import ToolCatalog
from mdai_router_trainer.models import TaskRecord
from mdai_router_trainer.providers import ProviderResponse
from mdai_router_trainer.prompting import RouterPromptBuilder
from mdai_router_trainer.resume import ResumeStore
from mdai_router_trainer.runner import DatasetRunner


class FakeProvider:
    name = "fake"
    model = "test-model"

    def __init__(self) -> None:
        self.calls = 0

    def route(self, system_prompt: str, task: str) -> ProviderResponse:
        self.calls += 1
        return ProviderResponse(self.name, self.model, '{"tools":["ReadFile"]}')


def test_runner_writes_record_and_resumes(tmp_path: Path) -> None:
    catalog = ToolCatalog("test", (type("Tool", (), {"name": "ReadFile", "description": "read"})(),))
    provider = FakeProvider()
    runner = DatasetRunner(catalog, [provider], ResumeStore(tmp_path / "checkpoint.json"), tmp_path / "output")
    system_prompt, _ = RouterPromptBuilder(catalog).build("Dosyayı oku")
    task = TaskRecord("001", "Dosyayı oku")

    assert runner.run([task], system_prompt) == 1
    assert runner.run([task], system_prompt) == 0
    assert provider.calls == 1
    record = json.loads((tmp_path / "output" / "fake.jsonl").read_text(encoding="utf-8"))
    assert record["tools"] == ["ReadFile"]
    assert record["success"] is True
