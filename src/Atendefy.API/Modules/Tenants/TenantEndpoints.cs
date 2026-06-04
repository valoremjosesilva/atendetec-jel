using Atendefy.API.Modules.Tenants.Models;
using Microsoft.AspNetCore.Mvc;

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

        return app;
    }
}
