using Atendefy.API.Modules.Scheduling;
using Atendefy.API.Modules.Scheduling.Models;
using Atendefy.Tests.Integration;
using FluentAssertions;

namespace Atendefy.Tests.Scheduling;

public class SchedulingServiceTests
{
    private static SchedulingService Create() => new(new InMemoryTenantDbContextFactory());
    private static string NewSchema() => "tenant_" + Guid.NewGuid().ToString("N");

    [Fact]
    public async Task Upsert_Then_Get_ReturnsConfig()
    {
        var sut = Create();
        var schema = NewSchema();

        var result = await sut.UpsertAsync(schema, new CalendarConfigRequest(
            "https://cal.com/empresa/consulta", true, "só consultas", "calcom"));

        result.IsSuccess.Should().BeTrue();
        var cfg = await sut.GetAsync(schema);
        cfg!.Enabled.Should().BeTrue();
        cfg.BookingUrl.Should().Be("https://cal.com/empresa/consulta");
        cfg.Provider.Should().Be("calcom");
    }

    [Fact]
    public async Task Upsert_EnabledWithoutUrl_Fails()
    {
        var result = await Create().UpsertAsync(NewSchema(),
            new CalendarConfigRequest(null, true, null, "calcom"));
        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task Upsert_InvalidUrl_Fails()
    {
        var result = await Create().UpsertAsync(NewSchema(),
            new CalendarConfigRequest("notaurl", true, null, "calcom"));
        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task Upsert_InvalidProvider_Fails()
    {
        var result = await Create().UpsertAsync(NewSchema(),
            new CalendarConfigRequest("https://cal.com/x", false, null, "banana"));
        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task Upsert_Twice_UpdatesSameRow()
    {
        var sut = Create();
        var schema = NewSchema();

        await sut.UpsertAsync(schema, new CalendarConfigRequest("https://cal.com/a", true, null, "calcom"));
        await sut.UpsertAsync(schema, new CalendarConfigRequest("https://cal.com/b", false, null, "calendly"));

        var cfg = await sut.GetAsync(schema);
        cfg!.BookingUrl.Should().Be("https://cal.com/b");
        cfg.Enabled.Should().BeFalse();
        cfg.Provider.Should().Be("calendly");
    }

    [Fact]
    public async Task Upsert_GeneratesWebhookToken_OnEnable_AndKeepsItStable()
    {
        var sut = Create();
        var schema = NewSchema();

        await sut.UpsertAsync(schema, new CalendarConfigRequest("https://cal.com/a", true, null, "calcom"));
        var first = (await sut.GetAsync(schema))!.WebhookToken;
        first.Should().NotBeNullOrEmpty();

        // Salvar de novo não troca o token.
        await sut.UpsertAsync(schema, new CalendarConfigRequest("https://cal.com/a", true, "x", "calcom"));
        (await sut.GetAsync(schema))!.WebhookToken.Should().Be(first);
    }

    [Fact]
    public async Task UpsertAppointment_IsIdempotentByExternalId()
    {
        var sut = Create();
        var schema = NewSchema();

        await sut.UpsertAppointmentAsync(schema, new Appointment
        {
            ExternalId = "uid1", Title = "Consulta", Status = "confirmed",
            AttendeeName = "João"
        });
        await sut.UpsertAppointmentAsync(schema, new Appointment
        {
            ExternalId = "uid1", Title = "Consulta", Status = "cancelled"
        });

        var list = await sut.ListAppointmentsAsync(schema);
        list.Should().HaveCount(1);
        list[0].Status.Should().Be("cancelled");
        list[0].AttendeeName.Should().Be("João");  // preservado (incoming null não sobrescreve)
    }
}
