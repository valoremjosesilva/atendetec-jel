using Atendefy.API.Infrastructure.Cache;
using Atendefy.API.Infrastructure.Email;
using Atendefy.API.Infrastructure.RateLimiting;
using Atendefy.API.Infrastructure.Security;
using Atendefy.API.Modules.Tenants.Models;
using Microsoft.AspNetCore.Mvc;

namespace Atendefy.API.Modules.Tenants;

public static class TenantEndpoints
{
    private const int RegisterPerMinutePerIp = 5;          // limite estrito do cadastro anônimo
    private static readonly TimeSpan VerifyTokenTtl = TimeSpan.FromHours(48);

    public static IEndpointRouteBuilder MapTenantEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/tenants").WithTags("Tenants").AllowAnonymous();

        group.MapPost("/register", async (
            [FromBody] RegisterTenantRequest request,
            TenantService tenantService,
            TenantRateLimiter rateLimiter,
            TurnstileValidator turnstile,
            RedisService redis,
            IEmailSender emailSender,
            IConfiguration config,
            HttpContext ctx) =>
        {
            var ip = ClientIp(ctx);

            // 1) Rate-limit por IP (anti-flood de cadastros).
            if (!await rateLimiter.IsAllowedAsync(ip, "register", RegisterPerMinutePerIp))
                return Results.Json(new { error = "Muitas tentativas. Aguarde um minuto e tente novamente." },
                    statusCode: StatusCodes.Status429TooManyRequests);

            // 2) Captcha (Cloudflare Turnstile).
            if (!await turnstile.IsValidAsync(request.CaptchaToken, ip))
                return Results.BadRequest(new { error = "Falha na verificação anti-robô. Recarregue a página e tente novamente." });

            // 3) Cria as linhas (sem provisionar schema).
            var result = await tenantService.RegisterAsync(request);
            if (!result.IsSuccess) return Results.BadRequest(new { error = result.Error });

            // 4) Gera token de verificação e envia o e-mail.
            await SendVerificationEmailAsync(redis, emailSender, config, result.Value!);

            return Results.Created($"/tenants/{result.Value!.Id}",
                new { result.Value.Id, result.Value.Subdomain, result.Value.Name });
        });

        // Confirmação de e-mail: troca o token (Redis) por EmailVerified=true.
        group.MapPost("/verify-email", async (
            [FromBody] VerifyEmailRequest request,
            TenantService tenantService,
            RedisService redis) =>
        {
            if (string.IsNullOrWhiteSpace(request.Token))
                return Results.BadRequest(new { error = "Token inválido." });

            var userIdStr = await redis.GetAsync(VerifyKey(request.Token));
            if (userIdStr is null || !Guid.TryParse(userIdStr, out var userId))
                return Results.BadRequest(new { error = "Link inválido ou expirado. Solicite um novo e-mail de confirmação." });

            var result = await tenantService.MarkEmailVerifiedAsync(userId);
            if (!result.IsSuccess) return Results.BadRequest(new { error = result.Error });

            await redis.DeleteAsync(VerifyKey(request.Token));
            return Results.Ok(new { verified = true });
        });

        // Reenvia o e-mail de verificação. Resposta sempre genérica (não revela se a conta existe).
        group.MapPost("/resend-verification", async (
            [FromBody] ResendVerificationRequest request,
            TenantService tenantService,
            TenantRateLimiter rateLimiter,
            RedisService redis,
            IEmailSender emailSender,
            IConfiguration config,
            HttpContext ctx) =>
        {
            var ip = ClientIp(ctx);
            if (!await rateLimiter.IsAllowedAsync(ip, "resend", RegisterPerMinutePerIp))
                return Results.Json(new { error = "Muitas tentativas. Aguarde um minuto." },
                    statusCode: StatusCodes.Status429TooManyRequests);

            if (!string.IsNullOrWhiteSpace(request.Subdomain) && !string.IsNullOrWhiteSpace(request.Email))
            {
                var owner = await tenantService.FindOwnerForResendAsync(request.Subdomain, request.Email);
                if (owner is not null)
                    await SendVerificationEmailAsync(redis, emailSender, config, owner);
            }

            return Results.Ok(new { sent = true });
        });

        // Admin: listar empresas pendentes de aprovação. Protegido por X-Admin-Key.
        group.MapGet("/pending", async (
            HttpContext ctx, TenantService tenantService, IConfiguration config) =>
        {
            if (!AdminAuth.IsAdmin(ctx, config)) return Results.StatusCode(StatusCodes.Status403Forbidden);
            return Results.Ok(await tenantService.ListPendingAsync());
        });

        // Admin: aprovar (ativar) uma empresa. Protegido por X-Admin-Key.
        group.MapPost("/{subdomain}/activate", async (
            string subdomain, HttpContext ctx, TenantService tenantService, IConfiguration config) =>
        {
            if (!AdminAuth.IsAdmin(ctx, config)) return Results.StatusCode(StatusCodes.Status403Forbidden);
            var result = await tenantService.ActivateAsync(subdomain);
            return result.IsSuccess
                ? Results.Ok(new { activated = subdomain })
                : Results.BadRequest(new { error = result.Error });
        });

        // /me — perfil + entitlements do plano + uso mensal da IA, para o painel saber o que exibir.
        app.MapGet("/me", async (
            HttpContext ctx,
            EntitlementsService entitlements,
            Atendefy.API.Infrastructure.Cache.RedisService redis) =>
        {
            var tenantIdStr = ctx.User.FindFirst("tenant_id")?.Value;
            if (string.IsNullOrEmpty(tenantIdStr) || !Guid.TryParse(tenantIdStr, out var tenantId))
                return Results.Json(new { error = "Token inválido" }, statusCode: 401);

            var (planName, limits) = await entitlements.GetPlanForTenantAsync(tenantId);

            long messagesUsed = 0;
            try { messagesUsed = await redis.GetCounterAsync(
                EntitlementsService.MonthlyUsageKey(tenantIdStr, DateTime.UtcNow)); }
            catch { /* Redis indisponível: uso 0 não bloqueia a UI. */ }

            return Results.Ok(new
            {
                role = ctx.User.FindFirst("role")?.Value,
                planName,
                entitlements = new
                {
                    aiEnabled = limits.AiEnabled,
                    schedulingEnabled = limits.SchedulingEnabled,
                    whatsAppAccounts = limits.WhatsAppAccounts,
                    messagesPerMonth = limits.MessagesPerMonth,
                    teamMembers = limits.TeamMembers
                },
                usage = new { messagesUsed }
            });
        }).RequireAuthorization().WithTags("Tenants");

        return app;
    }

    private static string ClientIp(HttpContext ctx) =>
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    private static string VerifyKey(string token) => $"emailverify:{token}";

    // Gera um token, guarda no Redis (TTL) e envia o e-mail com o link de confirmação.
    private static async Task SendVerificationEmailAsync(
        RedisService redis, IEmailSender emailSender, IConfiguration config, RegisteredTenant owner)
    {
        var token = Guid.NewGuid().ToString("N");
        await redis.SetAsync(VerifyKey(token), owner.OwnerUserId.ToString(), VerifyTokenTtl);

        var baseDomain = config["App:BaseDomain"];
        var link = string.IsNullOrEmpty(baseDomain)
            ? $"/verify-email?token={token}"
            : $"https://app.{baseDomain}/verify-email?token={token}";

        var html =
            $"<p>Olá, {System.Net.WebUtility.HtmlEncode(owner.OwnerName)}!</p>" +
            $"<p>Confirme seu e-mail para concluir o cadastro da empresa <strong>{System.Net.WebUtility.HtmlEncode(owner.Name)}</strong> no Mensagee.</p>" +
            $"<p><a href=\"{link}\">Confirmar meu e-mail</a></p>" +
            $"<p>Ou copie e cole este endereço no navegador:<br>{link}</p>" +
            "<p>Se você não fez este cadastro, ignore esta mensagem.</p>";

        await emailSender.SendAsync(owner.OwnerEmail, "Confirme seu e-mail — Mensagee", html);
    }
}
