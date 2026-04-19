from functools import lru_cache

from pydantic_settings import BaseSettings, SettingsConfigDict


class Settings(BaseSettings):
    model_config = SettingsConfigDict(env_file=".env", env_file_encoding="utf-8", extra="ignore")

    gnucash_postgres_url: str = ""
    sync_api_key: str = ""
    income_account: str = "Income:Charging"
    seq_server_url: str = ""
    seq_api_key: str = ""
    app_name: str = "gnucash-sync"


@lru_cache
def get_settings() -> Settings:
    return Settings()
