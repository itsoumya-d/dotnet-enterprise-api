# Orders API — ASP.NET Core reference service

A small, production-shaped Web API that demonstrates the stack this repository's author works in day to day: **C#, ASP.NET Core, REST, OpenAPI/Swagger, SQL, EF Core, CI/CD and containerisation**. It is a portfolio sample, intentionally small and fully tested.

## Stack

| Layer | Choice |
|---|---|
| Language / runtime | C# 14, .NET 10 |
| Web framework | ASP.NET Core controllers |
| API contract | OpenAPI document at `/openapi/v1.json` |
| Data access | EF Core (SQLite for portability; provider swap for SQL Server) |
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

Validation rules: customer name required, at least one item, positive quantity, non-negative unit price, status limited to the allowed set.

## Run

```bash
dotnet restore
dotnet run --project src/OrdersApi
```

Then open `http://localhost:5000/openapi/v1.json` for the OpenAPI (Swagger) document.

## Test

```bash
dotnet test OrdersApi.slnx
```

Six integration tests cover: empty listing, creation + retrieval, invalid payload rejection, status transition, invalid status rejection, and missing-order 404.

## Why this repo exists

This is a focused sample built to make a specific claim verifiable: a working ASP.NET Core service with a typed domain model, REST endpoints, OpenAPI contract, SQL persistence, validation, integration tests and CI. It mirrors the day-to-day work described in senior .NET technical-lead roles. The SQLite provider is used so the sample runs anywhere; switching to SQL Server is a connection-string and provider change (`UseSqlServer`), not an architectural rewrite.

## CI

`.github/workflows/ci.yml` runs on every push and pull request: `dotnet restore`, `dotnet build --configuration Release --no-restore`, `dotnet test --no-build --configuration Release`.
