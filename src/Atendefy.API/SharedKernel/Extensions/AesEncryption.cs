using System.Security.Cryptography;
using System.Text;

namespace Atendefy.API.SharedKernel.Extensions;

public static class AesEncryption
{
    public static string Encrypt(string plainText, string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(plainText);
        ArgumentException.ThrowIfNullOrEmpty(key);

        var keyBytes = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        using var aes = Aes.Create();
        aes.Key = keyBytes;
        aes.GenerateIV();
        using var encryptor = aes.CreateEncryptor();
        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var cipherBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);
        var result = new byte[aes.IV.Length + cipherBytes.Length];
        aes.IV.CopyTo(result, 0);
        cipherBytes.CopyTo(result, aes.IV.Length);
        return Convert.ToBase64String(result);
    }

    public static string Decrypt(string cipherText, string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(cipherText);
        ArgumentException.ThrowIfNullOrEmpty(key);

        byte[] fullBytes;
        try
        {
            fullBytes = Convert.FromBase64String(cipherText);
        }
        catch (FormatException ex)
        {
            throw new ArgumentException("cipherText is not valid Base64.", nameof(cipherText), ex);
        }

        if (fullBytes.Length < 17)
            throw new ArgumentException("cipherText is too short to contain a valid IV + payload.", nameof(cipherText));

        var keyBytes = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        using var aes = Aes.Create();
        aes.Key = keyBytes;
        aes.IV = fullBytes[..16];
        using var decryptor = aes.CreateDecryptor();
        var plainBytes = decryptor.TransformFinalBlock(fullBytes, 16, fullBytes.Length - 16);
        return Encoding.UTF8.GetString(plainBytes);
    }
}
