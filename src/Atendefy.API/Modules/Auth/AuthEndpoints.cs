using Atendefy.API.Modules.Auth.Models;
using Microsoft.AspNetCore.Mvc;

namespace Atendefy.API.Modules.Auth;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/auth").WithTags("Auth");

        group.MapPost("/login", async (
            [FromBody] LoginRequest request,
            AuthService authService,
            HttpContext ctx) =>
        {
            // TenantId é o subdomínio resolvido pelo TenantResolver middleware
            var tenantIdentifier = ctx.Items["TenantId"]?.ToString();
            if (string.IsNullOrEmpty(tenantIdentifier))
                return Results.Unauthorized();

            var result = await authService.LoginAsync(request, tenantIdentifier);
            return result.IsSuccess
                ? Results.Ok(result.Value)
                : Results.Unauthorized();
        });

        return app;
    }
}
