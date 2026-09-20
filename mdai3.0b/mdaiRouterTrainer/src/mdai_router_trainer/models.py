from dataclasses import dataclass
from typing import Any


@dataclass(frozen=True)
class ToolDefinition:
    name: str
    description: str


@dataclass(frozen=True)
class TaskRecord:
    task_id: str
    task: str

    @classmethod
    def from_json(cls, value: dict[str, Any]) -> "TaskRecord":
        task_id = value.get("id")
        task = value.get("task")
        if not isinstance(task_id, str) or not task_id.strip():
            raise ValueError("Task kaydı geçerli bir 'id' içermelidir.")
        if not isinstance(task, str) or not task.strip():
            raise ValueError("Task kaydı geçerli bir 'task' içermelidir.")
        return cls(task_id=task_id.strip(), task=task.strip())


@dataclass(frozen=True)
class RouterDecision:
    tools: tuple[str, ...]

    def __post_init__(self) -> None:
        normalized = tuple(dict.fromkeys(tool.strip() for tool in self.tools if tool.strip()))
        object.__setattr__(self, "tools", normalized)
