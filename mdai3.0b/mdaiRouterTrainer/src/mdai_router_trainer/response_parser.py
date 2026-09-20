import json
import re
from typing import Any

from .models import RouterDecision


class MalformedRouterResponse(ValueError):
    """Raised when a teacher response has no valid tools array."""


def parse_router_response(response: str) -> RouterDecision:
    value = _parse_json_object(response)
    tools = value.get("tools")
    if not isinstance(tools, list):
        raise MalformedRouterResponse("Teacher yanıtında tools dizisi bulunamadı.")
    return RouterDecision(tuple(item for item in tools if isinstance(item, str)))


def _parse_json_object(response: str) -> dict[str, Any]:
    text = response.strip()
    candidates = [text]
    fenced = re.search(r"```(?:json)?\s*(\{.*?\})\s*```", text, re.IGNORECASE | re.DOTALL)
    if fenced:
        candidates.append(fenced.group(1))
    start = text.find("{")
    end = text.rfind("}")
    if start >= 0 and end > start:
        candidates.append(text[start : end + 1])
    for candidate in candidates:
        try:
            value = json.loads(candidate)
        except json.JSONDecodeError:
            continue
        if isinstance(value, dict):
            return value
    raise MalformedRouterResponse("Teacher yanıtı geçerli JSON nesnesi değil.")
