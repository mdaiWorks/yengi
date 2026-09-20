import json
from pathlib import Path

from mdai_router_trainer.consensus import ConsensusKind, calculate_consensus
from mdai_router_trainer.models import TaskRecord
from mdai_router_trainer.training import write_training_jsonl


def test_consensus_ignores_tool_order() -> None:
    result = calculate_consensus([
        ("ReadFile", "CreateOrUpdateFile"),
        ("CreateOrUpdateFile", "ReadFile"),
    ])
    assert result.kind is ConsensusKind.FULL
    assert result.final_tools == ("CreateOrUpdateFile", "ReadFile")
    assert result.label == "teacher-consensus"


def test_disagreement_has_no_final_tools() -> None:
    result = calculate_consensus([("ReadFile",), ("WebSearch",), ("RunTests",)])
    assert result.kind is ConsensusKind.DISAGREEMENT
    assert result.final_tools == ()


def test_training_output_contains_only_task_and_tools(tmp_path: Path) -> None:
    path = tmp_path / "training.jsonl"
    count = write_training_jsonl(path, [(TaskRecord("1", "Dosyayı oku"), ("ReadFile",))])
    record = json.loads(path.read_text(encoding="utf-8"))
    assert count == 1
    assert record["messages"][1]["content"] == '{"tools":["ReadFile"]}'
