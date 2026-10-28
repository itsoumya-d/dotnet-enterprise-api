using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OrdersApi.Data;
using OrdersApi.Models;

namespace OrdersApi.Tests;

public class OrdersApiFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        _connection.Open();
        builder.UseEnvironment("Testing");
        builder.ConfigureServices(services =>
        {
            var descriptor = services.SingleOrDefault(service => service.ServiceType == typeof(DbContextOptions<OrdersDbContext>));
            if (descriptor is not null)
            {
                services.Remove(descriptor);
            }

            services.AddSingleton(_connection);
            services.AddDbContext<OrdersDbContext>((provider, options) =>
                options.UseSqlite(provider.GetRequiredService<SqliteConnection>()));
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _connection.Dispose();
        }
    }
}

public class OrdersApiTests : IDisposable
{
    private readonly OrdersApiFactory _factory = new();
    private readonly HttpClient _client;

    public OrdersApiTests()
    {
        _client = _factory.CreateClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [Fact]
    public async Task GetOrders_WhenEmpty_ReturnsEmptyArray()
    {
        var orders = await _client.GetFromJsonAsync<List<Order>>("/api/orders");

        Assert.NotNull(orders);
        Assert.Empty(orders);
    }

    [Fact]
    public async Task PostOrder_CreatesOrder_AndCanBeFetched()
    {
        var request = new CreateOrderRequest("Acme Ltd", new List<CreateOrderItem>
        {
            new("Widget", 2, 19.95m),
            new("Gadget", 1, 5.00m),
        });

        var response = await _client.PostAsJsonAsync("/api/orders", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<Order>();
        Assert.NotNull(created);
        Assert.Equal("Acme Ltd", created.CustomerName);
        Assert.Equal("Pending", created.Status);
        Assert.Equal(2, created.Items.Count);
        Assert.EndsWith($"/api/Orders/{created.Id}", response.Headers.Location!.ToString());

        var fetched = await _client.GetFromJsonAsync<Order>($"/api/orders/{created.Id}");
        Assert.NotNull(fetched);
        Assert.Equal("Acme Ltd", fetched.CustomerName);
    }

    [Fact]
    public async Task PostOrder_WithInvalidPayload_ReturnsBadRequest()
    {
        var invalid = new CreateOrderRequest("", new List<CreateOrderItem>
        {
            new("Widget", 0, -1m),
        });

        var response = await _client.PostAsJsonAsync("/api/orders", invalid);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PutStatus_UpdatesOrder()
    {
        var created = await CreateOrder();

        var response = await _client.PutAsJsonAsync($"/api/orders/{created.Id}/status", new UpdateStatusRequest("Confirmed"));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var fetched = await _client.GetFromJsonAsync<Order>($"/api/orders/{created.Id}");
        Assert.NotNull(fetched);
        Assert.Equal("Confirmed", fetched.Status);
    }

    [Fact]
    public async Task PutStatus_WithInvalidStatus_ReturnsBadRequest()
    {
        var created = await CreateOrder();

        var response = await _client.PutAsJsonAsync($"/api/orders/{created.Id}/status", new UpdateStatusRequest("Archived"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetMissingOrder_ReturnsNotFound()
    {
        var response = await _client.GetAsync("/api/orders/9999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task<Order> CreateOrder()
    {
        var request = new CreateOrderRequest("Contoso", new List<CreateOrderItem> { new("Widget", 1, 10m) });
        var response = await _client.PostAsJsonAsync("/api/orders", request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<Order>())!;
    }
}
