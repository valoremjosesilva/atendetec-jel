using Atendefy.API.Infrastructure.Database;
using Atendefy.API.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using static Atendefy.API.SharedKernel.AppConstants;

namespace Atendefy.API.Modules.Billing;

public class SuspensionWorker(IServiceProvider serviceProvider, ILogger<SuspensionWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await RunOnceAsync();
            await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
        }
    }

    public async Task RunOnceAsync()
    {
        using var scope = serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PublicDbContext>();

        var gracePeriodCutoff = DateTime.UtcNow.AddDays(-3);

        var overdueTenantsIds = await db.Invoices
            .Where(i => i.Status == InvoiceStatus.Overdue && i.DueDate < gracePeriodCutoff)
            .Select(i => i.TenantId)
            .Distinct()
            .ToListAsync();

        if (overdueTenantsIds.Count == 0) return;

        var tenantsToSuspend = await db.Tenants
            .Where(t => overdueTenantsIds.Contains(t.Id) && t.Status == TenantStatus.Active)
            .ToListAsync();

        foreach (var tenant in tenantsToSuspend)
        {
            tenant.Status = TenantStatus.Suspended;
            tenant.UpdatedAt = DateTime.UtcNow;
            logger.LogWarning("Tenant {TenantId} ({Name}) suspenso por inadimplência", tenant.Id, tenant.Name);
        }

        if (tenantsToSuspend.Count > 0)
            await db.SaveChangesAsync();
    }
}
