using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Atendefy.API.Infrastructure.Database;

public class TenantDbContextFactory(string connectionString)
{
    public TenantDbContext Create(string schemaName)
    {
        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .ReplaceService<IModelCacheKeyFactory, TenantModelCacheKeyFactory>()
            .Options;
        return new TenantDbContext(options, schemaName);
    }
}
