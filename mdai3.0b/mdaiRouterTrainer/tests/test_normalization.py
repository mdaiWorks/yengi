from pathlib import Path

from mdai_router_trainer.catalog import ToolCatalog
from mdai_router_trainer.normalization import ToolNormalizer


CATALOG_PATH = Path(__file__).parents[1] / "catalog" / "tool-catalog.mdaiAgent-1.0.json"


def test_normalizer_keeps_canonical_names_and_deduplicates() -> None:
    catalog = ToolCatalog.from_json_file(CATALOG_PATH)
    result = ToolNormalizer(catalog).normalize(["ReadFile", "ReadFile"])
    assert result.tools == ("ReadFile",)
    assert result.unknown_tools == ()


def test_normalizer_uses_explicit_aliases_only() -> None:
    catalog = ToolCatalog.from_json_file(CATALOG_PATH)
    normalizer = ToolNormalizer(catalog, {"createFile": "CreateOrUpdateFile"})
    result = normalizer.normalize(["create_file", "UnknownTool"])
    assert result.tools == ("CreateOrUpdateFile",)
    assert result.unknown_tools == ("UnknownTool",)
