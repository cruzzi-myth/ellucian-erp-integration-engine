// Controllers/TenantController.cs

using EllucianIntegrationEngine.Core;
using Microsoft.AspNetCore.Mvc;

namespace EllucianIntegrationEngine.Controllers;

[ApiController]
[Route("api/tenants")]
public sealed class TenantController : ControllerBase
{
    private readonly ITenantConfigStore _store;
    public TenantController(ITenantConfigStore store) => _store = store;

    /// <summary>List all configured demo tenants.</summary>
    [HttpGet]
    public IActionResult GetAll() =>
        Ok(_store.GetAll().Select(t => new
        {
            tenantId        = t.TenantId,
            institutionName = t.InstitutionName,
            isActive        = t.IsActive,
            note            = $"Use X-Tenant-Id: {t.TenantId} header in requests"
        }));
}
