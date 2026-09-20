from pathlib import Path
from typing import Iterable

from .catalog import ToolCatalog
from .models import TaskRecord
from .normalization import ToolNormalizer
from .providers import RouterProvider
from .records import ProviderRecord, append_raw_response, append_record, utc_timestamp
from .response_parser import MalformedRouterResponse, parse_router_response
from .resume import ResumeStore


class DatasetRunner:
    def __init__(
        self,
        catalog: ToolCatalog,
        providers: Iterable[RouterProvider],
        resume_store: ResumeStore,
        output_directory: Path,
        raw_directory: Path | None = None,
    ) -> None:
        self._catalog = catalog
        self._providers = tuple(providers)
        self._resume = resume_store
        self._output_directory = output_directory
        self._raw_directory = raw_directory
        self._normalizer = ToolNormalizer(catalog)

    def run(self, tasks: Iterable[TaskRecord], system_prompt: str) -> int:
        task_list = tuple(tasks)
        total_tasks = len(task_list)
        processed = 0
        for task_number, task in enumerate(task_list, start=1):
            for provider in self._providers:
                if self._resume.should_skip(task.task_id, provider.name):
                    continue
                print(f"[collect] {task_number}/{total_tasks} task={task.task_id} provider={provider.name}", flush=True)
                self._resume.mark(task.task_id, provider.name, "PROCESSING")
                record = self._process_one(task, provider, system_prompt)
                append_record(self._output_directory / f"{provider.name}.jsonl", record)
                self._resume.mark(
                    task.task_id,
                    provider.name,
                    "COMPLETED" if record.success else "FAILED",
                    record.error,
                )
                processed += 1
                result = "OK" if record.success else "FAILED"
                print(f"[collect] {task_number}/{total_tasks} task={task.task_id} provider={provider.name} result={result}", flush=True)
        return processed

    def _process_one(self, task: TaskRecord, provider: RouterProvider, system_prompt: str) -> ProviderRecord:
        try:
            response = provider.route(system_prompt, task.task)
            if self._raw_directory is not None:
                append_raw_response(
                    self._raw_directory / f"{provider.name}.jsonl",
                    task.task_id,
                    provider.name,
                    response.raw_text,
                )
            decision = parse_router_response(response.raw_text)
            normalized = self._normalizer.normalize(list(decision.tools))
            return ProviderRecord(
                task_id=task.task_id,
                task=task.task,
                teacher=response.provider,
                model=response.model,
                tools=normalized.tools,
                unknown_tools=normalized.unknown_tools,
                confidence=None,
                success=True,
                timestamp=utc_timestamp(),
                tool_catalog_version=self._catalog.version,
            )
        except MalformedRouterResponse as error:
            return self._failure_record(task, provider, str(error), malformed=True)
        except Exception as error:
            return self._failure_record(task, provider, str(error))

    def _failure_record(self, task: TaskRecord, provider: RouterProvider, error: str, malformed: bool = False) -> ProviderRecord:
        return ProviderRecord(
            task_id=task.task_id,
            task=task.task,
            teacher=provider.name,
            model=provider.model,
            tools=(),
            unknown_tools=(),
            confidence=None,
            success=False,
            timestamp=utc_timestamp(),
            tool_catalog_version=self._catalog.version,
            malformed_response=malformed,
            error=error,
        )
