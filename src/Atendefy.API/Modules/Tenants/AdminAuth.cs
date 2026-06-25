using System.Security.Cryptography;
using System.Text;

namespace Atendefy.API.Modules.Tenants;

/// <summary>
/// Autenticação do superadmin: compara o header X-Admin-Key com a config Admin:Key
/// (comparação em tempo constante). Sem chave configurada => nega tudo.
/// </summary>
public static class AdminAuth
{
    public static bool IsAdmin(HttpContext ctx, IConfiguration config)
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
