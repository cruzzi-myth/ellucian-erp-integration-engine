// Program.cs — Ellucian ERP Integration Engine Demo
// Run with: dotnet run
// Then open: http://localhost:5000/swagger

using EllucianIntegrationEngine.Adapters;
using EllucianIntegrationEngine.Core;
using EllucianIntegrationEngine.Infrastructure.Data;
using EllucianIntegrationEngine.Infrastructure.Middleware;
using EllucianIntegrationEngine.Infrastructure.Outbox;
using EllucianIntegrationEngine.Infrastructure.Retry;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// ─── Logging ──────────────────────────────────────────────────────────────
builder.Logging.ClearProviders();
builder.Logging.AddConsole(opts => opts.FormatterName = "simple");
builder.Logging.SetMinimumLevel(LogLevel.Warning); // suppress EF noise; we use Console.WriteLine for demo output

// ─── Services ─────────────────────────────────────────────────────────────
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new()
    {
        Title       = "Ellucian ERP Integration Engine — Demo",
        Version     = "v1",
        Description = "Live demo of outbox pattern, adapter dispatch, and exponential retry ladder across 200+ institutions."
    });
});

// SQLite — zero-setup database for local demo
builder.Services.AddDbContext<IntegrationDbContext>(opts =>
    opts.UseSqlite("Data Source=integration-engine-demo.db"));

// Tenant store (in-memory — 3 pre-seeded institutions)
builder.Services.AddSingleton<ITenantConfigStore, InMemoryTenantConfigStore>();
builder.Services.AddMemoryCache();

// Outbox publisher
builder.Services.AddScoped<OutboxPublisher>();

// Retry orchestrator
builder.Services.AddScoped<RetryOrchestrator>();

// DLQ router (console + in-memory store, singleton so controller can read it)
builder.Services.AddSingleton<ConsoleDlqRouter>();
builder.Services.AddSingleton<IDeadLetterRouter>(sp => sp.GetRequiredService<ConsoleDlqRouter>());

// Adapters — each registered as IIntegrationAdapter so IEnumerable<IIntegrationAdapter>
// can be injected to get all of them at once
builder.Services.AddSingleton<IIntegrationAdapter, FinancialAidAdapter>();
builder.Services.AddSingleton<IIntegrationAdapter, PaymentProcessorAdapter>();
builder.Services.AddSingleton<IIntegrationAdapter, StudentInfoAdapter>();
builder.Services.AddSingleton<IIntegrationAdapter, TranscriptServiceAdapter>();

// Background outbox processor — polls every 2s
builder.Services.AddHostedService<OutboxProcessorService>();

// ─── App ──────────────────────────────────────────────────────────────────
var app = builder.Build();

// Ensure SQLite DB and schema are created on first run
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<IntegrationDbContext>();
    db.Database.EnsureCreated();
}

app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Integration Engine v1");
    c.RoutePrefix = "swagger";
    c.DocumentTitle = "Ellucian ERP Integration Engine";
});

app.UseMiddleware<TenantResolverMiddleware>();
app.MapControllers();

// Startup banner
Console.WriteLine();
Console.WriteLine("[36m╔══════════════════════════════════════════════════════════╗[0m");
Console.WriteLine("[36m║  Ellucian ERP Integration Engine — Demo                  ║[0m");
Console.WriteLine("[36m║  500k+ Daily Transactions · 200+ Institutions · .NET 8   ║[0m");
Console.WriteLine("[36m╠══════════════════════════════════════════════════════════╣[0m");
Console.WriteLine("[36m║  Swagger UI:  http://localhost:5000/swagger               ║[0m");
Console.WriteLine("[36m║  Tenants:     GET /api/tenants                            ║[0m");
Console.WriteLine("[36m║  Submit:      POST /api/integration/submit                ║[0m");
Console.WriteLine("[36m║  Outbox:      GET /api/integration/outbox                 ║[0m");
Console.WriteLine("[36m║  DLQ:         GET /api/integration/dlq                    ║[0m");
Console.WriteLine("[36m╠══════════════════════════════════════════════════════════╣[0m");
Console.WriteLine("[36m║  Demo tenants:  mit-university, stanford-university,      ║[0m");
Console.WriteLine("[36m║                 harvard-university                        ║[0m");
Console.WriteLine("[36m║  Retry ladder:  3s → 8s → 15s → 25s → 45s → DLQ          ║[0m");
Console.WriteLine("[36m╚══════════════════════════════════════════════════════════╝[0m");
Console.WriteLine();

app.Run("http://localhost:5000");
