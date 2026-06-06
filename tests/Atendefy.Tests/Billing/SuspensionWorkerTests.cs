using Atendefy.API.Infrastructure.Database;
using Atendefy.API.Modules.Billing;
using Atendefy.API.Modules.Billing.Models;
using Atendefy.API.Modules.Tenants.Models;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Atendefy.Tests.Billing;

public class SuspensionWorkerTests
{
    private static ServiceProvider CreateServiceProvider()
    {
        var dbName = Guid.NewGuid().ToString();
        var services = new ServiceCollection();
        services.AddDbContext<PublicDbContext>(opt =>
            opt.UseInMemoryDatabase(dbName));
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task RunOnceAsync_WhenInvoiceOverduePastGracePeriod_ShouldSuspendTenant()
    {
        var sp = CreateServiceProvider();
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PublicDbContext>();

        var tenant = new Tenant { Name = "Inadimplente", Subdomain = "inadimplente", Status = "active" };
        var plan = new Plan { Name = "Starter", PriceMonthly = 99m, LimitsJson = "{}" };
        db.Tenants.Add(tenant);
        db.Plans.Add(plan);

        var sub = new Subscription
        {
            TenantId = tenant.Id, PlanId = plan.Id,
            Status = "past_due", Provider = "asaas", BillingCycle = "monthly"
        };
        db.Subscriptions.Add(sub);

        db.Invoices.Add(new Invoice
        {
            SubscriptionId = sub.Id, TenantId = tenant.Id, Amount = 99m,
            Status = "overdue", Provider = "asaas", BillingType = "BOLETO",
            DueDate = DateTime.UtcNow.AddDays(-4)  // past 3-day grace period
        });
        await db.SaveChangesAsync();

        var worker = new SuspensionWorker(sp, NullLogger<SuspensionWorker>.Instance);
        await worker.RunOnceAsync();

        await db.Entry(tenant).ReloadAsync();
        var updated = tenant;
        updated.Status.Should().Be("suspended");
        updated.UpdatedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task RunOnceAsync_WhenInvoiceOverdueWithinGracePeriod_ShouldNotSuspend()
    {
        var sp = CreateServiceProvider();
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PublicDbContext>();

        var tenant = new Tenant { Name = "Ainda no Prazo", Subdomain = "prazo", Status = "active" };
        var plan = new Plan { Name = "Pro", PriceMonthly = 199m, LimitsJson = "{}" };
        db.Tenants.Add(tenant);
        db.Plans.Add(plan);

        var sub = new Subscription
        {
            TenantId = tenant.Id, PlanId = plan.Id,
            Status = "past_due", Provider = "asaas", BillingCycle = "monthly"
        };
        db.Subscriptions.Add(sub);

        db.Invoices.Add(new Invoice
        {
            SubscriptionId = sub.Id, TenantId = tenant.Id, Amount = 199m,
            Status = "overdue", Provider = "asaas", BillingType = "BOLETO",
            DueDate = DateTime.UtcNow.AddDays(-1)  // still within grace period
        });
        await db.SaveChangesAsync();

        var worker = new SuspensionWorker(sp, NullLogger<SuspensionWorker>.Instance);
        await worker.RunOnceAsync();

        await db.Entry(tenant).ReloadAsync();
        tenant.Status.Should().Be("active");  // not suspended yet
    }

    [Fact]
    public async Task RunOnceAsync_WhenTenantAlreadySuspended_ShouldNotUpdateUpdatedAt()
    {
        var sp = CreateServiceProvider();
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PublicDbContext>();

        var tenant = new Tenant { Name = "Já Suspenso", Subdomain = "ja-suspenso", Status = "suspended" };
        var plan = new Plan { Name = "Starter", PriceMonthly = 99m, LimitsJson = "{}" };
        db.Tenants.Add(tenant);
        db.Plans.Add(plan);

        var sub = new Subscription
        {
            TenantId = tenant.Id, PlanId = plan.Id,
            Status = "past_due", Provider = "asaas", BillingCycle = "monthly"
        };
        db.Subscriptions.Add(sub);

        db.Invoices.Add(new Invoice
        {
            SubscriptionId = sub.Id, TenantId = tenant.Id, Amount = 99m,
            Status = "overdue", Provider = "asaas", BillingType = "BOLETO",
            DueDate = DateTime.UtcNow.AddDays(-10)
        });
        await db.SaveChangesAsync();

        var worker = new SuspensionWorker(sp, NullLogger<SuspensionWorker>.Instance);
        await worker.RunOnceAsync();

        await db.Entry(tenant).ReloadAsync();
        tenant.Status.Should().Be("suspended");
        tenant.UpdatedAt.Should().BeNull();  // not touched — already suspended
    }
}
