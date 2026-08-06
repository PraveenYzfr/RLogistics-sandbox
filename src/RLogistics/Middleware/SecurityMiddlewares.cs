using System.Diagnostics;

namespace RLogistics.Middleware;

/// <summary>Adds X-Correlation-Id for distributed tracing across UI/API/GENIE.</summary>
public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Correlation-Id";

    public async Task InvokeAsync(HttpContext context)
    {
        var id = context.Request.Headers.TryGetValue(HeaderName, out var existing) &&
                 !string.IsNullOrWhiteSpace(existing)
            ? existing.ToString()
            : Guid.NewGuid().ToString("N");

        context.TraceIdentifier = id;
        context.Response.Headers[HeaderName] = id;
        using (context.RequestServices.GetRequiredService<ILoggerFactory>()
                   .CreateLogger("Correlation")
                   .BeginScope(new Dictionary<string, object> { ["CorrelationId"] = id }))
        {
            await next(context);
        }
    }
}

/// <summary>Enterprise security response headers (defense in depth).</summary>
public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(() =>
        {
            var h = context.Response.Headers;
            h["X-Content-Type-Options"] = "nosniff";
            h["X-Frame-Options"] = "DENY";
            h["Referrer-Policy"] = "no-referrer";
            h["X-XSS-Protection"] = "0";
            h["Permissions-Policy"] = "geolocation=(), microphone=(), camera=()";
            if (!h.ContainsKey("Content-Security-Policy"))
                h["Content-Security-Policy"] = "default-src 'self'; img-src 'self' data:; style-src 'self' 'unsafe-inline'; script-src 'self' 'unsafe-inline'";
            return Task.CompletedTask;
        });
        await next(context);
    }
}

/// <summary>Central exception → RFC-style JSON for API paths (prevents stack leaks).</summary>
public sealed class ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> log)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex) when (context.Request.Path.StartsWithSegments("/api"))
        {
            if (ex is UnauthorizedAccessException u)
            {
                log.LogWarning(u, "Forbidden");
                await Write(context, StatusCodes.Status403Forbidden, u.Message);
                return;
            }
            if (ex is KeyNotFoundException k)
            {
                await Write(context, StatusCodes.Status404NotFound, k.Message);
                return;
            }
            if (ex is InvalidOperationException inv)
            {
                await Write(context, StatusCodes.Status400BadRequest, inv.Message);
                return;
            }
            log.LogError(ex, "Unhandled error {Trace}", context.TraceIdentifier);
            await Write(context, StatusCodes.Status500InternalServerError, "An unexpected error occurred.");
        }
    }

    private static Task Write(HttpContext ctx, int status, string message)
    {
        if (ctx.Response.HasStarted) return Task.CompletedTask;
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "application/json";
        return ctx.Response.WriteAsJsonAsync(new
        {
            error = message,
            correlationId = ctx.TraceIdentifier,
            trace = Activity.Current?.Id
        });
    }
}
