import json
from pathlib import Path
from typing import Iterator

from .models import TaskRecord


class TaskInputError(ValueError):
    """Raised when a JSONL task file contains an invalid record."""


def read_tasks(path: Path) -> Iterator[TaskRecord]:
    seen_ids: set[str] = set()
    with path.open("r", encoding="utf-8") as stream:
        for line_number, line in enumerate(stream, start=1):
            if not line.strip():
                continue
            try:
                value = json.loads(line)
            except json.JSONDecodeError as error:
                raise TaskInputError(f"{path}:{line_number} geçersiz JSON: {error.msg}") from error
            if not isinstance(value, dict):
                raise TaskInputError(f"{path}:{line_number} nesne olmalıdır.")
            try:
                task = TaskRecord.from_json(value)
            except ValueError as error:
                raise TaskInputError(f"{path}:{line_number} {error}") from error
            if task.task_id in seen_ids:
                raise TaskInputError(f"{path}:{line_number} yinelenen task id: {task.task_id}")
            seen_ids.add(task.task_id)
            yield task
