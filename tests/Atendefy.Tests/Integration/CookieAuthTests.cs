using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using System.Net;
using System.Net.Http.Json;

namespace Atendefy.Tests.Integration;

// Fluxo de autenticação do SPA: tokens em cookies HttpOnly (ver AuthCookies).
// O header Authorization continua funcionando (usado pelos demais testes).
[Collection("Integration")]
public class CookieAuthTests(ApiFactory factory)
{
    // HandleCookies=false: controle manual dos cookies — sem isso o HttpClient
    // guarda e reenvia cookies sozinho, mascarando o que cada teste verifica.
    private HttpClient CreateClient() =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri($"http://{ApiFactory.Subdomain}.{ApiFactory.BaseDomain}"),
            HandleCookies = false,
        });

    private static async Task<HttpResponseMessage> LoginAsync(HttpClient client) =>
        await client.PostAsJsonAsync("/auth/login", new
        {
            email = ApiFactory.UserEmail,
            password = ApiFactory.UserPassword
        });

    // Extrai "nome=valor" de um Set-Cookie da response.
    private static string ExtractCookie(HttpResponseMessage response, string name) =>
        response.Headers.GetValues("Set-Cookie")
            .First(c => c.StartsWith($"{name}="))
            .Split(';')[0];

    [Fact]
    public async Task AccessCookie_GrantsAccessToProtectedEndpoint()
    {
        var client = CreateClient();
        var login = await LoginAsync(client);
        login.StatusCode.Should().Be(HttpStatusCode.OK);

        var accessCookie = ExtractCookie(login, "atendefy_access");

        var request = new HttpRequestMessage(HttpMethod.Get, "/conversations?page=1&pageSize=10");
        request.Headers.Add("Cookie", accessCookie);

        var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Refresh_WithCookie_RotatesTokens()
    {
        var client = CreateClient();
        var login = await LoginAsync(client);
        var refreshCookie = ExtractCookie(login, "atendefy_refresh");

        var request = new HttpRequestMessage(HttpMethod.Post, "/auth/refresh");
        request.Headers.Add("Cookie", refreshCookie);

        var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var cookies = response.Headers.GetValues("Set-Cookie").ToList();
        cookies.Should().Contain(c => c.StartsWith("atendefy_access="));
        cookies.Should().Contain(c => c.StartsWith("atendefy_refresh="));
    }

    [Fact]
    public async Task Refresh_WithoutCookieOrBody_Returns401()
    {
        var client = CreateClient();

        var response = await client.PostAsync("/auth/refresh", null);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Refresh_WithTokenInBody_StillWorks()
    {
        // Fallback para clients de API que não usam cookies
        var client = CreateClient();
        var login = await LoginAsync(client);
        var refreshToken = ExtractCookie(login, "atendefy_refresh").Split('=', 2)[1];

        var response = await client.PostAsJsonAsync("/auth/refresh", new { refreshToken });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Logout_ExpiresCookies()
    {
        var client = CreateClient();

        var response = await client.PostAsync("/auth/logout", null);
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Cookies expirados: valor vazio + expires no passado
        var cookies = response.Headers.GetValues("Set-Cookie").ToList();
        cookies.Should().Contain(c =>
            c.StartsWith("atendefy_access=;") && c.Contains("expires=", StringComparison.OrdinalIgnoreCase));
        cookies.Should().Contain(c =>
            c.StartsWith("atendefy_refresh=;") && c.Contains("expires=", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SseStream_WithQueryStringToken_NoLongerAuthenticates()
    {
        // O token em query string vazava em logs — foi removido em favor do cookie
        var client = CreateClient();
        var token = factory.MintToken();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var response = await client.GetAsync(
            $"/conversations/stream?token={Uri.EscapeDataString(token)}",
            HttpCompletionOption.ResponseHeadersRead, cts.Token);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
