using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using OrdersApi.Data;
using OrdersApi.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<OpenBankingOptions>(builder.Configuration.GetSection("OpenBanking"));
builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();
builder.Services.AddSingleton<OpenBankingService>();
builder.Services.AddSingleton<IEventPublisher>(services =>
{
    var options = services.GetRequiredService<IOptions<OpenBankingOptions>>().Value;
    return string.IsNullOrWhiteSpace(options.AzureServiceBusConnectionString)
        ? new NoopEventPublisher()
        : ActivatorUtilities.CreateInstance<AzureServiceBusPublisher>(services);
});
builder.Services.AddSingleton<IAuditStore>(services =>
{
    var options = services.GetRequiredService<IOptions<OpenBankingOptions>>().Value;
    return string.IsNullOrWhiteSpace(options.CosmosConnectionString)
        ? new InMemoryAuditStore()
        : ActivatorUtilities.CreateInstance<CosmosAuditStore>(services);
});

var otlpEndpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing =>
    {
        tracing.AddAspNetCoreInstrumentation();
        if (!string.IsNullOrWhiteSpace(otlpEndpoint))
        {
            tracing.AddOtlpExporter();
        }
    })
    .WithMetrics(metrics =>
    {
        metrics.AddAspNetCoreInstrumentation();
        if (!string.IsNullOrWhiteSpace(otlpEndpoint))
        {
            metrics.AddOtlpExporter();
        }
    });

var ordersConnection = builder.Configuration.GetConnectionString("Orders") ?? "Data Source=orders.db";
builder.Services.AddDbContext<OrdersDbContext>(options =>
{
    if (ordersConnection.Contains("Server=", StringComparison.OrdinalIgnoreCase) ||
        ordersConnection.Contains("Data Source=tcp:", StringComparison.OrdinalIgnoreCase))
    {
        options.UseSqlServer(ordersConnection);
    }
    else
    {
        options.UseSqlite(ordersConnection);
    }
});

var app = builder.Build();

if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Testing"))
{
    app.MapOpenApi();
}

app.UseAuthorization();
app.MapControllers();
app.MapHealthChecks("/healthz");

using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<OrdersDbContext>().Database.EnsureCreated();
}

app.Run();

public partial class Program;
