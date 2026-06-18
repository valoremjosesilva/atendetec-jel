using Atendefy.API.Modules.Auth;
using FluentAssertions;

namespace Atendefy.Tests.Auth;

public class JwtServiceTests
{
    private readonly JwtService _sut =
        new("test_secret_key_minimum_32_chars_!!!", "Atendefy", "atendefy.com.br");

    [Fact]
    public void GenerateAccessToken_ShouldReturnNonEmptyToken()
    {
        var token = _sut.GenerateAccessToken(Guid.NewGuid(), Guid.NewGuid(), "Owner", "test@test.com");
        token.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void GenerateRefreshToken_ShouldRoundTripUserAndTenant()
    {
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        var token = _sut.GenerateRefreshToken(userId, tenantId, "Owner", "owner@test.com");
        var principal = _sut.ValidateRefreshToken(token);

        principal.Should().NotBeNull();
        principal!.UserId.Should().Be(userId);
        principal.TenantId.Should().Be(tenantId);
    }

    [Fact]
    public void ValidateRefreshToken_WithAccessToken_ShouldReturnNull()
    {
        // An access token has a different audience and must not be accepted as a refresh token.
        var accessToken = _sut.GenerateAccessToken(Guid.NewGuid(), Guid.NewGuid(), "Owner", "owner@test.com");
        _sut.ValidateRefreshToken(accessToken).Should().BeNull();
    }

    [Fact]
    public void ValidateToken_WithRefreshToken_ShouldReturnNull()
    {
        // Conversely, a refresh token must not pass access-token validation.
        var refreshToken = _sut.GenerateRefreshToken(Guid.NewGuid(), Guid.NewGuid(), "Owner", "owner@test.com");
        _sut.ValidateToken(refreshToken).Should().BeNull();
    }

    [Fact]
    public void ValidateToken_WithValidToken_ShouldReturnPrincipalWithClaims()
    {
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var token = _sut.GenerateAccessToken(userId, tenantId, "Admin", "admin@test.com");

        var principal = _sut.ValidateToken(token);

        principal.Should().NotBeNull();
        principal!.FindFirst("sub")!.Value.Should().Be(userId.ToString());
        principal!.FindFirst("tenant_id")!.Value.Should().Be(tenantId.ToString());
    }

    [Fact]
    public void ValidateToken_WithInvalidToken_ShouldReturnNull()
    {
        var principal = _sut.ValidateToken("not.a.valid.token");
        principal.Should().BeNull();
    }

    [Fact]
    public void ValidateToken_WithExpiredToken_ShouldReturnNull()
    {
        var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler { MapInboundClaims = false };
        var key = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(
            System.Text.Encoding.UTF8.GetBytes("test_secret_key_minimum_32_chars_!!!"));
        var token = handler.WriteToken(new System.IdentityModel.Tokens.Jwt.JwtSecurityToken(
            issuer: "Atendefy",
            audience: "atendefy.com.br",
            expires: DateTime.UtcNow.AddMinutes(-1),
            signingCredentials: new Microsoft.IdentityModel.Tokens.SigningCredentials(
                key, Microsoft.IdentityModel.Tokens.SecurityAlgorithms.HmacSha256)));

        var principal = _sut.ValidateToken(token);

        principal.Should().BeNull();
    }
}
