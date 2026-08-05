using Atendefy.API.Infrastructure.Database;
using Atendefy.API.Modules.Leads;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Atendefy.Tests.Leads;

public class LeadServiceTests
{
    private static PublicDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<PublicDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    [Fact]
    public async Task Create_WithValidData_PersistsLead()
    {
        var db = CreateDb();
        var sut = new LeadService(db);

        var result = await sut.CreateAsync(
            "Maria da Silva", "(11) 98888-7777", "maria@exemplo.com", "Loja de roupas", "Quero saber mais");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBe(Guid.Empty);
        var lead = await db.Leads.SingleAsync();
        lead.Name.Should().Be("Maria da Silva");
        lead.Phone.Should().Be("(11) 98888-7777");
        lead.BusinessType.Should().Be("Loja de roupas");
        lead.IsContacted.Should().BeFalse();
    }

    [Fact]
    public async Task Create_DuplicatePhoneWithin24h_ReturnsSuccessWithoutInserting()
    {
        var db = CreateDb();
        var sut = new LeadService(db);
        await sut.CreateAsync("Maria", "(11) 98888-7777", null, null, null);

        var result = await sut.CreateAsync("Maria de novo", "(11) 98888-7777", null, null, null);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(Guid.Empty);
        (await db.Leads.CountAsync()).Should().Be(1);
    }

    [Theory]
    [InlineData("", "(11) 98888-7777")]      // nome vazio
    [InlineData("Maria", "")]                // telefone vazio
    [InlineData("Maria", "9 8888")]          // menos de 10 dígitos
    [InlineData("Maria", "abc11988887777")]  // letras no telefone
    public async Task Create_InvalidRequiredFields_Fails(string name, string phone)
    {
        var db = CreateDb();
        var sut = new LeadService(db);

        var result = await sut.CreateAsync(name, phone, null, null, null);

        result.IsSuccess.Should().BeFalse();
        (await db.Leads.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Create_InvalidEmail_Fails()
    {
        var db = CreateDb();
        var sut = new LeadService(db);

        var result = await sut.CreateAsync("Maria", "(11) 98888-7777", "nao-e-email", null, null);

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task Create_MessageTooLong_Fails()
    {
        var db = CreateDb();
        var sut = new LeadService(db);

        var result = await sut.CreateAsync(
            "Maria", "(11) 98888-7777", null, null, new string('x', 1001));

        result.IsSuccess.Should().BeFalse();
    }
}
