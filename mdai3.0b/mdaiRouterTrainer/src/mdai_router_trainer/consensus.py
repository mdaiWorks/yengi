from dataclasses import dataclass
from enum import Enum


class ConsensusKind(str, Enum):
    FULL = "FULL"
    PARTIAL = "PARTIAL"
    DISAGREEMENT = "DISAGREEMENT"


@dataclass(frozen=True)
class ConsensusResult:
    kind: ConsensusKind
    final_tools: tuple[str, ...]
    label: str = "teacher-consensus"


def calculate_consensus(tool_sets: list[tuple[str, ...]]) -> ConsensusResult:
    if not tool_sets:
        raise ValueError("Consensus için en az bir provider sonucu gerekir.")
    sets = [frozenset(tools) for tools in tool_sets]
    if all(tool_set == sets[0] for tool_set in sets[1:]):
        return ConsensusResult(ConsensusKind.FULL, tuple(sorted(sets[0])))
    common = set.intersection(*(set(tool_set) for tool_set in sets))
    if common:
        return ConsensusResult(ConsensusKind.PARTIAL, tuple(sorted(common)))
    return ConsensusResult(ConsensusKind.DISAGREEMENT, ())
