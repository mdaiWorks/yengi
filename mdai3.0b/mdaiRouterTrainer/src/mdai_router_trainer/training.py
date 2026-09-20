import json
from pathlib import Path
from typing import Iterable

from .models import TaskRecord


def write_training_jsonl(
    path: Path,
    records: Iterable[tuple[TaskRecord, tuple[str, ...]]],
) -> int:
    count = 0
    with path.open("w", encoding="utf-8") as stream:
        for task, tools in records:
            payload = {
                "messages": [
                    {"role": "user", "content": task.task},
                    {
                        "role": "assistant",
                        "content": json.dumps(
                            {"tools": list(tools)},
                            ensure_ascii=False,
                            separators=(",", ":"),
                        ),
                    },
                ]
            }
            stream.write(json.dumps(payload, ensure_ascii=False) + "\n")
            count += 1
    return count


def write_full_consensus_training_jsonl(path: Path, consensus_records: Iterable[dict]) -> int:
    records = (
        (
            TaskRecord(str(record["taskId"]), str(record["task"])),
            tuple(str(tool) for tool in record.get("finalTools", [])),
        )
        for record in consensus_records
        if record.get("consensus") == "FULL"
    )
    return write_training_jsonl(path, records)
