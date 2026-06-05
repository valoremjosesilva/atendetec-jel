using System.Text.Json;

namespace Atendefy.API.Modules.WhatsApp.Models;

public record MetaConfig(string PhoneNumberId, string AccessToken)
{
    public static MetaConfig FromJson(string json)
    {
        var doc = JsonDocument.Parse(json);
        return new MetaConfig(
            doc.RootElement.GetProperty("phone_number_id").GetString()!,
            doc.RootElement.GetProperty("access_token").GetString()!
        );
    }

    public string ToJson() =>
        JsonSerializer.Serialize(new { phone_number_id = PhoneNumberId, access_token = AccessToken });
}

public record EvolutionConfig(string BaseUrl, string Instance, string ApiKey)
{
    public static EvolutionConfig FromJson(string json)
    {
        var doc = JsonDocument.Parse(json);
        return new EvolutionConfig(
            doc.RootElement.GetProperty("base_url").GetString()!,
            doc.RootElement.GetProperty("instance").GetString()!,
            doc.RootElement.GetProperty("api_key").GetString()!
        );
    }

    public string ToJson() =>
        JsonSerializer.Serialize(new { base_url = BaseUrl, instance = Instance, api_key = ApiKey });
}
