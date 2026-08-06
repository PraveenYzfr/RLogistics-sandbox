using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;

namespace RLogistics.Caching;

public interface ICacheService
{
    Task<T?> GetAsync<T>(string key, CancellationToken ct = default);
    Task SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken ct = default);
    Task RemoveAsync(string key, CancellationToken ct = default);
    Task RemoveByPrefixAsync(string prefix, CancellationToken ct = default);
}

/// <summary>JSON distributed cache (Redis or in-memory). Decorator uses this for request/vendor reads.</summary>
public sealed class DistributedCacheService(
    IDistributedCache cache,
    IOptions<RedisOptions> options,
    ILogger<DistributedCacheService> log) : ICacheService
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);
    private readonly RedisOptions _opts = options.Value;

    public async Task<T?> GetAsync<T>(string key, CancellationToken ct = default)
    {
        try
        {
            var bytes = await cache.GetAsync(key, ct);
            if (bytes is null || bytes.Length == 0) return default;
            return JsonSerializer.Deserialize<T>(bytes, JsonOpts);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Cache get failed for {Key}", key);
            return default;
        }
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken ct = default)
    {
        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(value, JsonOpts);
            var entry = new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = ttl ?? TimeSpan.FromSeconds(_opts.DefaultTtlSeconds)
            };
            await cache.SetAsync(key, bytes, entry, ct);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Cache set failed for {Key}", key);
        }
    }

    public async Task RemoveAsync(string key, CancellationToken ct = default)
    {
        try { await cache.RemoveAsync(key, ct); }
        catch (Exception ex) { log.LogWarning(ex, "Cache remove failed for {Key}", key); }
    }

    /// <summary>
    /// Memory/Redis IDistributedCache has no prefix delete; we track known keys per logical group when needed.
    /// Callers invalidate specific keys (request:{id}, vendors:all).
    /// </summary>
    public Task RemoveByPrefixAsync(string prefix, CancellationToken ct = default) =>
        Task.CompletedTask; // intentional no-op — explicit key invalidation preferred
}

public static class CacheKeys
{
    public static string Request(int id) => $"request:detail:{id}";
    public static string Vendors(string? type) => $"vendors:{(type ?? "all")}";
    public static string RequestList(string scope) => $"requests:list:{scope}";
}
