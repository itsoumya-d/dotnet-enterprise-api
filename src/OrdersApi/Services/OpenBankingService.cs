using Microsoft.Extensions.Options;
using OrdersApi.Models;

namespace OrdersApi.Services;

public sealed class OpenBankingException(string message, int statusCode) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

/// <summary>
/// In-memory Open Banking reference flow: consent -> authorised payment ->
/// double-entry-style balance movement -> audit event. The ledger itself is
/// deliberately simple; the point is the idempotency and authorisation
/// invariants that must hold before a payment is accepted.
/// </summary>
public sealed class OpenBankingService
{
    private readonly OpenBankingOptions _options;
    private readonly IEventPublisher _publisher;
    private readonly IAuditStore _audit;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, Consent> _consents = new();
    private readonly Dictionary<string, Payment> _payments = new();
    private readonly Dictionary<string, string> _paymentsByKey = new();
    private readonly Dictionary<string, long> _balances = new(StringComparer.OrdinalIgnoreCase)
    {
        ["customer_demo"] = 10_000,
    };

    public OpenBankingService(IOptions<OpenBankingOptions> options, IEventPublisher publisher, IAuditStore audit)
    {
        _options = options.Value;
        _publisher = publisher;
        _audit = audit;
    }

    public async Task<Consent> CreateConsentAsync(CreateConsentRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.AccountId) || request.Permissions.Count == 0)
        {
            throw new OpenBankingException("account_id and permissions are required", StatusCodes.Status400BadRequest);
        }

        var consent = new Consent
        {
            Id = $"consent_{Guid.NewGuid():N}",
            AccountId = request.AccountId.Trim(),
            Permissions = request.Permissions.Distinct().ToList(),
            Status = "authorised",
        };

        await _gate.WaitAsync(cancellationToken);
        try
        {
            _consents[consent.Id] = consent;
        }
        finally
        {
            _gate.Release();
        }

        await EmitAsync("consent.created", new Dictionary<string, object?>
        {
            ["consent_id"] = consent.Id,
            ["account_id"] = consent.AccountId,
            ["permissions"] = consent.Permissions,
        }, cancellationToken);
        return consent;
    }

    public Consent? GetConsent(string consentId)
    {
        _gate.Wait();
        try
        {
            return _consents.GetValueOrDefault(consentId);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<(Payment Payment, bool Created)> CreatePaymentAsync(
        string idempotencyKey,
        CreatePaymentRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new OpenBankingException("Idempotency-Key header is required", StatusCodes.Status400BadRequest);
        }
        if (request.AmountCents <= 0)
        {
            throw new OpenBankingException("amount_cents must be positive", StatusCodes.Status400BadRequest);
        }
        if (request.Currency.Length != 3)
        {
            throw new OpenBankingException("currency must be a 3-letter ISO code", StatusCodes.Status400BadRequest);
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_paymentsByKey.TryGetValue(idempotencyKey, out var existingId))
            {
                var existing = _payments[existingId];
                if (existing.ConsentId != request.ConsentId ||
                    existing.AccountId != request.AccountId ||
                    existing.AmountCents != request.AmountCents ||
                    existing.Currency != request.Currency)
                {
                    throw new OpenBankingException("idempotency key reused with a different payload", StatusCodes.Status409Conflict);
                }
                return (existing, false);
            }

            if (!_consents.TryGetValue(request.ConsentId, out var consent))
            {
                throw new OpenBankingException("consent not found", StatusCodes.Status404NotFound);
            }
            if (consent.Status != "authorised")
            {
                throw new OpenBankingException("consent is not authorised", StatusCodes.Status403Forbidden);
            }
            if (!consent.Permissions.Contains("WritePayments"))
            {
                throw new OpenBankingException("consent does not include WritePayments", StatusCodes.Status403Forbidden);
            }

            var balance = _balances.GetValueOrDefault(request.AccountId);
            if (balance < request.AmountCents)
            {
                throw new OpenBankingException("insufficient funds", StatusCodes.Status422UnprocessableEntity);
            }

            _balances[request.AccountId] = balance - request.AmountCents;
            var payment = new Payment
            {
                Id = $"payment_{Guid.NewGuid():N}",
                ConsentId = request.ConsentId,
                AccountId = request.AccountId,
                AmountCents = request.AmountCents,
                Currency = request.Currency,
                Status = "completed",
            };
            _payments[payment.Id] = payment;
            _paymentsByKey[idempotencyKey] = payment.Id;

            await EmitAsync("payment.completed", new Dictionary<string, object?>
            {
                ["payment_id"] = payment.Id,
                ["consent_id"] = payment.ConsentId,
                ["account_id"] = payment.AccountId,
                ["amount_cents"] = payment.AmountCents,
                ["currency"] = payment.Currency,
            }, cancellationToken);
            return (payment, true);
        }
        finally
        {
            _gate.Release();
        }
    }

    public Payment? GetPayment(string paymentId)
    {
        _gate.Wait();
        try
        {
            return _payments.GetValueOrDefault(paymentId);
        }
        finally
        {
            _gate.Release();
        }
    }

    public long GetBalance(string accountId)
    {
        _gate.Wait();
        try
        {
            return _balances.GetValueOrDefault(accountId);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task EmitAsync(string type, Dictionary<string, object?> payload, CancellationToken cancellationToken)
    {
        var auditEvent = new AuditEvent { Type = type, Payload = payload };
        await _publisher.PublishAsync(auditEvent, cancellationToken);
        await _audit.AppendAsync(auditEvent, cancellationToken);
    }
}
