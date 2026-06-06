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
    public void GenerateRefreshToken_ShouldReturn64ByteBase64String()
    {
        var token = _sut.GenerateRefreshToken();
        var bytes = Convert.FromBase64String(token);
        bytes.Length.Should().Be(64);
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
