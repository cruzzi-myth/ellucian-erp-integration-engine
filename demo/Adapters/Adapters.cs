// Adapters/Adapters.cs — all 4 mock adapters with distinct failure profiles

using EllucianIntegrationEngine.Adapters;
using EllucianIntegrationEngine.Core;

namespace EllucianIntegrationEngine.Adapters;

/// <summary>
/// Financial Aid Platform — highest failure rate in production.
/// This is the integration that prompted the outbox pattern in the first place.
/// Frequently rate-limits during enrollment periods.
/// </summary>
public sealed class FinancialAidAdapter : MockAdapterBase
{
    public override string AdapterName         => "financial-aid-platform";
    protected override double TransientFailureRate => 0.55; // 55% — high, great for demo
    protected override double TerminalFailureRate  => 0.10;
    protected override int    MinLatencyMs         => 150;
    protected override int    MaxLatencyMs         => 600;
    protected override string[] TransientErrors    => ["TIMEOUT", "RATE_LIMITED", "SERVICE_UNAVAILABLE"];
}

/// <summary>
/// Payment Processor — moderate failure rate.
/// Terminal failures represent rejected transactions (bad account, fraud flag).
/// </summary>
public sealed class PaymentProcessorAdapter : MockAdapterBase
{
    public override string AdapterName         => "payment-processor";
    protected override double TransientFailureRate => 0.35;
    protected override double TerminalFailureRate  => 0.12; // higher terminal — payment rejections
    protected override int    MinLatencyMs         => 100;
    protected override int    MaxLatencyMs         => 350;
    protected override string[] TerminalErrors     => ["CARD_DECLINED", "INSUFFICIENT_FUNDS", "FRAUD_BLOCK"];
}

/// <summary>
/// Student Information System — low failure rate, mostly reliable.
/// Occasional timeouts during batch processing windows.
/// </summary>
public sealed class StudentInfoAdapter : MockAdapterBase
{
    public override string AdapterName         => "student-info-system";
    protected override double TransientFailureRate => 0.25;
    protected override double TerminalFailureRate  => 0.05;
    protected override int    MinLatencyMs         => 60;
    protected override int    MaxLatencyMs         => 250;
}

/// <summary>
/// Transcript Service — very reliable, fast.
/// Good for showing a clean happy-path in demo contrast.
/// </summary>
public sealed class TranscriptServiceAdapter : MockAdapterBase
{
    public override string AdapterName         => "transcript-service";
    protected override double TransientFailureRate => 0.15; // 15% — mostly succeeds
    protected override double TerminalFailureRate  => 0.03;
    protected override int    MinLatencyMs         => 40;
    protected override int    MaxLatencyMs         => 180;
}

/// <summary>
/// Routes DLQ messages to the console with a big red alert — plus stores in-memory
/// so the /api/integration/dlq endpoint can return them.
/// </summary>
public sealed class ConsoleDlqRouter : IDeadLetterRouter
{
    private readonly List<DlqEntry> _entries = new();

    public Task RouteAsync(OutboxMessage message, AdapterResult finalResult)
    {
        var entry = new DlqEntry(
            message.Id,
            message.TenantId,
            message.AdapterName,
            message.OperationType,
            message.RetryCount,
            finalResult.ErrorCode ?? "UNKNOWN",
            finalResult.ErrorMessage ?? "",
            DateTime.UtcNow
        );
        lock (_entries) { _entries.Add(entry); }

        Console.WriteLine();
        Console.WriteLine("[31m╔══════════════════════════════════════════════════════════╗[0m");
        Console.WriteLine($"[31m║  🚨 DEAD LETTER QUEUE — Alert firing in 90s             ║[0m");
        Console.WriteLine($"[31m║  Tenant:   {message.TenantId,-46}║[0m");
        Console.WriteLine($"[31m║  Adapter:  {message.AdapterName,-46}║[0m");
        Console.WriteLine($"[31m║  Op:       {message.OperationType,-46}║[0m");
        Console.WriteLine($"[31m║  Retries:  {message.RetryCount,-46}║[0m");
        Console.WriteLine($"[31m║  Error:    {(finalResult.ErrorCode ?? "UNKNOWN"),-46}║[0m");
        Console.WriteLine($"[31m║  Msg ID:   {message.Id.ToString()[..8],-46}║[0m");
        Console.WriteLine("[31m╚══════════════════════════════════════════════════════════╝[0m");
        Console.WriteLine();

        return Task.CompletedTask;
    }

    public IReadOnlyList<DlqEntry> GetAll()
    {
        lock (_entries) { return _entries.ToList(); }
    }
}

public sealed record DlqEntry(
    Guid MessageId,
    string TenantId,
    string AdapterName,
    string OperationType,
    int RetryCount,
    string ErrorCode,
    string ErrorMessage,
    DateTime RoutedAt
);
