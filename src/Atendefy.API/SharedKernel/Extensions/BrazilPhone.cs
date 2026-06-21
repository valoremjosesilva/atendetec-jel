namespace Atendefy.API.SharedKernel.Extensions;

public static class BrazilPhone
{
    /// <summary>
    /// Insere o nono dígito em celulares brasileiros quando ausente.
    /// A Meta entrega o número do webhook sem o 9 (ex.: 5547 99077813), mas o
    /// envio pela Graph API exige o 9 (5547 9 99077813), senão dá 131030/recipient.
    /// Mantém intactos números já com 9, fixos e números de outros países.
    /// </summary>
    public static string NormalizeForSending(string phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) return phone;

        var digits = new string(phone.Where(char.IsDigit).ToArray());

        // 55 (país) + 2 (DDD) + 8 (assinante sem o 9) = 12 dígitos.
        if (digits.Length != 12 || !digits.StartsWith("55")) return digits;

        // Primeiro dígito do assinante 6-9 => celular (fixos começam em 2-5).
        var firstSubscriberDigit = digits[4];
        if (firstSubscriberDigit < '6') return digits;

        // Insere o 9 logo após o DDD: 55 + DD + 9 + 8 dígitos.
        return string.Concat(digits.AsSpan(0, 4), "9", digits.AsSpan(4));
    }
}
