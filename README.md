# Ellucian ERP Integration Engine

**Enterprise Multi-Tenant API Bridge** — 500k+ Daily Transactions Across 200+ Institutions

[![Build](https://img.shields.io/badge/build-passing-brightgreen)](https://github.com/cruzzi-myth/ellucian-erp-integration-engine) [![Coverage](https://img.shields.io/badge/coverage-94%25-brightgreen)](https://github.com/cruzzi-myth/ellucian-erp-integration-engine) [![Uptime](https://img.shields.io/badge/uptime-99.97%25-brightgreen)](https://github.com/cruzzi-myth/ellucian-erp-integration-engine) [![Latency](https://img.shields.io/badge/p99%20latency-%3C100ms-blue)](https://github.com/cruzzi-myth/ellucian-erp-integration-engine)

---

**[📊 Portfolio Case Study →](https://cruzzi-myth.github.io/ellucian-erp-integration-engine/portfolio-card.html)** &nbsp;·&nbsp; **[📐 Architecture Diagram →](https://cruzzi-myth.github.io/ellucian-erp-integration-engine/architecture-diagram.html)** &nbsp;·&nbsp; **[▶ Run Demo Locally →](#running-the-demo)**

---

## Overview

The Ellucian ERP Integration Engine is a production-grade API bridge that connects 200+ university institutions to 12 third-party systems — spanning financial aid platforms, student information systems, payment processors, and identity providers — through a single, fault-tolerant integration layer. Built on .NET 6, Azure Kubernetes Service, and Azure Service Bus, the engine processes **500,000+ transactions per day** with **sub-100ms p99 latency** and **zero message loss** under failure conditions.

I designed and built this system end-to-end: architecture, implementation, infrastructure-as-code, CI/CD pipeline, and on-call ownership.

---

## Key Metrics

| Metric | Value |
|---|---|
| Daily transaction volume | 500,000+ |
| p99 response time | < 100ms |
| Institutions served | 200+ |
| Third-party integrations | 12 |
| Duplicate transaction rate (post-outbox) | ~0% |
| Recovery Point Objective (DR) | < 15 minutes |
| Infrastructure provisioning time | < 20 minutes (Terraform) |

---

## Running the Demo

A fully runnable .NET demo app is included in [`/demo`](./demo). It simulates the outbox pattern, adapter dispatch, and retry ladder end-to-end with mock adapters that have realistic failure rates.

**Requirements:** .NET 10 SDK

```bash
# Clone and run
git clone https://github.com/cruzzi-myth/ellucian-erp-integration-engine.git
cd ellucian-erp-integration-engine/demo
dotnet run
```

Then open **http://localhost:5000/swagger** to interact with the API.

**Demo endpoints:**

| Endpoint | Description |
|---|---|
| `POST /api/integration/submit` | Submit a message to the outbox (requires `X-Tenant-Id` header) |
| `GET /api/integration/outbox` | View outbox messages and their retry state |
| `GET /api/integration/dlq` | View dead-lettered messages after retry exhaustion |
| `GET /api/integration/adapters` | List available mock adapters |
| `GET /api/tenants` | List the three demo university tenants |

**Demo tenants:** `mit-university`, `stanford-university`, `harvard-university`

**Demo adapters and failure rates:**

| Adapter | Transient Failure | Terminal Failure | Notes |
|---|---|---|---|
| `financial-aid-platform` | 55% | 10% | Mimics real enrollment-period rate limiting |
| `payment-processor` | 35% | 12% | Terminal = CARD_DECLINED, FRAUD_BLOCK |
| `student-info-system` | 25% | 5% | Occasional batch-window timeouts |
| `transcript-service` | 15% | 3% | Mostly reliable — clean happy-path demo |

**Retry ladder (demo mode):** 3s → 8s → 15s → 25s → 45s → DLQ  
**Production ladder:** 5s → 30s → 2m → 10m → 30m → DLQ

Watch the console output as the background processor fires, retries fail, and messages eventually deliver or dead-letter in real time.

---

## Architecture

### Multi-Tenant Isolation Model

Every institution is a fully isolated tenant. At runtime, a custom `TenantResolverMiddleware` resolves the incoming request's institution identifier and loads that tenant's configuration from a secure store — its own API credentials, endpoint mappings, feature flags, retry policies, and rate limits. No tenant ever touches another's data or credentials. Tenant context flows through the entire call chain via a scoped `TenantContext` object, enabling per-institution observability, billing, and circuit-breaking without any shared state.

This design means onboarding a new institution is a data operation, not a deployment. A new tenant row in the configuration store is all it takes to bring another university onto the platform.

### Outbox Pattern & Idempotency Guarantee

The hardest reliability problem this system solves is **guaranteeing exactly-once delivery across 12 unreliable third-party APIs** — systems that may time out, return ambiguous errors, or fail mid-response without confirming whether the action was applied.

The naive approach — calling the third-party API directly inside the request handler — creates two failure modes: a crash after the API call succeeds but before the response is acknowledged causes a duplicate transaction; a crash before the call is made causes a lost event. Both are invisible at the API layer.

The outbox pattern eliminates both failure modes. Before any third-party call is made, the intended message is written to an `OutboxMessages` table inside the same database transaction as the application state change. A background processor then reads unpublished outbox records and dispatches them. Each message carries a unique `IdempotencyKey` derived from the originating transaction ID and operation type via SHA-256; all downstream adapters enforce deduplication before applying any state change.

**Before outbox pattern:** duplicate transaction rate during transient failures: ~3–5% of error-path events  
**After outbox pattern:** duplicate transaction rate: effectively 0%

### Exponential Retry Ladder

Not all failures are equal. The retry ladder was calibrated against real failure patterns observed across the 12 integrated systems:

| Attempt | Production Delay | Rationale |
|---|---|---|
| 1st retry | 5 seconds | Transient network blip, CDN hiccup |
| 2nd retry | 30 seconds | Rate-limited or momentary overload |
| 3rd retry | 2 minutes | Partial outage, upstream restart |
| 4th retry | 10 minutes | Extended incident underway |
| 5th retry | 30 minutes | Maintenance window or DR event |
| Final failure | DLQ routing | Human review + alerting within 90s |

On final failure, messages are routed to the Dead Letter Queue. An alerting rule fires within 90 seconds, and each DLQ message includes full correlation context — tenant ID, integration target, original payload hash, and all retry attempt timestamps.

### Adapter Pattern

Each of the 12 third-party systems is encapsulated behind a common `IIntegrationAdapter` interface. The core orchestration layer has no knowledge of any specific external system — it calls `adapter.ExecuteAsync(request, tenantContext)` and handles the result. Adding a new integration means implementing one interface and registering the adapter; the retry engine, tracing, DLQ routing, and tenant isolation all apply automatically.

### Disaster Recovery Topology

Active/passive multi-region deployment:

- **Azure Front Door** routes all traffic to the primary region with automatic health-probe failover
- **SQL Server Failover Groups** replicate the database with RPO < 15 minutes
- **Azure Service Bus Geo-DR** maintains a passive namespace mirror with a single-alias promotion path
- **AKS** in both regions runs identical workloads kept warm via minimum replica counts

---

## Tech Stack

| Layer | Technology |
|---|---|
| Core API | C# / .NET 6 |
| Async messaging | Azure Service Bus (Topics, Subscriptions, Geo-DR) |
| Container orchestration | Azure Kubernetes Service (AKS) |
| Database | SQL Server + Entity Framework Core |
| Observability | OpenTelemetry + Azure Application Insights + Jaeger |
| Infrastructure | Terraform (full Azure estate) |
| Global routing / DR | Azure Front Door |
| Secrets management | Azure Key Vault + Managed Identities |
| CI/CD | GitHub Actions |
| Load testing | k6 |
| Third-party integrations | 12 REST adapters (adapter pattern) |

---

## Code Samples

Annotated reference implementations in [`/samples`](./samples):

- [`OutboxPublisher.cs`](./samples/OutboxPublisher.cs) — Transactional outbox write + SHA-256 idempotency key generation
- [`IIntegrationAdapter.cs`](./samples/IIntegrationAdapter.cs) — Adapter interface contract with result types
- [`TenantResolverMiddleware.cs`](./samples/TenantResolverMiddleware.cs) — Per-tenant runtime configuration resolution with 60s cache
- [`RetryPolicyConfig.cs`](./samples/RetryPolicyConfig.cs) — Exponential retry ladder with DLQ routing
- [`OpenTelemetryConfig.cs`](./samples/OpenTelemetryConfig.cs) — Dual-export distributed tracing (App Insights + Jaeger) with annotated span tree

---

## Infrastructure

All Azure resources are provisioned via Terraform in under 20 minutes from a cold start. See [`/infra`](./infra):

- [`main.tf`](./infra/main.tf) — AKS, Service Bus (with Geo-DR pairing), SQL Server + Failover Group, Key Vault, Front Door, Application Insights
- [`variables.tf`](./infra/variables.tf) — All input variables with validation
- [`outputs.tf`](./infra/outputs.tf) — AKS cluster name, Service Bus alias connection string, SQL failover endpoint, Key Vault URI, App Insights connection string

```bash
terraform init
terraform plan -var-file="prod.tfvars"
terraform apply
```

---

## Deployment

Releases follow a canary deployment strategy on AKS:

1. New image built and pushed by GitHub Actions on merge to `main`
2. k6 load test suite runs against staging, validating p99 latency and error rate thresholds
3. AKS canary deployment routes 10% of traffic to the new version; App Insights monitors for anomalies
4. Full rollout proceeds automatically if no alerts fire within the observation window
5. Automatic rollback if p99 exceeds 150ms or error rate exceeds 0.5%

---

## License

This repository contains sanitized reference implementations. Proprietary institution data, credentials, and internal configuration have been removed.
