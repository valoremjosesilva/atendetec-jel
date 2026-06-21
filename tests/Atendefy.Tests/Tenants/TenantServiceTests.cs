using Atendefy.API.Infrastructure.Database;
using Atendefy.API.Modules.Tenants;
using Atendefy.API.Modules.Tenants.Models;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Atendefy.Tests.Tenants;

public class TenantServiceTests
{
    private static PublicDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<PublicDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static TenantService CreateService(PublicDbContext db, ITenantProvisioner provisioner) =>
        new(db, provisioner, Microsoft.Extensions.Logging.Abstractions.NullLogger<TenantService>.Instance);

    [Fact]
    public async Task Register_WithValidData_ShouldCreateTenantAndOwner()
    {
        var db = CreateDb();
        var provisioner = Substitute.For<ITenantProvisioner>();
        var sut = CreateService(db, provisioner);

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
    public async Task Register_ShouldCreateTenantAsPending()
    {
        var db = CreateDb();
        var sut = CreateService(db, Substitute.For<ITenantProvisioner>());

        await sut.RegisterAsync(new RegisterTenantRequest(
            "Nova Co", "nova-co", "Owner", "owner@nova.com", "P@ss123"));

        var tenant = await db.Tenants.FirstAsync(t => t.Subdomain == "nova-co");
        tenant.Status.Should().Be("pending");
    }

    [Fact]
    public async Task Activate_ShouldFlipPendingToActive()
    {
        var db = CreateDb();
        db.Tenants.Add(new Tenant { Name = "Co", Subdomain = "co", Status = "pending" });
        await db.SaveChangesAsync();
        var sut = CreateService(db, Substitute.For<ITenantProvisioner>());

        var result = await sut.ActivateAsync("co");

        result.IsSuccess.Should().BeTrue();
        var tenant = await db.Tenants.FirstAsync(t => t.Subdomain == "co");
        tenant.Status.Should().Be("active");
    }

    [Fact]
    public async Task Activate_UnknownSubdomain_ShouldFail()
    {
        var db = CreateDb();
        var sut = CreateService(db, Substitute.For<ITenantProvisioner>());

        var result = await sut.ActivateAsync("nao-existe");

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task Register_WithDuplicateSubdomain_ShouldFailWithMessage()
    {
        var db = CreateDb();
        db.Tenants.Add(new Tenant { Name = "Existing", Subdomain = "existing" });
        await db.SaveChangesAsync();

        var provisioner = Substitute.For<ITenantProvisioner>();
        var result = await CreateService(db, provisioner)
            .RegisterAsync(new RegisterTenantRequest("Other", "existing", "User", "u@t.com", "P@ss1"));

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("subdomínio");
    }

    [Fact]
    public async Task Register_ShouldNormalizeSubdomainToLowercase()
    {
        var db = CreateDb();
        var provisioner = Substitute.For<ITenantProvisioner>();
        var sut = CreateService(db, provisioner);

        await sut.RegisterAsync(new RegisterTenantRequest(
            "Test", "minhaempresa", "User", "u@t.com", "P@ss1"));

        var tenant = await db.Tenants.FirstAsync();
        tenant.Subdomain.Should().Be("minhaempresa");
    }

    [Fact]
    public async Task Register_WithDuplicateSubdomain_ShouldNotCallProvisioner()
    {
        var db = CreateDb();
        db.Tenants.Add(new Tenant { Name = "Existing", Subdomain = "existing" });
        await db.SaveChangesAsync();

        var provisioner = Substitute.For<ITenantProvisioner>();
        await CreateService(db, provisioner)
            .RegisterAsync(new RegisterTenantRequest("Other", "existing", "User", "u@t.com", "P@ss1"));

        await provisioner.DidNotReceive().ProvisionSchemaAsync(Arg.Any<string>());
    }

    [Fact]
    public async Task Register_WhenProvisionerThrows_ShouldReturnFailAndRemoveTenant()
    {
        var db = CreateDb();
        var provisioner = Substitute.For<ITenantProvisioner>();
        provisioner.ProvisionSchemaAsync(Arg.Any<string>())
            .ThrowsAsync(new Exception("Postgres DDL error"));

        var result = await CreateService(db, provisioner)
            .RegisterAsync(new RegisterTenantRequest(
                "Test Co", "testco", "Owner", "owner@test.com", "P@ss123"));

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("provisionar");
        var tenant = await db.Tenants.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.Subdomain == "testco");
        tenant.Should().BeNull();
    }

    [Fact]
    public async Task Register_WithPasswordOver72Chars_ShouldReturnFail()
    {
        var db = CreateDb();
        var provisioner = Substitute.For<ITenantProvisioner>();
        var longPassword = new string('a', 73);

        var result = await CreateService(db, provisioner)
            .RegisterAsync(new RegisterTenantRequest(
                "Test", "test-long", "User", "u@t.com", longPassword));

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("Senha inválida");
    }

    [Fact]
    public async Task Register_WithEmptyRequiredField_ShouldReturnFail()
    {
        var db = CreateDb();
        var provisioner = Substitute.For<ITenantProvisioner>();

        var result = await CreateService(db, provisioner)
            .RegisterAsync(new RegisterTenantRequest(
                "Test", "", "User", "u@t.com", "P@ss1"));

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("Todos os campos são obrigatórios");
        await provisioner.DidNotReceive().ProvisionSchemaAsync(Arg.Any<string>());
    }

    [Fact]
    public async Task Register_WithInvalidSubdomain_ShouldReturnFail()
    {
        var db = CreateDb();
        var provisioner = Substitute.For<ITenantProvisioner>();

        var result = await CreateService(db, provisioner)
            .RegisterAsync(new RegisterTenantRequest(
                "Test", "invalid subdomain!", "User", "u@t.com", "P@ss1"));

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Subdomínio inválido");
        await provisioner.DidNotReceive().ProvisionSchemaAsync(Arg.Any<string>());
    }
}
