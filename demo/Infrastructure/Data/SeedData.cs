// Infrastructure/Data/SeedData.cs
// Seeds 3 demo institutions and registers them in the tenant store.

using EllucianIntegrationEngine.Core;

namespace EllucianIntegrationEngine.Infrastructure.Data;

public sealed class InMemoryTenantConfigStore : ITenantConfigStore
{
    private static readonly Dictionary<string, TenantConfig> _tenants = new()
    {
        ["mit-university"] = new TenantConfig
        {
            TenantId    = "mit-university",
            InstitutionName = "Massachusetts Institute of Technology",
            IsActive    = true
        },
        ["stanford-university"] = new TenantConfig
        {
            TenantId    = "stanford-university",
            InstitutionName = "Stanford University",
            IsActive    = true
        },
        ["harvard-university"] = new TenantConfig
        {
            TenantId    = "harvard-university",
            InstitutionName = "Harvard University",
            IsActive    = true
        }
    };

    public Task<TenantConfig?> GetAsync(string tenantId) =>
        Task.FromResult(_tenants.TryGetValue(tenantId.ToLowerInvariant(), out var cfg) ? cfg : null);

    public IEnumerable<TenantConfig> GetAll() => _tenants.Values;
}
