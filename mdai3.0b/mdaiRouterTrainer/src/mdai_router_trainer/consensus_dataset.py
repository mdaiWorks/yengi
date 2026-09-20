import json
from collections import Counter, defaultdict
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Iterable

from .consensus import calculate_consensus


@dataclass(frozen=True)
class QualityReport:
    total_tasks: int
    provider_success: dict[str, int]
    provider_failure: dict[str, int]
    full_consensus: int
    partial_consensus: int
    disagreement: int
    average_confidence: float | None
    tool_frequency: dict[str, int]
    unknown_tool_count: int
    malformed_response_count: int

    def to_json(self) -> dict[str, Any]:
        return {
            "totalTasks": self.total_tasks,
            "providerSuccess": self.provider_success,
            "providerFailure": self.provider_failure,
            "fullConsensus": self.full_consensus,
            "partialConsensus": self.partial_consensus,
            "disagreement": self.disagreement,
            "averageConfidence": self.average_confidence,
            "toolFrequency": self.tool_frequency,
            "unknownToolCount": self.unknown_tool_count,
            "malformedResponseCount": self.malformed_response_count,
        }


def read_provider_records(paths: Iterable[Path]) -> list[dict[str, Any]]:
    records: list[dict[str, Any]] = []
    for path in paths:
        with path.open("r", encoding="utf-8") as stream:
            for line_number, line in enumerate(stream, start=1):
                if not line.strip():
                    continue
                try:
                    value = json.loads(line)
                except json.JSONDecodeError as error:
                    raise ValueError(f"{path}:{line_number} geçersiz JSON") from error
                if not isinstance(value, dict):
                    raise ValueError(f"{path}:{line_number} nesne olmalıdır")
                if "task_id" in value and "teacher" in value:
                    records.append(value)
    return records


def build_consensus(records: Iterable[dict[str, Any]]) -> list[dict[str, Any]]:
    grouped: dict[str, list[dict[str, Any]]] = defaultdict(list)
    for record in records:
        grouped[str(record["task_id"])].append(record)

    output: list[dict[str, Any]] = []
    for task_id, task_records in grouped.items():
        successful = [record for record in task_records if record.get("success") is True]
        if not successful:
            continue
        result = calculate_consensus([tuple(record.get("tools", [])) for record in successful])
        output.append({
            "taskId": task_id,
            "task": successful[0].get("task", ""),
            "teacherTools": {
                str(record.get("teacher", "unknown")): list(record.get("tools", []))
                for record in successful
            },
            "consensus": result.kind.value,
            "finalTools": list(result.final_tools),
            "label": result.label,
        })
    return sorted(output, key=lambda item: item["taskId"])


def write_jsonl(path: Path, records: Iterable[dict[str, Any]]) -> int:
    path.parent.mkdir(parents=True, exist_ok=True)
    count = 0
    with path.open("w", encoding="utf-8") as stream:
        for record in records:
            stream.write(json.dumps(record, ensure_ascii=False) + "\n")
            count += 1
    return count


def build_quality_report(records: Iterable[dict[str, Any]], consensus_records: Iterable[dict[str, Any]], limit: int = 100) -> QualityReport:
    source = list(records)
    task_ids = {str(record.get("task_id")) for record in source}
    consensus = list(consensus_records)
    provider_success = Counter(str(record.get("teacher", "unknown")) for record in source if record.get("success") is True)
    provider_failure = Counter(str(record.get("teacher", "unknown")) for record in source if record.get("success") is not True)
    confidence = [float(record["confidence"]) for record in source if isinstance(record.get("confidence"), (int, float))]
    frequency = Counter(tool for item in consensus[:limit] for tool in item.get("finalTools", []))
    return QualityReport(
        total_tasks=min(len(task_ids), limit),
        provider_success=dict(provider_success),
        provider_failure=dict(provider_failure),
        full_consensus=sum(item.get("consensus") == "FULL" for item in consensus[:limit]),
        partial_consensus=sum(item.get("consensus") == "PARTIAL" for item in consensus[:limit]),
        disagreement=sum(item.get("consensus") == "DISAGREEMENT" for item in consensus[:limit]),
        average_confidence=round(sum(confidence) / len(confidence), 4) if confidence else None,
        tool_frequency=dict(frequency),
        unknown_tool_count=sum(len(record.get("unknown_tools", [])) for record in source),
        malformed_response_count=sum(bool(record.get("malformed_response")) for record in source),
    )
