# API gateway and mutual TLS

The Open Banking surface is designed to sit behind an API gateway. Two
deployment patterns are documented here; the application code stays the same.

## Azure API Management

- **OAuth 2.0 / OIDC:** validate JWT access tokens with the tenant's JWKS.
- **mTLS:** require client certificates from the Open Banking directory CA and
  forward the thumbprint in `X-Client-Cert-Thumbprint`.
- **Rate limits and quotas:** per TPP subscription, per operation.
- **WAF:** OWASP core rule set in prevention mode.
- **Backend:** App Service or Azure Functions; private endpoint only.

## Kong

- `mtls-auth` plugin with the trusted CA.
- `openid-connect` plugin for OIDC/JWKS.
- `rate-limiting` and `request-size-limiting` policies.
- `correlation-id` plugin; propagate `traceparent` for OpenTelemetry.

## Application controls

- Bearer token check in `OpenBankingController`.
- Consent-scoped `WritePayments` check.
- Idempotency-key conflict detection.
- Audit event on every consent and payment.
