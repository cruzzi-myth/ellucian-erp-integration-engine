// Infrastructure/Outbox/OutboxPublisher.cs

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EllucianIntegrationEngine.Core;
using EllucianIntegrationEngine.Infrastructure.Data;

namespace EllucianIntegrationEngine.Infrastructure.Outbox;

public sealed class OutboxPublisher
{
    private readonly IntegrationDbContext _db;

    public OutboxPublisher(IntegrationDbContext db) => _db = db;

    public async Task<OutboxMessage> PublishAsync(
        string tenantId,
        string adapterName,
        string operationType,
        object payload,
        Guid sourceTransactionId)
    {
        // Idempotency key is deterministic: re-submitting the same transaction
        // produces the same key and hits the unique index — no duplicate processing.
        var idempotencyKey = DeriveKey(sourceTransactionId, operationType);

        // Check for duplicate before inserting
        var existing = _db.OutboxMessages.FirstOrDefault(m => m.IdempotencyKey == idempotencyKey);
        if (existing is not null)
        {
            Console.WriteLine($"[33m[Outbox]  [0mDuplicate detected — idempotency key already exists: {idempotencyKey[..12]}...");
            return existing;
        }

        var msg = new OutboxMessage
        {
            TenantId        = tenantId,
            AdapterName     = adapterName,
            OperationType   = operationType,
            Payload         = JsonSerializer.Serialize(payload),
            IdempotencyKey  = idempotencyKey,
            Status          = OutboxStatus.Pending,
            NextRetryAt     = DateTime.UtcNow
        };

        _db.OutboxMessages.Add(msg);
        await _db.SaveChangesAsync();

        Console.WriteLine($"[32m[Outbox]  [0mMessage written → ID={msg.Id.ToString()[..8]}... Op={operationType} Tenant={tenantId}");
        return msg;
    }

    private static string DeriveKey(Guid txId, string opType)
    {
        var input = $"{txId}:{opType}";
        var hash  = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash)[..32];
    }
}
