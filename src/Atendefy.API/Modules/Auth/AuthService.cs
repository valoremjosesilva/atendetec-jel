using Atendefy.API.Infrastructure.Database;
using Atendefy.API.Modules.Auth.Models;
using Atendefy.API.SharedKernel;
using Microsoft.EntityFrameworkCore;
using static Atendefy.API.SharedKernel.AppConstants;

namespace Atendefy.API.Modules.Auth;

public class AuthService(PublicDbContext db, JwtService jwtService)
{
    // tenantIdentifier = subdomínio (ex: "clinica-abc") resolvido pelo TenantResolver
    public async Task<Result<AuthResponse>> LoginAsync(LoginRequest request, string tenantIdentifier)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
            return Result<AuthResponse>.Fail("Email e senha são obrigatórios");
        if (request.Password.Length > 72)
            return Result<AuthResponse>.Fail("Senha inválida");

        var tenant = await db.Tenants
            .FirstOrDefaultAsync(t => t.Subdomain == tenantIdentifier
                                   && t.Status == TenantStatus.Active);

        if (tenant is null)
            return Result<AuthResponse>.Fail("Tenant não encontrado");

        var user = await db.TenantUsers
            .FirstOrDefaultAsync(u => u.TenantId == tenant.Id
                                   && u.Email == request.Email.ToLowerInvariant());

        if (user is null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            return Result<AuthResponse>.Fail("Email ou senha inválidos");

        var accessToken = jwtService.GenerateAccessToken(user.Id, tenant.Id, user.Role, user.Email);
        var refreshToken = jwtService.GenerateRefreshToken();

        return Result<AuthResponse>.Ok(new AuthResponse(
            AccessToken: accessToken,
            RefreshToken: refreshToken,
            ExpiresAt: DateTime.UtcNow.AddMinutes(15),
            TenantId: tenant.Id.ToString(),
            UserId: user.Id.ToString(),
            Role: user.Role
        ));
    }
}
