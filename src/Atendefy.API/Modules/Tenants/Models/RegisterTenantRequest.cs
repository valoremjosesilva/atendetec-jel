namespace Atendefy.API.Modules.Tenants.Models;

public record RegisterTenantRequest(
    string CompanyName,
    string Subdomain,
    string OwnerName,
    string OwnerEmail,
    string OwnerPassword,
    string? CaptchaToken = null
);

public record VerifyEmailRequest(string Token);

public record ResendVerificationRequest(string Subdomain, string Email);
