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
            IHostEnvironment env,
            HttpContext ctx) =>
        {
            // TenantId é o subdomínio resolvido pelo TenantResolver middleware
            var tenantIdentifier = ctx.Items["TenantId"]?.ToString();
            if (string.IsNullOrEmpty(tenantIdentifier))
                return Results.Json(new { error = "Tenant não identificado" }, statusCode: 401);

            var result = await authService.LoginAsync(request, tenantIdentifier);
            if (!result.IsSuccess)
                return Results.Json(new { error = result.Error }, statusCode: 401);

            var auth = result.Value!;
            AuthCookies.Set(ctx, auth.AccessToken, auth.RefreshToken, secure: env.IsProduction());
            return Results.Ok(new SessionResponse(auth.ExpiresAt, auth.TenantId, auth.UserId, auth.Role));
        });

        group.MapPost("/refresh", async (
            [FromBody] RefreshRequest? request,
            AuthService authService,
            IHostEnvironment env,
            HttpContext ctx) =>
        {
            // Cookie HttpOnly primeiro (SPA); body como fallback (clients de API).
            var refreshToken = ctx.Request.Cookies[AuthCookies.Refresh] ?? request?.RefreshToken;

            var result = await authService.RefreshAsync(refreshToken ?? string.Empty);
            if (!result.IsSuccess)
            {
                AuthCookies.Clear(ctx, secure: env.IsProduction());
                return Results.Json(new { error = result.Error }, statusCode: 401);
            }

            var auth = result.Value!;
            AuthCookies.Set(ctx, auth.AccessToken, auth.RefreshToken, secure: env.IsProduction());
            return Results.Ok(new SessionResponse(auth.ExpiresAt, auth.TenantId, auth.UserId, auth.Role));
        });

        group.MapPost("/logout", (IHostEnvironment env, HttpContext ctx) =>
        {
            AuthCookies.Clear(ctx, secure: env.IsProduction());
            return Results.NoContent();
        });

        return app;
    }
}
