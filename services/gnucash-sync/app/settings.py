from pathlib import Path

from pydantic_settings import BaseSettings, SettingsConfigDict

# Load service `.env` regardless of process cwd (e.g. uvicorn started from repo root).
_SERVICE_ROOT = Path(__file__).resolve().parent.parent
_service_env = _SERVICE_ROOT / ".env"
_model_config: dict = {"env_file_encoding": "utf-8", "extra": "ignore"}
if _service_env.is_file():
    _model_config["env_file"] = _service_env


class Settings(BaseSettings):
    model_config = SettingsConfigDict(**_model_config)

    gnucash_postgres_url: str = ""
    sync_api_key: str = ""
    income_account: str = "Income:Charging"
    invoice_tax_table_name: str = "BTW hoog"
    invoice_post_account: str = ""
    invoice_tax_included: bool = False
    seq_server_url: str = ""
    seq_api_key: str = ""
    app_name: str = "gnucash-sync"


_settings_cache: Settings | None = None
_settings_env_mtime: float | None = None


def get_settings() -> Settings:
    """Return settings; reload from ``.env`` when the file mtime changes (uvicorn does not reload on ``.env``)."""
    global _settings_cache, _settings_env_mtime
    mtime: float | None
    try:
        mtime = _service_env.stat().st_mtime if _service_env.is_file() else None
    except OSError:
        mtime = None
    if _settings_cache is None or mtime != _settings_env_mtime:
        _settings_env_mtime = mtime
        _settings_cache = Settings()
    return _settings_cache
