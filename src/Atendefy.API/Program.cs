using Atendefy.API.Infrastructure.Cache;
using Atendefy.API.Infrastructure.Database;
using Atendefy.API.Infrastructure.Messaging;
using Atendefy.API.Infrastructure.RateLimiting;
using Atendefy.API.Modules.AI;
using Atendefy.API.Modules.Auth;
using Atendefy.API.Modules.Chatbot;
using Atendefy.API.Modules.Tenants;
using Atendefy.API.Modules.Webhooks;
using Atendefy.API.Modules.WhatsApp;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using StackExchange.Redis;
using System.Text;

Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateBootstrapLogger();

var builder = WebApplication.CreateBuilder(args);

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
var rateLimit     = builder.Configuration.GetValue<int>("RateLimit:MessagesPerMinute", 60);

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
builder.Services.AddSingleton<ITenantProvisioner>(_ => new TenantProvisioner(connStr));

// Auth
builder.Services.AddSingleton(new JwtService(jwtSecret, jwtIssuer, jwtAudience));
builder.Services.AddScoped<AuthService>();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opt =>
    {
        opt.MapInboundClaims = false;
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
builder.Services.AddScoped<WhatsAppAccountService>();

// AI
builder.Services.AddHttpClient("ai");
builder.Services.AddSingleton<AIProviderFactory>();
builder.Services.AddScoped(sp =>
    new AiConfigService(sp.GetRequiredService<TenantDbContextFactory>(), encryptionKey));

// Webhooks
builder.Services.AddSingleton(new MetaWebhookValidator(metaAppSecret));
builder.Services.AddScoped<EvolutionWebhookValidator>();

// Chatbot
builder.Services.AddSingleton<ConversationService>();
builder.Services.AddHostedService(sp => new ConversationWorker(
    sp.GetRequiredService<RedisStreamService>(),
    sp.GetRequiredService<ConversationService>(),
    sp.GetRequiredService<TenantDbContextFactory>(),
    sp.GetRequiredService<PublicDbContext>(),
    sp.GetRequiredService<AIProviderFactory>(),
    sp.GetRequiredService<WhatsAppProviderFactory>(),
    sp.GetRequiredService<TenantRateLimiter>(),
    encryptionKey,
    sp.GetRequiredService<ILogger<ConversationWorker>>()));

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

app.UseExceptionHandler();
app.UseStatusCodePages();
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
app.MapWhatsAppEndpoints();
app.MapAIEndpoints();
app.MapWebhookEndpoints();

// Automatic migrations on startup
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<PublicDbContext>();
    try { await db.Database.MigrateAsync(); }
    catch (Exception ex) { Log.Fatal(ex, "Database migration failed"); throw; }
}

app.Run();

public partial class Program { }
