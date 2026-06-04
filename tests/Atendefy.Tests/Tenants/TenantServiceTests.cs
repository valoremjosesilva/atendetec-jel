using Atendefy.API.Infrastructure.Database;
using Atendefy.API.Modules.Tenants;
using Atendefy.API.Modules.Tenants.Models;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Atendefy.Tests.Tenants;

public class TenantServiceTests
{
    private static PublicDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<PublicDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    [Fact]
    public async Task Register_WithValidData_ShouldCreateTenantAndOwner()
    {
        var db = CreateDb();
        var provisioner = Substitute.For<ITenantProvisioner>();
        var sut = new TenantService(db, provisioner);

        var result = await sut.RegisterAsync(new RegisterTenantRequest(
            CompanyName: "Clínica ABC",
            Subdomain: "clinica-abc",
            OwnerName: "Dr. João",
            OwnerEmail: "joao@clinica.com",
            OwnerPassword: "Senha@123"
        ));

        result.IsSuccess.Should().BeTrue();
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Subdomain == "clinica-abc");
        tenant.Should().NotBeNull();
        tenant!.Name.Should().Be("Clínica ABC");
        var owner = await db.TenantUsers.FirstOrDefaultAsync(u => u.TenantId == tenant.Id);
        owner!.Role.Should().Be("Owner");
        await provisioner.Received(1).ProvisionSchemaAsync(Arg.Any<string>());
    }

    [Fact]
    public async Task Register_WithDuplicateSubdomain_ShouldFailWithMessage()
    {
        var db = CreateDb();
        db.Tenants.Add(new Tenant { Name = "Existing", Subdomain = "existing" });
        await db.SaveChangesAsync();

        var provisioner = Substitute.For<ITenantProvisioner>();
        var result = await new TenantService(db, provisioner)
            .RegisterAsync(new RegisterTenantRequest("Other", "existing", "User", "u@t.com", "P@ss1"));

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("subdomínio");
    }

    [Fact]
    public async Task Register_ShouldNormalizeSubdomainToLowercase()
    {
        var db = CreateDb();
        var provisioner = Substitute.For<ITenantProvisioner>();
        var sut = new TenantService(db, provisioner);

        await sut.RegisterAsync(new RegisterTenantRequest(
            "Test", "MiNhAEmPrEsA", "User", "u@t.com", "P@ss1"));

        var tenant = await db.Tenants.FirstAsync();
        tenant.Subdomain.Should().Be("minhaempresa");
    }
}
