namespace RLogistics.Caching;

public sealed class RedisOptions
{
    public const string SectionName = "Redis";

    /// <summary>When false, uses in-memory distributed cache (dev without Docker).</summary>
    public bool Enabled { get; set; }

    public string Configuration { get; set; } = "localhost:6379";
    public string InstanceName { get; set; } = "rlogistics:";
    public int DefaultTtlSeconds { get; set; } = 60;
    public int SessionIdleHours { get; set; } = 4;
}
