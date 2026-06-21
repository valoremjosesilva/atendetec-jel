using Atendefy.API.Infrastructure.Database;
using Atendefy.API.Modules.Scheduling.Models;
using Atendefy.API.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Atendefy.API.Modules.Scheduling;

public class SchedulingService(TenantDbContextFactory dbFactory)
{
    private static readonly HashSet<string> ValidProviders = ["calcom", "calendly", "other"];

    public async Task<CalendarConfig?> GetAsync(string schemaName)
    {
        await using var db = dbFactory.Create(schemaName);
        return await db.CalendarConfigs.FirstOrDefaultAsync();
    }

    public async Task<Result<CalendarConfig>> UpsertAsync(string schemaName, CalendarConfigRequest request)
    {
        var provider = string.IsNullOrWhiteSpace(request.Provider) ? "calcom" : request.Provider;
        if (!ValidProviders.Contains(provider))
            return Result<CalendarConfig>.Fail("Provider inválido. Use 'calcom', 'calendly' ou 'other'.");

        var bookingUrl = request.BookingUrl?.Trim();
        if (!string.IsNullOrEmpty(bookingUrl) && !IsHttpUrl(bookingUrl))
            return Result<CalendarConfig>.Fail("Link de agendamento inválido. Use uma URL http(s) completa.");

        if (request.Enabled && string.IsNullOrEmpty(bookingUrl))
            return Result<CalendarConfig>.Fail("Informe o link de agendamento para ativar.");

        await using var db = dbFactory.Create(schemaName);

        var existing = await db.CalendarConfigs.FirstOrDefaultAsync();
        if (existing is not null)
        {
            existing.Provider = provider;
            existing.BookingUrl = bookingUrl;
            existing.Enabled = request.Enabled;
            existing.Instructions = request.Instructions?.Trim();
            existing.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return Result<CalendarConfig>.Ok(existing);
        }

        var config = new CalendarConfig
        {
            Provider = provider,
            BookingUrl = bookingUrl,
            Enabled = request.Enabled,
            Instructions = request.Instructions?.Trim()
        };
        db.CalendarConfigs.Add(config);
        await db.SaveChangesAsync();
        return Result<CalendarConfig>.Ok(config);
    }

    private static bool IsHttpUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
