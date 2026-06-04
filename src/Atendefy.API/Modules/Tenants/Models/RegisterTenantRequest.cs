namespace Atendefy.API.Modules.Tenants.Models;

public record RegisterTenantRequest(
    string CompanyName,
    string Subdomain,
    string OwnerName,
    string OwnerEmail,
    string OwnerPassword
);
