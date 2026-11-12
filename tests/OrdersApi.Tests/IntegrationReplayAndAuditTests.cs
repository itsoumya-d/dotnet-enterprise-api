using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OrdersApi.Data;
using OrdersApi.Models;
using OrdersApi.Services;

namespace OrdersApi.Tests;

public sealed class SwitchableDispatcher : IIntegrationDispatcher
{
    public bool ShouldFail { get; set; } = true;

    public Task DispatchAsync(IntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        if (ShouldFail)
        {
            throw new InvalidOperationException("custody downstream timeout");
        }
        return Task.CompletedTask;
    }
}

public sealed class AuditReplayApiFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    public SwitchableDispatcher Dispatcher { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        _connection.Open();
        builder.UseEnvironment("Testing");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<OrdersDbContext>>();
            services.RemoveAll<IIntegrationDispatcher>();
            services.AddSingleton(_connection);
            services.AddDbContext<OrdersDbContext>((provider, options) =>
                options.UseSqlite(provider.GetRequiredService<SqliteConnection>()));
            services.AddSingleton<IIntegrationDispatcher>(Dispatcher);
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

public sealed class IntegrationReplayAndAuditTests : IDisposable
{
    private readonly AuditReplayApiFactory _factory = new();
    private readonly HttpClient _client;

    public IntegrationReplayAndAuditTests()
    {
        _client = _factory.CreateClient();
        _client.DefaultRequestHeaders.Add("X-API-Key", "dev-integration-key");
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    private async Task<int> CreateTenantAsync(string name)
    {
        var resp = await _client.PostAsJsonAsync(
            "/api/integrations/tenants",
            new CreateTenantRequest(name, "CRM", "CustodyCore"));
        resp.EnsureSuccessStatusCode();
        var tenant = await resp.Content.ReadFromJsonAsync<TenantIntegration>();
        return tenant!.Id;
    }

    [Fact]
    public async Task SummaryAndAuditAndBatchReplay_ProtectMultiTenantRecovery()
    {
        var tenantA = await CreateTenantAsync("WealthTenantA");
        var tenantB = await CreateTenantAsync("WealthTenantB");

        var evtAResp = await _client.PostAsJsonAsync(
            "/api/integrations/events",
            new EnqueueIntegrationEventRequest(tenantA, "custody.position.sync", "replay-key-a1", "{\"acct\":101}"));
        var evtA = (await evtAResp.Content.ReadFromJsonAsync<IntegrationEvent>())!;

        var evtBResp = await _client.PostAsJsonAsync(
            "/api/integrations/events",
            new EnqueueIntegrationEventRequest(tenantB, "custody.position.sync", "replay-key-b1", "{\"acct\":202}"));
        var evtB = (await evtBResp.Content.ReadFromJsonAsync<IntegrationEvent>())!;

        // Exhaust 3 attempts on both tenants while downstream fails.
        for (var i = 0; i < 3; i++)
        {
            await _client.PostAsync($"/api/integrations/events/{evtA.Id}/process", null);
            await _client.PostAsync($"/api/integrations/events/{evtB.Id}/process", null);
        }

        var degradedSummary = await _client.GetFromJsonAsync<TenantIntegrationSummary>($"/api/integrations/tenants/{tenantA}/summary");
        Assert.NotNull(degradedSummary);
        Assert.Equal("degraded", degradedSummary.HealthStatus);
        Assert.Equal(1, degradedSummary.DeadLetterCount);
        Assert.Null(degradedSummary.LastSyncedAt);

        // Batch replay only tenant A dead letters.
        var replayResp = await _client.PostAsJsonAsync(
            "/api/integrations/events/dead-letters/replay",
            new ReplayDeadLettersRequest(tenantA, 25));
        replayResp.EnsureSuccessStatusCode();
        var replay = await replayResp.Content.ReadFromJsonAsync<DeadLetterReplayResult>();
        Assert.NotNull(replay);
        Assert.Equal(1, replay.RequeuedCount);
        Assert.Contains(evtA.Id, replay.EventIds);
        Assert.DoesNotContain(evtB.Id, replay.EventIds);

        // Downstream recovers; processing tenant A updates LastSyncedAt and heals summary.
        _factory.Dispatcher.ShouldFail = false;
        var reprocessedResp = await _client.PostAsync($"/api/integrations/events/{evtA.Id}/process", null);
        reprocessedResp.EnsureSuccessStatusCode();
        var reprocessed = await reprocessedResp.Content.ReadFromJsonAsync<IntegrationEvent>();
        Assert.Equal("processed", reprocessed!.Status);

        var healthySummary = await _client.GetFromJsonAsync<TenantIntegrationSummary>($"/api/integrations/tenants/{tenantA}/summary");
        Assert.NotNull(healthySummary);
        Assert.Equal("healthy", healthySummary.HealthStatus);
        Assert.Equal(1, healthySummary.ProcessedCount);
        Assert.NotNull(healthySummary.LastSyncedAt);

        var auditEvents = await _client.GetFromJsonAsync<List<AuditEvent>>("/api/integrations/audit?limit=50");
        Assert.NotNull(auditEvents);
        Assert.Contains(auditEvents, a => a.Type == "integration.dead_letter");
        Assert.Contains(auditEvents, a => a.Type == "integration.dead_letters_replayed");
        Assert.Contains(auditEvents, a => a.Type == "integration.processed");
    }

    [Fact]
    public async Task ReplayDeadLetters_InvalidMaxCount_ReturnsBadRequest()
    {
        var resp = await _client.PostAsJsonAsync(
            "/api/integrations/events/dead-letters/replay",
            new ReplayDeadLettersRequest(null, 500));
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task SummaryAndAudit_WithoutApiKey_ReturnUnauthorized()
    {
        using var anonymous = _factory.CreateClient();
        var summaryResp = await anonymous.GetAsync("/api/integrations/tenants/1/summary");
        var replayResp = await anonymous.PostAsJsonAsync("/api/integrations/events/dead-letters/replay", new ReplayDeadLettersRequest());
        var auditResp = await anonymous.GetAsync("/api/integrations/audit");
        Assert.Equal(HttpStatusCode.Unauthorized, summaryResp.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, replayResp.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, auditResp.StatusCode);
    }
}
