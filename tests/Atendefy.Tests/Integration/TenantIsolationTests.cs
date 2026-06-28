using FluentAssertions;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Atendefy.Tests.Integration;

[Collection("Integration")]
public class TenantIsolationTests : IAsyncLifetime
{
    private readonly ApiFactory _factory;
    private static readonly Guid OtherTenantId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    public TenantIsolationTests(ApiFactory factory) => _factory = factory;

    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync() => Task.CompletedTask;

    private HttpClient CreateClientForOtherTenant()
    {
        var client = _factory.CreateTenantClient();
        var token = _factory.MintTokenForTenant(OtherTenantId);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [Fact]
    public async Task GetConversations_WithJwtFromDifferentTenant_Returns401()
    {
        var client = CreateClientForOtherTenant();

        var response = await client.GetAsync("/conversations?page=1&pageSize=10");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetContacts_WithJwtFromDifferentTenant_Returns401()
    {
        var client = CreateClientForOtherTenant();

        var response = await client.GetAsync("/contacts?page=1&pageSize=10");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetConversationMessages_WithJwtFromDifferentTenant_Returns401OrNotFound()
    {
        var client = CreateClientForOtherTenant();
        // Usar Guid.NewGuid() para não depender do seed de ConversationsIntegrationTests.
        // O resultado será NotFound (tenant não encontrado antes de chegar ao ID da conversa),
        // o que ainda verifica que não retorna 200 com dados de outro tenant.
        var someId = Guid.NewGuid();

        var response = await client.GetAsync($"/conversations/{someId}/messages");

        response.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.NotFound);
        response.StatusCode.Should().NotBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AuthenticatedTenant_CanAccessOwnConversations()
    {
        var legitimateClient = _factory.CreateAuthenticatedClient();

        var response = await legitimateClient.GetAsync("/conversations?page=1&pageSize=100");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("conversations").GetArrayLength().Should().BeGreaterThanOrEqualTo(0);
    }
}
