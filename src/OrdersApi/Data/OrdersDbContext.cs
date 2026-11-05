using Microsoft.EntityFrameworkCore;
using OrdersApi.Models;

namespace OrdersApi.Data;

public class OrdersDbContext(DbContextOptions<OrdersDbContext> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<TenantIntegration> TenantIntegrations => Set<TenantIntegration>();
    public DbSet<IntegrationEvent> IntegrationEvents => Set<IntegrationEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>()
            .HasMany(order => order.Items)
            .WithOne()
            .HasForeignKey(item => item.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<OrderItem>()
            .Property(item => item.UnitPrice)
            .HasColumnType("decimal(18,2)");

        modelBuilder.Entity<TenantIntegration>()
            .HasMany(tenant => tenant.Events)
            .WithOne(item => item.TenantIntegration)
            .HasForeignKey(item => item.TenantIntegrationId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<IntegrationEvent>()
            .HasIndex(item => item.IdempotencyKey)
            .IsUnique();
    }
}
