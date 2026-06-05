using Atendefy.API.Modules.AI;
using Atendefy.API.Modules.AI.Models;
using Atendefy.Tests.Helpers;
using FluentAssertions;

namespace Atendefy.Tests.AI;

public class AnthropicProviderTests
{
    [Fact]
    public async Task CompleteAsync_ShouldCallAnthropicAndReturnContent()
    {
        var responseJson = """
            {
                "content": [{"text": "Ola! Como posso ajudar?"}],
                "usage": {"output_tokens": 8}
            }
            """;
        var handler = MockHttpMessageHandler.ReturnsJson(responseJson);
        var provider = new AnthropicProvider(new HttpClient(handler), "sk-ant-test");

        var result = await provider.CompleteAsync(new AICompletionRequest(
            SystemPrompt: "Voce e assistente.",
            Messages: [new ChatMessage("user", "Oi!")],
            Model: "claude-haiku-4-5-20251001"
        ));

        result.Content.Should().Be("Ola! Como posso ajudar?");
        result.TokensUsed.Should().Be(8);
        handler.Requests[0].RequestUri!.ToString().Should().Contain("messages");
        handler.Requests[0].Headers.GetValues("x-api-key").First().Should().Be("sk-ant-test");
    }
}
