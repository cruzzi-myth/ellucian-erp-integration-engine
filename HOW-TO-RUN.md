# How to Run the Ellucian ERP Demo

Quick reference for running the live demo locally (for interviews or testing).

---

## Starting the Demo

Open Terminal and run:

```bash
cd ~/Documents/Claude/Projects/Ellucian\ ERP\ Integration\ Engine\ Enterprise\ Paltform/demo
dotnet run
```

Wait a few seconds for the startup banner to appear, then open your browser to:

```
http://localhost:5000/swagger
```

That's it — the live demo is running.

---

## Stopping the Demo

Press **Ctrl+C** in the Terminal window where it's running.

---

## Using the Demo (Interview Flow)

**Step 1 — Pick a tenant** (copy one of these into the `X-Tenant-Id` header):
- `mit-university`
- `stanford-university`
- `harvard-university`

**Step 2 — Pick an adapter** (use `GET /api/integration/adapters` to list them, or paste one):
- `financial-aid-platform` — 55% transient failures, best for showing retries
- `payment-processor` — 35% transient, 12% terminal (CARD_DECLINED, FRAUD_BLOCK)
- `student-info-system` — 25% transient, reliable most of the time
- `transcript-service` — 15% transient, mostly succeeds (good happy-path demo)

**Step 3 — Submit a message** via `POST /api/integration/submit`

Watch the Terminal console — you'll see the outbox processor fire, retries fail and reschedule, and eventually either a green ✅ delivery or a red 🚨 DLQ alert.

**Step 4 — Check the outbox state** via `GET /api/integration/outbox`

**Step 5 — Check dead-lettered messages** via `GET /api/integration/dlq`

---

## Retry Ladder (Demo Mode)

| Attempt | Delay |
|---|---|
| 1st retry | 3 seconds |
| 2nd retry | 8 seconds |
| 3rd retry | 15 seconds |
| 4th retry | 25 seconds |
| 5th retry | 45 seconds → DLQ |

Production uses 5s / 30s / 2m / 10m / 30m.

---

## Live Portfolio Links

| Asset | URL |
|---|---|
| Portfolio Card | https://cruzzi-myth.github.io/ellucian-erp-integration-engine/portfolio-card.html |
| Architecture Diagram | https://cruzzi-myth.github.io/ellucian-erp-integration-engine/architecture-diagram.html |
| GitHub Repo | https://github.com/cruzzi-myth/ellucian-erp-integration-engine |
| Professional Portfolio | https://cruzzi-myth.github.io/Professional-portfolio/ |

---

## If dotnet run Fails

Make sure you have .NET 10 SDK installed:
```bash
dotnet --version
```

If it shows anything below 10.x, download the latest SDK from:
```
https://dotnet.microsoft.com/download
```

If there's a leftover database file causing issues, delete it and retry:
```bash
rm ~/Documents/Claude/Projects/Ellucian\ ERP\ Integration\ Engine\ Enterprise\ Paltform/demo/integration-engine-demo.db 2>/dev/null
dotnet run
```
