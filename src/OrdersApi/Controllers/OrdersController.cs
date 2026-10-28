using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OrdersApi.Data;
using OrdersApi.Models;

namespace OrdersApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class OrdersController(OrdersDbContext db) : ControllerBase
{
    private static readonly string[] AllowedStatuses = ["Pending", "Confirmed", "Shipped", "Cancelled"];

    [HttpGet]
    public async Task<ActionResult<IEnumerable<Order>>> GetAll()
    {
        var orders = await db.Orders
            .Include(order => order.Items)
            .AsNoTracking()
            .OrderBy(order => order.Id)
            .ToListAsync();
        return Ok(orders);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<Order>> GetById(int id)
    {
        var order = await db.Orders
            .Include(entity => entity.Items)
            .AsNoTracking()
            .FirstOrDefaultAsync(entity => entity.Id == id);
        return order is null ? NotFound() : Ok(order);
    }

    [HttpPost]
    public async Task<ActionResult<Order>> Create(CreateOrderRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.CustomerName))
        {
            ModelState.AddModelError(nameof(request.CustomerName), "CustomerName is required.");
        }

        var items = request.Items ?? new List<CreateOrderItem>();
        if (items.Count == 0)
        {
            ModelState.AddModelError(nameof(request.Items), "At least one item is required.");
        }
        else if (items.Any(item => item.Quantity <= 0 || item.UnitPrice < 0))
        {
            ModelState.AddModelError(nameof(request.Items), "Quantity must be positive and UnitPrice non-negative.");
        }

        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var order = new Order
        {
            CustomerName = request.CustomerName.Trim(),
            Status = "Pending",
            Items = items.Select(item => new OrderItem
            {
                ProductName = item.ProductName.Trim(),
                Quantity = item.Quantity,
                UnitPrice = item.UnitPrice,
            }).ToList(),
        };

        db.Orders.Add(order);
        await db.SaveChangesAsync();
        return CreatedAtAction(nameof(GetById), new { id = order.Id }, order);
    }

    [HttpPut("{id:int}/status")]
    public async Task<IActionResult> UpdateStatus(int id, UpdateStatusRequest request)
    {
        if (!AllowedStatuses.Contains(request.Status))
        {
            ModelState.AddModelError(nameof(request.Status), $"Status must be one of: {string.Join(", ", AllowedStatuses)}.");
            return ValidationProblem(ModelState);
        }

        var order = await db.Orders.FindAsync(id);
        if (order is null)
        {
            return NotFound();
        }

        order.Status = request.Status;
        await db.SaveChangesAsync();
        return NoContent();
    }
}
