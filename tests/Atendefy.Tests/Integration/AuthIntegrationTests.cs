using FluentAssertions;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Atendefy.Tests.Integration;

[Collection("Integration")]
public class AuthIntegrationTests(ApiFactory factory)
{
    [Fact]
    public async Task Login_WithValidCredentials_Returns200WithSessionAndCookies()
    {
        var client = factory.CreateTenantClient();

        var response = await client.PostAsJsonAsync("/auth/login", new
        {
            email = ApiFactory.UserEmail,
            password = ApiFactory.UserPassword
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("role").GetString().Should().Be("Owner");
        body.GetProperty("tenantId").GetString().Should().NotBeNullOrEmpty();

        // Tokens não transitam no body — vão em cookies HttpOnly
        body.TryGetProperty("accessToken", out _).Should().BeFalse();
        body.TryGetProperty("refreshToken", out _).Should().BeFalse();

        var cookies = response.Headers.GetValues("Set-Cookie").ToList();
        cookies.Should().Contain(c =>
            c.StartsWith("atendefy_access=") &&
            c.Contains("httponly", StringComparison.OrdinalIgnoreCase) &&
            c.Contains("samesite=strict", StringComparison.OrdinalIgnoreCase));
        cookies.Should().Contain(c => c.StartsWith("atendefy_refresh="));
    }

    [Fact]
    public async Task Login_WithWrongPassword_Returns401()
    {
        var client = factory.CreateTenantClient();

        var response = await client.PostAsJsonAsync("/auth/login", new
        {
            email = ApiFactory.UserEmail,
            password = "wrong-password"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("error").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Login_WithUnknownTenant_Returns401()
    {
        var client = factory.CreateTenantClient("unknown-tenant");

        var response = await client.PostAsJsonAsync("/auth/login", new
        {
            email = ApiFactory.UserEmail,
            password = ApiFactory.UserPassword
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_WithoutTenantSubdomain_Returns401()
    {
        // Default client uses localhost — TenantResolver returns null
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/auth/login", new
        {
            email = ApiFactory.UserEmail,
            password = ApiFactory.UserPassword
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
