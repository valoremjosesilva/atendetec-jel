namespace Atendefy.API.Modules.WhatsApp.Models;

// Configuração do servidor Evolution conhecida pela própria API (vinda de Evolution:BaseUrl /
// Evolution:ApiKey). Permite montar o config da conta automaticamente, sem o usuário digitar.
// CallbackUrl é a URL base (interna) que o Evolution usa para entregar webhooks de volta à API.
public record EvolutionServerConfig(string BaseUrl, string ApiKey, string CallbackUrl);
