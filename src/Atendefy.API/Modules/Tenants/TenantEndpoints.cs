using Atendefy.API.Modules.Tenants.Models;
using Microsoft.AspNetCore.Mvc;
using System.Security.Cryptography;
using System.Text;

namespace Atendefy.API.Modules.Tenants;

public static class TenantEndpoints
{
    public static IEndpointRouteBuilder MapTenantEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/tenants").WithTags("Tenants").AllowAnonymous();

        group.MapPost("/register", async (
            [FromBody] RegisterTenantRequest request,
            TenantService tenantService) =>
        {
            var result = await tenantService.RegisterAsync(request);
            return result.IsSuccess
                ? Results.Created($"/tenants/{result.Value!.Id}",
                    new { result.Value.Id, result.Value.Subdomain, result.Value.Name })
                : Results.BadRequest(new { error = result.Error });
        });

        // Admin: listar empresas pendentes de aprovação. Protegido por X-Admin-Key.
        group.MapGet("/pending", async (
            HttpContext ctx, TenantService tenantService, IConfiguration config) =>
        {
            if (!IsAdmin(ctx, config)) return Results.StatusCode(StatusCodes.Status403Forbidden);
            return Results.Ok(await tenantService.ListPendingAsync());
        });

        // Admin: aprovar (ativar) uma empresa. Protegido por X-Admin-Key.
        group.MapPost("/{subdomain}/activate", async (
            string subdomain, HttpContext ctx, TenantService tenantService, IConfiguration config) =>
        {
            if (!IsAdmin(ctx, config)) return Results.StatusCode(StatusCodes.Status403Forbidden);
            var result = await tenantService.ActivateAsync(subdomain);
            return result.IsSuccess
                ? Results.Ok(new { activated = subdomain })
                : Results.BadRequest(new { error = result.Error });
        });

        return app;
    }

    // Compara o header X-Admin-Key com Admin:Key (config). Sem chave configurada => nega tudo.
    private static bool IsAdmin(HttpContext ctx, IConfiguration config)
    {
        var expected = config["Admin:Key"];
        if (string.IsNullOrEmpty(expected)) return false;

        var provided = ctx.Request.Headers["X-Admin-Key"].ToString();
        if (string.IsNullOrEmpty(provided)) return false;

        var a = Encoding.UTF8.GetBytes(provided);
        var b = Encoding.UTF8.GetBytes(expected);
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }
}
