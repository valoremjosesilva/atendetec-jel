using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Atendefy.API.Infrastructure.Database;

public class TenantModelCacheKeyFactory : IModelCacheKeyFactory
{
    public object Create(DbContext context, bool designTime) =>
        context is TenantDbContext tenantContext
            ? (context.GetType(), tenantContext.SchemaName, designTime)
            : (context.GetType(), designTime);
}
