using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Atendefy.API.Infrastructure.Cache;
using Atendefy.API.Modules.AI;
using Atendefy.API.Modules.AI.Models;
using Atendefy.API.Modules.Scheduling.Models;
using Atendefy.API.Modules.WhatsApp.Models;

namespace Atendefy.API.Modules.Scheduling.Horafy;

/// <summary>
/// Máquina de estados do agendamento conversacional contra o Horafy:
/// serviço → profissional → dia → horário → confirmação → criação. Cada passo devolve um
/// <see cref="BookingFlowReply"/> (texto + interativo). O estado vive no Redis.
/// </summary>
public sealed class BookingFlowService(
    HorafyClient horafy,
    RedisService redis,
    ILogger<BookingFlowService> logger)
{
    private static readonly TimeSpan StateTtl = TimeSpan.FromMinutes(15);
    private const int MaxOptions = 9;       // listas curtas (WhatsApp lista: até 10 linhas)
    private const int DayHorizon = 14;      // dias à frente para ofertar

    private static readonly string[] WeekdaysPt = ["dom", "seg", "ter", "qua", "qui", "sex", "sáb"];

    private static string Key(string tenantId, string phone) => $"bookingflow:{tenantId}:{phone}";

    public async Task<bool> HasActiveFlowAsync(string tenantId, string phone) =>
        await redis.ExistsAsync(Key(tenantId, phone));

    public async Task<BookingFlowReply> HandleAsync(
        HorafyConnection conn,
        CalendarConfig calendar,
        string tenantId,
        string phone,
        string? contactName,
        string userMessage,
        IAIProvider aiProvider,
        string model,
        CancellationToken ct = default)
    {
        var text = (userMessage ?? string.Empty).Trim();
        var state = await LoadAsync(tenantId, phone);

        if (IsCancel(text))
        {
            await ClearAsync(tenantId, phone);
            return Reply("Sem problema, cancelei o agendamento. Se quiser, é só me chamar de novo. 🙂");
        }

        if (state is null)
            return await StartAsync(conn, calendar, tenantId, phone, ct);

        return state.Step switch
        {
            BookingStep.Service      => await OnServiceAsync(conn, state, tenantId, phone, text, aiProvider, model, ct),
            BookingStep.Professional => await OnProfessionalAsync(conn, state, tenantId, phone, text, aiProvider, model, ct),
            BookingStep.Day          => await OnDayAsync(conn, state, tenantId, phone, text, aiProvider, model, ct),
            BookingStep.Time         => await OnTimeAsync(conn, state, tenantId, phone, text, aiProvider, model, ct),
            BookingStep.Confirm      => await OnConfirmAsync(conn, state, tenantId, phone, contactName, text, ct),
            _                        => await StartAsync(conn, calendar, tenantId, phone, ct)
        };
    }

    // ── Passo 1: serviço ─────────────────────────────────────────────────────────
    private async Task<BookingFlowReply> StartAsync(
        HorafyConnection conn, CalendarConfig calendar, string tenantId, string phone, CancellationToken ct)
    {
        var services = await horafy.GetServicesAsync(conn, ct);
        if (services.Count == 0)
        {
            await ClearAsync(tenantId, phone);
            return Reply("No momento não há serviços disponíveis para agendamento. Posso ajudar em algo mais?");
        }

        var chosen = calendar.DefaultServiceId is { } def
            ? services.FirstOrDefault(s => s.Id == def)
            : null;
        chosen ??= services.Count == 1 ? services[0] : null;

        if (chosen is not null)
        {
            var s = new BookingFlowState { Step = BookingStep.Service, ServiceId = chosen.Id, ServiceName = chosen.Name };
            return await GoToProfessionalAsync(conn, s, tenantId, phone, ct);
        }

        var options = services.Take(MaxOptions)
            .Select(s => new FlowOption { Id = s.Id.ToString(), Label = $"{s.Name} ({s.DurationMinutes}min)" })
            .ToList();

        await SaveAsync(tenantId, phone, new BookingFlowState { Step = BookingStep.Service, Options = options });
        return List("Vamos agendar! Qual serviço você deseja?", options);
    }

    private async Task<BookingFlowReply> OnServiceAsync(
        HorafyConnection conn, BookingFlowState state, string tenantId, string phone,
        string text, IAIProvider ai, string model, CancellationToken ct)
    {
        var opt = await ResolveAsync(text, state.Options, ai, model, ct);
        if (opt is null) return List("Não entendi. Qual serviço você deseja?", state.Options);

        state.ServiceId = Guid.Parse(opt.Id);
        state.ServiceName = StripDuration(opt.Label);
        return await GoToProfessionalAsync(conn, state, tenantId, phone, ct);
    }

    // ── Passo 2: profissional ────────────────────────────────────────────────────
    private async Task<BookingFlowReply> GoToProfessionalAsync(
        HorafyConnection conn, BookingFlowState state, string tenantId, string phone, CancellationToken ct)
    {
        var resources = await horafy.GetResourcesByServiceAsync(conn, state.ServiceId!.Value, ct);
        if (resources.Count == 0)
        {
            await ClearAsync(tenantId, phone);
            return Reply($"Não encontrei profissionais disponíveis para {state.ServiceName}. Posso ajudar em algo mais?");
        }

        if (resources.Count == 1)
        {
            state.ResourceId = resources[0].Id;
            state.ResourceName = resources[0].Name;
            return await GoToDayAsync(conn, state, tenantId, phone, ct);
        }

        state.Options = resources.Take(MaxOptions)
            .Select(r => new FlowOption
            {
                Id = r.Id.ToString(),
                Label = string.IsNullOrWhiteSpace(r.Specialty) ? r.Name : $"{r.Name} — {r.Specialty}"
            })
            .ToList();
        state.Step = BookingStep.Professional;
        await SaveAsync(tenantId, phone, state);
        return List("Com qual profissional você prefere?", state.Options);
    }

    private async Task<BookingFlowReply> OnProfessionalAsync(
        HorafyConnection conn, BookingFlowState state, string tenantId, string phone,
        string text, IAIProvider ai, string model, CancellationToken ct)
    {
        var opt = await ResolveAsync(text, state.Options, ai, model, ct);
        if (opt is null) return List("Não entendi. Com qual profissional você prefere?", state.Options);

        state.ResourceId = Guid.Parse(opt.Id);
        state.ResourceName = opt.Label.Split(" — ")[0];
        return await GoToDayAsync(conn, state, tenantId, phone, ct);
    }

    // ── Passo 3: dia ─────────────────────────────────────────────────────────────
    private async Task<BookingFlowReply> GoToDayAsync(
        HorafyConnection conn, BookingFlowState state, string tenantId, string phone, CancellationToken ct)
    {
        var from = DateOnly.FromDateTime(DateTime.UtcNow);
        var to = from.AddDays(DayHorizon);
        var days = await horafy.GetAvailableDaysAsync(conn, state.ResourceId!.Value, from, to, state.ServiceId, ct);

        if (days.Count == 0)
        {
            await ClearAsync(tenantId, phone);
            return Reply($"Não há dias disponíveis nos próximos {DayHorizon} dias para {state.ResourceName}. " +
                         "Tente novamente mais tarde ou peça para falar com um atendente.");
        }

        state.Options = days.Take(MaxOptions)
            .Select(d => new FlowOption { Id = d.ToString("yyyy-MM-dd"), Label = FormatDay(d) })
            .ToList();
        state.Step = BookingStep.Day;
        await SaveAsync(tenantId, phone, state);
        return List("Para qual dia?", state.Options);
    }

    private async Task<BookingFlowReply> OnDayAsync(
        HorafyConnection conn, BookingFlowState state, string tenantId, string phone,
        string text, IAIProvider ai, string model, CancellationToken ct)
    {
        var opt = await ResolveAsync(text, state.Options, ai, model, ct);
        if (opt is null) return List("Não entendi. Para qual dia?", state.Options);

        state.Date = opt.Id;
        return await GoToTimeAsync(conn, state, tenantId, phone, ct);
    }

    // ── Passo 4: horário ─────────────────────────────────────────────────────────
    private async Task<BookingFlowReply> GoToTimeAsync(
        HorafyConnection conn, BookingFlowState state, string tenantId, string phone, CancellationToken ct)
    {
        var date = DateOnly.ParseExact(state.Date!, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var slots = await horafy.GetSlotsAsync(conn, state.ResourceId!.Value, date, state.ServiceId, ct);

        if (slots.Count == 0)
            return await GoToDayAsync(conn, state, tenantId, phone, ct);

        state.Options = slots.Take(MaxOptions)
            .Select(s => new FlowOption { Id = s.ToString("o"), Label = s.ToString("HH:mm") })
            .ToList();
        state.Step = BookingStep.Time;
        await SaveAsync(tenantId, phone, state);
        return List($"Horários livres em {FormatDay(date)}:", state.Options, "Responda com o número do horário.");
    }

    private async Task<BookingFlowReply> OnTimeAsync(
        HorafyConnection conn, BookingFlowState state, string tenantId, string phone,
        string text, IAIProvider ai, string model, CancellationToken ct)
    {
        var opt = await ResolveAsync(text, state.Options, ai, model, ct);
        if (opt is null) return List("Não entendi. Escolha um horário:", state.Options);

        state.Slot = opt.Id;
        state.Step = BookingStep.Confirm;
        await SaveAsync(tenantId, phone, state);

        var slot = DateTimeOffset.Parse(state.Slot, CultureInfo.InvariantCulture);
        var date = DateOnly.FromDateTime(slot.DateTime);
        var summary =
            $"Confirma o agendamento?\n\n" +
            $"• Serviço: {state.ServiceName}\n" +
            $"• Profissional: {state.ResourceName}\n" +
            $"• Dia: {FormatDay(date)}\n" +
            $"• Horário: {slot:HH:mm}\n\n" +
            "Responda *SIM* para confirmar ou *NÃO* para cancelar.";
        return Confirm(summary);
    }

    // ── Passo 5: confirmação / criação ───────────────────────────────────────────
    private async Task<BookingFlowReply> OnConfirmAsync(
        HorafyConnection conn, BookingFlowState state, string tenantId, string phone,
        string? contactName, string text, CancellationToken ct)
    {
        if (IsNo(text))
        {
            await ClearAsync(tenantId, phone);
            return Reply("Tudo bem, não confirmei. Quando quiser agendar, é só me chamar. 🙂");
        }
        if (!IsYes(text))
        {
            var preview = DateTimeOffset.Parse(state.Slot!, CultureInfo.InvariantCulture);
            return Confirm($"Responda *SIM* para confirmar o horário das {preview:HH:mm} ou *NÃO* para cancelar.");
        }

        var slot = DateTimeOffset.Parse(state.Slot!, CultureInfo.InvariantCulture);
        var externalId = $"atendefy:{phone}:{slot:yyyyMMddHHmm}";

        try
        {
            var result = await horafy.CreateBookingAsync(conn, new HorafyCreateBooking(
                ServiceIds:    [state.ServiceId!.Value],
                ResourceId:    state.ResourceId!.Value,
                ScheduledAt:   slot,
                CustomerName:  string.IsNullOrWhiteSpace(contactName) ? "Cliente WhatsApp" : contactName!,
                CustomerEmail: null,
                CustomerPhone: phone,
                Notes:         "Agendado via WhatsApp (Atendefy).",
                ExternalId:    externalId,
                Source:        "atendefy"), ct);

            await ClearAsync(tenantId, phone);

            var date = DateOnly.FromDateTime(slot.DateTime);
            return Reply(result is { AlreadyExisted: true }
                ? $"Esse agendamento já estava registrado: {state.ServiceName} com {state.ResourceName} em {FormatDay(date)} às {slot:HH:mm}. ✅"
                : $"Pronto! Agendamento confirmado: {state.ServiceName} com {state.ResourceName} em {FormatDay(date)} às {slot:HH:mm}. ✅");
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
        {
            logger.LogInformation("Horário já ocupado ao confirmar para {Phone}; reofertando.", phone);
            var reoffer = await GoToTimeAsync(conn, state, tenantId, phone, ct);
            return reoffer with
            {
                Text = reoffer.Text + "\n\n(O horário anterior acabou de ser ocupado — escolha outro.)"
            };
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Falha ao criar agendamento no Horafy para {Phone}", phone);
            return Reply("Tive um problema ao confirmar o agendamento agora. Pode tentar novamente em instantes?");
        }
    }

    // ── Resolução da escolha (id → número → texto → IA) ──────────────────────────
    private async Task<FlowOption?> ResolveAsync(
        string text, List<FlowOption> options, IAIProvider ai, string model, CancellationToken ct)
    {
        if (options.Count == 0) return null;

        // 0. Id exato (resposta interativa do WhatsApp devolve o id da opção)
        var byId = options.FirstOrDefault(o => string.Equals(o.Id, text, StringComparison.OrdinalIgnoreCase));
        if (byId is not null) return byId;

        // 1. Número (1..N)
        var digits = Regex.Match(text, @"\d+");
        if (digits.Success && int.TryParse(digits.Value, out var n) && n >= 1 && n <= options.Count)
            return options[n - 1];

        // 2. Correspondência por texto
        var lower = text.ToLowerInvariant();
        if (lower.Length >= 2)
        {
            var matches = options
                .Where(o => o.Label.ToLowerInvariant().Contains(lower) || lower.Contains(o.Label.ToLowerInvariant()))
                .ToList();
            if (matches.Count == 1) return matches[0];
        }

        // 3. IA (melhor esforço)
        try
        {
            var list = string.Join("\n", options.Select((o, i) => $"{i + 1}) {o.Label}"));
            var sys = "Você associa a resposta do cliente a uma das opções numeradas. " +
                      $"Responda APENAS com o número (1 a {options.Count}) ou 0 se nenhuma servir.";
            var prompt = $"Opções:\n{list}\n\nResposta do cliente: \"{text}\"";
            var res = await ai.CompleteAsync(new AICompletionRequest(sys, [new ChatMessage("user", prompt)], model, MaxTokens: 5));
            var m = Regex.Match(res.Content ?? string.Empty, @"\d+");
            if (m.Success && int.TryParse(m.Value, out var idx) && idx >= 1 && idx <= options.Count)
                return options[idx - 1];
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Falha ao resolver opção por IA");
        }

        return null;
    }

    // ── Construção de respostas ──────────────────────────────────────────────────
    private static BookingFlowReply Reply(string text) => new(text);

    private static BookingFlowReply List(string header, List<FlowOption> options, string? footer = null)
    {
        var fallback = Render(header, options, footer);
        var interactive = new InteractiveMessage(
            InteractiveKind.List,
            Body: header,
            Options: options.Select(o => new InteractiveOption(o.Id, o.Label)).ToList(),
            ButtonText: "Ver opções",
            Footer: footer,
            FallbackText: fallback);
        return new BookingFlowReply(fallback, interactive);
    }

    private static BookingFlowReply Confirm(string body) =>
        new(body, new InteractiveMessage(
            InteractiveKind.Buttons,
            Body: body,
            Options: [new InteractiveOption("sim", "Sim"), new InteractiveOption("nao", "Não")],
            FallbackText: body));

    private static string Render(string header, List<FlowOption> options, string? footer = null)
    {
        var body = string.Join("\n", options.Select((o, i) => $"{i + 1}. {o.Label}"));
        return $"{header}\n{body}\n\n{footer ?? "Responda com o número da opção."}";
    }

    private static string FormatDay(DateOnly d) => $"{WeekdaysPt[(int)d.DayOfWeek]} {d:dd/MM}";

    private static string StripDuration(string label)
    {
        var i = label.LastIndexOf(" (", StringComparison.Ordinal);
        return i > 0 ? label[..i] : label;
    }

    private static bool IsCancel(string t) =>
        Regex.IsMatch(t, @"^(cancelar|parar|desistir|sair|esquece|deixa)\b", RegexOptions.IgnoreCase);

    private static bool IsYes(string t) =>
        Regex.IsMatch(t, @"^(sim|s|confirmar|confirmo|ok|isso|pode|claro|positivo|👍)\b", RegexOptions.IgnoreCase);

    private static bool IsNo(string t) =>
        Regex.IsMatch(t, @"^(n[ãa]o|n|negativo)\b", RegexOptions.IgnoreCase);

    private async Task<BookingFlowState?> LoadAsync(string tenantId, string phone)
    {
        var json = await redis.GetAsync(Key(tenantId, phone));
        if (string.IsNullOrEmpty(json)) return null;
        try { return JsonSerializer.Deserialize<BookingFlowState>(json); }
        catch { return null; }
    }

    private Task SaveAsync(string tenantId, string phone, BookingFlowState state) =>
        redis.SetAsync(Key(tenantId, phone), JsonSerializer.Serialize(state), StateTtl);

    private Task ClearAsync(string tenantId, string phone) =>
        redis.DeleteAsync(Key(tenantId, phone));
}
