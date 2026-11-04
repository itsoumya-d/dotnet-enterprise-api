# Open Banking integration notes

The tested flow is in `src/OrdersApi/Controllers/OpenBankingController.cs` and
`src/OrdersApi/Services/OpenBankingService.cs`.

## Security model

- **OAuth 2.0 / OpenID Connect:** production deployments validate the bearer
  token against the identity provider's JWKS; the local `dev-token` is a
  development shim. Scopes should include `payments`.
- **Financial-grade API (FAPI):** PKCE, signed request objects, certificate-bound
  access tokens (`cnf.x5t#S256`), and `mTLS` at the gateway.
- **Mutual TLS:** Azure API Management or Kong terminates mTLS with a private CA
  and forwards the client certificate thumbprint to the service.
- **Consent:** a payment requires an authorised consent with `WritePayments`.
- **Idempotency:** `Idempotency-Key` is scoped to the consent and account; a
  replay returns the original payment, a different payload returns `409`.
- **Audit:** each consent/payment emits an `AuditEvent`; Cosmos DB persists it
  when configured.

## Azure hosting

| Component | Choice |
|---|---|
| API | ASP.NET Core on Azure App Service or Azure Functions (see `functions/openbanking-payment`) |
| Gateway | Azure API Management (OAuth2/OIDC, mTLS, rate limits, WAF) |
| Messaging | Azure Service Bus queue `payments` |
| Audit | Azure Cosmos DB container `audit` (partition key `/id`) |
| SQL | Azure SQL Database via `UseSqlServer` |
| Telemetry | OpenTelemetry -> Azure Monitor / OTLP collector |

## Local flow

1. `POST /openbanking/v1/consents` with `Authorization: Bearer dev-token`.
2. `POST /openbanking/v1/payments` with the returned consent id and an
   `Idempotency-Key`.
3. `GET /openbanking/v1/accounts/{id}/balance` to verify the movement.
4. Replay the payment with the same key; the service returns `200` and the
   original payment id.
