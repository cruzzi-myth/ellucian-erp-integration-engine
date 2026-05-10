// Infrastructure/Middleware/TenantResolverMiddleware.cs

using EllucianIntegrationEngine.Core;
using Microsoft.Extensions.Caching.Memory;

namespace EllucianIntegrationEngine.Infrastructure.Middleware;

public sealed class TenantResolverMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ITenantConfigStore _store;
    private readonly IMemoryCache _cache;
    private readonly ILogger<TenantResolverMiddleware> _log;

    public TenantResolverMiddleware(RequestDelegate next, ITenantConfigStore store,
        IMemoryCache cache, ILogger<TenantResolverMiddleware> log)
    {
        _next = next; _store = store; _cache = cache; _log = log;
    }

    public async Task InvokeAsync(HttpContext ctx)
    {
        // Skip middleware for swagger/health endpoints
        if (ctx.Request.Path.StartsWithSegments("/swagger") ||
            ctx.Request.Path.StartsWithSegments("/health") ||
            ctx.Request.Path.StartsWithSegments("/api/tenants"))
        {
            await _next(ctx);
            return;
        }

        if (!ctx.Request.Headers.TryGetValue("X-Tenant-Id", out var tenantIdVal) ||
            string.IsNullOrWhiteSpace(tenantIdVal))
        {
            ctx.Response.StatusCode = 400;
            await ctx.Response.WriteAsJsonAsync(new { error = "Missing X-Tenant-Id header" });
            return;
        }

        var tenantId = tenantIdVal.ToString().Trim().ToLowerInvariant();
        var cacheKey = $"tenant:{tenantId}";

        if (!_cache.TryGetValue(cacheKey, out TenantContext? tenantCtx))
        {
            var cfg = await _store.GetAsync(tenantId);
            if (cfg is null)
            {
                _log.LogWarning("Unknown tenant: {TenantId}", tenantId);
                ctx.Response.StatusCode = 401;
                await ctx.Response.WriteAsJsonAsync(new { error = $"Unknown tenant: {tenantId}" });
                return;
            }

            tenantCtx = new TenantContext
            {
                TenantId = cfg.TenantId,
                InstitutionName = cfg.InstitutionName,
                IsActive = cfg.IsActive
            };
            _cache.Set(cacheKey, tenantCtx, TimeSpan.FromSeconds(60));
        }

        if (!tenantCtx!.IsActive)
        {
            ctx.Response.StatusCode = 403;
            await ctx.Response.WriteAsJsonAsync(new { error = "Tenant account is inactive" });
            return;
        }

        Console.WriteLine($"[36m[Tenant] [0mResolved: {tenantCtx.InstitutionName} ({tenantCtx.TenantId})");
        ctx.Items[TenantContext.HttpContextKey] = tenantCtx;
        await _next(ctx);
    }
}
