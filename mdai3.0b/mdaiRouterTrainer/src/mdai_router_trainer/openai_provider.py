import json
import time
from pathlib import Path
from urllib.error import HTTPError, URLError
from urllib.request import Request, urlopen

from .providers import ProviderResponse


class ProviderRequestError(RuntimeError):
    def __init__(self, message: str, retryable: bool = False) -> None:
        super().__init__(message)
        self.retryable = retryable


class OpenAICompatibleRouterProvider:
    def __init__(self, name: str, base_url: str, model: str, api_key: str | None = None, timeout_seconds: int = 60, max_retries: int = 3, backoff_seconds: float = 2.0, endpoint: str = "chat/completions") -> None:
        self.name = name
        self.model = model
        self._base_url = base_url.rstrip("/")
        self._api_key = api_key
        self._timeout_seconds = timeout_seconds
        self._max_retries = max(0, max_retries)
        self._backoff_seconds = max(0.0, backoff_seconds)
        self._endpoint = endpoint.strip("/")

    def route(self, system_prompt: str, task: str) -> ProviderResponse:
        if self._endpoint == "responses":
            payload = {
                "model": self.model,
                "input": f"{system_prompt}\n\nUSER TASK:\n{task}",
                "temperature": 0,
            }
        else:
            payload = {
                "model": self.model,
                "messages": [
                    {"role": "system", "content": system_prompt},
                    {"role": "user", "content": task},
                ],
                "temperature": 0,
                "response_format": {"type": "json_object"},
            }
        headers = {
            "Content-Type": "application/json",
            "User-Agent": "mdaiRouterTrainer/0.1",
        }
        if self._api_key:
            headers["Authorization"] = f"Bearer {self._api_key}"
        request = Request(
            f"{self._base_url}/{self._endpoint}",
            data=json.dumps(payload).encode("utf-8"),
            headers=headers,
            method="POST",
        )
        started = time.perf_counter()
        document = self._request_with_retry(request)

        if self._endpoint == "responses":
            content = document.get("output_text")
            if not isinstance(content, str):
                try:
                    content = document["output"][0]["content"][0]["text"]
                except (KeyError, IndexError, TypeError) as error:
                    raise ProviderRequestError(f"{self.name} yanıtında output_text/output content bulunamadı") from error
        else:
            try:
                content = document["choices"][0]["message"]["content"]
            except (KeyError, IndexError, TypeError) as error:
                raise ProviderRequestError(f"{self.name} yanıtında choices/message/content bulunamadı") from error
        if not isinstance(content, str):
            raise ProviderRequestError(f"{self.name} content alanı metin değil")
        usage = document.get("usage") or {}
        return ProviderResponse(
            provider=self.name,
            model=self.model,
            raw_text=content,
            latency_ms=round((time.perf_counter() - started) * 1000),
            prompt_tokens=usage.get("prompt_tokens"),
            completion_tokens=usage.get("completion_tokens"),
        )

    def _request_with_retry(self, request: Request) -> dict:
        for attempt in range(self._max_retries + 1):
            try:
                with urlopen(request, timeout=self._timeout_seconds) as response:
                    return json.loads(response.read().decode("utf-8"))
            except HTTPError as error:
                retryable = error.code == 429 or error.code >= 500
                if not retryable or attempt == self._max_retries:
                    raise ProviderRequestError(f"{self.name} HTTP {error.code}", retryable) from error
            except (URLError, TimeoutError) as error:
                if attempt == self._max_retries:
                    raise ProviderRequestError(f"{self.name} bağlantı hatası: {error}", retryable=True) from error
            except (UnicodeDecodeError, json.JSONDecodeError) as error:
                raise ProviderRequestError(f"{self.name} geçersiz JSON yanıtı döndürdü", retryable=False) from error
            time.sleep(self._backoff_seconds * (2**attempt))
        raise ProviderRequestError(f"{self.name} isteği başarısız oldu", retryable=True)
