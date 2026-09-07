from functools import lru_cache

from pydantic_settings import BaseSettings, SettingsConfigDict


class Settings(BaseSettings):
    app_env: str = "development"
    app_secret_key: str = "change-me-in-development"
    database_url: str = "postgresql+psycopg://app:change-me@localhost:5432/noavia"
    storage_root: str = "/data/storage"
    clamav_host: str = "localhost"
    clamav_port: int = 3310
    clamav_fail_open: bool = False
    openrouter_api_key: str | None = None
    openrouter_model: str = "openai/gpt-4o-mini"
    openrouter_base_url: str = "https://openrouter.ai/api/v1"
    openrouter_site_url: str | None = None
    openrouter_app_name: str | None = None
    source_url: str = "https://www.service.bund.de/Content/Globals/Functions/RSSFeed/RSSGenerator_Ausschreibungen.xml"
    source_keywords: str = "software,digitalisierung,cloud,daten,beratung,IT,entwicklung,systemintegration,webanwendung"
    source_timeout_seconds: int = 30
    scan_interval_minutes: int = 1440
    source_stale_hours: int = 36
    smtp_host: str | None = None
    smtp_port: int = 1025
    smtp_from: str = "tenders@noavia.local"

    model_config = SettingsConfigDict(env_file=".env", extra="ignore")


@lru_cache
def get_settings() -> Settings:
    return Settings()
