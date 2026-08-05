using Atendefy.API.Infrastructure.RateLimiting;
using Microsoft.AspNetCore.Mvc;

namespace Atendefy.API.Modules.Leads;

public static class LeadEndpoints
{
    private const int LeadsPerMinutePerIp = 5; // limite estrito: endpoint anônimo da landing

    public record CreateLeadRequest(
        string Name,
        string Phone,
        string? Email,
        string? BusinessType,
        string? Message,
        // Honeypot: humanos não veem o campo na landing; bot que preencher é ignorado.
        string? Website);

    public static IEndpointRouteBuilder MapLeadEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/leads").WithTags("Leads").AllowAnonymous();

        group.MapPost("/", async (
            [FromBody] CreateLeadRequest request,
            LeadService leadService,
            TenantRateLimiter rateLimiter,
            HttpContext ctx) =>
        {
            var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            if (!await rateLimiter.IsAllowedAsync(ip, "leads", LeadsPerMinutePerIp))
                return Results.Json(new { error = "Muitas tentativas. Aguarde um minuto e tente novamente." },
                    statusCode: StatusCodes.Status429TooManyRequests);

            // Honeypot preenchido: responde como sucesso e descarta.
            if (!string.IsNullOrEmpty(request.Website))
                return Results.Created("/leads", new { id = Guid.Empty });

            var result = await leadService.CreateAsync(
                request.Name ?? string.Empty,
                request.Phone ?? string.Empty,
                request.Email,
                request.BusinessType,
                request.Message,
                ctx.RequestAborted);

            return result.IsSuccess
                ? Results.Created("/leads", new { id = result.Value })
                : Results.BadRequest(new { error = result.Error });
        });

        return app;
    }
}
