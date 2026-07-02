using Atendefy.API.Infrastructure.Cache;
using Npgsql;
using Atendefy.API.Infrastructure.Database;
using Atendefy.API.Infrastructure.Messaging;
using Atendefy.API.Infrastructure.RateLimiting;
using Atendefy.API.Modules.AI;
using Atendefy.API.Modules.Auth;
using Atendefy.API.Modules.Billing;
using Atendefy.API.Modules.Billing.Gateways;
using Atendefy.API.Modules.Chatbot;
using Atendefy.API.Modules.Scheduling;
using Atendefy.API.Modules.Scheduling.Horafy;
using Atendefy.API.Modules.Tenants;
using Atendefy.API.Modules.Webhooks;
using Atendefy.API.Modules.WhatsApp;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using StackExchange.Redis;
using System.Text;

var isTesting = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Testing";

if (!isTesting)
    Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateBootstrapLogger();

var builder = WebApplication.CreateBuilder(args);

if (!isTesting)
    builder.Host.UseSerilog((ctx, services, config) =>
        config.ReadFrom.Configuration(ctx.Configuration).ReadFrom.Services(services));

var connStr       = builder.Configuration.GetConnectionString("Postgres")!;
var redisConn     = builder.Configuration.GetConnectionString("Redis")!;
var jwtSecret     = builder.Configuration["Jwt:Secret"]!;
var jwtIssuer     = builder.Configuration["Jwt:Issuer"]!;
var jwtAudience   = builder.Configuration["Jwt:Audience"]!;
var baseDomain    = builder.Configuration["App:BaseDomain"]!;
var encryptionKey = builder.Configuration["Encryption:Key"]!;
var metaAppSecret = builder.Configuration["Meta:AppSecret"] ?? string.Empty;
var evolutionBaseUrl = builder.Configuration["Evolution:BaseUrl"] ?? "http://evolution-api:8080";
var evolutionApiKey  = builder.Configuration["Evolution:ApiKey"] ?? string.Empty;
var evolutionCallbackUrl = builder.Configuration["Evolution:CallbackUrl"] ?? "http://atendefy-api:8080";
var rateLimit     = builder.Configuration.GetValue<int>("RateLimit:MessagesPerMinute", 60);
var asaasKey      = builder.Configuration["Asaas:ApiKey"] ?? string.Empty;
var asaasWebhook  = builder.Configuration["Asaas:WebhookToken"] ?? string.Empty;
var asaasSandbox  = builder.Configuration.GetValue<bool>("Asaas:Sandbox", true);
var stripeKey     = builder.Configuration["Stripe:SecretKey"] ?? string.Empty;
var stripeWebhook = builder.Configuration["Stripe:WebhookSecret"] ?? string.Empty;
var turnstileSecret = builder.Configuration["Turnstile:SecretKey"] ?? string.Empty;

// Database
builder.Services.AddDbContext<PublicDbContext>(opt => opt.UseNpgsql(connStr));
builder.Services.AddSingleton(new TenantDbContextFactory(connStr));

// Redis
builder.Services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisConn));
builder.Services.AddSingleton<RedisService>();
builder.Services.AddSingleton<RedisStreamService>();

// Rate Limiting
builder.Services.AddSingleton(sp =>
    new TenantRateLimiter(sp.GetRequiredService<RedisService>(), rateLimit));

// Tenant
builder.Services.AddSingleton(new TenantResolver(baseDomain));
builder.Services.AddScoped<TenantService>();
builder.Services.AddScoped<EntitlementsService>();
builder.Services.AddScoped<AdminService>();

// Anti-abuso do cadastro: captcha (Turnstile) + e-mail de verificação
builder.Services.AddHttpClient("turnstile");
builder.Services.AddSingleton(sp => new Atendefy.API.Infrastructure.Security.TurnstileValidator(
    sp.GetRequiredService<IHttpClientFactory>(), turnstileSecret));
builder.Services.AddSingleton(new Atendefy.API.Infrastructure.Email.SmtpSettings(
    Host: builder.Configuration["Email:SmtpHost"] ?? string.Empty,
    Port: builder.Configuration.GetValue("Email:SmtpPort", 587),
    User: builder.Configuration["Email:SmtpUser"] ?? string.Empty,
    Password: builder.Configuration["Email:SmtpPassword"] ?? string.Empty,
    FromAddress: builder.Configuration["Email:FromAddress"] ?? string.Empty,
    FromName: builder.Configuration["Email:FromName"] ?? "Mensagee"));
builder.Services.AddSingleton<Atendefy.API.Infrastructure.Email.IEmailSender,
    Atendefy.API.Infrastructure.Email.SmtpEmailSender>();
builder.Services.AddSingleton<ITenantProvisioner>(_ => new TenantProvisioner(connStr));

// Auth
builder.Services.AddSingleton(new JwtService(jwtSecret, jwtIssuer, jwtAudience));
builder.Services.AddScoped<AuthService>();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opt =>
    {
        opt.MapInboundClaims = false;
        opt.Events = new JwtBearerEvents
        {
            OnMessageReceived = ctx =>
            {
                // SPA: o access token vem em cookie HttpOnly (same-origin via proxy /api),
                // inclusive para SSE — EventSource envia cookies automaticamente.
                // O header Authorization, quando presente (testes, integrações), tem
                // precedência via processamento padrão do JwtBearer.
                if (string.IsNullOrEmpty(ctx.Token) &&
                    !ctx.Request.Headers.ContainsKey("Authorization") &&
                    ctx.Request.Cookies.TryGetValue(AuthCookies.Access, out var cookieToken))
                {
                    ctx.Token = cookieToken;
                }
                return Task.CompletedTask;
            }
        };
        opt.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            ValidateIssuer = true, ValidIssuer = jwtIssuer,
            ValidateAudience = true, ValidAudience = jwtAudience,
            ValidateLifetime = true, ClockSkew = TimeSpan.Zero
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddProblemDetails();

// WhatsApp
builder.Services.AddHttpClient("whatsapp");
builder.Services.AddSingleton<WhatsAppProviderFactory>();
builder.Services.AddSingleton(new Atendefy.API.Modules.WhatsApp.Models.EvolutionServerConfig(
    evolutionBaseUrl, evolutionApiKey, evolutionCallbackUrl));
builder.Services.AddScoped<WhatsAppAccountService>();

// AI
// Permite apontar o provider "openai" para qualquer API compatível com OpenAI
// (Gemini via endpoint OpenAI, Groq, OpenRouter, etc.) sem mexer no código.
var openAiBaseUrl = builder.Configuration["AI:OpenAiBaseUrl"]
    ?? "https://api.openai.com/v1/chat/completions";
builder.Services.AddHttpClient("ai");
builder.Services.AddSingleton(sp => new AIProviderFactory(
    sp.GetRequiredService<IHttpClientFactory>(), openAiBaseUrl));
builder.Services.AddScoped(sp =>
    new AiConfigService(sp.GetRequiredService<TenantDbContextFactory>(), encryptionKey,
        sp.GetRequiredService<RedisService>()));

// Scheduling (agendamento via link — Cal.com/Calendly — e via API — Horafy)
builder.Services.AddScoped(sp =>
    new SchedulingService(sp.GetRequiredService<TenantDbContextFactory>(), encryptionKey));
builder.Services.AddHttpClient("horafy");
builder.Services.AddSingleton<HorafyClient>();
builder.Services.AddSingleton<BookingFlowService>();

// Webhooks
builder.Services.AddSingleton(new MetaWebhookValidator(metaAppSecret));
builder.Services.AddScoped<EvolutionWebhookValidator>();

// Chatbot
builder.Services.AddSingleton<ConversationService>();
builder.Services.AddSingleton<IConversationEventEmitter, ConversationEventEmitter>();
builder.Services.AddHostedService(sp => new ConversationWorker(
    sp.GetRequiredService<RedisStreamService>(),
    sp.GetRequiredService<ConversationService>(),
    sp.GetRequiredService<TenantDbContextFactory>(),
    sp.GetRequiredService<AIProviderFactory>(),
    sp.GetRequiredService<WhatsAppProviderFactory>(),
    sp.GetRequiredService<TenantRateLimiter>(),
    encryptionKey,
    sp.GetRequiredService<IConversationEventEmitter>(),
    sp.GetRequiredService<IServiceScopeFactory>(),
    sp.GetRequiredService<RedisService>(),
    sp.GetRequiredService<BookingFlowService>(),
    sp.GetRequiredService<ILogger<ConversationWorker>>()));

// Billing
builder.Services.AddHttpClient("billing");
builder.Services.AddSingleton<IBillingGatewayFactory>(sp =>
    new BillingGatewayFactory(
        sp.GetRequiredService<IHttpClientFactory>(),
        asaasKey, asaasWebhook, asaasSandbox,
        stripeKey, stripeWebhook));
builder.Services.AddScoped<BillingService>();
builder.Services.AddHostedService<SuspensionWorker>();

// Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// CORS
var allowedOrigins = new List<string> { $"https://app.{baseDomain}" };
if (builder.Environment.IsDevelopment())
    allowedOrigins.Add("http://localhost:5173");

builder.Services.AddCors(opt => opt.AddDefaultPolicy(p => p
    .WithOrigins(allowedOrigins.ToArray())
    .AllowAnyHeader().AllowAnyMethod().AllowCredentials()));

var app = builder.Build();

// Atrás do Caddy: confia no X-Forwarded-For para obter o IP real do cliente (usado no rate-limit
// do cadastro). KnownProxies/Networks limpos = aceita o header do proxy interno.
var fwdOptions = new Microsoft.AspNetCore.Builder.ForwardedHeadersOptions
{
    ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor
};
fwdOptions.KnownIPNetworks.Clear();
fwdOptions.KnownProxies.Clear();
app.UseForwardedHeaders(fwdOptions);

if (!app.Environment.IsEnvironment("Testing"))
{
    app.UseExceptionHandler();
    app.UseStatusCodePages();
}
if (!app.Environment.IsEnvironment("Testing"))
    app.UseSerilogRequestLogging();
app.UseCors();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.Use(async (ctx, next) =>
{
    var resolver = ctx.RequestServices.GetRequiredService<TenantResolver>();
    var tenantId = resolver.Resolve(ctx);
    if (tenantId is not null)
        ctx.Items["TenantId"] = tenantId;
    await next();
});

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }))
   .WithTags("System");

app.MapAuthEndpoints();
app.MapTenantEndpoints();
app.MapAdminEndpoints();
app.MapWhatsAppEndpoints();
app.MapAIEndpoints();
app.MapSchedulingEndpoints();
app.MapWebhookEndpoints();
app.MapBillingEndpoints();
app.MapBillingWebhookEndpoints();
app.MapConversationEndpoints();
app.MapContactEndpoints();
app.MapQuickReplyEndpoints();
app.MapDashboardEndpoints();

if (!app.Environment.IsEnvironment("Testing"))
{
    // Automatic migrations on startup
    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<PublicDbContext>();
        try { await db.Database.MigrateAsync(); }
        catch (Exception ex) { Log.Fatal(ex, "Database migration failed"); throw; }

        // Seed idempotente dos planos Basic/Pro/Premium (só quando não há nenhum plano).
        try
        {
            if (!await db.Plans.AnyAsync())
            {
                db.Plans.AddRange(
                    new Atendefy.API.Modules.Billing.Models.Plan
                    {
                        Name = "Basic", PriceMonthly = 0, PriceYearly = 0, IsActive = true,
                        LimitsJson = new Atendefy.API.Modules.Billing.Models.PlanLimits(
                            MessagesPerMonth: 1000, WhatsAppAccounts: 1, TeamMembers: 1,
                            AiEnabled: true, SchedulingEnabled: false).ToJson()
                    },
                    new Atendefy.API.Modules.Billing.Models.Plan
                    {
                        Name = "Pro", PriceMonthly = 0, PriceYearly = 0, IsActive = true,
                        LimitsJson = new Atendefy.API.Modules.Billing.Models.PlanLimits(
                            MessagesPerMonth: 5000, WhatsAppAccounts: 3, TeamMembers: 5,
                            AiEnabled: true, SchedulingEnabled: true).ToJson()
                    },
                    new Atendefy.API.Modules.Billing.Models.Plan
                    {
                        Name = "Premium", PriceMonthly = 0, PriceYearly = 0, IsActive = true,
                        LimitsJson = new Atendefy.API.Modules.Billing.Models.PlanLimits(
                            MessagesPerMonth: 50000, WhatsAppAccounts: 10, TeamMembers: 20,
                            AiEnabled: true, SchedulingEnabled: true).ToJson()
                    });
                await db.SaveChangesAsync();
                Log.Information("Planos Basic/Pro/Premium criados (seed inicial)");
            }
        }
        catch (Exception ex) { Log.Error(ex, "Falha ao semear planos"); }
    }

    // Tenant schema patches: DDL roda só para tenants novos ou quando o patch mudou
    // (controle por hash em public.tenant_schema_patches) — o boot deixa de ser
    // O(N tenants) no caso comum. Ver TenantSchemaMigrator.
    using (var tenantMigScope = app.Services.CreateScope())
    {
        var publicDb2 = tenantMigScope.ServiceProvider.GetRequiredService<PublicDbContext>();
        var tenants = await publicDb2.Tenants.ToListAsync();
        var schemas = tenants.Select(t => t.SchemaName).ToList();
        await new TenantSchemaMigrator(connStr).RunAsync(schemas);
    }
}

app.Run();

public partial class Program { }
