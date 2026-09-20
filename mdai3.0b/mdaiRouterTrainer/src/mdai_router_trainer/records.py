import json
from dataclasses import asdict, dataclass
from datetime import datetime, timezone
from pathlib import Path


@dataclass(frozen=True)
class ProviderRecord:
    task_id: str
    task: str
    teacher: str
    model: str
    tools: tuple[str, ...]
    unknown_tools: tuple[str, ...]
    confidence: float | None
    success: bool
    timestamp: str
    tool_catalog_version: str
    malformed_response: bool = False
    error: str | None = None

    def to_json(self) -> dict:
        value = asdict(self)
        value["tools"] = list(self.tools)
        value["unknown_tools"] = list(self.unknown_tools)
        return value


def append_record(path: Path, record: ProviderRecord) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("a", encoding="utf-8") as stream:
        stream.write(json.dumps(record.to_json(), ensure_ascii=False) + "\n")


def append_raw_response(path: Path, task_id: str, provider: str, raw_text: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    payload = {"taskId": task_id, "provider": provider, "rawResponse": raw_text}
    with path.open("a", encoding="utf-8") as stream:
        stream.write(json.dumps(payload, ensure_ascii=False) + "\n")


def utc_timestamp() -> str:
    return datetime.now(timezone.utc).isoformat()
