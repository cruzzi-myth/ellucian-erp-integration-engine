// OpenTelemetryConfig.cs
// Distributed tracing configuration for the integration engine.
//
// Every third-party API call produces a named span. Every span carries:
//   - tenant.id       → which institution triggered this call
//   - adapter.name    → which of the 12 systems was called
//   - idempotency.key → links the trace back to the originating OutboxMessage
//   - retry.attempt   → which attempt in the retry ladder this span represents
//   - correlation.id  → propagated from the inbound HTTP request header
//
// Traces export to two backends simultaneously:
//   - Azure Application Insights (production alerting, live metrics)
//   - Jaeger (local dev + staging trace exploration)
//
// With this setup, a DLQ alert can be triaged in seconds:
// search by tenant ID + idempotency key → see every retry attempt as a span,
// each with its own error code, duration, and adapter response payload.

using System;
using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace EllucianIntegrationEngine.Observability;

public static class OpenTelemetryConfig
{
    // One ActivitySource per service — spans created from this source
    // are automatically picked up by the tracer provider registered below.
    public static readonly ActivitySource IntegrationSource =
        new("EllucianIntegrationEngine", version: "1.0.0");

    public static readonly ActivitySource OutboxSource =
        new("EllucianIntegrationEngine.Outbox", version: "1.0.0");

    /// <summary>
    /// Registers OpenTelemetry tracing in the DI container.
    /// Call this from Program.cs / Startup.cs.
    /// </summary>
    public static IServiceCollection AddIntegrationTracing(
        this IServiceCollection services,
        string appInsightsConnectionString,
        string jaegerEndpoint)
    {
        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(
                    serviceName: "integration-engine",
                    serviceVersion: "1.0.0")
                // Environment tag appears on every span — makes it trivial to
                // filter prod vs staging in the trace explorer
                .AddAttributes(new[]
                {
                    new System.Collections.Generic.KeyValuePair<string, object>("deployment.environment",
                        Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "unknown")
                }))
            .WithTracing(tracing => tracing
                // Instrument our own activity sources
                .AddSource(IntegrationSource.Name)
                .AddSource(OutboxSource.Name)
                // Auto-instrument inbound HTTP requests (ASP.NET Core)
                .AddAspNetCoreInstrumentation(opts =>
                {
                    // Don't trace health check endpoints — they'd drown the signal
                    opts.Filter = ctx => !ctx.Request.Path.StartsWithSegments("/health");
                    opts.RecordException = true;
                })
                // Auto-instrument outbound HTTP calls made by adapter HttpClients
                .AddHttpClientInstrumentation(opts =>
                {
                    opts.RecordException = true;
                    // Capture request/response bodies selectively — only on errors
                    // to avoid PII leakage and trace bloat
                    opts.FilterHttpRequestMessage = req => true;
                })
                // Auto-instrument EF Core queries (OutboxMessages reads/writes)
                .AddEntityFrameworkCoreInstrumentation(opts =>
                {
                    opts.SetDbStatementForText = false; // omit SQL text — may contain tenant data
                    opts.SetDbStatementForStoredProcedure = true;
                })
                // Dual export: App Insights for prod alerting, Jaeger for dev exploration
                .AddAzureMonitorTraceExporter(opts =>
                {
                    opts.ConnectionString = appInsightsConnectionString;
                })
                .AddOtlpExporter(opts =>
                {
                    // Jaeger accepts OTLP over gRPC on port 4317 by default
                    opts.Endpoint = new Uri(jaegerEndpoint);
                })
                // Sample 100% of traces in prod — at 500k tx/day this is ~6 tx/sec,
                // which is well within App Insights ingestion limits.
                // Revisit if volume grows 10x.
                .SetSampler(new AlwaysOnSampler()));

        return services;
    }
}

/// <summary>
/// Helper for creating well-structured integration spans.
/// Used by RetryOrchestrator to wrap every adapter call in a named span
/// with all the attributes needed for triage.
/// </summary>
public static class IntegrationSpans
{
    /// <summary>
    /// Creates a span for a single adapter execution attempt.
    /// Attach this to the retry loop — each attempt gets its own child span.
    /// </summary>
    public static Activity? StartAdapterCall(
        string adapterName,
        string tenantId,
        string idempotencyKey,
        string operationType,
        int attemptNumber)
    {
        var activity = OpenTelemetryConfig.IntegrationSource.StartActivity(
            // Span name format follows OTel semantic conventions for RPC calls:
            // "{system}.{operation}" — makes spans groupable in App Insights
            $"adapter.{adapterName}.{operationType}",
            ActivityKind.Client);

        if (activity is null) return null; // tracing disabled or not sampled

        activity
            .SetTag("adapter.name", adapterName)
            .SetTag("tenant.id", tenantId)
            .SetTag("idempotency.key", idempotencyKey)
            .SetTag("operation.type", operationType)
            .SetTag("retry.attempt", attemptNumber)
            // peer.service is a standard OTel attribute — surfaces the external
            // system name in App Insights "Dependencies" view
            .SetTag("peer.service", adapterName);

        return activity;
    }

    /// <summary>
    /// Creates a span for an outbox message write.
    /// Links to the parent HTTP request span via the ambient Activity context.
    /// </summary>
    public static Activity? StartOutboxWrite(string tenantId, string operationType, string idempotencyKey)
    {
        return OpenTelemetryConfig.OutboxSource
            .StartActivity("outbox.publish", ActivityKind.Producer)
            ?
            .SetTag("tenant.id", tenantId)
            .SetTag("operation.type", operationType)
            .SetTag("idempotency.key", idempotencyKey)
            .SetTag("messaging.system", "sql-server-outbox")
            .SetTag("messaging.destination", "OutboxMessages");
    }

    /// <summary>
    /// Marks a span as failed with structured error attributes.
    /// Prefer this over SetStatus alone — App Insights surfaces error.type
    /// as a searchable dimension.
    /// </summary>
    public static void RecordFailure(this Activity? activity, string errorCode, string message, bool isTerminal)
    {
        if (activity is null) return;

        activity
            .SetStatus(ActivityStatusCode.Error, message)
            .SetTag("error.type", errorCode)
            .SetTag("error.terminal", isTerminal)
            .AddEvent(new ActivityEvent("integration.failure",
                tags: new ActivityTagsCollection
                {
                    ["error.code"] = errorCode,
                    ["error.message"] = message,
                    ["error.terminal"] = isTerminal
                }));
    }
}

/// <summary>
/// Example of what a full integration call trace looks like.
/// This is the span tree you'd see in Jaeger or App Insights for a single
/// financial-aid disbursement request from University of Example (tenant: "univ-example").
///
/// POST /api/integrations/financial-aid/disbursement [200 OK, 47ms]
/// └── outbox.publish [2ms]
///     tenant.id=univ-example, idempotency.key=a3f7b2..., operation.type=disbursement.create
/// └── adapter.financial-aid-platform.disbursement.create [38ms] (attempt 1)
///     tenant.id=univ-example, peer.service=financial-aid-platform
///     └── HTTP POST https://api.financialaid-vendor.com/v2/disbursements [35ms]
///         http.status_code=200, http.url=https://api.financialaid-vendor.com/v2/disbursements
///
/// On a retry scenario (attempt 1 fails, attempt 2 succeeds):
///
/// POST /api/integrations/financial-aid/disbursement [200 OK, 5089ms]
/// └── outbox.publish [2ms]
/// └── adapter.financial-aid-platform.disbursement.create [44ms] (attempt 1) [ERROR]
///     error.type=TIMEOUT, error.terminal=false
///     └── HTTP POST https://api.financialaid-vendor.com/v2/disbursements [5000ms] [TIMEOUT]
/// └── [5s delay — retry.attempt=1]
/// └── adapter.financial-aid-platform.disbursement.create [38ms] (attempt 2) [OK]
///     └── HTTP POST https://api.financialaid-vendor.com/v2/disbursements [35ms]
///         http.status_code=200
/// </summary>
internal static class TraceExample { /* documentation only — not executable */ }
