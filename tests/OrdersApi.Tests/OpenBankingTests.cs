using System.Net;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using OrdersApi.Models;

namespace OrdersApi.Tests;

public class OpenBankingTests : IDisposable
{
    private readonly OrdersApiFactory _factory = new();
    private readonly HttpClient _client;

    public OpenBankingTests()
    {
        _client = _factory.CreateClient();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "dev-token");
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [Fact]
    public async Task ConsentThenIdempotentPayment()
    {
        var consentResponse = await _client.PostAsJsonAsync("/openbanking/v1/consents",
            new CreateConsentRequest("customer_demo", new List<string> { "ReadAccounts", "WritePayments" }));
        Assert.Equal(HttpStatusCode.Created, consentResponse.StatusCode);
        var consent = await consentResponse.Content.ReadFromJsonAsync<Consent>();
        Assert.NotNull(consent);
        Assert.Equal("authorised", consent.Status);

        var paymentRequest = new CreatePaymentRequest(consent.Id, "customer_demo", 400, "USD");
        var first = await SendPayment(paymentRequest, "test-pay-1");
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var payment = await first.Content.ReadFromJsonAsync<Payment>();
        Assert.NotNull(payment);
        Assert.Equal("completed", payment.Status);

        var replay = await SendPayment(paymentRequest, "test-pay-1");
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        var replayed = await replay.Content.ReadFromJsonAsync<Payment>();
        Assert.NotNull(replayed);
        Assert.Equal(payment.Id, replayed.Id);

        var balance = await _client.GetFromJsonAsync<AccountBalance>("/openbanking/v1/accounts/customer_demo/balance");
        Assert.NotNull(balance);
        Assert.Equal(9_600, balance.BalanceCents);
    }

    [Fact]
    public async Task PaymentRequiresBearerToken()
    {
        using var anonymous = _factory.CreateClient();
        var response = await anonymous.GetAsync("/openbanking/v1/accounts/customer_demo/balance");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ReusingKeyWithDifferentPayloadReturnsConflict()
    {
        var consentResponse = await _client.PostAsJsonAsync("/openbanking/v1/consents",
            new CreateConsentRequest("customer_demo", new List<string> { "WritePayments" }));
        var consent = await consentResponse.Content.ReadFromJsonAsync<Consent>();
        Assert.NotNull(consent);

        var first = await SendPayment(new CreatePaymentRequest(consent.Id, "customer_demo", 100, "USD"), "test-pay-2");
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var conflicting = await SendPayment(new CreatePaymentRequest(consent.Id, "customer_demo", 200, "USD"), "test-pay-2");
        Assert.Equal(HttpStatusCode.Conflict, conflicting.StatusCode);
    }

    private async Task<HttpResponseMessage> SendPayment(CreatePaymentRequest request, string idempotencyKey)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, "/openbanking/v1/payments")
        {
            Content = JsonContent.Create(request),
        };
        message.Headers.Add("Idempotency-Key", idempotencyKey);
        return await _client.SendAsync(message);
    }
}
