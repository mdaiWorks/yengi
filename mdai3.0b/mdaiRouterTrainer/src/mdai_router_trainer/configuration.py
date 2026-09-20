import json
import os
from dataclasses import dataclass
from pathlib import Path
from typing import Any


@dataclass(frozen=True)
class ProviderConfig:
    name: str
    base_url: str
    model: str
    api_key_env: str | None = None
    api_key_value: str | None = None
    endpoint: str = "chat/completions"
    timeout_seconds: int = 60
    max_retries: int = 3
    backoff_seconds: float = 2.0

    @property
    def api_key(self) -> str | None:
        if self.api_key_value:
            return self.api_key_value
        if self.api_key_env:
            return os.getenv(self.api_key_env)
        return None


@dataclass(frozen=True)
class TrainerConfig:
    providers: tuple[ProviderConfig, ...]

    @classmethod
    def from_json_file(cls, path: Path) -> "TrainerConfig":
        with path.open("r", encoding="utf-8") as stream:
            document: dict[str, Any] = json.load(stream)
        raw_providers = document.get("providers", [])
        if not isinstance(raw_providers, list):
            raise ValueError("providers dizisi zorunludur.")
        providers = tuple(
            ProviderConfig(
                name=item["name"],
                base_url=item["baseUrl"],
                model=item["model"],
                api_key_env=item.get("apiKeyEnv"),
                api_key_value=item.get("apiKey"),
                endpoint=item.get("endpoint", "chat/completions"),
                timeout_seconds=int(item.get("timeoutSeconds", 60)),
                max_retries=int(item.get("maxRetries", 3)),
                backoff_seconds=float(item.get("backoffSeconds", 2.0)),
            )
            for item in raw_providers
        )
        return cls(providers)

    @classmethod
    def from_environment(cls) -> "TrainerConfig":
        providers = (
            ProviderConfig("gemini", os.getenv("MDAI_GEMINI_BASE_URL", ""), os.getenv("MDAI_GEMINI_MODEL", ""), "MDAI_GEMINI_API_KEY"),
            ProviderConfig("groq", os.getenv("MDAI_GROQ_BASE_URL", ""), os.getenv("MDAI_GROQ_MODEL", ""), "MDAI_GROQ_API_KEY"),
            ProviderConfig("qwen", os.getenv("MDAI_QWEN_BASE_URL", ""), os.getenv("MDAI_QWEN_MODEL", ""), None),
        )
        return cls(tuple(provider for provider in providers if provider.base_url and provider.model))
