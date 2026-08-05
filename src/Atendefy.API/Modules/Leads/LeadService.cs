using Atendefy.API.Infrastructure.Database;
using Atendefy.API.Modules.Leads.Models;
using Atendefy.API.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Atendefy.API.Modules.Leads;

public class LeadService(PublicDbContext db)
{
    public const int NameMax = 200;
    public const int PhoneMax = 30;
    public const int EmailMax = 256;
    public const int BusinessTypeMax = 100;
    public const int MessageMax = 1000;

    /// <summary>
    /// Registra um lead da landing. Reenvio com o mesmo telefone nas últimas 24h
    /// (duplo clique, refresh) responde sucesso sem inserir de novo.
    /// </summary>
    public async Task<Result<Guid>> CreateAsync(
        string name, string phone, string? email, string? businessType, string? message,
        CancellationToken ct = default)
    {
        name = name.Trim();
        phone = phone.Trim();

        if (string.IsNullOrWhiteSpace(name) || name.Length > NameMax)
            return Result<Guid>.Fail("Conta pra gente seu nome.");

        var digits = phone.Count(char.IsDigit);
        if (phone.Length > PhoneMax || digits < 10 || !phone.All(c => char.IsDigit(c) || " ()+.-".Contains(c)))
            return Result<Guid>.Fail("Informe um WhatsApp válido, com DDD. Ex.: (11) 98888-7777.");

        if (email is { Length: > EmailMax } || (!string.IsNullOrWhiteSpace(email) && !email.Contains('@')))
            return Result<Guid>.Fail("E-mail inválido.");

        if (businessType is { Length: > BusinessTypeMax })
            return Result<Guid>.Fail("Tipo de negócio muito longo.");

        if (message is { Length: > MessageMax })
            return Result<Guid>.Fail("Mensagem muito longa (máx. 1000 caracteres).");

        var since = DateTime.UtcNow.AddHours(-24);
        var recent = await db.Leads.AnyAsync(l => l.Phone == phone && l.CreatedAt >= since, ct);
        if (recent) return Result<Guid>.Ok(Guid.Empty);

        var lead = new Lead
        {
            Name = name,
            Phone = phone,
            Email = string.IsNullOrWhiteSpace(email) ? null : email.Trim(),
            BusinessType = string.IsNullOrWhiteSpace(businessType) ? null : businessType.Trim(),
            Message = string.IsNullOrWhiteSpace(message) ? null : message.Trim(),
        };

        db.Leads.Add(lead);
        await db.SaveChangesAsync(ct);

        return Result<Guid>.Ok(lead.Id);
    }
}
