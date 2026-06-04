namespace Atendefy.API.Modules.Auth.Models;

public record LoginRequest(string Email, string Password);
public record RefreshRequest(string RefreshToken);
