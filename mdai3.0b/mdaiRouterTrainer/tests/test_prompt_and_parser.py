import pytest

from mdai_router_trainer.catalog import ToolCatalog
from mdai_router_trainer.prompting import RouterPromptBuilder
from mdai_router_trainer.response_parser import MalformedRouterResponse, parse_router_response


def catalog() -> ToolCatalog:
    return ToolCatalog("test", ())


def test_prompt_requests_only_tools() -> None:
    system, task = RouterPromptBuilder(catalog()).build("Bir dosyayı oku")
    assert '"tools"' in system
    assert task == "Bir dosyayı oku"


def test_parser_accepts_json_fence_and_ignores_metadata() -> None:
    decision = parse_router_response('```json\n{"tools":["ReadFile"],"confidence":0.9}\n```')
    assert decision.tools == ("ReadFile",)


def test_parser_rejects_missing_tools() -> None:
    with pytest.raises(MalformedRouterResponse):
        parse_router_response('{"modelCategory":"coding"}')
