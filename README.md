# Ellucian ERP Integration Engine

**Enterprise Multi-Tenant API Bridge** — 500k+ Daily Transactions Across 200+ Institutions

[![Build](https://img.shields.io/badge/build-passing-brightgreen)](https://github.com) [![Coverage](https://img.shields.io/badge/coverage-94%25-brightgreen)](https://github.com) [![Uptime](https://img.shields.io/badge/uptime-99.97%25-brightgreen)](https://github.com) [![Latency](https://img.shields.io/badge/p99%20latency-%3C100ms-blue)](https://github.com)

---

> **[📐 View Full Architecture Diagram →](./architecture-diagram.html)**

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

## Architecture

### Multi-Tenant Isolation Model

Every institution is a fully isolated tenant. At runtime, a custom `TenantResolverMiddleware` resolves the incoming request's institution identifier and loads that tenant's configuration from a secure store — its own API credentials, endpoint mappings, feature flags, retry policies, and rate limits. No tenant ever touches another's data or credentials. Tenant context flows through the entire call chain via a scoped `TenantContext` object, enabling per-institution observability, billing, and circuit-breaking without any shared state.

This design means onboarding a new institution is a data operation, not a deployment. A new tenant row in the configuration store is all it takes to bring another university onto the platform.

### Outbox Pattern & Idempotency Guarantee

The hardest reliability problem this system solves is **guaranteeing exactly-once delivery across 12 unreliable third-party APIs** — systems that may time out, return ambiguous errors, or fail mid-response without confirming whether the action was applied.

The naive approach — calling the third-party API directly inside the request handler — creates two failure modes: a crash after the API call succeeds but before the response is acknowledged causes a duplicate transaction; a crash before the call is made causes a lost event. Both are invisible at the API layer.

The outbox pattern eliminates both failure modes. Before any third-party call is made, the intended message is written to an `OutboxMessages` table inside the same database transaction as the application state change. A background processor then reads unpublished outbox records and dispatches them to Azure Service Bus. Each message carries a unique `IdempotencyKey` derived from the originating transaction ID; all downstream adapters enforce deduplication before applying any state change.

**Before outbox pattern:** duplicate transaction rate during transient failures: ~3–5% of error-path events  
**After outbox pattern:** duplicate transaction rate: effectively 0%

The outbox processor uses SQL Server's Change Tracking feature to detect new records with minimal polling overhead, keeping end-to-end delivery latency under 500ms even under load.

### Exponential Retry Ladder

Not all failures are equal. A third-party API that returns HTTP 429 needs seconds; one that's mid-maintenance window needs minutes. The retry ladder was calibrated against real failure patterns observed across the 12 integrated systems:

| Attempt | Delay | Rationale |
|---|---|---|
| 1st retry | 5 seconds | Transient network blip, CDN hiccup |
| 2nd retry | 30 seconds | Rate-limited or momentary overload |
| 3rd retry | 2 minutes | Partial outage, upstream restart |
| 4th retry | 10 minutes | Extended incident underway |
| 5th retry | 30 minutes | Maintenance window or DR event |
| Final failure | DLQ routing | Human review + alerting |

On final failure, messages are routed to a per-tenant Dead Letter Queue on Azure Service Bus. An alerting rule fires within 90 seconds, and each DLQ message includes full correlation context — tenant ID, integration target, original payload hash, and all retry attempt timestamps — so on-call engineers can triage and replay without guesswork.

### Adapter Pattern

Each of the 12 third-party systems is encapsulated behind a common `IIntegrationAdapter` interface. The core orchestration layer has no knowledge of any specific external system — it calls `adapter.ExecuteAsync(request, tenantContext)` and handles the result. Adding a new integration means implementing one interface and registering the adapter; the retry engine, tracing, DLQ routing, and tenant isolation all apply automatically.

This architecture has paid off repeatedly: two new integrations were added in production without any changes to the core engine.

### Disaster Recovery Topology

The system is deployed across two Azure regions in an active/passive configuration:

- **Azure Front Door** routes all traffic to the primary region and fails over to secondary automatically based on health probe results
- **SQL Server Failover Groups** replicate the application database with automatic failover, achieving RPO < 15 minutes
- **Azure Service Bus Geo-DR** maintains a passive namespace mirror; a single alias switch promotes the secondary namespace with no message loss on the primary-to-secondary failover path
- **AKS** in both regions runs identical workloads, kept warm via minimum replica counts so failover time is measured in seconds, not minutes

Full DR runbook, including manual failover procedure and validation checklist, is maintained in the `/docs/runbooks` directory.

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

See [`/samples`](./samples) for sanitized, annotated excerpts of the core patterns:

- [`OutboxPublisher.cs`](./samples/OutboxPublisher.cs) — Transactional outbox write + idempotency key generation
- [`IIntegrationAdapter.cs`](./samples/IIntegrationAdapter.cs) — Adapter interface contract
- [`TenantResolverMiddleware.cs`](./samples/TenantResolverMiddleware.cs) — Per-tenant runtime configuration resolution
- [`RetryPolicyConfig.cs`](./samples/RetryPolicyConfig.cs) — Exponential retry ladder with DLQ routing
- [`OpenTelemetryConfig.cs`](./samples/OpenTelemetryConfig.cs) — Distributed tracing setup, span creation per adapter call, and annotated example trace tree

---

## Infrastructure

All Azure resources are provisioned via Terraform in under 20 minutes from a cold start:

```
terraform init
terraform plan -var-file="prod.tfvars"
terraform apply
```

See [`/infra`](./infra) for the full Terraform source:

- [`main.tf`](./infra/main.tf) — AKS, Service Bus (with Geo-DR), SQL Server + Failover Group, Key Vault, Front Door, Application Insights
- [`variables.tf`](./infra/variables.tf) — All input variables with validation
- [`outputs.tf`](./infra/outputs.tf) — Outputs consumed by the GitHub Actions deploy pipeline

---

## Deployment

Releases follow a canary deployment strategy on AKS:

1. New image is built and pushed by GitHub Actions on merge to `main`
2. k6 load test suite runs against staging, validating p99 latency and error rate thresholds
3. AKS canary deployment routes 10% of traffic to the new version; Datadog/App Insights monitors for anomalies
4. Full rollout proceeds automatically if no alerts fire within the observation window
5. Automatic rollback triggers if p99 exceeds 150ms or error rate exceeds 0.5%

---

## License

This repository contains sanitized reference implementations. Proprietary institution data, credentials, and internal configuration have been removed.
