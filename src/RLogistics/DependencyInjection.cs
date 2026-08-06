using FluentValidation;
using FluentValidation.AspNetCore;
using RLogistics.Abstractions;
using RLogistics.Caching;
using RLogistics.Data;
using RLogistics.Integrations.Notifications;
using RLogistics.Middleware;
using RLogistics.Patterns.Adapter;
using RLogistics.Patterns.Builder;
using RLogistics.Patterns.Decorator;
using RLogistics.Patterns.Facade;
using RLogistics.Patterns.Repository;
using RLogistics.Patterns.Strategy;
using RLogistics.Security;
using RLogistics.Services;
using RLogistics.Validation;
using Microsoft.EntityFrameworkCore;

namespace RLogistics;

public static class DependencyInjection
{
    /// <summary>Composition root — Dependency Injection (DIP + OCP).</summary>
    public static IServiceCollection AddRLogistics(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpContextAccessor();
        var inMemoryName = configuration["Testing:InMemoryDatabaseName"];
        var isTesting = string.Equals(
            configuration["ASPNETCORE_ENVIRONMENT"] ?? configuration["DOTNET_ENVIRONMENT"],
            "Testing",
            StringComparison.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(inMemoryName) || isTesting)
        {
            services.AddDbContext<RLogisticsDbContext>(options =>
                options.UseInMemoryDatabase(
                    string.IsNullOrWhiteSpace(inMemoryName)
                        ? "RLogisticsTests_" + Guid.NewGuid().ToString("N")
                        : inMemoryName));
        }
        else
        {
            services.AddDbContext<RLogisticsDbContext>(options =>
                options.UseSqlServer(configuration.GetConnectionString("RLogistics")));
        }

        services.Configure<RedisOptions>(configuration.GetSection(RedisOptions.SectionName));
        var redis = configuration.GetSection(RedisOptions.SectionName).Get<RedisOptions>() ?? new RedisOptions();

        if (redis.Enabled)
        {
            services.AddStackExchangeRedisCache(o =>
            {
                o.Configuration = redis.Configuration;
                o.InstanceName = redis.InstanceName;
            });
        }
        else
        {
            services.AddDistributedMemoryCache();
        }

        services.AddSingleton<ICacheService, DistributedCacheService>();

        services.Configure<Genie.GenieOptions>(configuration.GetSection(Genie.GenieOptions.SectionName));
        services.AddHttpClient<Genie.IGenieClient, Genie.GenieClient>((sp, client) =>
        {
            var o = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<Genie.GenieOptions>>().Value;
            client.BaseAddress = new Uri(o.BaseUrl.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        services.AddScoped<PersonaContext>();
        services.AddScoped<IRequestRepository, RequestRepository>();
        services.AddScoped<MockOutboxEmailTransport>();
        services.AddSingleton<PersonalGraphTokenStore>();
        services.AddScoped<GraphMailTransport>();
        services.AddScoped<IEmailTransport, CompositeEmailTransport>();
        services.AddHttpClient(nameof(CompositeTeamsNotifier));
        services.AddScoped<ITeamsNotifier, CompositeTeamsNotifier>();
        services.Configure<NotificationOptions>(configuration.GetSection(NotificationOptions.SectionName));
        services.AddScoped<IEmailNotificationService, EmailNotificationService>();
        services.AddScoped<IDisposalRequestBuilderFactory, DisposalRequestBuilderFactory>();

        // Concrete + Cache + Logging decorators (outermost first for calls)
        services.AddScoped<RequestService>();
        services.AddScoped<IRequestService>(sp =>
        {
            IRequestService core = sp.GetRequiredService<RequestService>();
            var cache = sp.GetRequiredService<ICacheService>();
            var logCache = sp.GetRequiredService<ILogger<CachingRequestServiceDecorator>>();
            core = new CachingRequestServiceDecorator(core, cache, logCache);

            var log = sp.GetRequiredService<ILogger<LoggingRequestServiceDecorator>>();
            var persona = sp.GetRequiredService<PersonaContext>();
            return new LoggingRequestServiceDecorator(core, log, persona);
        });

        services.AddScoped<IRequestWorkflowFacade, RequestWorkflowFacade>();
        services.AddSingleton<IAuthPresentationStrategyFactory, AuthPresentationStrategyFactory>();
        services.AddSingleton<IDispositionMessageStrategy, SanitizeMessageStrategy>();
        services.AddSingleton<IDispositionMessageStrategy, DestroyMessageStrategy>();
        services.AddSingleton<DispositionMessageResolver>();

        services.AddRLogisticsSecurity(configuration);

        services.AddFluentValidationAutoValidation();
        services.AddValidatorsFromAssemblyContaining<CreateRequestDtoValidator>();

        services.AddControllers();
        services.AddRazorPages();
        services.AddSession(options =>
        {
            options.IdleTimeout = TimeSpan.FromHours(Math.Max(1, redis.SessionIdleHours));
            options.Cookie.HttpOnly = true;
            options.Cookie.IsEssential = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        });

        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v1", new()
            {
                Title = "RLogistics Core API",
                Version = "v1",
                Description = "Auth: POST /api/auth/token (JWT) or header X-Api-Key. Redis optional via Redis:Enabled. GENIE sidecar uses this API."
            });
        });

        return services;
    }

    public static WebApplication UseRLogisticsPipeline(this WebApplication app)
    {
        app.UseMiddleware<ExceptionHandlingMiddleware>();
        app.UseMiddleware<CorrelationIdMiddleware>();
        app.UseMiddleware<SecurityHeadersMiddleware>();

        app.UseSwagger();
        app.UseSwaggerUI();
        app.UseStaticFiles();
        app.UseRouting();
        app.UseSession();
        app.UseAuthentication();
        app.UseMiddleware<PersonaMiddleware>();
        app.UseAuthorization();

        app.MapControllers();
        app.MapRazorPages();
        return app;
    }
}
