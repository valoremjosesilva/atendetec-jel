using Atendefy.API.SharedKernel;

namespace Atendefy.API.Modules.Leads.Models;

/// <summary>
/// Lead de interesse capturado pela landing pública (atende.mjml.com.br).
/// Tabela global (schema public) — existe antes de qualquer tenant.
/// </summary>
public class Lead : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    /// <summary>Telefone/WhatsApp informado pelo interessado.</summary>
    public string Phone { get; set; } = string.Empty;
    public string? Email { get; set; }
    /// <summary>Tipo de negócio em texto livre (loja, clínica, restaurante...).</summary>
    public string? BusinessType { get; set; }
    public string? Message { get; set; }
    /// <summary>Marcado pela equipe quando o contato foi feito.</summary>
    public bool IsContacted { get; set; }
}
