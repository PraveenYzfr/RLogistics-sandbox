from pydantic_settings import BaseSettings, SettingsConfigDict


class Settings(BaseSettings):
    model_config = SettingsConfigDict(env_file=".env", extra="ignore")

    rlogistics_url: str = "http://localhost:5088"
    rlogistics_api_key: str = "rlogistics-demo-coord-key-change-me"
    redis_url: str = "redis://localhost:6379/0"
    qdrant_url: str = "http://localhost:6333"
    genie_llm_mode: str = "offline"  # offline | openai | azure
    openai_api_key: str = ""
    openai_embedding_model: str = "text-embedding-3-small"
    azure_openai_endpoint: str = ""
    azure_openai_api_key: str = ""
    azure_openai_deployment: str = "gpt-4o-mini"
    azure_openai_embedding_deployment: str = "text-embedding-3-small"
    azure_openai_api_version: str = "2024-10-21"
    # Google Gemini embeddings (API key)
    gemini_api_key: str = ""
    gemini_embedding_model: str = "text-embedding-004"
    # Ollama local / self-hosted embeddings
    ollama_base_url: str = "http://localhost:11434"
    ollama_embedding_model: str = "nomic-embed-text"
    # RAG embeddings: offline | fastembed | ollama | azure_openai | gemini | openai
    # Primary switches: fastembed | ollama (local) | azure_openai | gemini
    rag_embedding_provider: str = "offline"
    rag_embedding_dimensions: int | None = None
    fastembed_model: str = "BAAI/bge-small-en-v1.5"
    genie_port: int = 8090
    cache_ttl_seconds: int = 45
    qdrant_collection: str = "rlogistics_sops"


settings = Settings()
