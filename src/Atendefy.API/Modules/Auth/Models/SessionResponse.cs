namespace Atendefy.API.Modules.Auth.Models;

// Resposta de login/refresh para o SPA: os tokens vão em cookies HttpOnly
// (ver AuthCookies), nunca no body.
public record SessionResponse(
    DateTime ExpiresAt,
    string TenantId,
    string UserId,
    string Role
);
