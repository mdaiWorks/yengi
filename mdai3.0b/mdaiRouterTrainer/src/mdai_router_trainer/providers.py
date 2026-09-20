from dataclasses import dataclass
from typing import Protocol


@dataclass(frozen=True)
class ProviderResponse:
    provider: str
    model: str
    raw_text: str
    latency_ms: int | None = None
    prompt_tokens: int | None = None
    completion_tokens: int | None = None


class RouterProvider(Protocol):
    name: str
    model: str

    def route(self, system_prompt: str, task: str) -> ProviderResponse:
        """Ask a teacher model for a tool-routing response."""
