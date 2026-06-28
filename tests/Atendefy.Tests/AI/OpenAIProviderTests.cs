using Atendefy.API.Modules.AI;
using Atendefy.API.Modules.AI.Models;
using Atendefy.Tests.Helpers;
using FluentAssertions;

namespace Atendefy.Tests.AI;

public class OpenAIProviderTests
{
    [Fact]
    public async Task CompleteAsync_ShouldCallOpenAIAndReturnContent()
    {
        var responseJson = """
            {
                "choices": [{"message": {"content": "Atendemos das 8h as 18h."}}],
                "usage": {"completion_tokens": 12}
            }
            """;
        var handler = MockHttpMessageHandler.ReturnsJson(responseJson);
        var provider = new OpenAIProvider(new HttpClient(handler), "sk-test");

        var result = await provider.CompleteAsync(new AICompletionRequest(
            SystemPrompt: "Voce e um assistente.",
            Messages: [new ChatMessage("user", "Qual o horario?")],
            Model: "gpt-4o-mini"
        ));

        result.Content.Should().Be("Atendemos das 8h as 18h.");
        result.TokensUsed.Should().Be(12);
        handler.Requests.Should().HaveCount(1);
        handler.Requests[0].RequestUri!.ToString().Should().Contain("chat/completions");
    }

    [Fact]
    public async Task CompleteAsync_ShouldIncludeSystemPromptAsFirstMessage()
    {
        var responseJson = """{"choices":[{"message":{"content":"ok"}}],"usage":{"completion_tokens":1}}""";
        var handler = MockHttpMessageHandler.ReturnsJson(responseJson);
        var provider = new OpenAIProvider(new HttpClient(handler), "sk-test");

        await provider.CompleteAsync(new AICompletionRequest(
            "Prompt do sistema.", [new ChatMessage("user", "oi")], "gpt-4o-mini"));

        var body = await handler.Requests[0].Content!.ReadAsStringAsync();
        body.Should().Contain("system");
        body.Should().Contain("Prompt do sistema.");
    }

    [Fact]
    public async Task CompleteAsync_WhenChoicesArrayIsEmpty_ReturnsEmptyContent()
    {
        var responseJson = """{"choices":[],"usage":{"completion_tokens":0}}""";
        var handler = MockHttpMessageHandler.ReturnsJson(responseJson);
        var provider = new OpenAIProvider(new HttpClient(handler), "sk-test");

        var result = await provider.CompleteAsync(new AICompletionRequest(
            SystemPrompt: "Prompt.", Messages: [new ChatMessage("user", "oi")], Model: "gpt-4o-mini"));

        result.Content.Should().BeEmpty();
        result.TokensUsed.Should().Be(0);
    }

    [Fact]
    public async Task CompleteAsync_WhenResponseIsMissingExpectedFields_ReturnsEmptyContent()
    {
        var responseJson = """{"error":{"message":"Rate limit exceeded"}}""";
        var handler = MockHttpMessageHandler.ReturnsJson(responseJson);
        var provider = new OpenAIProvider(new HttpClient(handler), "sk-test");

        var result = await provider.CompleteAsync(new AICompletionRequest(
            SystemPrompt: "Prompt.", Messages: [new ChatMessage("user", "oi")], Model: "gpt-4o-mini"));

        result.Content.Should().BeEmpty();
        result.TokensUsed.Should().Be(0);
    }
}
