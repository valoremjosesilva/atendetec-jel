using Atendefy.API.Infrastructure.Database;
using Atendefy.API.Modules.Auth;
using Atendefy.API.Modules.Auth.Models;
using Atendefy.API.Modules.Tenants.Models;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Atendefy.Tests.Auth;

public class AuthServiceTests
{
    private static PublicDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<PublicDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static JwtService CreateJwt() =>
        new("test_secret_key_minimum_32_chars_!!!", "Atendefy", "atendefy.com.br");

    [Fact]
    public async Task Login_WithValidCredentials_ShouldReturnAuthResponse()
    {
        var db = CreateDb();
        var tenantId = Guid.NewGuid();
        db.Tenants.Add(new Tenant { Id = tenantId, Name = "Clínica ABC", Subdomain = "clinica-abc" });
        db.TenantUsers.Add(new TenantUser
        {
            TenantId = tenantId,
            Email = "user@test.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("password123"),
            Role = "Owner",
            Name = "Test User"
        });
        await db.SaveChangesAsync();

        var result = await new AuthService(db, CreateJwt())
            .LoginAsync(new LoginRequest("user@test.com", "password123"), "clinica-abc");

        result.IsSuccess.Should().BeTrue();
        result.Value!.AccessToken.Should().NotBeNullOrEmpty();
        result.Value.Role.Should().Be("Owner");
        result.Value.TenantId.Should().Be(tenantId.ToString());
    }

    [Fact]
    public async Task Login_WithWrongPassword_ShouldReturnFail()
    {
        var db = CreateDb();
        var tenantId = Guid.NewGuid();
        db.Tenants.Add(new Tenant { Id = tenantId, Name = "Test", Subdomain = "test-co" });
        db.TenantUsers.Add(new TenantUser
        {
            TenantId = tenantId,
            Email = "user@test.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("correct"),
            Role = "Owner",
            Name = "Test"
        });
        await db.SaveChangesAsync();

        var result = await new AuthService(db, CreateJwt())
            .LoginAsync(new LoginRequest("user@test.com", "wrong"), "test-co");

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("Email ou senha inválidos");
    }

    [Fact]
    public async Task Login_WithUnknownSubdomain_ShouldReturnFail()
    {
        var db = CreateDb();

        var result = await new AuthService(db, CreateJwt())
            .LoginAsync(new LoginRequest("user@test.com", "any"), "unknown-tenant");

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("Tenant não encontrado");
    }
}
