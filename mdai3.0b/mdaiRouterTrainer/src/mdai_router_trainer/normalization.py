import re
from dataclasses import dataclass

from .catalog import ToolCatalog


@dataclass(frozen=True)
class NormalizationResult:
    tools: tuple[str, ...]
    unknown_tools: tuple[str, ...]


class ToolNormalizer:
    def __init__(self, catalog: ToolCatalog, aliases: dict[str, str] | None = None) -> None:
        self._catalog = catalog
        self._aliases = {
            self._key(alias): canonical
            for alias, canonical in (aliases or {}).items()
            if catalog.contains(canonical)
        }

    def normalize(self, tools: list[object]) -> NormalizationResult:
        known: list[str] = []
        unknown: list[str] = []
        for raw_tool in tools:
            if not isinstance(raw_tool, str) or not raw_tool.strip():
                unknown.append(str(raw_tool))
                continue
            value = raw_tool.strip()
            canonical = value if self._catalog.contains(value) else self._aliases.get(self._key(value))
            if canonical is None:
                unknown.append(value)
            elif canonical not in known:
                known.append(canonical)
        return NormalizationResult(tuple(known), tuple(unknown))

    @staticmethod
    def _key(value: str) -> str:
        return re.sub(r"[^a-z0-9]", "", value.lower())
