# Observability

The service wires OpenTelemetry tracing and metrics in `Program.cs`. The OTLP
exporter activates when `OTEL_EXPORTER_OTLP_ENDPOINT` is set; otherwise the
instrumentation is registered without a network exporter.

## Signals

| Signal | Source | Destination |
|---|---|---|
| Traces | ASP.NET Core instrumentation | OTLP collector / Azure Monitor |
| Metrics | ASP.NET Core instrumentation | OTLP collector / Prometheus |
| Health | `/healthz` | App Service / Container Apps probe |
| Logs | `ILogger` structured logs | Azure Monitor / ELK-compatible pipeline |
| Audit | `AuditEvent` | Cosmos DB / Service Bus |

## SLOs

- Availability: 99.9% over 30 days.
- p99 latency: < 300 ms for consent and payment calls.
- Error rate: < 0.5% 5xx.
- Audit durability: 100% of emitted events persisted or published.

## ELK-compatible logging

The service uses structured `ILogger` messages. Ship them through Serilog or the
OpenTelemetry logging exporter to an ELK stack; the event id, consent id,
payment id and idempotency key are the join keys.
