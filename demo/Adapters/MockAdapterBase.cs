// Adapters/MockAdapterBase.cs
// Base class for all demo adapters. Simulates realistic third-party API behavior:
// random transient failures, occasional terminal rejections, and variable latency.

using EllucianIntegrationEngine.Core;

namespace EllucianIntegrationEngine.Adapters;

public abstract class MockAdapterBase : IIntegrationAdapter
{
    public abstract string AdapterName { get; }

    // Override in each adapter to set how often it fails
    protected virtual double TransientFailureRate => 0.45;  // 45% chance of transient failure
    protected virtual double TerminalFailureRate  => 0.08;  // 8%  chance of terminal failure
    protected virtual int    MinLatencyMs         => 80;
    protected virtual int    MaxLatencyMs         => 400;

    protected virtual string[] TransientErrors => [
        "TIMEOUT", "RATE_LIMITED", "SERVICE_UNAVAILABLE", "GATEWAY_TIMEOUT"
    ];
    protected virtual string[] TerminalErrors => [
        "INVALID_PAYLOAD", "UNAUTHORIZED", "RESOURCE_NOT_FOUND"
    ];

    private static readonly Random _rng = new();

    public async Task<AdapterResult> ExecuteAsync(
        IntegrationRequest request, TenantContext tenant, CancellationToken ct = default)
    {
        // Simulate network latency
        var latency = _rng.Next(MinLatencyMs, MaxLatencyMs);
        await Task.Delay(latency, ct);

        var roll = _rng.NextDouble();

        if (roll < TerminalFailureRate)
        {
            var errorCode = TerminalErrors[_rng.Next(TerminalErrors.Length)];
            Console.WriteLine($"[31m[{AdapterName,-26}][0m TERMINAL  code={errorCode} latency={latency}ms");
            return AdapterResult.Terminal(errorCode, $"Terminal failure from {AdapterName}: {errorCode}");
        }

        if (roll < TerminalFailureRate + TransientFailureRate)
        {
            var errorCode = TransientErrors[_rng.Next(TransientErrors.Length)];
            Console.WriteLine($"[33m[{AdapterName,-26}][0m FAILED    code={errorCode} latency={latency}ms");
            return AdapterResult.Transient(errorCode, $"Transient failure from {AdapterName}: {errorCode}");
        }

        Console.WriteLine($"[32m[{AdapterName,-26}][0m SUCCESS   op={request.OperationType} latency={latency}ms");
        return AdapterResult.Success();
    }
}
