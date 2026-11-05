# Orders & Integration API — ASP.NET Core reference service

A production-shaped Web API for orders, Open Banking and tenant integration
workflows: **C#, ASP.NET Core, REST, OpenAPI, SQL/EF Core, Azure Service Bus,
Azure Cosmos DB, OpenTelemetry, API-key protection, retries, dead letters,
CI/CD and containerisation**.

The integration module adds tenant connection records and idempotent events
with attempts, exponential backoff, dead-letter handling, retry and API-key
protection. It is the same control loop used when connecting customer systems
across bespoke .NET applications and cloud platforms.

## Stack

| Layer | Choice |
|---|---|
| Language / runtime | C# 14, .NET 10 |
| Web framework | ASP.NET Core controllers |
| API contract | OpenAPI document at `/openapi/v1.json` |
| Data access | EF Core (SQLite for portability; SQL Server/Azure SQL provider swap) |
| Messaging | Azure Service Bus publisher (no-op without a connection string) |
| Audit store | Azure Cosmos DB (in-memory fallback without a connection string) |
| Observability | OpenTelemetry tracing/metrics + `/healthz` |
| Validation | Data annotations + explicit model-state checks |
| Tests | xUnit integration tests via `WebApplicationFactory` |
| CI | GitHub Actions: restore, build, test |
| Container | Multi-stage Dockerfile (`aspnet:10.0` runtime) |

## Endpoints

| Method | Route | Behaviour |
|---|---|---|
| `GET` | `/api/orders` | List orders with items |
| `GET` | `/api/orders/{id}` | Fetch one order, 404 when missing |
| `POST` | `/api/orders` | Create an order with items, 201 + Location |
| `PUT` | `/api/orders/{id}/status` | Move status through Pending → Confirmed → Shipped (or Cancelled) |
| `POST` | `/openbanking/v1/consents` | Create an authorised Open Banking consent |
| `GET` | `/openbanking/v1/consents/{id}` | Read consent status |
| `POST` | `/openbanking/v1/payments` | Idempotent consent-bound payment |
| `GET` | `/openbanking/v1/payments/{id}` | Read payment status |
| `GET` | `/openbanking/v1/accounts/{id}/balance` | Read the account balance |
| `GET` | `/healthz` | Health probe |
| `POST` | `/api/integrations/tenants` | Register a tenant integration (API key) |
| `GET` | `/api/integrations/tenants` | List tenant integrations (API key) |
| `POST` | `/api/integrations/events` | Enqueue an idempotent event (API key) |
| `POST` | `/api/integrations/events/{id}/process` | Dispatch once and record the outcome (API key) |
| `POST` | `/api/integrations/events/{id}/retry` | Reset a dead-letter event (API key) |
| `GET` | `/api/integrations/events/dead-letters` | List dead-letter events (API key) |

Validation rules: customer name required, at least one item, positive quantity, non-negative unit price, status limited to the allowed set. Open Banking payments require an `Authorization: Bearer` token, an authorised consent with `WritePayments`, and an `Idempotency-Key` header.

## Open Banking and Azure integration

The Open Banking flow is intentionally small but exercises the controls that matter before scale:

- **OAuth2-style bearer token:** the development token is `dev-token` (`OpenBanking:BearerToken`). In production this is replaced by OIDC/JWKS validation; the controller is the seam.
- **Idempotency:** replaying the same `Idempotency-Key` and payload returns the original payment; changing the payload returns `409`.
- **Consent authorisation:** a payment requires an authorised consent that includes `WritePayments`.
- **Audit trail:** every consent and payment emits a typed `AuditEvent`; Cosmos DB persists it when `OpenBanking:CosmosConnectionString` is set.
- **Event publication:** the same event is published to Azure Service Bus when `OpenBanking:AzureServiceBusConnectionString` is set.
- **Azure SQL:** set `ConnectionStrings:Orders` to a `Server=...;Database=...` connection string and the app uses `UseSqlServer`; the default SQLite path stays for local runs.
- **OpenTelemetry:** tracing and metrics are wired; the OTLP exporter activates when `OTEL_EXPORTER_OTLP_ENDPOINT` is set.

The local defaults are deliberately safe: no cloud credentials are required, and the Cosmos/Service Bus adapters no-op when unconfigured. This is a reference implementation, not a production payment system.

## Run

```bash
dotnet restore
dotnet run --project src/OrdersApi
```

Then open `http://localhost:5000/openapi/v1.json` for the OpenAPI (Swagger) document.

### Open Banking example

```bash
curl -s -X POST localhost:5000/openbanking/v1/consents \
  -H 'Authorization: Bearer dev-token' \
  -H 'Content-Type: application/json' \
  -d '{"accountId":"customer_demo","permissions":["ReadAccounts","WritePayments"]}'

curl -s -X POST localhost:5000/openbanking/v1/payments \
  -H 'Authorization: Bearer dev-token' \
  -H 'Idempotency-Key: payment-1' \
  -H 'Content-Type: application/json' \
  -d '{"consentId":"<consent id>","accountId":"customer_demo","amountCents":400,"currency":"USD"}'
```

## Test

```bash
dotnet test OrdersApi.slnx
```

Fifteen integration tests cover the orders flow (6), Open Banking (3) and
tenant integrations (6): API-key rejection, tenant creation, idempotent event
enqueue, conflict on changed payload, successful dispatch, and dead-letter
retry after repeated dispatcher failure.

## Why this repo exists

This is a focused sample built to make a specific claim verifiable: a working ASP.NET Core service with a typed domain model, REST and Open Banking endpoints, OpenAPI contract, SQL persistence, Azure messaging/audit seams, OpenTelemetry, validation, integration tests and CI. It mirrors the day-to-day work described in senior .NET technical-lead roles. The SQLite provider is used so the sample runs anywhere; switching to SQL Server/Azure SQL is a connection-string and provider change (`UseSqlServer`), not an architectural rewrite.

## CI

`.github/workflows/ci.yml` runs on every push and pull request: `dotnet restore`, `dotnet build --configuration Release --no-restore`, `dotnet test --no-build --configuration Release`.
