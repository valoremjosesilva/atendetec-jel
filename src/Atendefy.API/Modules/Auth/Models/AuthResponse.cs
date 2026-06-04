namespace Atendefy.API.Modules.Auth.Models;

public record AuthResponse(
    string AccessToken,
    string RefreshToken,
    DateTime ExpiresAt,
    string TenantId,
    string UserId,
    string Role
);
