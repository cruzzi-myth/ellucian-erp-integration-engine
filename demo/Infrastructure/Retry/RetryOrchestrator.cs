// Infrastructure/Retry/RetryOrchestrator.cs
// Executes ONE attempt per call. The background processor controls the scheduling
// between attempts — no thread.Sleep here, delays are stored as NextRetryAt in the DB.
//
// Demo retry ladder (shortened from production for watchability):
//   Attempt 1 fail → retry in  3s
//   Attempt 2 fail → retry in  8s
//   Attempt 3 fail → retry in 15s
//   Attempt 4 fail → retry in 25s
//   Attempt 5 fail → retry in 45s → DLQ

using EllucianIntegrationEngine.Core;
using EllucianIntegrationEngine.Infrastructure.Data;

namespace EllucianIntegrationEngine.Infrastructure.Retry;

public sealed class RetryOrchestrator
{
    // Demo delays — production uses 5s/30s/2m/10m/30m
    public static readonly int[] DelaysSeconds = [3, 8, 15, 25, 45];
    public static int MaxAttempts => DelaysSeconds.Length;

    private readonly IntegrationDbContext _db;
    private readonly IDeadLetterRouter _dlq;
    private readonly ILogger<RetryOrchestrator> _log;

    public RetryOrchestrator(IntegrationDbContext db, IDeadLetterRouter dlq,
        ILogger<RetryOrchestrator> log)
    {
        _db = db; _dlq = dlq; _log = log;
    }

    public async Task ProcessAsync(OutboxMessage msg, IIntegrationAdapter adapter,
        TenantContext tenant, CancellationToken ct)
    {
        var attempt = msg.RetryCount + 1;
        Console.WriteLine($"[35m[Retry]   [0mAttempt {attempt}/{MaxAttempts} → adapter={adapter.AdapterName} tenant={tenant.TenantId} id={msg.Id.ToString()[..8]}...");

        // Mark as processing
        msg.Status = OutboxStatus.Processing;
        await _db.SaveChangesAsync(ct);

        AdapterResult result;
        try
        {
            var request = new IntegrationRequest
            {
                IdempotencyKey = msg.IdempotencyKey,
                OperationType  = msg.OperationType,
                Payload        = msg.Payload
            };
            result = await adapter.ExecuteAsync(request, tenant, ct);
        }
        catch (Exception ex)
        {
            result = AdapterResult.Transient("UNHANDLED_EXCEPTION", ex.Message);
        }

        if (result.IsSuccess)
        {
            msg.Status      = OutboxStatus.Delivered;
            msg.ProcessedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            Console.WriteLine($"[32m[Retry]   [0m✅  Delivered after {attempt} attempt(s) → adapter={adapter.AdapterName} tenant={tenant.TenantId}");
            return;
        }

        // Record the failure
        msg.LastErrorCode    = result.ErrorCode;
        msg.LastErrorMessage = result.ErrorMessage;
        msg.RetryCount       = attempt;

        if (result.IsTerminalFailure || attempt >= MaxAttempts)
        {
            var reason = result.IsTerminalFailure ? "terminal failure" : "retries exhausted";
            Console.WriteLine($"[31m[Retry]   [0m💀  DLQ → {reason} | code={result.ErrorCode} tenant={tenant.TenantId} id={msg.Id.ToString()[..8]}...");
            msg.Status      = OutboxStatus.DeadLettered;
            msg.ProcessedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            await _dlq.RouteAsync(msg, result);
            return;
        }

        // Schedule next retry
        var delaySecs    = DelaysSeconds[attempt - 1];
        msg.Status       = OutboxStatus.Pending;
        msg.NextRetryAt  = DateTime.UtcNow.AddSeconds(delaySecs);
        await _db.SaveChangesAsync(ct);
        Console.WriteLine($"[33m[Retry]   [0m⏳  Failed (attempt {attempt}/{MaxAttempts}) → code={result.ErrorCode} | next retry in {delaySecs}s");
    }
}
