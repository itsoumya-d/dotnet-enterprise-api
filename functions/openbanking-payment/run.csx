#r "Newtonsoft.Json"

using System.Net;
using Newtonsoft.Json;

// Deployment sample only. The tested implementation lives in
// src/OrdersApi/Controllers/OpenBankingController.cs; this function shows how
// the same idempotent payment handler can be hosted on Azure Functions.
public static async Task<HttpResponseMessage> Run(HttpRequestMessage req, TraceWriter log)
{
    var idempotencyKey = req.Headers.TryGetValues("Idempotency-Key", out var values)
        ? values.FirstOrDefault()
        : null;

    if (string.IsNullOrWhiteSpace(idempotencyKey))
    {
        return req.CreateResponse(HttpStatusCode.BadRequest, new { error = "Idempotency-Key header is required" });
    }

    var body = await req.Content.ReadAsStringAsync();
    log.Info($"Open Banking payment received with idempotency key {idempotencyKey}");

    return req.CreateResponse(HttpStatusCode.Accepted, new
    {
        status = "accepted",
        idempotency_key = idempotencyKey,
        payload = JsonConvert.DeserializeObject(body)
    });
}
