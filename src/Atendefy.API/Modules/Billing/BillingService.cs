using Atendefy.API.Infrastructure.Database;
using Atendefy.API.Modules.Billing.Gateways;
using Atendefy.API.Modules.Billing.Models;
using Atendefy.API.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Atendefy.API.Modules.Billing;

public class BillingService(PublicDbContext db, IBillingGatewayFactory gatewayFactory)
{
    private static readonly HashSet<string> ValidProviders = [AppConstants.BillingProvider.Asaas, AppConstants.BillingProvider.Stripe];
    private static readonly HashSet<string> ValidCycles = [AppConstants.BillingCycle.Monthly, AppConstants.BillingCycle.Yearly];

    public async Task<Result<Invoice>> SubscribeAsync(
        Guid tenantId, string tenantName, string email, CreateSubscriptionRequest request)
    {
        if (!ValidProviders.Contains(request.Provider))
            return Result<Invoice>.Fail("Provider inválido. Use 'asaas' ou 'stripe'.");
        if (!ValidCycles.Contains(request.BillingCycle))
            return Result<Invoice>.Fail("Ciclo inválido. Use 'monthly' ou 'yearly'.");

        var plan = await db.Plans.FindAsync(request.PlanId);
        if (plan is null) return Result<Invoice>.Fail("Plano não encontrado.");

        var gateway = gatewayFactory.Create(request.Provider);
        var customerId = await gateway.CreateCustomerAsync(tenantName, email, request.CpfCnpj ?? string.Empty);

        var amount = request.BillingCycle == AppConstants.BillingCycle.Yearly ? plan.PriceYearly : plan.PriceMonthly;
        var dueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(3));
        var description = $"{plan.Name} - {(request.BillingCycle == AppConstants.BillingCycle.Yearly ? "Anual" : "Mensal")}";

        var charge = await gateway.CreateChargeAsync(new CreateChargeArgs(
            customerId, amount, request.BillingType, description, dueDate, request.PaymentMethodId));

        var now = DateTime.UtcNow;
        var subscription = new Subscription
        {
            TenantId = tenantId,
            PlanId = plan.Id,
            Status = AppConstants.SubscriptionStatus.Pending,
            BillingCycle = request.BillingCycle,
            Provider = request.Provider,
            ExternalCustomerId = customerId,
            ExternalId = charge.ExternalId,
            CurrentPeriodStart = now,
            CurrentPeriodEnd = request.BillingCycle == AppConstants.BillingCycle.Yearly ? now.AddYears(1) : now.AddMonths(1)
        };
        db.Subscriptions.Add(subscription);
        await db.SaveChangesAsync();

        var invoice = new Invoice
        {
            SubscriptionId = subscription.Id,
            TenantId = tenantId,
            Amount = amount,
            Status = AppConstants.InvoiceStatus.Pending,
            Provider = request.Provider,
            BillingType = request.BillingType,
            ExternalId = charge.ExternalId,
            DueDate = dueDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
            BoletoUrl = charge.BoletoUrl,
            BoletoBarcode = charge.BoletoBarcode,
            PixCopyPaste = charge.PixCopyPaste,
            ClientSecret = charge.ClientSecret
        };
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync();

        return Result<Invoice>.Ok(invoice);
    }

    public async Task ProcessPaymentEventAsync(WebhookEvent evt)
    {
        var invoice = await db.Invoices
            .FirstOrDefaultAsync(i => i.ExternalId == evt.ExternalId);
        if (invoice is null) return;

        var subscription = await db.Subscriptions.FindAsync(invoice.SubscriptionId);
        if (subscription is null) return;

        if (evt.IsPaid)
        {
            // Guard: já foi processado como pago; replay seguro de ignorar
            if (invoice.Status == AppConstants.InvoiceStatus.Paid) return;

            invoice.Status = AppConstants.InvoiceStatus.Paid;
            invoice.PaidAt = DateTime.UtcNow;
            subscription.Status = AppConstants.SubscriptionStatus.Active;

            var tenant = await db.Tenants.FindAsync(subscription.TenantId);
            if (tenant is not null)
            {
                tenant.PlanId = subscription.PlanId;
                tenant.Status = AppConstants.TenantStatus.Active;
                tenant.UpdatedAt = DateTime.UtcNow;
            }
        }
        else if (evt.IsOverdue)
        {
            // Guard: não rebaixar assinatura já paga ou cancelada
            if (invoice.Status is AppConstants.InvoiceStatus.Paid
                               or AppConstants.InvoiceStatus.Cancelled
                               or AppConstants.InvoiceStatus.Overdue) return;

            invoice.Status = AppConstants.InvoiceStatus.Overdue;
            subscription.Status = AppConstants.SubscriptionStatus.PastDue;
        }
        else if (evt.IsCancelled)
        {
            // Guard: já cancelado; replay seguro de ignorar
            if (invoice.Status == AppConstants.InvoiceStatus.Cancelled) return;

            invoice.Status = AppConstants.InvoiceStatus.Cancelled;
            subscription.Status = AppConstants.SubscriptionStatus.Cancelled;
        }
        else
        {
            // Evento desconhecido — não fazer nada
            return;
        }

        invoice.UpdatedAt = DateTime.UtcNow;
        subscription.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    public async Task<Result> CancelAsync(Guid tenantId)
    {
        var subscription = await db.Subscriptions
            .FirstOrDefaultAsync(s => s.TenantId == tenantId && s.Status != AppConstants.SubscriptionStatus.Cancelled);
        if (subscription is null) return Result.Fail("Assinatura ativa não encontrada.");

        if (!string.IsNullOrEmpty(subscription.ExternalId))
        {
            var gateway = gatewayFactory.Create(subscription.Provider);
            await gateway.CancelChargeAsync(subscription.ExternalId);
        }

        subscription.Status = AppConstants.SubscriptionStatus.Cancelled;
        subscription.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Result.Ok();
    }
}
