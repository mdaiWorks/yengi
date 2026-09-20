import json
from pathlib import Path

import pytest

from mdai_router_trainer.catalog import ToolCatalog
from mdai_router_trainer.models import RouterDecision, TaskRecord
from mdai_router_trainer.tasks import TaskInputError, read_tasks


CATALOG_PATH = Path(__file__).parents[1] / "catalog" / "tool-catalog.mdaiAgent-1.0.json"


def test_catalog_snapshot_has_unique_real_tools() -> None:
    catalog = ToolCatalog.from_json_file(CATALOG_PATH)
    assert catalog.version == "mdaiAgent-1.0"
    assert len(catalog.tools) == 21
    assert len(catalog.names()) == 21
    assert catalog.contains("CreateOrUpdateFile")


def test_router_decision_removes_empty_and_duplicate_tools() -> None:
    decision = RouterDecision(("ReadFile", "", "ReadFile", "SearchCode"))
    assert decision.tools == ("ReadFile", "SearchCode")


def test_task_reader_rejects_duplicate_ids(tmp_path: Path) -> None:
    task_file = tmp_path / "tasks.jsonl"
    task_file.write_text(
        json.dumps({"id": "001", "task": "Bir dosyayı oku"})
        + "\n"
        + json.dumps({"id": "001", "task": "Tekrar"})
        + "\n",
        encoding="utf-8",
    )

    with pytest.raises(TaskInputError, match="yinelenen task id"):
        list(read_tasks(task_file))


def test_task_reader_skips_blank_lines(tmp_path: Path) -> None:
    task_file = tmp_path / "tasks.jsonl"
    task_file.write_text('\n{"id":"001","task":"README dosyasını oku"}\n', encoding="utf-8")

    assert list(read_tasks(task_file)) == [TaskRecord("001", "README dosyasını oku")]
