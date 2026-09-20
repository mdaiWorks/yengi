import json

from .catalog import ToolCatalog


class RouterPromptBuilder:
    def __init__(self, catalog: ToolCatalog) -> None:
        self._catalog = catalog

    def build(self, task: str) -> tuple[str, str]:
        if not task.strip():
            raise ValueError("Task boş olamaz.")
        catalog = json.dumps(
            [{"name": tool.name, "description": tool.description} for tool in self._catalog.tools],
            ensure_ascii=False,
            separators=(",", ":"),
        )
        system = (
            "You are the mdaiAgent tool router. Select the minimum sufficient set of tools "
            "for the user's task. Do not execute tools or solve the task. Tool order is irrelevant. "
            "Return only valid JSON in the form {\"tools\":[\"CanonicalToolName\"]}. "
            f"Available tools: {catalog}"
        )
        return system, task.strip()
