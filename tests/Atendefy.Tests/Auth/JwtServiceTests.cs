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
        var token = _sut.GenerateAccessToken(Guid.NewGuid(), Guid.NewGuid(), "Owner");
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
        var token = _sut.GenerateAccessToken(userId, tenantId, "Admin");

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
}
