from pathlib import Path

from mdai_router_trainer.consensus_dataset import build_consensus, build_quality_report


def records() -> list[dict]:
    return [
        {"task_id": "001", "task": "Oku", "teacher": "gemini", "tools": ["ReadFile"], "success": True, "confidence": 0.8, "unknown_tools": [], "malformed_response": False},
        {"task_id": "001", "task": "Oku", "teacher": "groq", "tools": ["ReadFile"], "success": True, "confidence": 0.9, "unknown_tools": [], "malformed_response": False},
        {"task_id": "002", "task": "Bozuk", "teacher": "gemini", "tools": [], "success": False, "confidence": None, "unknown_tools": ["Bad"], "malformed_response": True},
    ]


def test_consensus_groups_provider_records() -> None:
    result = build_consensus(records())
    assert result[0]["consensus"] == "FULL"
    assert result[0]["label"] == "teacher-consensus"


def test_quality_report_counts_first_tasks() -> None:
    report = build_quality_report(records(), build_consensus(records()), limit=100)
    assert report.total_tasks == 2
    assert report.full_consensus == 1
    assert report.provider_failure == {"gemini": 1}
    assert report.unknown_tool_count == 1
    assert report.malformed_response_count == 1
