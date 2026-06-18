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
                return Results.Json(new { error = "Tenant não identificado" }, statusCode: 401);

            var result = await authService.LoginAsync(request, tenantIdentifier);
            return result.IsSuccess
                ? Results.Ok(result.Value)
                : Results.Json(new { error = result.Error }, statusCode: 401);
        });

        group.MapPost("/refresh", async (
            [FromBody] RefreshRequest request,
            AuthService authService) =>
        {
            var result = await authService.RefreshAsync(request.RefreshToken);
            return result.IsSuccess
                ? Results.Ok(result.Value)
                : Results.Json(new { error = result.Error }, statusCode: 401);
        });

        return app;
    }
}
