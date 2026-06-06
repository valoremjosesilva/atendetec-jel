using Atendefy.API.Infrastructure.Database;
using Atendefy.API.Modules.Webhooks.Models;
using Microsoft.EntityFrameworkCore;

namespace Atendefy.API.Modules.Webhooks;

public class EvolutionWebhookValidator(PublicDbContext publicDb)
{
    public async Task<WebhookRoute?> ResolveAsync(string token)
    {
        return await publicDb.WebhookRoutes
            .FirstOrDefaultAsync(r => r.Provider == "evolution" && r.LookupKey == token);
    }
}
