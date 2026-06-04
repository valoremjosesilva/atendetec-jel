namespace Atendefy.API.Modules.Tenants;

public interface ITenantProvisioner
{
    Task ProvisionSchemaAsync(string schemaName);
}
