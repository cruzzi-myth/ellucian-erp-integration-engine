// Controllers/IntegrationController.cs

using EllucianIntegrationEngine.Adapters;
using EllucianIntegrationEngine.Core;
using EllucianIntegrationEngine.Infrastructure.Data;
using EllucianIntegrationEngine.Infrastructure.Outbox;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EllucianIntegrationEngine.Controllers;

[ApiController]
[Route("api/integration")]
public sealed class IntegrationController : ControllerBase
{
    private readonly OutboxPublisher _outbox;
    private readonly IntegrationDbContext _db;
    private readonly IEnumerable<IIntegrationAdapter> _adapters;
    private readonly ConsoleDlqRouter _dlq;

    public IntegrationController(OutboxPublisher outbox, IntegrationDbContext db,
        IEnumerable<IIntegrationAdapter> adapters, ConsoleDlqRouter dlq)
    {
        _outbox = outbox; _db = db; _adapters = adapters; _dlq = dlq;
    }

    /// <summary>
    /// Submit a new integration request. The tenant is resolved from X-Tenant-Id header.
    /// The message is written to the OutboxMessages table — the background processor
    /// will pick it up within 2 seconds and begin dispatching.
    /// </summary>
    [HttpPost("submit")]
    public async Task<IActionResult> Submit([FromBody] SubmitRequest req)
    {
        var tenant = HttpContext.Items[TenantContext.HttpContextKey] as TenantContext;
        if (tenant is null) return Unauthorized(new { error = "Tenant not resolved" });

        // Validate adapter exists
        var adapterNames = _adapters.Select(a => a.AdapterName).ToList();
        if (!adapterNames.Contains(req.AdapterName, StringComparer.OrdinalIgnoreCase))
            return BadRequest(new { error = $"Unknown adapter '{req.AdapterName}'", available = adapterNames });

        var txId = Guid.NewGuid();
        var msg  = await _outbox.PublishAsync(
            tenant.TenantId,
            req.AdapterName,
            req.OperationType,
            req.Payload ?? new { },
            txId);

        return Accepted(new
        {
            messageId      = msg.Id,
            tenantId       = tenant.TenantId,
            institution    = tenant.InstitutionName,
            adapterName    = req.AdapterName,
            operationType  = req.OperationType,
            status         = msg.Status.ToString(),
            idempotencyKey = msg.IdempotencyKey,
            note           = "Message queued. Background processor will pick it up within 2 seconds."
        });
    }

    /// <summary>
    /// View all outbox messages and their current status.
    /// Great for watching retry counts climb during a demo.
    /// </summary>
    [HttpGet("outbox")]
    public async Task<IActionResult> GetOutbox([FromQuery] string? status = null)
    {
        var query = _db.OutboxMessages.AsQueryable();

        if (!string.IsNullOrWhiteSpace(status) &&
            Enum.TryParse<OutboxStatus>(status, true, out var s))
            query = query.Where(m => m.Status == s);

        var messages = await query.OrderByDescending(m => m.CreatedAt).Take(50).ToListAsync();

        return Ok(new
        {
            total    = messages.Count,
            messages = messages.Select(m => new
            {
                id             = m.Id,
                tenantId       = m.TenantId,
                adapterName    = m.AdapterName,
                operationType  = m.OperationType,
                status         = m.Status.ToString(),
                retryCount     = m.RetryCount,
                nextRetryAt    = m.NextRetryAt,
                lastErrorCode  = m.LastErrorCode,
                createdAt      = m.CreatedAt,
                processedAt    = m.ProcessedAt
            })
        });
    }

    /// <summary>Returns messages currently in the Dead Letter Queue.</summary>
    [HttpGet("dlq")]
    public IActionResult GetDlq()
    {
        var entries = _dlq.GetAll();
        return Ok(new { total = entries.Count, entries });
    }

    /// <summary>Returns available adapters for the submit form.</summary>
    [HttpGet("adapters")]
    public IActionResult GetAdapters() =>
        Ok(_adapters.Select(a => a.AdapterName).ToList());
}

public sealed record SubmitRequest(
    string AdapterName,
    string OperationType,
    object? Payload
);
