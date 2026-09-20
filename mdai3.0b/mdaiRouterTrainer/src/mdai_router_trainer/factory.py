from .configuration import ProviderConfig
from .openai_provider import OpenAICompatibleRouterProvider


def create_provider(config: ProviderConfig) -> OpenAICompatibleRouterProvider:
    if not config.base_url or not config.model:
        raise ValueError(f"{config.name} için baseUrl ve model zorunludur.")
    return OpenAICompatibleRouterProvider(
        name=config.name,
        base_url=config.base_url,
        model=config.model,
        api_key=config.api_key,
        endpoint=config.endpoint,
        timeout_seconds=config.timeout_seconds,
        max_retries=config.max_retries,
        backoff_seconds=config.backoff_seconds,
    )
