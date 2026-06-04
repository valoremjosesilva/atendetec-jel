using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Atendefy.API.Infrastructure.Database;

public class PublicDbContextFactory : IDesignTimeDbContextFactory<PublicDbContext>
{
    public PublicDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<PublicDbContext>()
            .UseNpgsql("Host=localhost;Database=atendefy;Username=atendefy;Password=dev_password")
            .Options;
        return new PublicDbContext(options);
    }
}
