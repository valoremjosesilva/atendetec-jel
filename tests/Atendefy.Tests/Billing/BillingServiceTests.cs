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

    [Fact]
    public async Task CancelAsync_WithActiveSubscription_ShouldMarkCancelledAndCallGateway()
    {
        var db = CreateDb();
        var tenant = new Tenant { Name = "Cancela Corp", Subdomain = "cancela-corp" };
        var plan = new Plan { Name = "Starter", PriceMonthly = 99m, LimitsJson = "{}" };
        db.Tenants.Add(tenant);
        db.Plans.Add(plan);

        var sub = new Subscription
        {
            TenantId = tenant.Id, PlanId = plan.Id,
            Status = "active", Provider = "asaas", BillingCycle = "monthly",
            ExternalId = "pay_to_cancel"
        };
        db.Subscriptions.Add(sub);
        await db.SaveChangesAsync();

        var gateway = Substitute.For<IBillingGateway>();
        var gatewayFactory = Substitute.For<IBillingGatewayFactory>();
        gatewayFactory.Create("asaas").Returns(gateway);

        var svc = new BillingService(db, gatewayFactory);
        var result = await svc.CancelAsync(tenant.Id);

        result.IsSuccess.Should().BeTrue();
        var updatedSub = await db.Subscriptions.FindAsync(sub.Id);
        updatedSub!.Status.Should().Be("cancelled");
        await gateway.Received(1).CancelChargeAsync("pay_to_cancel");
    }

    [Fact]
    public async Task ProcessPaymentEventAsync_WhenCancelled_ShouldMarkInvoiceAndSubscriptionCancelled()
    {
        var db = CreateDb();
        var tenant = new Tenant { Name = "Cancelled Corp", Subdomain = "cancelled-corp" };
        var plan = new Plan { Name = "Starter", PriceMonthly = 99m, LimitsJson = "{}" };
        db.Tenants.Add(tenant);
        db.Plans.Add(plan);

        var sub = new Subscription
        {
            TenantId = tenant.Id, PlanId = plan.Id,
            Status = "active", Provider = "stripe", BillingCycle = "monthly"
        };
        db.Subscriptions.Add(sub);

        var invoice = new Invoice
        {
            SubscriptionId = sub.Id, TenantId = tenant.Id, Amount = 99m,
            Status = "pending", Provider = "stripe", BillingType = "CREDIT_CARD",
            ExternalId = "pi_cancelled", DueDate = DateTime.UtcNow.AddDays(1)
        };
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync();

        var svc = new BillingService(db, Substitute.For<IBillingGatewayFactory>());
        await svc.ProcessPaymentEventAsync(new WebhookEvent("pi_cancelled", false, false, IsCancelled: true));

        var updatedInvoice = await db.Invoices.FindAsync(invoice.Id);
        updatedInvoice!.Status.Should().Be("cancelled");

        var updatedSub = await db.Subscriptions.FindAsync(sub.Id);
        updatedSub!.Status.Should().Be("cancelled");
    }

    [Fact]
    public async Task ProcessPaymentEvent_WhenAlreadyPaid_DoesNotChangePaidAt()
    {
        var db = CreateDb();
        var tenant = new Tenant { Name = "Paid Corp", Subdomain = "paid-corp" };
        var plan = new Plan { Name = "Starter", PriceMonthly = 99m, LimitsJson = "{}" };
        db.Tenants.Add(tenant);
        db.Plans.Add(plan);

        var sub = new Subscription
        {
            TenantId = tenant.Id, PlanId = plan.Id,
            Status = "active", Provider = "asaas", BillingCycle = "monthly"
        };
        db.Subscriptions.Add(sub);

        var originalPaidAt = DateTime.UtcNow.AddHours(-1);
        var invoice = new Invoice
        {
            SubscriptionId = sub.Id, TenantId = tenant.Id, Amount = 99m,
            Status = "paid", Provider = "asaas", BillingType = "BOLETO",
            ExternalId = "pay_already_paid", DueDate = DateTime.UtcNow.AddDays(1),
            PaidAt = originalPaidAt
        };
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync();

        var svc = new BillingService(db, Substitute.For<IBillingGatewayFactory>());
        await svc.ProcessPaymentEventAsync(new WebhookEvent("pay_already_paid", IsPaid: true, IsOverdue: false, IsCancelled: false));

        var updated = await db.Invoices.FindAsync(invoice.Id);
        updated!.PaidAt.Should().BeCloseTo(originalPaidAt, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task ProcessPaymentEvent_WhenOverdueArrivesAfterPaid_DoesNotDowngradeSubscription()
    {
        var db = CreateDb();
        var tenant = new Tenant { Name = "Paid Then Overdue", Subdomain = "paid-then-overdue" };
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
            Status = "paid", Provider = "asaas", BillingType = "BOLETO",
            ExternalId = "pay_paid_then_overdue", DueDate = DateTime.UtcNow.AddDays(-1),
            PaidAt = DateTime.UtcNow.AddDays(-2)
        };
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync();

        var svc = new BillingService(db, Substitute.For<IBillingGatewayFactory>());
        await svc.ProcessPaymentEventAsync(new WebhookEvent("pay_paid_then_overdue", IsPaid: false, IsOverdue: true, IsCancelled: false));

        var updatedSub = await db.Subscriptions.FindAsync(sub.Id);
        updatedSub!.Status.Should().Be("active");
    }

    [Fact]
    public async Task ProcessPaymentEvent_WhenAlreadyCancelled_DoesNotReprocess()
    {
        var db = CreateDb();
        var tenant = new Tenant { Name = "Already Cancelled", Subdomain = "already-cancelled" };
        var plan = new Plan { Name = "Starter", PriceMonthly = 99m, LimitsJson = "{}" };
        db.Tenants.Add(tenant);
        db.Plans.Add(plan);

        var sub = new Subscription
        {
            TenantId = tenant.Id, PlanId = plan.Id,
            Status = "cancelled", Provider = "asaas", BillingCycle = "monthly"
        };
        db.Subscriptions.Add(sub);

        var invoice = new Invoice
        {
            SubscriptionId = sub.Id, TenantId = tenant.Id, Amount = 99m,
            Status = "cancelled", Provider = "asaas", BillingType = "BOLETO",
            ExternalId = "pay_replay_cancelled", DueDate = DateTime.UtcNow.AddDays(1)
        };
        db.Invoices.Add(invoice);
        var beforeUpdate = invoice.UpdatedAt;
        await db.SaveChangesAsync();

        var svc = new BillingService(db, Substitute.For<IBillingGatewayFactory>());
        await svc.ProcessPaymentEventAsync(new WebhookEvent("pay_replay_cancelled", IsPaid: false, IsOverdue: false, IsCancelled: true));

        var updatedSub = await db.Subscriptions.FindAsync(sub.Id);
        updatedSub!.Status.Should().Be("cancelled");
    }
}
