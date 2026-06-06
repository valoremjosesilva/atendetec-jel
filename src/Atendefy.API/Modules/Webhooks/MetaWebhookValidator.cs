using System.Security.Cryptography;
using System.Text;

namespace Atendefy.API.Modules.Webhooks;

public class MetaWebhookValidator(string appSecret)
{
    public bool IsValid(byte[] body, string signature)
    {
        if (!signature.StartsWith("sha256=")) return false;

        var expectedHash = signature[7..];
        var actualHash = Convert.ToHexString(
            HMACSHA256.HashData(Encoding.UTF8.GetBytes(appSecret), body)).ToLower();

        try
        {
            return CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(expectedHash),
                Convert.FromHexString(actualHash));
        }
        catch
        {
            return false;
        }
    }
}
