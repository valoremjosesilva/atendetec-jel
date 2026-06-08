namespace Atendefy.API.Infrastructure.RateLimiting;

public class ApiRateLimitFilter(TenantRateLimiter rateLimiter) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        var tenantId = ctx.HttpContext.User.FindFirst("tenant_id")?.Value;
        if (string.IsNullOrEmpty(tenantId))
            return Results.Unauthorized();

        if (!await rateLimiter.IsAllowedAsync(tenantId, scope: "api"))
            return Results.StatusCode(StatusCodes.Status429TooManyRequests);

        return await next(ctx);
    }
}
