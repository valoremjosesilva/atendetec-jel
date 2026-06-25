using Microsoft.AspNetCore.Mvc;

namespace Atendefy.API.Modules.Tenants;

/// <summary>
/// Endpoints de superadmin (/admin). Protegidos pelo header X-Admin-Key (ver AdminAuth).
/// CRUD de planos + listar empresas + atribuir plano por empresa.
/// </summary>
public static class AdminEndpoints
{
    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/admin").WithTags("Admin").AllowAnonymous();

        // ---- Planos ----
        group.MapGet("/plans", async (HttpContext ctx, AdminService svc, IConfiguration config) =>
        {
            if (!AdminAuth.IsAdmin(ctx, config)) return Forbidden();
            return Results.Ok(await svc.ListPlansAsync());
        });

        group.MapPost("/plans", async (
            [FromBody] PlanRequest req, HttpContext ctx, AdminService svc, IConfiguration config) =>
        {
            if (!AdminAuth.IsAdmin(ctx, config)) return Forbidden();
            var result = await svc.CreatePlanAsync(req);
            return result.IsSuccess
                ? Results.Created($"/admin/plans/{result.Value!.Id}", result.Value)
                : Results.BadRequest(new { error = result.Error });
        });

        group.MapPut("/plans/{id:guid}", async (
            Guid id, [FromBody] PlanRequest req, HttpContext ctx, AdminService svc, IConfiguration config) =>
        {
            if (!AdminAuth.IsAdmin(ctx, config)) return Forbidden();
            var result = await svc.UpdatePlanAsync(id, req);
            return result.IsSuccess
                ? Results.Ok(result.Value)
                : Results.BadRequest(new { error = result.Error });
        });

        // ---- Empresas ----
        group.MapGet("/tenants", async (HttpContext ctx, AdminService svc, IConfiguration config) =>
        {
            if (!AdminAuth.IsAdmin(ctx, config)) return Forbidden();
            return Results.Ok(await svc.ListTenantsAsync());
        });

        group.MapPost("/tenants/{subdomain}/plan", async (
            string subdomain, [FromBody] AssignPlanRequest req,
            HttpContext ctx, AdminService svc, IConfiguration config) =>
        {
            if (!AdminAuth.IsAdmin(ctx, config)) return Forbidden();
            var result = await svc.AssignPlanAsync(subdomain, req.PlanId);
            return result.IsSuccess
                ? Results.Ok(new { assigned = subdomain, planId = req.PlanId })
                : Results.BadRequest(new { error = result.Error });
        });

        return app;
    }

    private static IResult Forbidden() => Results.StatusCode(StatusCodes.Status403Forbidden);
}
