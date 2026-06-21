using Atendefy.API.SharedKernel.Extensions;
using FluentAssertions;

namespace Atendefy.Tests.WhatsApp;

public class BrazilPhoneTests
{
    [Theory]
    // Celular BR sem o 9 (como a Meta entrega) -> insere o 9 após o DDD.
    [InlineData("554799077813", "5547999077813")]
    [InlineData("5511988887777", "5511988887777")]  // já tem o 9 (13 dígitos) -> intacto
    [InlineData("551133334444", "551133334444")]    // fixo (assinante começa em 3) -> intacto
    [InlineData("12025550123", "12025550123")]       // outro país (não 55) -> intacto
    public void NormalizeForSending_HandlesNinthDigit(string input, string expected)
    {
        BrazilPhone.NormalizeForSending(input).Should().Be(expected);
    }

    [Fact]
    public void NormalizeForSending_StripsNonDigits()
    {
        BrazilPhone.NormalizeForSending("+55 (47) 9907-7813").Should().Be("5547999077813");
    }
}
