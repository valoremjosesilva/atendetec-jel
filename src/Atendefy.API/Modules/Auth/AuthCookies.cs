namespace Atendefy.API.Modules.Auth;

// Gerencia os cookies HttpOnly de sessão do SPA. O frontend fala com a API
// pela mesma origem (proxy nginx/vite em /api), então SameSite=Strict funciona
// e o token nunca fica acessível a JavaScript (mitiga roubo por XSS).
public static class AuthCookies
{
    public const string Access = "atendefy_access";
    public const string Refresh = "atendefy_refresh";

    // Manter alinhado com as expirações dos tokens em JwtService (15 min / 7 dias).
    private static readonly TimeSpan AccessLifetime = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan RefreshLifetime = TimeSpan.FromDays(7);

    public static void Set(HttpContext ctx, string accessToken, string refreshToken, bool secure)
    {
        ctx.Response.Cookies.Append(Access, accessToken, Options(secure, AccessLifetime));
        ctx.Response.Cookies.Append(Refresh, refreshToken, Options(secure, RefreshLifetime));
    }

    public static void Clear(HttpContext ctx, bool secure)
    {
        ctx.Response.Cookies.Delete(Access, Options(secure, null));
        ctx.Response.Cookies.Delete(Refresh, Options(secure, null));
    }

    private static CookieOptions Options(bool secure, TimeSpan? maxAge) => new()
    {
        HttpOnly = true,
        Secure = secure,
        SameSite = SameSiteMode.Strict,
        Path = "/",
        MaxAge = maxAge,
    };
}
