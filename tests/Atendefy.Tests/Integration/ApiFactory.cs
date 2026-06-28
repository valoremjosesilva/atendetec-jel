using Atendefy.API.Infrastructure.Database;
using Atendefy.API.Modules.Auth;
using Atendefy.API.Modules.Auth.Models;
using Atendefy.API.Modules.Tenants.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using StackExchange.Redis;
using System.Net.Http.Headers;

namespace Atendefy.Tests.Integration;

public class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly string TenantSchemaName = $"tenant_{TenantId:N}";
    public const string Subdomain = "test-tenant";
    public const string BaseDomain = "atendefy.com.br";
    public const string UserEmail = "admin@test.com";
    public const string UserPassword = "TestPassword123!";
    private const string JwtSecret = "test_secret_key_minimum_32_chars_!!!";
    private const string JwtIssuer = "Atendefy";
    private const string JwtAudience = "atendefy.com.br";

    public ApiFactory()
    {
        // Program.cs reads config BEFORE Build() is called, so ConfigureWebHost's
        // ConfigureAppConfiguration is too late. We must provide values via env vars
        // set before the server starts. ASPNETCORE_CONTENTROOT points to the API
        // source directory so appsettings.Testing.json is found there.
        var apiSourceDir = FindApiSourceDirectory();
        if (apiSourceDir != null)
            Environment.SetEnvironmentVariable("ASPNETCORE_CONTENTROOT", apiSourceDir);

        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Testing");
    }

    private static string? FindApiSourceDirectory()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            if (Directory.GetFiles(dir, "*.slnx").Length > 0 ||
                Directory.GetFiles(dir, "*.sln").Length > 0)
            {
                var candidate = Path.Combine(dir, "src", "Atendefy.API");
                return Directory.Exists(candidate) ? candidate : null;
            }
            dir = Directory.GetParent(dir)?.FullName;
        }
        return null;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            // Replace PublicDbContext with InMemory
            services.RemoveAll<DbContextOptions<PublicDbContext>>();
            services.RemoveAll<PublicDbContext>();
            services.AddDbContext<PublicDbContext>(opt =>
                opt.UseInMemoryDatabase("IntegrationTestPublicDb"));

            // Replace TenantDbContextFactory with InMemory version
            services.RemoveAll<TenantDbContextFactory>();
            services.AddSingleton<TenantDbContextFactory>(new InMemoryTenantDbContextFactory());

            // Replace Redis IConnectionMultiplexer with mock
            services.RemoveAll<IConnectionMultiplexer>();
            var mockConn = Substitute.For<IConnectionMultiplexer>();
            var mockDb = Substitute.For<IDatabase>();
            mockDb.StringIncrementAsync(Arg.Any<RedisKey>(), Arg.Any<long>()).Returns(1L);
            mockDb.StringGetAsync(Arg.Any<RedisKey>()).Returns(new RedisValue());
            mockDb.StringSetAsync(Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), Arg.Any<TimeSpan?>(),
                Arg.Any<When>(), Arg.Any<CommandFlags>()).Returns(true);
            mockConn.GetDatabase().Returns(mockDb);
            services.AddSingleton(mockConn);

            // Remove hosted services — workers need real PostgreSQL/Redis
            services.RemoveAll<IHostedService>();
        });
    }

    public async Task InitializeAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PublicDbContext>();
        await db.Database.EnsureCreatedAsync();

        if (!await db.Tenants.AnyAsync(t => t.Id == TenantId))
        {
            db.Tenants.Add(new Tenant
            {
                Id = TenantId,
                Name = "Test Tenant",
                Subdomain = Subdomain,
                Status = "active"
            });
            db.TenantUsers.Add(new TenantUser
            {
                TenantId = TenantId,
                Email = UserEmail,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(UserPassword),
                Role = "Owner",
                Name = "Test Admin"
            });
            await db.SaveChangesAsync();
        }
    }

    public new Task DisposeAsync() => Task.CompletedTask;

    public string MintToken() =>
        Services.GetRequiredService<JwtService>()
            .GenerateAccessToken(Guid.NewGuid(), TenantId, "Owner", UserEmail);

    public string MintTokenForTenant(Guid tenantId) =>
        Services.GetRequiredService<JwtService>()
            .GenerateAccessToken(Guid.NewGuid(), tenantId, "Owner", "other@test.com");

    public HttpClient CreateTenantClient(string? subdomain = null)
    {
        subdomain ??= Subdomain;
        return CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri($"http://{subdomain}.{BaseDomain}")
        });
    }

    public HttpClient CreateAuthenticatedClient()
    {
        var client = CreateTenantClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MintToken());
        return client;
    }
}
