using System.Text;

namespace Atendefy.API.Modules.WhatsApp.Models;

public enum InteractiveKind
{
    /// <summary>Lista (até 10 linhas) — para serviços/profissionais/dias/horários.</summary>
    List,
    /// <summary>Botões de resposta (até 3) — para confirmação Sim/Não.</summary>
    Buttons
}

/// <summary>Opção interativa: Id retornado ao usuário tocar; Title exibido.</summary>
public sealed record InteractiveOption(string Id, string Title);

/// <summary>
/// Mensagem interativa do WhatsApp. Providers que não suportam (ex.: Evolution/Baileys,
/// onde listas/botões foram descontinuados) usam <see cref="FallbackText"/> / texto numerado.
/// </summary>
public sealed record InteractiveMessage(
    InteractiveKind Kind,
    string Body,
    IReadOnlyList<InteractiveOption> Options,
    string ButtonText = "Ver opções",
    string? Header = null,
    string? Footer = null,
    string FallbackText = "");

/// <summary>Renderização em texto numerado (fallback quando não há interativo nativo).</summary>
public static class InteractiveText
{
    public static string Render(InteractiveMessage m)
    {
        if (!string.IsNullOrEmpty(m.FallbackText)) return m.FallbackText;

        var sb = new StringBuilder();
        if (!string.IsNullOrEmpty(m.Header)) sb.AppendLine(m.Header);
        sb.AppendLine(m.Body);

        if (m.Kind == InteractiveKind.List)
        {
            for (var i = 0; i < m.Options.Count; i++)
                sb.AppendLine($"{i + 1}. {m.Options[i].Title}");
            sb.AppendLine();
            sb.Append("Responda com o número da opção.");
        }
        else
        {
            sb.Append(string.Join(" / ", m.Options.Select(o => o.Title)));
        }

        if (!string.IsNullOrEmpty(m.Footer)) { sb.AppendLine(); sb.Append(m.Footer); }
        return sb.ToString();
    }
}
