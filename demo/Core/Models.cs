// Core/Models.cs
// All core domain types: adapter contract, tenant context, outbox message.

namespace EllucianIntegrationEngine.Core;

// ─────────────────────────────────────────────
// ADAPTER CONTRACT
// ─────────────────────────────────────────────

public interface IIntegrationAdapter
{
    string AdapterName { get; }
    Task<AdapterResult> ExecuteAsync(IntegrationRequest request, TenantContext tenant, CancellationToken ct = default);
}

public sealed record IntegrationRequest
{
    public required string IdempotencyKey { get; init; }
    public required string OperationType { get; init; }
    public required object Payload { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}

public sealed record AdapterResult
{
    public bool IsSuccess { get; init; }
    public bool IsTerminalFailure { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }

    public static AdapterResult Success() =>
        new() { IsSuccess = true };

    public static AdapterResult Transient(string code, string message) =>
        new() { IsSuccess = false, IsTerminalFailure = false, ErrorCode = code, ErrorMessage = message };

    public static AdapterResult Terminal(string code, string message) =>
        new() { IsSuccess = false, IsTerminalFailure = true, ErrorCode = code, ErrorMessage = message };
}

// ─────────────────────────────────────────────
// TENANT CONTEXT
// ─────────────────────────────────────────────

public sealed class TenantContext
{
    public const string HttpContextKey = "TenantContext";
    public required string TenantId { get; init; }
    public required string InstitutionName { get; init; }
    public bool IsActive { get; init; } = true;
    public int RateLimitPerMinute { get; init; } = 1000;
    public int TimeoutMs { get; init; } = 5000;
}

public sealed class TenantConfig
{
    public required string TenantId { get; set; }
    public required string InstitutionName { get; set; }
    public bool IsActive { get; set; } = true;
}

public interface ITenantConfigStore
{
    Task<TenantConfig?> GetAsync(string tenantId);
    IEnumerable<TenantConfig> GetAll();
}

// ─────────────────────────────────────────────
// OUTBOX MESSAGE
// ─────────────────────────────────────────────

public sealed class OutboxMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string TenantId { get; set; }
    public required string OperationType { get; set; }
    public required string AdapterName { get; set; }
    public required string Payload { get; set; }
    public required string IdempotencyKey { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime NextRetryAt { get; set; } = DateTime.UtcNow;
    public OutboxStatus Status { get; set; } = OutboxStatus.Pending;
    public int RetryCount { get; set; } = 0;
    public string? LastErrorCode { get; set; }
    public string? LastErrorMessage { get; set; }
    public DateTime? ProcessedAt { get; set; }
}

public enum OutboxStatus
{
    Pending,
    Processing,
    Delivered,
    DeadLettered
}

// ─────────────────────────────────────────────
// DEAD LETTER
// ─────────────────────────────────────────────

public interface IDeadLetterRouter
{
    Task RouteAsync(OutboxMessage message, AdapterResult finalResult);
}
