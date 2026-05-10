// TenantResolverMiddleware.cs
// Resolves per-tenant configuration at runtime from every incoming request.
//
// The multi-tenant model is the foundation of the entire integration engine.
// 200+ institutions share one deployment — but each has isolated credentials,
// endpoint overrides, feature flags, retry policies, and rate limits.
// This middleware resolves all of that context before the request reaches
// any business logic or adapter. Nothing downstream touches the config store.

using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace EllucianIntegrationEngine.Middleware;

public sealed class TenantResolverMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ITenantConfigStore _configStore;
    private readonly IMemoryCache _cache;
    private readonly ILogger<TenantResolverMiddleware> _logger;

    // Tenant config is cached per-tenant with a short TTL.
    // This means runtime changes (new API key, feature flag toggle) propagate
    // within 60 seconds without a deployment — critical for 200+ tenants.
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);

    public TenantResolverMiddleware(
        RequestDelegate next,
        ITenantConfigStore configStore,
        IMemoryCache cache,
        ILogger<TenantResolverMiddleware> logger)
    {
        _next = next;
        _configStore = configStore;
        _cache = cache;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext httpContext)
    {
        // Tenant is identified by a header set by Azure Front Door
        // after validating the institution's mTLS certificate or API key.
        // By the time we see it here, the identity is already verified.
        if (!httpContext.Request.Headers.TryGetValue("X-Tenant-Id", out var tenantIdValues)
            || string.IsNullOrWhiteSpace(tenantIdValues))
        {
            httpContext.Response.StatusCode = 400;
            await httpContext.Response.WriteAsync("Missing X-Tenant-Id header.");
            return;
        }

        var tenantId = tenantIdValues.ToString().Trim().ToLowerInvariant();

        var tenantContext = await ResolveTenantContextAsync(tenantId);

        if (tenantContext is null)
        {
            _logger.LogWarning("Unknown tenant ID: {TenantId}", tenantId);
            httpContext.Response.StatusCode = 401;
            await httpContext.Response.WriteAsync("Unknown tenant.");
            return;
        }

        if (!tenantContext.IsActive)
        {
            _logger.LogWarning("Inactive tenant attempted request: {TenantId}", tenantId);
            httpContext.Response.StatusCode = 403;
            await httpContext.Response.WriteAsync("Tenant account inactive.");
            return;
        }

        // Attach resolved context to the request so all downstream components
        // (orchestrator, adapters, outbox publisher) can access it without
        // hitting the config store again.
        httpContext.Items[TenantContext.HttpContextKey] = tenantContext;

        await _next(httpContext);
    }

    private async Task<TenantContext?> ResolveTenantContextAsync(string tenantId)
    {
        var cacheKey = $"tenant:{tenantId}";

        if (_cache.TryGetValue(cacheKey, out TenantContext? cached))
            return cached;

        var config = await _configStore.GetAsync(tenantId);
        if (config is null) return null;

        var context = new TenantContext
        {
            TenantId = tenantId,
            InstitutionName = config.InstitutionName,
            IsActive = config.IsActive,
            ApiCredentials = config.ApiCredentials,
            EndpointOverrides = config.EndpointOverrides,
            FeatureFlags = config.FeatureFlags,
            RetryPolicy = config.RetryPolicy,
            RateLimitPerMinute = config.RateLimitPerMinute,
            TimeoutMs = config.TimeoutMs
        };

        _cache.Set(cacheKey, context, CacheTtl);
        return context;
    }
}

/// <summary>
/// Carries all per-tenant runtime configuration through the request pipeline.
/// Scoped per request — never shared between tenants.
/// </summary>
public sealed class TenantContext
{
    public const string HttpContextKey = "TenantContext";

    public required string TenantId { get; init; }
    public required string InstitutionName { get; init; }
    public bool IsActive { get; init; }

    /// <summary>
    /// Per-tenant API credentials loaded from Azure Key Vault references.
    /// The config store holds Key Vault URIs, not plaintext secrets.
    /// Actual secret values are resolved by the config store using Managed Identity.
    /// </summary>
    public required ApiCredentialSet ApiCredentials { get; init; }

    /// <summary>
    /// Institution-specific endpoint overrides for any of the 12 adapters.
    /// Most tenants use defaults; large institutions sometimes have
    /// dedicated API environments that require different base URLs.
    /// </summary>
    public IReadOnlyDictionary<string, string> EndpointOverrides { get; init; } = new Dictionary<string, string>();

    /// <summary>
    /// Per-tenant feature flags. Enables gradual rollout of new integrations
    /// or behavior changes without a deployment.
    /// Example flag: "use-v2-financial-aid-api"
    /// </summary>
    public IReadOnlySet<string> FeatureFlags { get; init; } = new HashSet<string>();

    /// <summary>
    /// Retry policy override. If null, the system default ladder is used
    /// (5s / 30s / 2m / 10m / 30m). Some institutions have SLAs that
    /// require faster escalation to DLQ.
    /// </summary>
    public RetryPolicyOverride? RetryPolicy { get; init; }

    public int RateLimitPerMinute { get; init; } = 1000;
    public int TimeoutMs { get; init; } = 5000;
}

public sealed record ApiCredentialSet
{
    public required string ClientId { get; init; }
    public required string ClientSecret { get; init; } // resolved from Key Vault at config load time
    public string? SubscriptionKey { get; init; }
}

public sealed record RetryPolicyOverride
{
    public int MaxAttempts { get; init; } = 5;
    public int[] DelaysSeconds { get; init; } = [5, 30, 120, 600, 1800];
}
