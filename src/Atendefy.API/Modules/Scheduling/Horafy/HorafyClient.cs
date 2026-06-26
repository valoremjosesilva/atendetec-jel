using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Atendefy.API.Infrastructure.Cache;

namespace Atendefy.API.Modules.Scheduling.Horafy;

/// <summary>
/// Cliente HTTP para a API de integração do Horafy. Faz o token-exchange (API key → JWT),
/// cacheia o token no Redis e expõe os passos do fluxo de agendamento:
/// serviços → profissionais do serviço → dias → horários → criar agendamento.
/// </summary>
public sealed class HorafyClient(
    IHttpClientFactory httpClientFactory,
    RedisService redis,
    ILogger<HorafyClient> logger)
{
    private const string ApiVersion = "1";

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true
    };

    // ── Catálogo / disponibilidade ──────────────────────────────────────────────
    public Task<IReadOnlyList<HorafyService>> GetServicesAsync(
        HorafyConnection c, CancellationToken ct = default) =>
        GetListAsync<HorafyService>(c, "/api/v1/services?onlyActive=true", ct);

    public Task<IReadOnlyList<HorafyResource>> GetResourcesByServiceAsync(
        HorafyConnection c, Guid serviceId, CancellationToken ct = default) =>
        GetListAsync<HorafyResource>(c, $"/api/v1/services/{serviceId}/resources", ct);

    public async Task<IReadOnlyList<DateOnly>> GetAvailableDaysAsync(
        HorafyConnection c, Guid resourceId, DateOnly from, DateOnly to,
        Guid? serviceId = null, CancellationToken ct = default)
    {
        var url = $"/api/v1/availability/resources/{resourceId}/days?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}"
                  + (serviceId is null ? "" : $"&serviceId={serviceId}");
        var raw = await GetListAsync<string>(c, url, ct);
        return raw.Select(s => DateOnly.Parse(s[..10])).ToList();
    }

    public Task<IReadOnlyList<DateTimeOffset>> GetSlotsAsync(
        HorafyConnection c, Guid resourceId, DateOnly date,
        Guid? serviceId = null, CancellationToken ct = default)
    {
        var url = $"/api/v1/availability/resources/{resourceId}/slots?date={date:yyyy-MM-dd}"
                  + (serviceId is null ? "" : $"&serviceId={serviceId}");
        return GetListAsync<DateTimeOffset>(c, url, ct);
    }

    // ── Criação de agendamento ──────────────────────────────────────────────────
    public async Task<HorafyBookingResult?> CreateBookingAsync(
        HorafyConnection c, HorafyCreateBooking booking, CancellationToken ct = default)
    {
        using var resp = await SendAsync(c, HttpMethod.Post, "/api/v1/integrations/bookings",
            content: JsonContent.Create(booking), ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<HorafyBookingResult>(Json, ct);
    }

    // ── Teste de conexão (usado pela UI) ────────────────────────────────────────
    public async Task<HorafyTestResult> TestConnectionAsync(HorafyConnection c, CancellationToken ct = default)
    {
        try
        {
            var services = await GetServicesAsync(c, ct);
            return new HorafyTestResult(true, services.Count, null);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Teste de conexão Horafy falhou para slug {Slug}", c.TenantSlug);
            return new HorafyTestResult(false, 0, ex.Message);
        }
    }

    // ── Infra interna ────────────────────────────────────────────────────────────
    private async Task<IReadOnlyList<T>> GetListAsync<T>(
        HorafyConnection c, string path, CancellationToken ct)
    {
        using var resp = await SendAsync(c, HttpMethod.Get, path, content: null, ct);
        resp.EnsureSuccessStatusCode();
        var list = await resp.Content.ReadFromJsonAsync<List<T>>(Json, ct);
        return list ?? [];
    }

    /// <summary>
    /// Envia a requisição com Bearer + X-Tenant-Slug + X-Api-Version. Em 401, invalida o
    /// token em cache e tenta uma única vez novamente.
    /// </summary>
    private async Task<HttpResponseMessage> SendAsync(
        HorafyConnection c, HttpMethod method, string path, HttpContent? content, CancellationToken ct)
    {
        var response = await SendOnceAsync(c, method, path, content, forceFreshToken: false, ct);
        if (response.StatusCode != HttpStatusCode.Unauthorized)
            return response;

        response.Dispose();
        return await SendOnceAsync(c, method, path, content, forceFreshToken: true, ct);
    }

    private async Task<HttpResponseMessage> SendOnceAsync(
        HorafyConnection c, HttpMethod method, string path, HttpContent? content,
        bool forceFreshToken, CancellationToken ct)
    {
        var token  = await GetTokenAsync(c, forceFreshToken, ct);
        var client = httpClientFactory.CreateClient("horafy");

        using var request = new HttpRequestMessage(method, BuildUrl(c, path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.TryAddWithoutValidation("X-Tenant-Slug", c.TenantSlug);
        request.Headers.TryAddWithoutValidation("X-Api-Version", ApiVersion);
        if (content is not null) request.Content = content;

        return await client.SendAsync(request, ct);
    }

    private async Task<string> GetTokenAsync(HorafyConnection c, bool forceFresh, CancellationToken ct)
    {
        var cacheKey = $"horafy:token:{c.TenantSlug}";

        if (!forceFresh)
        {
            var cached = await redis.GetAsync(cacheKey);
            if (!string.IsNullOrEmpty(cached)) return cached;
        }

        var client = httpClientFactory.CreateClient("horafy");
        using var request = new HttpRequestMessage(HttpMethod.Post, BuildUrl(c, "/api/v1/integrations/token"));
        request.Headers.TryAddWithoutValidation("X-Api-Key", c.ApiKey);

        using var resp = await client.SendAsync(request, ct);
        resp.EnsureSuccessStatusCode();

        var token = await resp.Content.ReadFromJsonAsync<ServiceTokenResponse>(Json, ct)
                    ?? throw new InvalidOperationException("Resposta de token vazia do Horafy.");

        // Cacheia até ~1 min antes de expirar (mínimo 30s).
        var ttl = token.ExpiresAt - DateTimeOffset.UtcNow - TimeSpan.FromMinutes(1);
        if (ttl < TimeSpan.FromSeconds(30)) ttl = TimeSpan.FromSeconds(30);
        await redis.SetAsync(cacheKey, token.AccessToken, ttl);

        return token.AccessToken;
    }

    private static string BuildUrl(HorafyConnection c, string path) =>
        $"{c.BaseUrl.TrimEnd('/')}{path}";

    private sealed record ServiceTokenResponse(string AccessToken, DateTimeOffset ExpiresAt, string? TokenType);
}
