namespace OrdersApi.Models;

public class Order
{
    public int Id { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string Status { get; set; } = "Pending";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public List<OrderItem> Items { get; set; } = new();
}

public class OrderItem
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
}

public record CreateOrderItem(string ProductName, int Quantity, decimal UnitPrice);

public record CreateOrderRequest(string CustomerName, List<CreateOrderItem> Items);

public record UpdateStatusRequest(string Status);
