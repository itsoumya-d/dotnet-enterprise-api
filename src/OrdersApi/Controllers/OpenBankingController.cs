using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using OrdersApi.Models;
using OrdersApi.Services;

namespace OrdersApi.Controllers;

[ApiController]
[Route("openbanking/v1")]
public class OpenBankingController(
    OpenBankingService service,
    IOptions<OpenBankingOptions> options,
    ILogger<OpenBankingController> logger) : ControllerBase
{
    private readonly OpenBankingOptions _options = options.Value;

    private bool IsAuthorised()
    {
        var header = Request.Headers.Authorization.ToString();
        const string prefix = "Bearer ";
        return header.StartsWith(prefix, StringComparison.Ordinal) &&
               header[prefix.Length..] == _options.BearerToken;
    }

    [HttpPost("consents")]
    public async Task<ActionResult<Consent>> CreateConsent(CreateConsentRequest request, CancellationToken cancellationToken)
    {
        if (!IsAuthorised()) return Unauthorized();
        try
        {
            var consent = await service.CreateConsentAsync(request, cancellationToken);
            return CreatedAtAction(nameof(GetConsent), new { consentId = consent.Id }, consent);
        }
        catch (OpenBankingException ex)
        {
            logger.LogWarning(ex, "Consent rejected");
            return Problem(ex.Message, statusCode: ex.StatusCode);
        }
    }

    [HttpGet("consents/{consentId}")]
    public ActionResult<Consent> GetConsent(string consentId)
    {
        if (!IsAuthorised()) return Unauthorized();
        var consent = service.GetConsent(consentId);
        return consent is null ? NotFound() : Ok(consent);
    }

    [HttpPost("payments")]
    public async Task<ActionResult<Payment>> CreatePayment(CreatePaymentRequest request, CancellationToken cancellationToken)
    {
        if (!IsAuthorised()) return Unauthorized();
        var idempotencyKey = Request.Headers["Idempotency-Key"].ToString();
        try
        {
            var (payment, created) = await service.CreatePaymentAsync(idempotencyKey, request, cancellationToken);
            return created
                ? CreatedAtAction(nameof(GetPayment), new { paymentId = payment.Id }, payment)
                : Ok(payment);
        }
        catch (OpenBankingException ex)
        {
            logger.LogWarning(ex, "Payment rejected");
            return Problem(ex.Message, statusCode: ex.StatusCode);
        }
    }

    [HttpGet("payments/{paymentId}")]
    public ActionResult<Payment> GetPayment(string paymentId)
    {
        if (!IsAuthorised()) return Unauthorized();
        var payment = service.GetPayment(paymentId);
        return payment is null ? NotFound() : Ok(payment);
    }

    [HttpGet("accounts/{accountId}/balance")]
    public ActionResult<AccountBalance> GetBalance(string accountId)
    {
        if (!IsAuthorised()) return Unauthorized();
        return Ok(new AccountBalance(accountId, service.GetBalance(accountId), "USD"));
    }
}
