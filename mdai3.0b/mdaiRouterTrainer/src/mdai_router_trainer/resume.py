import json
import os
from dataclasses import asdict, dataclass
from pathlib import Path


@dataclass
class TaskStatus:
    task_id: str
    provider: str
    status: str = "PENDING"
    error: str | None = None


class ResumeStore:
    def __init__(self, path: Path) -> None:
        self._path = path
        self._statuses: dict[str, TaskStatus] = {}
        self.load()

    def load(self) -> None:
        if not self._path.exists():
            return
        with self._path.open("r", encoding="utf-8") as stream:
            document = json.load(stream)
        for item in document.get("statuses", []):
            status = TaskStatus(**item)
            self._statuses[self._key(status.task_id, status.provider)] = status

    def get(self, task_id: str, provider: str) -> TaskStatus:
        return self._statuses.get(
            self._key(task_id, provider), TaskStatus(task_id=task_id, provider=provider)
        )

    def mark(self, task_id: str, provider: str, status: str, error: str | None = None) -> None:
        if status not in {"PENDING", "PROCESSING", "COMPLETED", "FAILED", "RATE_LIMITED"}:
            raise ValueError(f"Geçersiz task durumu: {status}")
        item = TaskStatus(task_id, provider, status, error)
        self._statuses[self._key(task_id, provider)] = item
        self.save()

    def should_skip(self, task_id: str, provider: str) -> bool:
        return self.get(task_id, provider).status == "COMPLETED"

    def save(self) -> None:
        self._path.parent.mkdir(parents=True, exist_ok=True)
        temporary = self._path.with_suffix(self._path.suffix + ".tmp")
        payload = {"statuses": [asdict(item) for item in self._statuses.values()]}
        with temporary.open("w", encoding="utf-8") as stream:
            json.dump(payload, stream, ensure_ascii=False, indent=2)
        os.replace(temporary, self._path)

    @staticmethod
    def _key(task_id: str, provider: str) -> str:
        return f"{provider}\0{task_id}"
