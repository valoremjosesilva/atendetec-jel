using Atendefy.API.Infrastructure.Database;
using Atendefy.API.Modules.Scheduling.Horafy;
using Atendefy.API.Modules.Scheduling.Models;
using Atendefy.API.SharedKernel;
using Atendefy.API.SharedKernel.Extensions;
using Microsoft.EntityFrameworkCore;

namespace Atendefy.API.Modules.Scheduling;

public class SchedulingService(TenantDbContextFactory dbFactory, string encryptionKey)
{
    private static readonly HashSet<string> ValidProviders = ["calcom", "calendly", "horafy", "other"];

    public async Task<CalendarConfig?> GetAsync(string schemaName)
    {
        await using var db = dbFactory.Create(schemaName);
        return await db.CalendarConfigs.FirstOrDefaultAsync();
    }

    /// <summary>
    /// Retorna a conexão (com a chave já descriptografada) quando o provider é "horafy"
    /// e está completamente configurado. Null caso contrário.
    /// </summary>
    public async Task<HorafyConnection?> GetHorafyConnectionAsync(string schemaName)
    {
        await using var db = dbFactory.Create(schemaName);
        var cfg = await db.CalendarConfigs.FirstOrDefaultAsync();
        if (cfg is null || cfg.Provider != "horafy") return null;
        if (string.IsNullOrEmpty(cfg.ApiBaseUrl) || string.IsNullOrEmpty(cfg.TenantSlug)
            || string.IsNullOrEmpty(cfg.ApiKeyEncrypted))
            return null;

        var apiKey = AesEncryption.Decrypt(cfg.ApiKeyEncrypted, encryptionKey);
        return new HorafyConnection(cfg.ApiBaseUrl, cfg.TenantSlug, apiKey);
    }

    public async Task<Result<CalendarConfig>> UpsertAsync(string schemaName, CalendarConfigRequest request)
    {
        var provider = string.IsNullOrWhiteSpace(request.Provider) ? "calcom" : request.Provider.Trim();
        if (!ValidProviders.Contains(provider))
            return Result<CalendarConfig>.Fail("Provider inválido. Use 'calcom', 'calendly', 'horafy' ou 'other'.");

        var isHorafy   = provider == "horafy";
        var bookingUrl = request.BookingUrl?.Trim();
        var apiBaseUrl = request.ApiBaseUrl?.Trim();
        var tenantSlug = request.TenantSlug?.Trim();
        var apiKey     = request.ApiKey?.Trim();

        if (isHorafy)
        {
            if (!string.IsNullOrEmpty(apiBaseUrl) && !IsHttpUrl(apiBaseUrl))
                return Result<CalendarConfig>.Fail("URL da API do Horafy inválida. Use uma URL http(s) completa.");
        }
        else
        {
            if (!string.IsNullOrEmpty(bookingUrl) && !IsHttpUrl(bookingUrl))
                return Result<CalendarConfig>.Fail("Link de agendamento inválido. Use uma URL http(s) completa.");
            if (request.Enabled && string.IsNullOrEmpty(bookingUrl))
                return Result<CalendarConfig>.Fail("Informe o link de agendamento para ativar.");
        }

        await using var db = dbFactory.Create(schemaName);
        var existing = await db.CalendarConfigs.FirstOrDefaultAsync();

        // A chave pode já existir (atualização sem reenviar a chave).
        var hasKeyAfterSave = !string.IsNullOrEmpty(apiKey) || !string.IsNullOrEmpty(existing?.ApiKeyEncrypted);
        if (isHorafy && request.Enabled &&
            (string.IsNullOrEmpty(apiBaseUrl) || string.IsNullOrEmpty(tenantSlug) || !hasKeyAfterSave))
            return Result<CalendarConfig>.Fail(
                "Para ativar o Horafy, informe a URL da API, o slug do tenant e a chave de API.");

        if (existing is not null)
        {
            existing.Provider     = provider;
            existing.Enabled      = request.Enabled;
            existing.Instructions = request.Instructions?.Trim();

            if (isHorafy)
            {
                existing.ApiBaseUrl        = apiBaseUrl;
                existing.TenantSlug        = tenantSlug;
                existing.DefaultServiceId  = request.DefaultServiceId;
                existing.DefaultResourceId = request.DefaultResourceId;
                if (!string.IsNullOrEmpty(apiKey))
                    existing.ApiKeyEncrypted = AesEncryption.Encrypt(apiKey, encryptionKey);
            }
            else
            {
                existing.BookingUrl = bookingUrl;
                if (request.Enabled && string.IsNullOrEmpty(existing.WebhookToken))
                    existing.WebhookToken = Guid.NewGuid().ToString("N");
            }

            existing.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return Result<CalendarConfig>.Ok(existing);
        }

        var config = new CalendarConfig
        {
            Provider     = provider,
            Enabled      = request.Enabled,
            Instructions = request.Instructions?.Trim()
        };

        if (isHorafy)
        {
            config.ApiBaseUrl        = apiBaseUrl;
            config.TenantSlug        = tenantSlug;
            config.DefaultServiceId  = request.DefaultServiceId;
            config.DefaultResourceId = request.DefaultResourceId;
            if (!string.IsNullOrEmpty(apiKey))
                config.ApiKeyEncrypted = AesEncryption.Encrypt(apiKey, encryptionKey);
        }
        else
        {
            config.BookingUrl   = bookingUrl;
            config.WebhookToken = request.Enabled ? Guid.NewGuid().ToString("N") : null;
        }

        db.CalendarConfigs.Add(config);
        await db.SaveChangesAsync();
        return Result<CalendarConfig>.Ok(config);
    }

    public async Task<List<Appointment>> ListAppointmentsAsync(string schemaName, int take = 50)
    {
        await using var db = dbFactory.Create(schemaName);
        return await db.Appointments
            .OrderByDescending(a => a.StartTime)
            .Take(take)
            .ToListAsync();
    }

    // Idempotente por external_id (uid do Cal.com): cria ou atualiza o agendamento.
    public async Task UpsertAppointmentAsync(string schemaName, Appointment incoming)
    {
        await using var db = dbFactory.Create(schemaName);
        var existing = await db.Appointments
            .FirstOrDefaultAsync(a => a.ExternalId == incoming.ExternalId);

        if (existing is null)
        {
            db.Appointments.Add(incoming);
        }
        else
        {
            existing.Title = incoming.Title ?? existing.Title;
            existing.StartTime = incoming.StartTime ?? existing.StartTime;
            existing.EndTime = incoming.EndTime ?? existing.EndTime;
            existing.AttendeeName = incoming.AttendeeName ?? existing.AttendeeName;
            existing.AttendeeEmail = incoming.AttendeeEmail ?? existing.AttendeeEmail;
            existing.AttendeePhone = incoming.AttendeePhone ?? existing.AttendeePhone;
            existing.Status = incoming.Status;
            existing.UpdatedAt = DateTime.UtcNow;
        }
        await db.SaveChangesAsync();
    }

    private static bool IsHttpUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
