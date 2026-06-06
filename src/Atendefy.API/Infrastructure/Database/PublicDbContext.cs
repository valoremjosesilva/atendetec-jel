using Atendefy.API.Modules.Billing.Models;
using Atendefy.API.Modules.Tenants.Models;
using Atendefy.API.Modules.Webhooks.Models;
using Microsoft.EntityFrameworkCore;

namespace Atendefy.API.Infrastructure.Database;

public class PublicDbContext(DbContextOptions<PublicDbContext> options) : DbContext(options)
{
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<TenantUser> TenantUsers => Set<TenantUser>();
    public DbSet<WebhookRoute> WebhookRoutes => Set<WebhookRoute>();
    public DbSet<Plan> Plans => Set<Plan>();
    public DbSet<Subscription> Subscriptions => Set<Subscription>();
    public DbSet<Invoice> Invoices => Set<Invoice>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("public");

        modelBuilder.Entity<Tenant>(e =>
        {
            e.ToTable("tenants");
            e.HasKey(x => x.Id);
            e.Property(x => x.Subdomain).IsRequired().HasMaxLength(100);
            e.HasIndex(x => x.Subdomain).IsUnique();
            e.Property(x => x.Name).IsRequired().HasMaxLength(200);
            e.Property(x => x.Status).HasMaxLength(50);
            e.Ignore(x => x.SchemaName);
            e.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<TenantUser>(e =>
        {
            e.ToTable("tenant_users");
            e.HasKey(x => x.Id);
            e.Property(x => x.Email).IsRequired().HasMaxLength(200);
            e.HasIndex(x => new { x.TenantId, x.Email }).IsUnique();
            e.Property(x => x.Role).HasMaxLength(50);
            e.HasQueryFilter(x => !x.IsDeleted);
            e.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<WebhookRoute>(e =>
        {
            e.ToTable("webhook_routes");
            e.HasKey(x => x.Id);
            e.Property(x => x.Provider).HasMaxLength(50).IsRequired();
            e.Property(x => x.LookupKey).HasMaxLength(200).IsRequired();
            e.HasIndex(x => x.LookupKey).IsUnique();
            e.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Plan>(e =>
        {
            e.ToTable("plans");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).IsRequired().HasMaxLength(100);
            e.Property(x => x.PriceMonthly).HasColumnType("numeric(10,2)");
            e.Property(x => x.PriceYearly).HasColumnType("numeric(10,2)");
            e.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<Subscription>(e =>
        {
            e.ToTable("subscriptions");
            e.HasKey(x => x.Id);
            e.Property(x => x.Status).HasMaxLength(50).IsRequired();
            e.Property(x => x.BillingCycle).HasMaxLength(20).IsRequired();
            e.Property(x => x.Provider).HasMaxLength(50).IsRequired();
            e.Property(x => x.ExternalCustomerId).HasMaxLength(200);
            e.Property(x => x.ExternalId).HasMaxLength(200);
            e.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Plan>().WithMany().HasForeignKey(x => x.PlanId).OnDelete(DeleteBehavior.Restrict);
            e.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<Invoice>(e =>
        {
            e.ToTable("invoices");
            e.HasKey(x => x.Id);
            e.Property(x => x.Amount).HasColumnType("numeric(10,2)");
            e.Property(x => x.Status).HasMaxLength(50).IsRequired();
            e.Property(x => x.Provider).HasMaxLength(50).IsRequired();
            e.Property(x => x.BillingType).HasMaxLength(50).IsRequired();
            e.Property(x => x.ExternalId).HasMaxLength(200);
            e.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.TenantId);
            e.HasIndex(x => x.ExternalId);
            e.HasOne<Subscription>().WithMany().HasForeignKey(x => x.SubscriptionId).OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(x => !x.IsDeleted);
        });
    }
}
