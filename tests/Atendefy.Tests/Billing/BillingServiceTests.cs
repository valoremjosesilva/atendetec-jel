using Atendefy.API.Infrastructure.Database;
using Atendefy.API.Modules.Billing;
using Atendefy.API.Modules.Billing.Gateways;
using Atendefy.API.Modules.Billing.Models;
using Atendefy.API.Modules.Tenants.Models;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Atendefy.Tests.Billing;

public class BillingServiceTests
{
    private static PublicDbContext CreateDb()
    {
        var opts = new DbContextOptionsBuilder<PublicDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new PublicDbContext(opts);
    }

    [Fact]
    public async Task SubscribeAsync_WithValidPlan_ShouldCreateSubscriptionAndInvoice()
    {
        var db = CreateDb();
        var plan = new Plan { Name = "Starter", PriceMonthly = 99.90m, PriceYearly = 999m, LimitsJson = "{}" };
        var tenant = new Tenant { Name = "Empresa ABC", Subdomain = "empresa-abc" };
        db.Plans.Add(plan);
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();

        var gateway = Substitute.For<IBillingGateway>();
        gateway.CreateCustomerAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
               .Returns("cus_ext_001");
        gateway.CreateChargeAsync(Arg.Any<CreateChargeArgs>())
               .Returns(new BillingCharge("pay_ext_001", "https://boleto.url", "1234.5678", null, null));

        var gatewayFactory = Substitute.For<IBillingGatewayFactory>();
        gatewayFactory.Create("asaas").Returns(gateway);

        var svc = new BillingService(db, gatewayFactory);
        var request = new CreateSubscriptionRequest(plan.Id, "asaas", "BOLETO", "monthly", "12345678000190", null);

        var result = await svc.SubscribeAsync(tenant.Id, tenant.Name, "cto@empresa.com", request);

        result.IsSuccess.Should().BeTrue();
        var sub = await db.Subscriptions.FirstOrDefaultAsync(s => s.TenantId == tenant.Id);
        sub.Should().NotBeNull();
        sub!.Status.Should().Be("pending");
        sub.ExternalCustomerId.Should().Be("cus_ext_001");

        var invoice = await db.Invoices.FirstOrDefaultAsync(i => i.SubscriptionId == sub.Id);
        invoice.Should().NotBeNull();
        invoice!.Status.Should().Be("pending");
        invoice.ExternalId.Should().Be("pay_ext_001");
        invoice.BoletoUrl.Should().Be("https://boleto.url");
    }

    [Fact]
    public async Task ProcessPaymentEventAsync_WhenPaid_ShouldActivateSubscriptionAndUpdateTenant()
    {
        var db = CreateDb();
        var plan = new Plan { Name = "Pro", PriceMonthly = 199m, PriceYearly = 1990m, LimitsJson = "{}" };
        var tenant = new Tenant { Name = "Empresa XYZ", Subdomain = "empresa-xyz" };
        db.Plans.Add(plan);
        db.Tenants.Add(tenant);

        var sub = new Subscription
        {
            TenantId = tenant.Id,
            PlanId = plan.Id,
            Status = "pending",
            Provider = "asaas",
            BillingCycle = "monthly",
            ExternalCustomerId = "cus_ext_002"
        };
        db.Subscriptions.Add(sub);

        var invoice = new Invoice
        {
            SubscriptionId = sub.Id,
            TenantId = tenant.Id,
            Amount = 199m,
            Status = "pending",
            Provider = "asaas",
            BillingType = "BOLETO",
            ExternalId = "pay_ext_002",
            DueDate = DateTime.UtcNow.AddDays(3)
        };
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync();

        var svc = new BillingService(db, Substitute.For<IBillingGatewayFactory>());
        await svc.ProcessPaymentEventAsync(new WebhookEvent("pay_ext_002", IsPaid: true, IsOverdue: false, IsCancelled: false));

        var updatedInvoice = await db.Invoices.FindAsync(invoice.Id);
        updatedInvoice!.Status.Should().Be("paid");
        updatedInvoice.PaidAt.Should().NotBeNull();

        var updatedSub = await db.Subscriptions.FindAsync(sub.Id);
        updatedSub!.Status.Should().Be("active");

        var updatedTenant = await db.Tenants.FindAsync(tenant.Id);
        updatedTenant!.PlanId.Should().Be(plan.Id);
        updatedTenant.Status.Should().Be("active");
    }

    [Fact]
    public async Task ProcessPaymentEventAsync_WhenOverdue_ShouldMarkInvoiceAndSubscription()
    {
        var db = CreateDb();
        var tenant = new Tenant { Name = "Empresa Late", Subdomain = "empresa-late" };
        var plan = new Plan { Name = "Starter", PriceMonthly = 99m, LimitsJson = "{}" };
        db.Tenants.Add(tenant);
        db.Plans.Add(plan);

        var sub = new Subscription
        {
            TenantId = tenant.Id, PlanId = plan.Id,
            Status = "active", Provider = "asaas", BillingCycle = "monthly"
        };
        db.Subscriptions.Add(sub);

        var invoice = new Invoice
        {
            SubscriptionId = sub.Id, TenantId = tenant.Id, Amount = 99m,
            Status = "pending", Provider = "asaas", BillingType = "BOLETO",
            ExternalId = "pay_ext_late", DueDate = DateTime.UtcNow.AddDays(-1)
        };
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync();

        var svc = new BillingService(db, Substitute.For<IBillingGatewayFactory>());
        await svc.ProcessPaymentEventAsync(new WebhookEvent("pay_ext_late", false, IsOverdue: true, false));

        var updatedInvoice = await db.Invoices.FindAsync(invoice.Id);
        updatedInvoice!.Status.Should().Be("overdue");

        var updatedSub = await db.Subscriptions.FindAsync(sub.Id);
        updatedSub!.Status.Should().Be("past_due");
    }

    [Fact]
    public async Task SubscribeAsync_WithInvalidProvider_ShouldReturnFail()
    {
        var db = CreateDb();
        var plan = new Plan { Name = "Starter", PriceMonthly = 99m, LimitsJson = "{}" };
        db.Plans.Add(plan);
        await db.SaveChangesAsync();

        var svc = new BillingService(db, Substitute.For<IBillingGatewayFactory>());
        var result = await svc.SubscribeAsync(Guid.NewGuid(), "Test", "test@test.com",
            new CreateSubscriptionRequest(plan.Id, "paypal", "BOLETO", "monthly", "123", null));

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task SubscribeAsync_WithUnknownPlan_ShouldReturnFail()
    {
        var db = CreateDb();
        var svc = new BillingService(db, Substitute.For<IBillingGatewayFactory>());
        var result = await svc.SubscribeAsync(Guid.NewGuid(), "Test", "test@test.com",
            new CreateSubscriptionRequest(Guid.NewGuid(), "asaas", "BOLETO", "monthly", "123", null));

        result.IsSuccess.Should().BeFalse();
    }
}
