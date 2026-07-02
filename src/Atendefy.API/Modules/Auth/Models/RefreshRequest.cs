namespace Atendefy.API.Modules.Auth.Models;

// RefreshToken no body é fallback para clients de API — o SPA usa o cookie HttpOnly.
public record RefreshRequest(string? RefreshToken);
