from pydantic_settings import BaseSettings, SettingsConfigDict


class Settings(BaseSettings):
    model_config = SettingsConfigDict(env_file=".env", extra="ignore")

    rlogistics_url: str = "http://localhost:5088"
    rlogistics_api_key: str = "rlogistics-demo-coord-key-change-me"
    redis_url: str = "redis://localhost:6379/0"
    qdrant_url: str = "http://localhost:6333"
    # Legacy alias — prefer llm_vendor
    genie_llm_mode: str = "offline"
    # LLM: vendor switch + low/high tier (both tiers stay on chosen vendor)
    # llm_vendor: offline | azure | openai | gemini | ollama | claude
    llm_vendor: str = "offline"
    llm_default_tier: str = "low"  # low | high — default when caller omits tier
    # Azure OpenAI chat deployments
    openai_api_key: str = ""
    openai_embedding_model: str = "text-embedding-3-small"
    openai_llm_low_model: str = "gpt-4o-mini"
    openai_llm_high_model: str = "gpt-4o"
    azure_openai_endpoint: str = ""
    azure_openai_api_key: str = ""
    azure_openai_deployment: str = "gpt-4o-mini"
    azure_openai_llm_low_deployment: str = "gpt-4o-mini"
    azure_openai_llm_high_deployment: str = "gpt-4o"
    azure_openai_embedding_deployment: str = "text-embedding-3-small"
    azure_openai_api_version: str = "2024-10-21"
    # Google Gemini
    gemini_api_key: str = ""
    gemini_embedding_model: str = "text-embedding-004"
    gemini_llm_low_model: str = "gemini-2.0-flash-lite"
    gemini_llm_high_model: str = "gemini-2.0-flash"
    # Anthropic Claude
    anthropic_api_key: str = ""
    claude_llm_low_model: str = "claude-3-5-haiku-latest"
    claude_llm_high_model: str = "claude-3-5-sonnet-latest"
    # Ollama local
    ollama_base_url: str = "http://localhost:11434"
    ollama_embedding_model: str = "nomic-embed-text"
    ollama_llm_low_model: str = "llama3.2:3b"
    ollama_llm_high_model: str = "llama3.1:8b"
    # RAG embeddings
    rag_embedding_provider: str = "offline"
    rag_embedding_dimensions: int | None = None
    fastembed_model: str = "BAAI/bge-small-en-v1.5"
    # Vector store: memory | qdrant | azure_ai_search
    vector_backend: str = "qdrant"
    azure_search_endpoint: str = ""
    azure_search_api_key: str = ""
    azure_search_index: str = "rlogistics-sops"
    azure_search_api_version: str = "2024-07-01"
    genie_port: int = 8090
    cache_ttl_seconds: int = 45
    qdrant_collection: str = "rlogistics_sops"
    # Observability / cost
    cost_embed_per_1m: float = 0.02
    cost_llm_in_per_1m: float = 0.15
    cost_llm_out_per_1m: float = 0.60
    usage_jsonl_path: str = "data/usage.jsonl"
    eval_jsonl_path: str = "data/eval_cases.jsonl"
    # Rate + spend
    spend_enabled: bool = True
    rate_limit_rpm: int = 60
    rate_limit_embeds_day: int = 5000
    spend_limit_usd_day: float = 5.0
    # Eval
    eval_auto_capture: bool = True
    # MCP remote (localhost Streamable HTTP) — empty = use rlogistics_api_key
    mcp_http_enabled: bool = True
    mcp_api_key: str = ""
    # Multi-agent
    agent_max_steps: int = 8


settings = Settings()
