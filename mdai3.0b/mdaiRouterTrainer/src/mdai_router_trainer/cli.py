import argparse
import json
from pathlib import Path

from .catalog import ToolCatalog
from .configuration import TrainerConfig
from .consensus_dataset import build_consensus, build_quality_report, read_provider_records, write_jsonl
from .factory import create_provider
from .prompting import RouterPromptBuilder
from .resume import ResumeStore
from .runner import DatasetRunner
from .tasks import read_tasks
from .training import write_full_consensus_training_jsonl


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(prog="mdai-router-trainer")
    subparsers = parser.add_subparsers(dest="command", required=True)

    collect = subparsers.add_parser("collect", help="Teacher provider sonuçlarını topla")
    collect.add_argument("--config", type=Path, required=True)
    collect.add_argument("--catalog", type=Path, required=True)
    collect.add_argument("--tasks", type=Path, required=True)
    collect.add_argument("--output", type=Path, required=True)
    collect.add_argument("--checkpoint", type=Path, required=True)
    collect.add_argument("--providers", nargs="+", help="Çalıştırılacak provider adları")

    aggregate = subparsers.add_parser("aggregate", help="Consensus ve training çıktısı üret")
    aggregate.add_argument("--catalog", type=Path, required=True)
    aggregate.add_argument("--input", type=Path, required=True)
    aggregate.add_argument("--output", type=Path, required=True)
    aggregate.add_argument("--report", type=Path, required=True)
    aggregate.add_argument("--training", type=Path, required=True)
    return parser


def run_collect(args: argparse.Namespace) -> None:
    config = TrainerConfig.from_json_file(args.config)
    catalog = ToolCatalog.from_json_file(args.catalog)
    selected = set(args.providers) if args.providers else None
    providers = [
        create_provider(provider)
        for provider in config.providers
        if selected is None or provider.name in selected
    ]
    if not providers:
        raise ValueError("Seçilen provider bulunamadı.")
    system_prompt, _ = RouterPromptBuilder(catalog).build("placeholder")
    processed = DatasetRunner(
        catalog,
        providers,
        ResumeStore(args.checkpoint),
        args.output,
        raw_directory=args.output.parent / "raw",
    ).run(read_tasks(args.tasks), system_prompt)
    print(f"processed={processed}")


def run_aggregate(args: argparse.Namespace) -> None:
    catalog = ToolCatalog.from_json_file(args.catalog)
    records = read_provider_records(sorted(args.input.glob("*.jsonl")))
    consensus = build_consensus(records)
    write_jsonl(args.output, consensus)
    report = build_quality_report(records, consensus, limit=100)
    args.report.parent.mkdir(parents=True, exist_ok=True)
    args.report.write_text(json.dumps(report.to_json(), ensure_ascii=False, indent=2), encoding="utf-8")
    write_full_consensus_training_jsonl(args.training, consensus)
    print(f"consensus={len(consensus)} catalog={catalog.version}")


def main() -> None:
    args = build_parser().parse_args()
    if args.command == "collect":
        run_collect(args)
    else:
        run_aggregate(args)


if __name__ == "__main__":
    main()
