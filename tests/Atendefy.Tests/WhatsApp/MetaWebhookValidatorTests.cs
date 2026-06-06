using Atendefy.API.Modules.Webhooks;
using FluentAssertions;
using System.Security.Cryptography;
using System.Text;

namespace Atendefy.Tests.WhatsApp;

public class MetaWebhookValidatorTests
{
    private const string Secret = "meu_app_secret";
    private readonly MetaWebhookValidator _validator = new(Secret);

    [Fact]
    public void IsValid_WithCorrectSignature_ShouldReturnTrue()
    {
        var body = """{"object":"whatsapp_business_account","entry":[]}""";
        var bodyBytes = Encoding.UTF8.GetBytes(body);
        var hash = Convert.ToHexString(
            HMACSHA256.HashData(Encoding.UTF8.GetBytes(Secret), bodyBytes)).ToLower();
        var signature = $"sha256={hash}";

        _validator.IsValid(bodyBytes, signature).Should().BeTrue();
    }

    [Fact]
    public void IsValid_WithWrongSignature_ShouldReturnFalse()
    {
        var body = Encoding.UTF8.GetBytes("some body");
        _validator.IsValid(body, "sha256=invalidsignature00000000000000000000000000000000000000000000000000").Should().BeFalse();
    }

    [Fact]
    public void IsValid_WithMissingPrefix_ShouldReturnFalse()
    {
        var body = Encoding.UTF8.GetBytes("body");
        _validator.IsValid(body, "invalidsignature").Should().BeFalse();
    }
}
