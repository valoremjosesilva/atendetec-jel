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
            .FirstOrDefaultAsync(t => t.Subdomain == tenantIdentifier);

        if (tenant is null)
            return Result<AuthResponse>.Fail("Tenant não encontrado");

        if (tenant.Status != TenantStatus.Active)
            return Result<AuthResponse>.Fail(tenant.Status == TenantStatus.Pending
                ? "Sua empresa está em análise e ainda não foi liberada."
                : "Acesso suspenso. Entre em contato com o suporte.");

        var user = await db.TenantUsers
            .FirstOrDefaultAsync(u => u.TenantId == tenant.Id
                                   && u.Email == request.Email.ToLowerInvariant());

        if (user is null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            return Result<AuthResponse>.Fail("Email ou senha inválidos");

        return Result<AuthResponse>.Ok(BuildAuthResponse(tenant.Id, user.Id, user.Role, user.Email));
    }

    // Troca um refresh token válido por um novo par de tokens (access + refresh).
    // O refresh token é autocontido (carrega tenant/usuário), então não precisa de X-Tenant-Key.
    public async Task<Result<AuthResponse>> RefreshAsync(string refreshToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
            return Result<AuthResponse>.Fail("Refresh token é obrigatório");

        var principal = jwtService.ValidateRefreshToken(refreshToken);
        if (principal is null)
            return Result<AuthResponse>.Fail("Sessão expirada. Faça login novamente.");

        var tenant = await db.Tenants
            .FirstOrDefaultAsync(t => t.Id == principal.TenantId && t.Status == TenantStatus.Active);
        if (tenant is null)
            return Result<AuthResponse>.Fail("Tenant não encontrado");

        var user = await db.TenantUsers
            .FirstOrDefaultAsync(u => u.Id == principal.UserId && u.TenantId == tenant.Id);
        if (user is null)
            return Result<AuthResponse>.Fail("Usuário não encontrado");

        return Result<AuthResponse>.Ok(BuildAuthResponse(tenant.Id, user.Id, user.Role, user.Email));
    }

    private AuthResponse BuildAuthResponse(Guid tenantId, Guid userId, string role, string email) =>
        new(
            AccessToken: jwtService.GenerateAccessToken(userId, tenantId, role, email),
            RefreshToken: jwtService.GenerateRefreshToken(userId, tenantId, role, email),
            ExpiresAt: DateTime.UtcNow.AddMinutes(15),
            TenantId: tenantId.ToString(),
            UserId: userId.ToString(),
            Role: role
        );
}
