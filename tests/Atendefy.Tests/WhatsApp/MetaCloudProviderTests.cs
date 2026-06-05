using Atendefy.API.Modules.WhatsApp;
using Atendefy.API.Modules.WhatsApp.Models;
using Atendefy.Tests.Helpers;
using FluentAssertions;

namespace Atendefy.Tests.WhatsApp;

public class MetaCloudProviderTests
{
    [Fact]
    public async Task SendMessageAsync_ShouldPostToMetaGraphApi()
    {
        var handler = MockHttpMessageHandler.ReturnsJson("""{"messages":[{"id":"wamid.test"}]}""");
        var httpClient = new HttpClient(handler);
        var config = new MetaConfig("123456", "token_abc");
        var provider = new MetaCloudProvider(httpClient, config);

        await provider.SendMessageAsync(new OutboundMessage("5511999999999", "Olá!"));

        handler.Requests.Should().HaveCount(1);
        var req = handler.Requests[0];
        req.Method.Should().Be(HttpMethod.Post);
        req.RequestUri!.ToString().Should().Contain("123456/messages");
        req.Headers.Authorization!.Parameter.Should().Be("token_abc");
        var body = await req.Content!.ReadAsStringAsync();
        body.Should().Contain("5511999999999");
        body.Should().Contain("Ol");
    }
}
