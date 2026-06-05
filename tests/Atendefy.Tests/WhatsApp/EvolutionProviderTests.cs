using Atendefy.API.Modules.WhatsApp;
using Atendefy.API.Modules.WhatsApp.Models;
using Atendefy.Tests.Helpers;
using FluentAssertions;

namespace Atendefy.Tests.WhatsApp;

public class EvolutionProviderTests
{
    [Fact]
    public async Task SendMessageAsync_ShouldPostToEvolutionApi()
    {
        var handler = MockHttpMessageHandler.ReturnsJson("""{"key":{"id":"msg_001"}}""");
        var httpClient = new HttpClient(handler);
        var config = new EvolutionConfig("http://evolution:8080", "my-instance", "apikey_xyz");
        var provider = new EvolutionProvider(httpClient, config);

        await provider.SendMessageAsync(new OutboundMessage("5511988888888", "Oi!"));

        handler.Requests.Should().HaveCount(1);
        var req = handler.Requests[0];
        req.Method.Should().Be(HttpMethod.Post);
        req.RequestUri!.ToString().Should().Contain("my-instance");
        req.Headers.GetValues("apikey").First().Should().Be("apikey_xyz");
        var body = await req.Content!.ReadAsStringAsync();
        body.Should().Contain("5511988888888");
        body.Should().Contain("Oi");
    }
}
