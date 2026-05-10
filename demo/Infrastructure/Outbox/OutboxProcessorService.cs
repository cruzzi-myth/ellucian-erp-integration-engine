// Infrastructure/Outbox/OutboxProcessorService.cs
// Background service that polls the OutboxMessages table every 2 seconds
// and dispatches ready messages to their adapters via the RetryOrchestrator.

using EllucianIntegrationEngine.Core;
using EllucianIntegrationEngine.Infrastructure.Data;
using EllucianIntegrationEngine.Infrastructure.Retry;
using Microsoft.EntityFrameworkCore;

namespace EllucianIntegrationEngine.Infrastructure.Outbox;

public sealed class OutboxProcessorService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OutboxProcessorService> _log;
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    public OutboxProcessorService(IServiceScopeFactory scopeFactory,
        ILogger<OutboxProcessorService> log)
    {
        _scopeFactory = scopeFactory;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Console.WriteLine("[36m[Outbox]  [0mProcessor started — polling every 2s...");

        while (!stoppingToken.IsCancellationRequested)
        {
            await ProcessPendingMessagesAsync(stoppingToken);
            await Task.Delay(PollInterval, stoppingToken);
        }
    }

    private async Task ProcessPendingMessagesAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();

        var db          = scope.ServiceProvider.GetRequiredService<IntegrationDbContext>();
        var orchestrator = scope.ServiceProvider.GetRequiredService<RetryOrchestrator>();
        var adapters    = scope.ServiceProvider.GetRequiredService<IEnumerable<IIntegrationAdapter>>();
        var tenantStore = scope.ServiceProvider.GetRequiredService<ITenantConfigStore>();

        var adapterMap = adapters.ToDictionary(a => a.AdapterName, StringComparer.OrdinalIgnoreCase);

        // Fetch all messages that are due for processing
        var now = DateTime.UtcNow;
        var messages = await db.OutboxMessages
            .Where(m => m.Status == OutboxStatus.Pending && m.NextRetryAt <= now)
            .OrderBy(m => m.CreatedAt)
            .Take(10) // process up to 10 per tick
            .ToListAsync(ct);

        foreach (var msg in messages)
        {
            if (ct.IsCancellationRequested) break;

            // Resolve adapter
            if (!adapterMap.TryGetValue(msg.AdapterName, out var adapter))
            {
                Console.WriteLine($"[31m[Outbox]  [0mNo adapter registered for '{msg.AdapterName}' — sending to DLQ");
                msg.Status = OutboxStatus.DeadLettered;
                msg.LastErrorCode = "ADAPTER_NOT_FOUND";
                await db.SaveChangesAsync(ct);
                continue;
            }

            // Resolve tenant
            var tenantCfg = await tenantStore.GetAsync(msg.TenantId);
            if (tenantCfg is null)
            {
                Console.WriteLine($"[31m[Outbox]  [0mUnknown tenant '{msg.TenantId}' on message {msg.Id.ToString()[..8]}... — skipping");
                msg.Status = OutboxStatus.DeadLettered;
                msg.LastErrorCode = "TENANT_NOT_FOUND";
                await db.SaveChangesAsync(ct);
                continue;
            }

            var tenant = new TenantContext
            {
                TenantId = tenantCfg.TenantId,
                InstitutionName = tenantCfg.InstitutionName
            };

            try
            {
                await orchestrator.ProcessAsync(msg, adapter, tenant, ct);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Unhandled error processing message {MessageId}", msg.Id);
                msg.Status = OutboxStatus.Pending;
                msg.NextRetryAt = DateTime.UtcNow.AddSeconds(5);
                await db.SaveChangesAsync(ct);
            }
        }
    }
}
