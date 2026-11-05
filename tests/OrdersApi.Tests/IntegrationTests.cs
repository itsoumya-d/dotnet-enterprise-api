using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OrdersApi.Data;
using OrdersApi.Models;
using OrdersApi.Services;

namespace OrdersApi.Tests;

public class FailingDispatcher : IIntegrationDispatcher
{
    public Task DispatchAsync(IntegrationEvent integrationEvent, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("downstream unavailable");
}

public class IntegrationApiFactory : WebApplicationFactory<Program>
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

public class IntegrationTests : IDisposable
{
    private readonly IntegrationApiFactory _factory = new();
    private readonly HttpClient _client;

    public IntegrationTests()
    {
        _client = _factory.CreateClient();
        _client.DefaultRequestHeaders.Add("X-API-Key", "dev-integration-key");
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    private async Task<int> CreateTenantAsync()
    {
        var response = await _client.PostAsJsonAsync("/api/integrations/tenants", new CreateTenantRequest("Acme", "Shopify", "NetSuite"));
        response.EnsureSuccessStatusCode();
        var tenant = await response.Content.ReadFromJsonAsync<TenantIntegration>();
        return tenant!.Id;
    }

    [Fact]
    public async Task CreateTenant_WithoutApiKey_ReturnsUnauthorized()
    {
        using var anonymous = _factory.CreateClient();
        var response = await anonymous.PostAsJsonAsync("/api/integrations/tenants", new CreateTenantRequest("Acme", "A", "B"));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreateTenant_ReturnsCreatedTenant()
    {
        var id = await CreateTenantAsync();
        var tenants = await _client.GetFromJsonAsync<List<TenantIntegration>>("/api/integrations/tenants");
        Assert.Contains(tenants!, tenant => tenant.Id == id && tenant.TenantName == "Acme");
    }

    [Fact]
    public async Task Enqueue_IsIdempotent_ForSamePayload()
    {
        var tenantId = await CreateTenantAsync();
        var request = new EnqueueIntegrationEventRequest(tenantId, "order.created", "evt-1", "{\"order\":42}");
        var first = await _client.PostAsJsonAsync("/api/integrations/events", request);
        var second = await _client.PostAsJsonAsync("/api/integrations/events", request);
        first.EnsureSuccessStatusCode();
        second.EnsureSuccessStatusCode();
        var firstEvent = await first.Content.ReadFromJsonAsync<IntegrationEvent>();
        var secondEvent = await second.Content.ReadFromJsonAsync<IntegrationEvent>();
        Assert.Equal(firstEvent!.Id, secondEvent!.Id);
    }

    [Fact]
    public async Task Enqueue_WithChangedPayload_ReturnsConflict()
    {
        var tenantId = await CreateTenantAsync();
        var request = new EnqueueIntegrationEventRequest(tenantId, "order.created", "evt-1", "{\"order\":42}");
        await _client.PostAsJsonAsync("/api/integrations/events", request);
        var changed = request with { Payload = "{\"order\":43}" };
        var response = await _client.PostAsJsonAsync("/api/integrations/events", changed);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Process_MarksEventProcessed()
    {
        var tenantId = await CreateTenantAsync();
        var created = await _client.PostAsJsonAsync("/api/integrations/events",
            new EnqueueIntegrationEventRequest(tenantId, "order.created", "evt-1", "{}"));
        var integrationEvent = await created.Content.ReadFromJsonAsync<IntegrationEvent>();
        var response = await _client.PostAsync($"/api/integrations/events/{integrationEvent!.Id}/process", null);
        response.EnsureSuccessStatusCode();
        var processed = await response.Content.ReadFromJsonAsync<IntegrationEvent>();
        Assert.Equal("processed", processed!.Status);
    }

    [Fact]
    public async Task FailingDispatcher_MovesEventToDeadLetter_AndRetryResetsIt()
    {
        using var factory = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                var descriptor = services.SingleOrDefault(service => service.ServiceType == typeof(IIntegrationDispatcher));
                if (descriptor is not null)
                {
                    services.Remove(descriptor);
                }
                services.AddSingleton<IIntegrationDispatcher, FailingDispatcher>();
            }));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-API-Key", "dev-integration-key");
        var tenantResponse = await client.PostAsJsonAsync("/api/integrations/tenants", new CreateTenantRequest("Acme", "A", "B"));
        var tenant = await tenantResponse.Content.ReadFromJsonAsync<TenantIntegration>();
        var eventResponse = await client.PostAsJsonAsync("/api/integrations/events",
            new EnqueueIntegrationEventRequest(tenant!.Id, "order.created", "evt-fail", "{}"));
        var integrationEvent = await eventResponse.Content.ReadFromJsonAsync<IntegrationEvent>();

        for (var i = 0; i < 3; i++)
        {
            await client.PostAsync($"/api/integrations/events/{integrationEvent!.Id}/process", null);
        }

        var deadLetters = await client.GetFromJsonAsync<List<IntegrationEvent>>("/api/integrations/events/dead-letters");
        Assert.Contains(deadLetters!, item => item.Id == integrationEvent!.Id && item.Status == "dead_letter");

        var retry = await client.PostAsync($"/api/integrations/events/{integrationEvent!.Id}/retry", null);
        retry.EnsureSuccessStatusCode();
        var reset = await retry.Content.ReadFromJsonAsync<IntegrationEvent>();
        Assert.Equal("pending", reset!.Status);
    }
}
