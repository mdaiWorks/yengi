import json
from pathlib import Path
from typing import Any

from .models import ToolDefinition


class ToolCatalog:
    def __init__(self, version: str, tools: tuple[ToolDefinition, ...]) -> None:
        if not version.strip():
            raise ValueError("Katalog sürümü boş olamaz.")
        names = [tool.name for tool in tools]
        if len(names) != len(set(names)):
            raise ValueError("Katalog içinde yinelenen tool adı bulunuyor.")
        if any(not tool.name.strip() or not tool.description.strip() for tool in tools):
            raise ValueError("Her tool adı ve açıklaması zorunludur.")
        self.version = version
        self.tools = tools
        self._names = frozenset(names)

    @classmethod
    def from_json_file(cls, path: Path) -> "ToolCatalog":
        with path.open("r", encoding="utf-8") as stream:
            document: dict[str, Any] = json.load(stream)
        raw_tools = document.get("tools")
        if not isinstance(raw_tools, list):
            raise ValueError("Katalog 'tools' dizisi içermelidir.")
        tools = tuple(
            ToolDefinition(name=item["name"], description=item["description"])
            for item in raw_tools
            if isinstance(item, dict)
            and isinstance(item.get("name"), str)
            and isinstance(item.get("description"), str)
        )
        if len(tools) != len(raw_tools):
            raise ValueError("Katalogdaki her tool name ve description alanını içermelidir.")
        return cls(version=str(document.get("catalogVersion", "")), tools=tools)

    def contains(self, tool_name: str) -> bool:
        return tool_name in self._names

    def names(self) -> frozenset[str]:
        return self._names
