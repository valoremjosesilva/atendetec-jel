using Atendefy.API.SharedKernel;

namespace Atendefy.API.Modules.Webhooks.Models;

public class WebhookRoute : BaseEntity
{
    public Guid TenantId { get; set; }
    public string Provider { get; set; } = string.Empty;   // "meta" | "evolution"
    public string LookupKey { get; set; } = string.Empty;  // phone_number_id ou token
    public Guid AccountId { get; set; }                    // whatsapp_accounts.id no schema do tenant
}
