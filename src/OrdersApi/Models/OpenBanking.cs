namespace OrdersApi.Models;

public class Consent
{
    public string Id { get; set; } = string.Empty;
    public string AccountId { get; set; } = string.Empty;
    public List<string> Permissions { get; set; } = new();
    public string Status { get; set; } = "authorised";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class Payment
{
    public string Id { get; set; } = string.Empty;
    public string ConsentId { get; set; } = string.Empty;
    public string AccountId { get; set; } = string.Empty;
    public long AmountCents { get; set; }
    public string Currency { get; set; } = "USD";
    public string Status { get; set; } = "completed";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class AuditEvent
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Type { get; set; } = string.Empty;
    public Dictionary<string, object?> Payload { get; set; } = new();
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
}

public record CreateConsentRequest(string AccountId, List<string> Permissions);

public record CreatePaymentRequest(string ConsentId, string AccountId, long AmountCents, string Currency);

public record AccountBalance(string AccountId, long BalanceCents, string Currency);
