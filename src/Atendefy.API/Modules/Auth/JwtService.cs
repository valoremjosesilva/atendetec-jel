using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace Atendefy.API.Modules.Auth;

public class JwtService(string secret, string issuer, string audience)
{
    private readonly SymmetricSecurityKey _key = new(Encoding.UTF8.GetBytes(secret));
    // Refresh tokens carry a distinct audience so they can never be used as access
    // tokens (the JWT bearer middleware validates ValidAudience = the access audience).
    private readonly string _refreshAudience = $"{audience}:refresh";

    public string GenerateAccessToken(Guid userId, Guid tenantId, string role, string email)
    {
        var claims = new[]
        {
            new Claim("sub", userId.ToString()),
            new Claim("tenant_id", tenantId.ToString()),
            new Claim("role", role),
            new Claim("email", email),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(15),
            signingCredentials: new SigningCredentials(_key, SecurityAlgorithms.HmacSha256)
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public string GenerateRefreshToken(Guid userId, Guid tenantId, string role, string email)
    {
        var claims = new[]
        {
            new Claim("sub", userId.ToString()),
            new Claim("tenant_id", tenantId.ToString()),
            new Claim("role", role),
            new Claim("email", email),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: _refreshAudience,
            claims: claims,
            expires: DateTime.UtcNow.AddDays(7),
            signingCredentials: new SigningCredentials(_key, SecurityAlgorithms.HmacSha256)
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public RefreshPrincipal? ValidateRefreshToken(string token)
    {
        var parameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = _key,
            ValidateIssuer = true,
            ValidIssuer = issuer,
            ValidateAudience = true,
            ValidAudience = _refreshAudience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero,
        };

        try
        {
            var principal = new JwtSecurityTokenHandler { MapInboundClaims = false }
                .ValidateToken(token, parameters, out _);

            return new RefreshPrincipal(
                Guid.Parse(principal.FindFirst("sub")!.Value),
                Guid.Parse(principal.FindFirst("tenant_id")!.Value),
                principal.FindFirst("role")?.Value ?? string.Empty,
                principal.FindFirst("email")?.Value ?? string.Empty);
        }
        catch
        {
            return null;
        }
    }

    public ClaimsPrincipal? ValidateToken(string token)
    {
        var parameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = _key,
            ValidateIssuer = true,
            ValidIssuer = issuer,
            ValidateAudience = true,
            ValidAudience = audience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero,
        };

        try
        {
            return new JwtSecurityTokenHandler { MapInboundClaims = false }
                .ValidateToken(token, parameters, out _);
        }
        catch
        {
            return null;
        }
    }
}

public record RefreshPrincipal(Guid UserId, Guid TenantId, string Role, string Email);
