using Atendefy.API.Modules.Scheduling;
using FluentAssertions;
using System.Text.Json;

namespace Atendefy.Tests.Scheduling;

public class CalcomPayloadParserTests
{
    private static JsonElement Json(string s) => JsonDocument.Parse(s).RootElement;

    [Fact]
    public void Parse_BookingCreated_ExtractsFields()
    {
        var appt = CalcomPayloadParser.Parse(Json("""
        {
          "triggerEvent": "BOOKING_CREATED",
          "payload": {
            "uid": "abc123",
            "title": "Consulta 30 min",
            "startTime": "2026-06-25T14:00:00Z",
            "endTime": "2026-06-25T14:30:00Z",
            "attendees": [{ "name": "João", "email": "joao@x.com", "phoneNumber": "+5547999077813" }]
          }
        }
        """));

        appt.Should().NotBeNull();
        appt!.ExternalId.Should().Be("abc123");
        appt.Title.Should().Be("Consulta 30 min");
        appt.AttendeeName.Should().Be("João");
        appt.AttendeePhone.Should().Be("+5547999077813");
        appt.Status.Should().Be("confirmed");
        appt.StartTime.Should().NotBeNull();
    }

    [Fact]
    public void Parse_Cancelled_SetsStatus()
    {
        var appt = CalcomPayloadParser.Parse(Json("""
        { "triggerEvent": "BOOKING_CANCELLED", "payload": { "uid": "x" } }
        """));
        appt!.Status.Should().Be("cancelled");
    }

    [Fact]
    public void Parse_PhoneFromResponses_WhenNotInAttendee()
    {
        var appt = CalcomPayloadParser.Parse(Json("""
        {
          "triggerEvent": "BOOKING_CREATED",
          "payload": { "uid": "y", "responses": { "phone": { "value": "+551122223333" } } }
        }
        """));
        appt!.AttendeePhone.Should().Be("+551122223333");
    }

    [Fact]
    public void Parse_MissingPayloadOrUid_ReturnsNull()
    {
        CalcomPayloadParser.Parse(Json("""{ "triggerEvent": "X" }""")).Should().BeNull();
        CalcomPayloadParser.Parse(Json("""{ "payload": { "title": "no uid" } }""")).Should().BeNull();
    }
}
