# Atendefy — Plano 1: Foundation, Auth & Tenants

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Scaffold completo do projeto ASP.NET Core 8, infraestrutura Docker Compose, banco PostgreSQL multi-schema, JWT auth e provisionamento automático de tenants — base para todos os módulos subsequentes.

**Architecture:** Monolito Modular com Vertical Slice. Cada módulo tem seus próprios endpoints, serviços e modelos. Multi-tenancy via schema PostgreSQL por tenant (`tenant_{Id:N}`). Tenant resolvido por subdomínio ou header `X-Tenant-Key`. Tenant middleware injeta o tenantId em `HttpContext.Items["TenantId"]`.

**Tech Stack:** .NET 8, ASP.NET Core 8 Minimal APIs, EF Core 8 + Npgsql, StackExchange.Redis, Microsoft.AspNetCore.Authentication.JwtBearer, BCrypt.Net-Next, Serilog, xUnit + FluentAssertions + NSubstitute

---

## Planos Subsequentes

Este plano entrega a base. Os próximos planos dependem dele:

| Plano | Arquivo | Conteúdo |
|---|---|---|
| Plano 2 | `2026-06-04-atendefy-plano2-whatsapp-ai.md` | WhatsApp Gateway (Meta + Evolution), AI Provider, Conversation Engine |
| Plano 3 | `2026-06-04-atendefy-plano3-billing.md` | Billing Module, Asaas, Stripe |
| Plano 4 | `2026-06-04-atendefy-plano4-frontend.md` | React + Vite SPA, todas as telas |

---

## Mapa de Arquivos

```
Atendefy/
├── src/
│   └── Atendefy.API/
│       ├── Modules/
│       │   ├── Auth/
│       │   │   ├── AuthEndpoints.cs
│       │   │   ├── AuthService.cs
│       │   │   ├── JwtService.cs
│       │   │   └── Models/
│       │   │       ├── LoginRequest.cs
│       │   │       └── AuthResponse.cs
│       │   └── Tenants/
│       │       ├── TenantEndpoints.cs
│       │       ├── TenantService.cs
│       │       ├── ITenantProvisioner.cs
│       │       ├── TenantProvisioner.cs
│       │       └── Models/
│       │           ├── Tenant.cs
│       │           ├── TenantUser.cs
│       │           └── RegisterTenantRequest.cs
│       ├── Infrastructure/
│       │   ├── Database/
│       │   │   ├── PublicDbContext.cs
│       │   │   ├── TenantDbContext.cs
│       │   │   ├── TenantResolver.cs
│       │   │   └── Migrations/
│       │   ├── Cache/
│       │   │   └── RedisService.cs
│       │   └── Messaging/
│       │       └── RedisStreamService.cs
│       ├── SharedKernel/
│       │   ├── Result.cs
│       │   ├── BaseEntity.cs
│       │   └── Extensions/
│       │       └── AesEncryption.cs
│       ├── Program.cs
│       ├── appsettings.json
│       ├── appsettings.Development.json
│       └── Dockerfile
├── tests/
│   └── Atendefy.Tests/
│       ├── SharedKernel/
│       │   └── ResultTests.cs
│       ├── Auth/
│       │   ├── JwtServiceTests.cs
│       │   └── AuthServiceTests.cs
│       ├── Tenants/
│       │   └── TenantServiceTests.cs
│       └── Infrastructure/
│           ├── TenantResolverTests.cs
│           └── RedisServiceTests.cs
├── infra/
│   ├── docker-compose.yml
│   ├── docker-compose.override.yml
│   ├── Caddyfile
│   └── .env.example
├── .github/
│   └── workflows/
│       ├── ci.yml
│       └── deploy.yml
└── Atendefy.sln
```

---

## Task 1: Scaffold da Solution .NET

**Files:**
- Create: `Atendefy.sln`
- Create: `src/Atendefy.API/Atendefy.API.csproj`
- Create: `tests/Atendefy.Tests/Atendefy.Tests.csproj`

- [ ] **Step 1: Criar solution e projetos**

```bash
mkdir -p src tests infra
dotnet new sln -n Atendefy
dotnet new webapi -n Atendefy.API -o src/Atendefy.API
dotnet new xunit -n Atendefy.Tests -o tests/Atendefy.Tests
dotnet sln add src/Atendefy.API/Atendefy.API.csproj
dotnet sln add tests/Atendefy.Tests/Atendefy.Tests.csproj
cd tests/Atendefy.Tests && dotnet add reference ../../src/Atendefy.API/Atendefy.API.csproj && cd ../..
```

- [ ] **Step 2: Adicionar pacotes NuGet ao API**

```bash
cd src/Atendefy.API
dotnet add package Microsoft.AspNetCore.Authentication.JwtBearer --version 8.0.0
dotnet add package Npgsql.EntityFrameworkCore.PostgreSQL --version 8.0.4
dotnet add package Microsoft.EntityFrameworkCore.Design --version 8.0.4
dotnet add package StackExchange.Redis --version 2.8.0
dotnet add package BCrypt.Net-Next --version 4.0.3
dotnet add package Serilog.AspNetCore --version 8.0.2
dotnet add package Serilog.Sinks.File --version 5.0.0
dotnet add package Serilog.Sinks.Console --version 5.0.1
dotnet add package System.IdentityModel.Tokens.Jwt --version 7.5.0
dotnet add package Microsoft.IdentityModel.Tokens --version 7.5.0
dotnet add package Swashbuckle.AspNetCore --version 6.6.2
cd ../..
```

- [ ] **Step 3: Adicionar pacotes NuGet aos Tests**

```bash
cd tests/Atendefy.Tests
dotnet add package FluentAssertions --version 6.12.0
dotnet add package NSubstitute --version 5.1.0
dotnet add package Microsoft.AspNetCore.Mvc.Testing --version 8.0.0
dotnet add package Microsoft.EntityFrameworkCore.InMemory --version 8.0.4
dotnet add package coverlet.collector --version 6.0.2
cd ../..
```

- [ ] **Step 4: Criar estrutura de diretórios**

```bash
cd src/Atendefy.API
mkdir -p Modules/Auth/Models
mkdir -p Modules/Tenants/Models
mkdir -p Infrastructure/Database/Migrations
mkdir -p Infrastructure/Cache
mkdir -p Infrastructure/Messaging
mkdir -p SharedKernel/Extensions
cd ../..
```

- [ ] **Step 5: Criar .gitignore e verificar build**

```bash
cat > .gitignore << 'EOF'
bin/
obj/
*.user
.env
logs/
*.log
.vs/
EOF
dotnet build Atendefy.sln
```

Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 6: Commit**

```bash
git init
git add .
git commit -m "feat: scaffold solution with API and test projects"
```

---

## Task 2: SharedKernel

**Files:**
- Create: `src/Atendefy.API/SharedKernel/Result.cs`
- Create: `src/Atendefy.API/SharedKernel/BaseEntity.cs`
- Create: `src/Atendefy.API/SharedKernel/Extensions/AesEncryption.cs`
- Test: `tests/Atendefy.Tests/SharedKernel/ResultTests.cs`

- [ ] **Step 1: Escrever testes para Result<T>**

Criar `tests/Atendefy.Tests/SharedKernel/ResultTests.cs`:

```csharp
using Atendefy.API.SharedKernel;
using FluentAssertions;

namespace Atendefy.Tests.SharedKernel;

public class ResultTests
{
    [Fact]
    public void Ok_ShouldReturnSuccessResult()
    {
        var result = Result<string>.Ok("hello");
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("hello");
        result.Error.Should().BeNull();
    }

    [Fact]
    public void Fail_ShouldReturnFailureResult()
    {
        var result = Result<string>.Fail("something went wrong");
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("something went wrong");
    }

    [Fact]
    public void Ok_WithNoValue_ShouldReturnSuccess()
    {
        var result = Result.Ok();
        result.IsSuccess.Should().BeTrue();
    }
}
```

- [ ] **Step 2: Rodar testes (esperar falha)**

```bash
dotnet test tests/Atendefy.Tests --filter "FullyQualifiedName~ResultTests"
```

Expected: FAIL — namespace `Atendefy.API.SharedKernel` não encontrado

- [ ] **Step 3: Implementar Result<T>**

Criar `src/Atendefy.API/SharedKernel/Result.cs`:

```csharp
namespace Atendefy.API.SharedKernel;

public class Result
{
    public bool IsSuccess { get; protected init; }
    public string? Error { get; protected init; }

    public static Result Ok() => new() { IsSuccess = true };
    public static Result Fail(string error) => new() { IsSuccess = false, Error = error };
}

public class Result<T> : Result
{
    public T? Value { get; private init; }

    public static Result<T> Ok(T value) => new() { IsSuccess = true, Value = value };
    public new static Result<T> Fail(string error) => new() { IsSuccess = false, Error = error };
}
```

- [ ] **Step 4: Implementar BaseEntity**

Criar `src/Atendefy.API/SharedKernel/BaseEntity.cs`:

```csharp
namespace Atendefy.API.SharedKernel;

public abstract class BaseEntity
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public bool IsDeleted { get; set; } = false;
    public DateTime? DeletedAt { get; set; }
}
```

- [ ] **Step 5: Implementar AES-256 encryption**

Criar `src/Atendefy.API/SharedKernel/Extensions/AesEncryption.cs`:

```csharp
using System.Security.Cryptography;
using System.Text;

namespace Atendefy.API.SharedKernel.Extensions;

public static class AesEncryption
{
    public static string Encrypt(string plainText, string key)
    {
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
        var keyBytes = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        var fullBytes = Convert.FromBase64String(cipherText);
        using var aes = Aes.Create();
        aes.Key = keyBytes;
        var iv = fullBytes[..16];
        var cipher = fullBytes[16..];
        aes.IV = iv;
        using var decryptor = aes.CreateDecryptor();
        var plainBytes = decryptor.TransformFinalBlock(cipher, 0, cipher.Length);
        return Encoding.UTF8.GetString(plainBytes);
    }
}
```

- [ ] **Step 6: Rodar testes**

```bash
dotnet test tests/Atendefy.Tests --filter "FullyQualifiedName~ResultTests"
```

Expected: PASS — 3 tests passed

- [ ] **Step 7: Commit**

```bash
git add src/Atendefy.API/SharedKernel/ tests/Atendefy.Tests/SharedKernel/
git commit -m "feat: add SharedKernel with Result pattern, BaseEntity and AES-256 encryption"
```

---

## Task 3: Docker Compose + Caddy

**Files:**
- Create: `infra/docker-compose.yml`
- Create: `infra/docker-compose.override.yml`
- Create: `infra/Caddyfile`
- Create: `infra/.env.example`
- Create: `src/Atendefy.API/Dockerfile`

- [ ] **Step 1: Criar docker-compose.yml**

Criar `infra/docker-compose.yml`:

```yaml
version: "3.9"

services:
  caddy:
    image: caddy:2-alpine
    restart: unless-stopped
    ports:
      - "80:80"
      - "443:443"
    volumes:
      - ./Caddyfile:/etc/caddy/Caddyfile:ro
      - caddy_data:/data
      - caddy_config:/config
    networks:
      - atendefy

  atendefy-api:
    image: ghcr.io/atendefy/api:${API_VERSION:-latest}
    restart: unless-stopped
    environment:
      - ASPNETCORE_ENVIRONMENT=Production
      - ASPNETCORE_URLS=http://+:8080
      - ConnectionStrings__Postgres=${POSTGRES_CONNECTION}
      - ConnectionStrings__Redis=${REDIS_CONNECTION}
      - Jwt__Secret=${JWT_SECRET}
      - Encryption__Key=${ENCRYPTION_KEY}
      - App__BaseDomain=${DOMAIN}
    depends_on:
      postgres:
        condition: service_healthy
      redis:
        condition: service_healthy
    networks:
      - atendefy

  postgres:
    image: postgres:16-alpine
    restart: unless-stopped
    environment:
      POSTGRES_DB: atendefy
      POSTGRES_USER: ${POSTGRES_USER}
      POSTGRES_PASSWORD: ${POSTGRES_PASSWORD}
    volumes:
      - postgres_data:/var/lib/postgresql/data
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U ${POSTGRES_USER} -d atendefy"]
      interval: 10s
      timeout: 5s
      retries: 5
    networks:
      - atendefy

  redis:
    image: redis:7-alpine
    restart: unless-stopped
    command: redis-server --requirepass ${REDIS_PASSWORD}
    volumes:
      - redis_data:/data
    healthcheck:
      test: ["CMD", "redis-cli", "-a", "${REDIS_PASSWORD}", "ping"]
      interval: 10s
      timeout: 5s
      retries: 5
    networks:
      - atendefy

  evolution-api:
    image: atendai/evolution-api:latest
    restart: unless-stopped
    environment:
      - SERVER_URL=https://evolution.${DOMAIN}
      - AUTHENTICATION_API_KEY=${EVOLUTION_API_KEY}
      - DATABASE_ENABLED=false
    networks:
      - atendefy

  uptime-kuma:
    image: louislam/uptime-kuma:1
    restart: unless-stopped
    volumes:
      - uptime_data:/app/data
    networks:
      - atendefy

volumes:
  postgres_data:
  redis_data:
  caddy_data:
  caddy_config:
  uptime_data:

networks:
  atendefy:
    driver: bridge
```

- [ ] **Step 2: Criar docker-compose.override.yml (desenvolvimento local)**

Criar `infra/docker-compose.override.yml`:

```yaml
version: "3.9"

services:
  postgres:
    ports:
      - "5432:5432"

  redis:
    ports:
      - "6379:6379"

  atendefy-api:
    build:
      context: ..
      dockerfile: src/Atendefy.API/Dockerfile
    environment:
      - ASPNETCORE_ENVIRONMENT=Development
      - ASPNETCORE_URLS=http://+:8080
    ports:
      - "8080:8080"
    volumes:
      - ../logs:/app/logs
```

- [ ] **Step 3: Criar Caddyfile**

Criar `infra/Caddyfile`:

```
api.{$DOMAIN} {
    reverse_proxy atendefy-api:8080
}

app.{$DOMAIN} {
    root * /srv/web
    file_server
    try_files {path} /index.html
}

evolution.{$DOMAIN} {
    reverse_proxy evolution-api:8080
}

monitor.{$DOMAIN} {
    reverse_proxy uptime-kuma:3001
}
```

- [ ] **Step 4: Criar .env.example**

Criar `infra/.env.example`:

```env
DOMAIN=atendefy.com.br
POSTGRES_USER=atendefy
POSTGRES_PASSWORD=change_me_strong_password
POSTGRES_CONNECTION=Host=postgres;Database=atendefy;Username=atendefy;Password=change_me_strong_password
REDIS_PASSWORD=change_me_redis_password
REDIS_CONNECTION=redis://:change_me_redis_password@redis:6379
JWT_SECRET=change_me_minimum_32_chars_secret_key_here_!!!
ENCRYPTION_KEY=change_me_minimum_32_chars_encryption_key_here
EVOLUTION_API_KEY=change_me_evolution_api_key
API_VERSION=latest
```

- [ ] **Step 5: Criar Dockerfile**

Criar `src/Atendefy.API/Dockerfile`:

```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["src/Atendefy.API/Atendefy.API.csproj", "src/Atendefy.API/"]
RUN dotnet restore "src/Atendefy.API/Atendefy.API.csproj"
COPY . .
WORKDIR "/src/src/Atendefy.API"
RUN dotnet publish -c Release -o /app/publish --no-restore

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Atendefy.API.dll"]
```

- [ ] **Step 6: Verificar que o docker-compose é válido**

```bash
cd infra
cp .env.example .env
docker compose config --quiet
cd ..
```

Expected: sem erros de sintaxe

- [ ] **Step 7: Commit**

```bash
git add infra/ src/Atendefy.API/Dockerfile
git commit -m "feat: add Docker Compose, Caddyfile and multi-stage Dockerfile"
```

---

## Task 4: Database Infrastructure (EF Core + Multi-Tenancy)

**Files:**
- Create: `src/Atendefy.API/Modules/Tenants/Models/Tenant.cs`
- Create: `src/Atendefy.API/Modules/Tenants/Models/TenantUser.cs`
- Create: `src/Atendefy.API/Infrastructure/Database/PublicDbContext.cs`
- Create: `src/Atendefy.API/Infrastructure/Database/TenantDbContext.cs`
- Create: `src/Atendefy.API/Infrastructure/Database/TenantResolver.cs`
- Test: `tests/Atendefy.Tests/Infrastructure/TenantResolverTests.cs`

- [ ] **Step 1: Escrever testes para TenantResolver**

Criar `tests/Atendefy.Tests/Infrastructure/TenantResolverTests.cs`:

```csharp
using Atendefy.API.Infrastructure.Database;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using NSubstitute;

namespace Atendefy.Tests.Infrastructure;

public class TenantResolverTests
{
    [Fact]
    public void Resolve_FromSubdomain_ShouldReturnSubdomainAsId()
    {
        var context = Substitute.For<HttpContext>();
        var request = Substitute.For<HttpRequest>();
        request.Host.Returns(new HostString("acme.atendefy.com.br"));
        request.Headers.Returns(new HeaderDictionary());
        context.Request.Returns(request);

        var resolver = new TenantResolver("atendefy.com.br");
        var tenantId = resolver.Resolve(context);

        tenantId.Should().Be("acme");
    }

    [Fact]
    public void Resolve_FromHeader_WhenNotTenantSubdomain_ShouldReturnHeaderValue()
    {
        var context = Substitute.For<HttpContext>();
        var request = Substitute.For<HttpRequest>();
        request.Host.Returns(new HostString("api.atendefy.com.br"));
        request.Headers.Returns(new HeaderDictionary { { "X-Tenant-Key", "tenant_xyz" } });
        context.Request.Returns(request);

        var resolver = new TenantResolver("atendefy.com.br");
        var tenantId = resolver.Resolve(context);

        tenantId.Should().Be("tenant_xyz");
    }

    [Fact]
    public void Resolve_PlatformSubdomain_ShouldFallbackToHeader()
    {
        var context = Substitute.For<HttpContext>();
        var request = Substitute.For<HttpRequest>();
        request.Host.Returns(new HostString("api.atendefy.com.br"));
        request.Headers.Returns(new HeaderDictionary());
        context.Request.Returns(request);

        var resolver = new TenantResolver("atendefy.com.br");
        var tenantId = resolver.Resolve(context);

        tenantId.Should().BeNull();
    }
}
```

- [ ] **Step 2: Rodar testes (esperar falha)**

```bash
dotnet test tests/Atendefy.Tests --filter "FullyQualifiedName~TenantResolverTests"
```

Expected: FAIL

- [ ] **Step 3: Criar modelos de Tenant**

Criar `src/Atendefy.API/Modules/Tenants/Models/Tenant.cs`:

```csharp
using Atendefy.API.SharedKernel;

namespace Atendefy.API.Modules.Tenants.Models;

public class Tenant : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public string Status { get; set; } = "active"; // active | suspended | cancelled
    public Guid? PlanId { get; set; }
    public string SchemaName => $"tenant_{Id:N}";
}
```

Criar `src/Atendefy.API/Modules/Tenants/Models/TenantUser.cs`:

```csharp
using Atendefy.API.SharedKernel;

namespace Atendefy.API.Modules.Tenants.Models;

public class TenantUser : BaseEntity
{
    public Guid TenantId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string Role { get; set; } = "Owner"; // Owner | Admin | Viewer
    public string Name { get; set; } = string.Empty;
}
```

- [ ] **Step 4: Criar PublicDbContext**

Criar `src/Atendefy.API/Infrastructure/Database/PublicDbContext.cs`:

```csharp
using Atendefy.API.Modules.Tenants.Models;
using Microsoft.EntityFrameworkCore;

namespace Atendefy.API.Infrastructure.Database;

public class PublicDbContext(DbContextOptions<PublicDbContext> options) : DbContext(options)
{
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<TenantUser> TenantUsers => Set<TenantUser>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("public");

        modelBuilder.Entity<Tenant>(e =>
        {
            e.ToTable("tenants");
            e.HasKey(x => x.Id);
            e.Property(x => x.Subdomain).IsRequired().HasMaxLength(100);
            e.HasIndex(x => x.Subdomain).IsUnique();
            e.Property(x => x.Name).IsRequired().HasMaxLength(200);
            e.Property(x => x.Status).HasMaxLength(50);
            e.Ignore(x => x.SchemaName);
            e.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<TenantUser>(e =>
        {
            e.ToTable("tenant_users");
            e.HasKey(x => x.Id);
            e.Property(x => x.Email).IsRequired().HasMaxLength(200);
            e.HasIndex(x => new { x.TenantId, x.Email }).IsUnique();
            e.Property(x => x.Role).HasMaxLength(50);
            e.HasQueryFilter(x => !x.IsDeleted);
        });
    }
}
```

- [ ] **Step 5: Criar TenantDbContext**

Criar `src/Atendefy.API/Infrastructure/Database/TenantDbContext.cs`:

```csharp
using Microsoft.EntityFrameworkCore;

namespace Atendefy.API.Infrastructure.Database;

public class TenantDbContext(DbContextOptions<TenantDbContext> options, string schema) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(schema);
        // Entidades do tenant são adicionadas via SQL direto no TenantProvisioner (Plano 1 Task 7)
        // Entidades EF adicionais serão mapeadas nos módulos do Plano 2
    }
}
```

- [ ] **Step 6: Criar TenantResolver**

Criar `src/Atendefy.API/Infrastructure/Database/TenantResolver.cs`:

```csharp
using Microsoft.AspNetCore.Http;

namespace Atendefy.API.Infrastructure.Database;

public class TenantResolver(string baseDomain)
{
    private static readonly HashSet<string> PlatformSubdomains =
        new(StringComparer.OrdinalIgnoreCase) { "api", "app", "www", "evolution", "monitor" };

    public string? Resolve(HttpContext context)
    {
        var host = context.Request.Host.Host;

        if (host.EndsWith($".{baseDomain}", StringComparison.OrdinalIgnoreCase))
        {
            var subdomain = host[..^(baseDomain.Length + 1)];
            if (!PlatformSubdomains.Contains(subdomain))
                return subdomain;
        }

        if (context.Request.Headers.TryGetValue("X-Tenant-Key", out var key)
            && !string.IsNullOrWhiteSpace(key))
            return key.ToString();

        return null;
    }
}
```

- [ ] **Step 7: Rodar testes**

```bash
dotnet test tests/Atendefy.Tests --filter "FullyQualifiedName~TenantResolverTests"
```

Expected: PASS — 3 tests passed

- [ ] **Step 8: Commit**

```bash
git add src/Atendefy.API/Infrastructure/Database/ src/Atendefy.API/Modules/Tenants/Models/
git add tests/Atendefy.Tests/Infrastructure/TenantResolverTests.cs
git commit -m "feat: add EF Core multi-schema database infrastructure and TenantResolver"
```

---

## Task 5: Redis Infrastructure

**Files:**
- Create: `src/Atendefy.API/Infrastructure/Cache/RedisService.cs`
- Create: `src/Atendefy.API/Infrastructure/Messaging/RedisStreamService.cs`
- Test: `tests/Atendefy.Tests/Infrastructure/RedisServiceTests.cs`

- [ ] **Step 1: Escrever testes para RedisService**

Criar `tests/Atendefy.Tests/Infrastructure/RedisServiceTests.cs`:

```csharp
using Atendefy.API.Infrastructure.Cache;
using FluentAssertions;
using NSubstitute;
using StackExchange.Redis;

namespace Atendefy.Tests.Infrastructure;

public class RedisServiceTests
{
    private readonly IDatabase _db = Substitute.For<IDatabase>();
    private readonly RedisService _sut;

    public RedisServiceTests()
    {
        var connection = Substitute.For<IConnectionMultiplexer>();
        connection.GetDatabase().Returns(_db);
        _sut = new RedisService(connection);
    }

    [Fact]
    public async Task SetAsync_ShouldCallRedisWithCorrectArguments()
    {
        _db.StringSetAsync(Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), Arg.Any<TimeSpan?>())
           .Returns(true);

        await _sut.SetAsync("key:test", "value", TimeSpan.FromMinutes(30));

        await _db.Received(1).StringSetAsync(
            "key:test", "value", TimeSpan.FromMinutes(30));
    }

    [Fact]
    public async Task GetAsync_WhenKeyExists_ShouldReturnValue()
    {
        _db.StringGetAsync("key:test").Returns(new RedisValue("hello"));

        var result = await _sut.GetAsync("key:test");

        result.Should().Be("hello");
    }

    [Fact]
    public async Task GetAsync_WhenKeyMissing_ShouldReturnNull()
    {
        _db.StringGetAsync("key:missing").Returns(RedisValue.Null);

        var result = await _sut.GetAsync("key:missing");

        result.Should().BeNull();
    }
}
```

- [ ] **Step 2: Rodar testes (esperar falha)**

```bash
dotnet test tests/Atendefy.Tests --filter "FullyQualifiedName~RedisServiceTests"
```

Expected: FAIL

- [ ] **Step 3: Implementar RedisService**

Criar `src/Atendefy.API/Infrastructure/Cache/RedisService.cs`:

```csharp
using StackExchange.Redis;

namespace Atendefy.API.Infrastructure.Cache;

public class RedisService(IConnectionMultiplexer connection)
{
    private readonly IDatabase _db = connection.GetDatabase();

    public async Task SetAsync(string key, string value, TimeSpan? expiry = null)
        => await _db.StringSetAsync(key, value, expiry);

    public async Task<string?> GetAsync(string key)
    {
        var value = await _db.StringGetAsync(key);
        return value.IsNull ? null : value.ToString();
    }

    public async Task DeleteAsync(string key)
        => await _db.KeyDeleteAsync(key);

    public async Task<bool> ExistsAsync(string key)
        => await _db.KeyExistsAsync(key);

    public async Task IncrementAsync(string key, long by = 1)
        => await _db.StringIncrementAsync(key, by);

    public async Task<long> GetCounterAsync(string key)
    {
        var value = await _db.StringGetAsync(key);
        return value.IsNull ? 0 : (long)value;
    }
}
```

- [ ] **Step 4: Implementar RedisStreamService**

Criar `src/Atendefy.API/Infrastructure/Messaging/RedisStreamService.cs`:

```csharp
using StackExchange.Redis;

namespace Atendefy.API.Infrastructure.Messaging;

public class RedisStreamService(IConnectionMultiplexer connection)
{
    private readonly IDatabase _db = connection.GetDatabase();

    public async Task PublishAsync(string stream, Dictionary<string, string> fields)
    {
        var entries = fields.Select(f => new NameValueEntry(f.Key, f.Value)).ToArray();
        await _db.StreamAddAsync(stream, entries);
    }

    public async Task<StreamEntry[]> ReadGroupAsync(string stream, string group, string consumer, int count = 10)
        => await _db.StreamReadGroupAsync(stream, group, consumer, ">", count);

    public async Task AcknowledgeAsync(string stream, string group, RedisValue messageId)
        => await _db.StreamAcknowledgeAsync(stream, group, messageId);

    public async Task EnsureConsumerGroupAsync(string stream, string group)
    {
        try
        {
            await _db.StreamCreateConsumerGroupAsync(stream, group, StreamPosition.Beginning);
        }
        catch (RedisServerException ex) when (ex.Message.Contains("BUSYGROUP"))
        {
            // grupo já existe — esperado
        }
    }
}
```

- [ ] **Step 5: Rodar testes**

```bash
dotnet test tests/Atendefy.Tests --filter "FullyQualifiedName~RedisServiceTests"
```

Expected: PASS — 3 tests passed

- [ ] **Step 6: Commit**

```bash
git add src/Atendefy.API/Infrastructure/Cache/ src/Atendefy.API/Infrastructure/Messaging/
git add tests/Atendefy.Tests/Infrastructure/RedisServiceTests.cs
git commit -m "feat: add Redis cache service and stream messaging infrastructure"
```

---

## Task 6: Auth Module (JWT)

**Files:**
- Create: `src/Atendefy.API/Modules/Auth/Models/LoginRequest.cs`
- Create: `src/Atendefy.API/Modules/Auth/Models/AuthResponse.cs`
- Create: `src/Atendefy.API/Modules/Auth/JwtService.cs`
- Create: `src/Atendefy.API/Modules/Auth/AuthService.cs`
- Create: `src/Atendefy.API/Modules/Auth/AuthEndpoints.cs`
- Test: `tests/Atendefy.Tests/Auth/JwtServiceTests.cs`
- Test: `tests/Atendefy.Tests/Auth/AuthServiceTests.cs`

- [ ] **Step 1: Escrever testes para JwtService**

Criar `tests/Atendefy.Tests/Auth/JwtServiceTests.cs`:

```csharp
using Atendefy.API.Modules.Auth;
using FluentAssertions;

namespace Atendefy.Tests.Auth;

public class JwtServiceTests
{
    private readonly JwtService _sut =
        new("test_secret_key_minimum_32_chars_!!!", "Atendefy", "atendefy.com.br");

    [Fact]
    public void GenerateAccessToken_ShouldReturnNonEmptyToken()
    {
        var token = _sut.GenerateAccessToken(Guid.NewGuid(), Guid.NewGuid(), "Owner");
        token.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void GenerateRefreshToken_ShouldReturn64ByteBase64String()
    {
        var token = _sut.GenerateRefreshToken();
        var bytes = Convert.FromBase64String(token);
        bytes.Length.Should().Be(64);
    }

    [Fact]
    public void ValidateToken_WithValidToken_ShouldReturnPrincipalWithClaims()
    {
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var token = _sut.GenerateAccessToken(userId, tenantId, "Admin");

        var principal = _sut.ValidateToken(token);

        principal.Should().NotBeNull();
        principal!.FindFirst("sub")!.Value.Should().Be(userId.ToString());
        principal!.FindFirst("tenant_id")!.Value.Should().Be(tenantId.ToString());
    }

    [Fact]
    public void ValidateToken_WithInvalidToken_ShouldReturnNull()
    {
        var principal = _sut.ValidateToken("not.a.valid.token");
        principal.Should().BeNull();
    }
}
```

- [ ] **Step 2: Rodar testes (esperar falha)**

```bash
dotnet test tests/Atendefy.Tests --filter "FullyQualifiedName~JwtServiceTests"
```

Expected: FAIL

- [ ] **Step 3: Criar modelos Auth**

Criar `src/Atendefy.API/Modules/Auth/Models/LoginRequest.cs`:

```csharp
namespace Atendefy.API.Modules.Auth.Models;

public record LoginRequest(string Email, string Password);
public record RefreshRequest(string RefreshToken);
```

Criar `src/Atendefy.API/Modules/Auth/Models/AuthResponse.cs`:

```csharp
namespace Atendefy.API.Modules.Auth.Models;

public record AuthResponse(
    string AccessToken,
    string RefreshToken,
    DateTime ExpiresAt,
    string TenantId,
    string UserId,
    string Role
);
```

- [ ] **Step 4: Implementar JwtService**

Criar `src/Atendefy.API/Modules/Auth/JwtService.cs`:

```csharp
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace Atendefy.API.Modules.Auth;

public class JwtService(string secret, string issuer, string audience)
{
    private readonly SymmetricSecurityKey _key = new(Encoding.UTF8.GetBytes(secret));

    public string GenerateAccessToken(Guid userId, Guid tenantId, string role)
    {
        var claims = new[]
        {
            new Claim("sub", userId.ToString()),
            new Claim("tenant_id", tenantId.ToString()),
            new Claim(ClaimTypes.Role, role),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(15),
            signingCredentials: new SigningCredentials(_key, SecurityAlgorithms.HmacSha256)
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public string GenerateRefreshToken()
        => Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));

    public ClaimsPrincipal? ValidateToken(string token)
    {
        var parameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = _key,
            ValidateIssuer = true,
            ValidIssuer = issuer,
            ValidateAudience = true,
            ValidAudience = audience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero,
        };

        try
        {
            return new JwtSecurityTokenHandler().ValidateToken(token, parameters, out _);
        }
        catch
        {
            return null;
        }
    }
}
```

- [ ] **Step 5: Escrever testes para AuthService**

> **Nota de design:** `TenantResolver.Resolve()` retorna o subdomínio como string (ex: `"clinica-abc"`), não um Guid. `AuthService` recebe essa string e busca o tenant pelo subdomínio no banco. O JWT gerado embute o `Guid` do tenant nos claims — requests autenticados subsequentes usam o claim `tenant_id` do JWT.

Criar `tests/Atendefy.Tests/Auth/AuthServiceTests.cs`:

```csharp
using Atendefy.API.Infrastructure.Database;
using Atendefy.API.Modules.Auth;
using Atendefy.API.Modules.Auth.Models;
using Atendefy.API.Modules.Tenants.Models;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Atendefy.Tests.Auth;

public class AuthServiceTests
{
    private static PublicDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<PublicDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static JwtService CreateJwt() =>
        new("test_secret_key_minimum_32_chars_!!!", "Atendefy", "atendefy.com.br");

    [Fact]
    public async Task Login_WithValidCredentials_ShouldReturnAuthResponse()
    {
        var db = CreateDb();
        var tenantId = Guid.NewGuid();
        db.Tenants.Add(new Tenant { Id = tenantId, Name = "Clínica ABC", Subdomain = "clinica-abc" });
        db.TenantUsers.Add(new TenantUser
        {
            TenantId = tenantId,
            Email = "user@test.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("password123"),
            Role = "Owner",
            Name = "Test User"
        });
        await db.SaveChangesAsync();

        var result = await new AuthService(db, CreateJwt())
            .LoginAsync(new LoginRequest("user@test.com", "password123"), "clinica-abc");

        result.IsSuccess.Should().BeTrue();
        result.Value!.AccessToken.Should().NotBeNullOrEmpty();
        result.Value.Role.Should().Be("Owner");
        result.Value.TenantId.Should().Be(tenantId.ToString());
    }

    [Fact]
    public async Task Login_WithWrongPassword_ShouldReturnFail()
    {
        var db = CreateDb();
        var tenantId = Guid.NewGuid();
        db.Tenants.Add(new Tenant { Id = tenantId, Name = "Test", Subdomain = "test-co" });
        db.TenantUsers.Add(new TenantUser
        {
            TenantId = tenantId,
            Email = "user@test.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("correct"),
            Role = "Owner",
            Name = "Test"
        });
        await db.SaveChangesAsync();

        var result = await new AuthService(db, CreateJwt())
            .LoginAsync(new LoginRequest("user@test.com", "wrong"), "test-co");

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("Email ou senha inválidos");
    }

    [Fact]
    public async Task Login_WithUnknownSubdomain_ShouldReturnFail()
    {
        var db = CreateDb();

        var result = await new AuthService(db, CreateJwt())
            .LoginAsync(new LoginRequest("user@test.com", "any"), "unknown-tenant");

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("Tenant não encontrado");
    }
}
```

- [ ] **Step 6: Implementar AuthService**

Criar `src/Atendefy.API/Modules/Auth/AuthService.cs`:

```csharp
using Atendefy.API.Infrastructure.Database;
using Atendefy.API.Modules.Auth.Models;
using Atendefy.API.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Atendefy.API.Modules.Auth;

public class AuthService(PublicDbContext db, JwtService jwtService)
{
    // tenantIdentifier = subdomínio (ex: "clinica-abc") resolvido pelo TenantResolver
    public async Task<Result<AuthResponse>> LoginAsync(LoginRequest request, string tenantIdentifier)
    {
        var tenant = await db.Tenants
            .FirstOrDefaultAsync(t => t.Subdomain == tenantIdentifier);

        if (tenant is null)
            return Result<AuthResponse>.Fail("Tenant não encontrado");

        var user = await db.TenantUsers
            .FirstOrDefaultAsync(u => u.TenantId == tenant.Id
                                   && u.Email == request.Email.ToLowerInvariant());

        if (user is null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            return Result<AuthResponse>.Fail("Email ou senha inválidos");

        var accessToken = jwtService.GenerateAccessToken(user.Id, tenant.Id, user.Role);
        var refreshToken = jwtService.GenerateRefreshToken();

        return Result<AuthResponse>.Ok(new AuthResponse(
            AccessToken: accessToken,
            RefreshToken: refreshToken,
            ExpiresAt: DateTime.UtcNow.AddMinutes(15),
            TenantId: tenant.Id.ToString(),
            UserId: user.Id.ToString(),
            Role: user.Role
        ));
    }
}
```

- [ ] **Step 7: Criar AuthEndpoints**

Criar `src/Atendefy.API/Modules/Auth/AuthEndpoints.cs`:

```csharp
using Atendefy.API.Modules.Auth.Models;
using Microsoft.AspNetCore.Mvc;

namespace Atendefy.API.Modules.Auth;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/auth").WithTags("Auth");

        group.MapPost("/login", async (
            [FromBody] LoginRequest request,
            AuthService authService,
            HttpContext ctx) =>
        {
            // TenantId é o subdomínio resolvido pelo TenantResolver middleware
            var tenantIdentifier = ctx.Items["TenantId"]?.ToString();
            if (string.IsNullOrEmpty(tenantIdentifier))
                return Results.Unauthorized();

            var result = await authService.LoginAsync(request, tenantIdentifier);
            return result.IsSuccess
                ? Results.Ok(result.Value)
                : Results.Unauthorized();
        });

        return app;
    }
}
```

- [ ] **Step 8: Rodar todos os testes Auth**

```bash
dotnet test tests/Atendefy.Tests --filter "FullyQualifiedName~Auth"
```

Expected: PASS — 7 tests passed (4 JwtService + 3 AuthService)

- [ ] **Step 9: Commit**

```bash
git add src/Atendefy.API/Modules/Auth/ tests/Atendefy.Tests/Auth/
git commit -m "feat: add JWT auth module with login endpoint and token validation"
```

---

## Task 7: Tenants Module (Cadastro + Provisionamento)

**Files:**
- Create: `src/Atendefy.API/Modules/Tenants/Models/RegisterTenantRequest.cs`
- Create: `src/Atendefy.API/Modules/Tenants/ITenantProvisioner.cs`
- Create: `src/Atendefy.API/Modules/Tenants/TenantProvisioner.cs`
- Create: `src/Atendefy.API/Modules/Tenants/TenantService.cs`
- Create: `src/Atendefy.API/Modules/Tenants/TenantEndpoints.cs`
- Test: `tests/Atendefy.Tests/Tenants/TenantServiceTests.cs`

- [ ] **Step 1: Escrever testes para TenantService**

Criar `tests/Atendefy.Tests/Tenants/TenantServiceTests.cs`:

```csharp
using Atendefy.API.Infrastructure.Database;
using Atendefy.API.Modules.Tenants;
using Atendefy.API.Modules.Tenants.Models;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Atendefy.Tests.Tenants;

public class TenantServiceTests
{
    private static PublicDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<PublicDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    [Fact]
    public async Task Register_WithValidData_ShouldCreateTenantAndOwner()
    {
        var db = CreateDb();
        var provisioner = Substitute.For<ITenantProvisioner>();
        var sut = new TenantService(db, provisioner);

        var result = await sut.RegisterAsync(new RegisterTenantRequest(
            CompanyName: "Clínica ABC",
            Subdomain: "clinica-abc",
            OwnerName: "Dr. João",
            OwnerEmail: "joao@clinica.com",
            OwnerPassword: "Senha@123"
        ));

        result.IsSuccess.Should().BeTrue();
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Subdomain == "clinica-abc");
        tenant.Should().NotBeNull();
        tenant!.Name.Should().Be("Clínica ABC");
        var owner = await db.TenantUsers.FirstOrDefaultAsync(u => u.TenantId == tenant.Id);
        owner!.Role.Should().Be("Owner");
        await provisioner.Received(1).ProvisionSchemaAsync(Arg.Any<string>());
    }

    [Fact]
    public async Task Register_WithDuplicateSubdomain_ShouldFailWithMessage()
    {
        var db = CreateDb();
        db.Tenants.Add(new Tenant { Name = "Existing", Subdomain = "existing" });
        await db.SaveChangesAsync();

        var provisioner = Substitute.For<ITenantProvisioner>();
        var result = await new TenantService(db, provisioner)
            .RegisterAsync(new RegisterTenantRequest("Other", "existing", "User", "u@t.com", "P@ss1"));

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("subdomínio");
    }

    [Fact]
    public async Task Register_ShouldNormalizeSubdomainToLowercase()
    {
        var db = CreateDb();
        var provisioner = Substitute.For<ITenantProvisioner>();
        var sut = new TenantService(db, provisioner);

        await sut.RegisterAsync(new RegisterTenantRequest(
            "Test", "MiNhAEmPrEsA", "User", "u@t.com", "P@ss1"));

        var tenant = await db.Tenants.FirstAsync();
        tenant.Subdomain.Should().Be("minhaempresa");
    }
}
```

- [ ] **Step 2: Rodar testes (esperar falha)**

```bash
dotnet test tests/Atendefy.Tests --filter "FullyQualifiedName~TenantServiceTests"
```

Expected: FAIL

- [ ] **Step 3: Criar modelos e interface**

Criar `src/Atendefy.API/Modules/Tenants/Models/RegisterTenantRequest.cs`:

```csharp
namespace Atendefy.API.Modules.Tenants.Models;

public record RegisterTenantRequest(
    string CompanyName,
    string Subdomain,
    string OwnerName,
    string OwnerEmail,
    string OwnerPassword
);
```

Criar `src/Atendefy.API/Modules/Tenants/ITenantProvisioner.cs`:

```csharp
namespace Atendefy.API.Modules.Tenants;

public interface ITenantProvisioner
{
    Task ProvisionSchemaAsync(string schemaName);
}
```

- [ ] **Step 4: Implementar TenantProvisioner**

Criar `src/Atendefy.API/Modules/Tenants/TenantProvisioner.cs`:

```csharp
using Npgsql;

namespace Atendefy.API.Modules.Tenants;

public class TenantProvisioner(string connectionString) : ITenantProvisioner
{
    public async Task ProvisionSchemaAsync(string schemaName)
    {
        // schemaName vem de Tenant.SchemaName = $"tenant_{Id:N}" — derivado de GUID, nunca do usuário
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        var sql = $"""
            CREATE SCHEMA IF NOT EXISTS "{schemaName}";

            CREATE TABLE IF NOT EXISTS "{schemaName}".whatsapp_accounts (
                id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                provider VARCHAR(50) NOT NULL,
                phone VARCHAR(20),
                config_json JSONB,
                status VARCHAR(50) DEFAULT 'disconnected',
                created_at TIMESTAMPTZ DEFAULT NOW(),
                updated_at TIMESTAMPTZ,
                is_deleted BOOLEAN DEFAULT FALSE
            );

            CREATE TABLE IF NOT EXISTS "{schemaName}".ai_configs (
                id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                provider VARCHAR(50) NOT NULL,
                api_key_encrypted TEXT,
                model VARCHAR(100),
                system_prompt TEXT,
                created_at TIMESTAMPTZ DEFAULT NOW(),
                updated_at TIMESTAMPTZ
            );

            CREATE TABLE IF NOT EXISTS "{schemaName}".conversations (
                id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                contact_phone VARCHAR(30) NOT NULL,
                started_at TIMESTAMPTZ DEFAULT NOW(),
                message_count INT DEFAULT 0,
                is_deleted BOOLEAN DEFAULT FALSE
            );

            CREATE TABLE IF NOT EXISTS "{schemaName}".messages (
                id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                conversation_id UUID REFERENCES "{schemaName}".conversations(id),
                role VARCHAR(20) NOT NULL,
                content TEXT NOT NULL,
                tokens_used INT DEFAULT 0,
                created_at TIMESTAMPTZ DEFAULT NOW()
            );

            CREATE TABLE IF NOT EXISTS "{schemaName}".usage_counters (
                month VARCHAR(7) PRIMARY KEY,
                messages_sent INT DEFAULT 0,
                tokens_consumed BIGINT DEFAULT 0,
                cost_usd DECIMAL(10,4) DEFAULT 0
            );
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }
}
```

- [ ] **Step 5: Implementar TenantService**

Criar `src/Atendefy.API/Modules/Tenants/TenantService.cs`:

```csharp
using Atendefy.API.Infrastructure.Database;
using Atendefy.API.Modules.Tenants.Models;
using Atendefy.API.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Atendefy.API.Modules.Tenants;

public class TenantService(PublicDbContext db, ITenantProvisioner provisioner)
{
    public async Task<Result<Tenant>> RegisterAsync(RegisterTenantRequest request)
    {
        var subdomain = request.Subdomain.ToLowerInvariant().Trim();

        if (await db.Tenants.AnyAsync(t => t.Subdomain == subdomain))
            return Result<Tenant>.Fail($"O subdomínio '{subdomain}' já está em uso");

        var tenant = new Tenant { Name = request.CompanyName, Subdomain = subdomain };
        db.Tenants.Add(tenant);

        db.TenantUsers.Add(new TenantUser
        {
            TenantId = tenant.Id,
            Name = request.OwnerName,
            Email = request.OwnerEmail.ToLowerInvariant().Trim(),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.OwnerPassword),
            Role = "Owner"
        });

        await db.SaveChangesAsync();
        await provisioner.ProvisionSchemaAsync(tenant.SchemaName);

        return Result<Tenant>.Ok(tenant);
    }
}
```

- [ ] **Step 6: Criar TenantEndpoints**

Criar `src/Atendefy.API/Modules/Tenants/TenantEndpoints.cs`:

```csharp
using Atendefy.API.Modules.Tenants.Models;
using Microsoft.AspNetCore.Mvc;

namespace Atendefy.API.Modules.Tenants;

public static class TenantEndpoints
{
    public static IEndpointRouteBuilder MapTenantEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/tenants").WithTags("Tenants");

        group.MapPost("/register", async (
            [FromBody] RegisterTenantRequest request,
            TenantService tenantService) =>
        {
            var result = await tenantService.RegisterAsync(request);
            return result.IsSuccess
                ? Results.Created($"/tenants/{result.Value!.Id}",
                    new { result.Value.Id, result.Value.Subdomain, result.Value.Name })
                : Results.BadRequest(new { error = result.Error });
        });

        return app;
    }
}
```

- [ ] **Step 7: Rodar testes**

```bash
dotnet test tests/Atendefy.Tests --filter "FullyQualifiedName~TenantServiceTests"
```

Expected: PASS — 3 tests passed

- [ ] **Step 8: Commit**

```bash
git add src/Atendefy.API/Modules/Tenants/ tests/Atendefy.Tests/Tenants/
git commit -m "feat: add Tenants module with registration and PostgreSQL schema auto-provisioning"
```

---

## Task 8: Program.cs + appsettings + Migrations

**Files:**
- Modify: `src/Atendefy.API/Program.cs`
- Create: `src/Atendefy.API/appsettings.json`
- Create: `src/Atendefy.API/appsettings.Development.json`

- [ ] **Step 1: Criar appsettings.json**

Criar `src/Atendefy.API/appsettings.json`:

```json
{
  "Serilog": {
    "MinimumLevel": {
      "Default": "Information",
      "Override": {
        "Microsoft": "Warning",
        "System": "Warning"
      }
    },
    "WriteTo": [
      { "Name": "Console" },
      {
        "Name": "File",
        "Args": {
          "path": "logs/atendefy-.log",
          "rollingInterval": "Day",
          "retainedFileCountLimit": 30
        }
      }
    ]
  },
  "ConnectionStrings": {
    "Postgres": "Host=localhost;Database=atendefy;Username=atendefy;Password=dev_password",
    "Redis": "localhost:6379"
  },
  "Jwt": {
    "Secret": "change_me_minimum_32_chars_secret_key_here_!!!",
    "Issuer": "Atendefy",
    "Audience": "atendefy.com.br"
  },
  "Encryption": {
    "Key": "change_me_minimum_32_chars_encryption_key_here"
  },
  "App": {
    "BaseDomain": "atendefy.com.br"
  }
}
```

Criar `src/Atendefy.API/appsettings.Development.json`:

```json
{
  "Serilog": {
    "MinimumLevel": { "Default": "Debug" }
  },
  "App": {
    "BaseDomain": "localhost"
  }
}
```

- [ ] **Step 2: Reescrever Program.cs**

Substituir o conteúdo de `src/Atendefy.API/Program.cs`:

```csharp
using Atendefy.API.Infrastructure.Cache;
using Atendefy.API.Infrastructure.Database;
using Atendefy.API.Infrastructure.Messaging;
using Atendefy.API.Modules.Auth;
using Atendefy.API.Modules.Tenants;
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

var connStr     = builder.Configuration.GetConnectionString("Postgres")!;
var redisConn   = builder.Configuration.GetConnectionString("Redis")!;
var jwtSecret   = builder.Configuration["Jwt:Secret"]!;
var jwtIssuer   = builder.Configuration["Jwt:Issuer"]!;
var jwtAudience = builder.Configuration["Jwt:Audience"]!;
var baseDomain  = builder.Configuration["App:BaseDomain"]!;

// Database
builder.Services.AddDbContext<PublicDbContext>(opt => opt.UseNpgsql(connStr));

// Redis
builder.Services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisConn));
builder.Services.AddSingleton<RedisService>();
builder.Services.AddSingleton<RedisStreamService>();

// Tenant
builder.Services.AddSingleton(new TenantResolver(baseDomain));
builder.Services.AddScoped<TenantService>();
builder.Services.AddSingleton<ITenantProvisioner>(_ => new TenantProvisioner(connStr));

// Auth
builder.Services.AddSingleton(new JwtService(jwtSecret, jwtIssuer, jwtAudience));
builder.Services.AddScoped<AuthService>();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opt => opt.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
        ValidateIssuer = true, ValidIssuer = jwtIssuer,
        ValidateAudience = true, ValidAudience = jwtAudience,
        ValidateLifetime = true, ClockSkew = TimeSpan.Zero
    });
builder.Services.AddAuthorization();

// Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// CORS
builder.Services.AddCors(opt => opt.AddDefaultPolicy(p => p
    .WithOrigins($"https://app.{baseDomain}", "http://localhost:5173")
    .AllowAnyHeader().AllowAnyMethod().AllowCredentials()));

var app = builder.Build();

app.UseSerilogRequestLogging();
app.UseCors();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthentication();
app.UseAuthorization();

// Resolve tenant e injeta no contexto
app.Use(async (ctx, next) =>
{
    var resolver = ctx.RequestServices.GetRequiredService<TenantResolver>();
    var tenantId = resolver.Resolve(ctx);
    if (tenantId is not null)
        ctx.Items["TenantId"] = tenantId;
    await next();
});

app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }))
   .WithTags("System");

app.MapAuthEndpoints();
app.MapTenantEndpoints();

// Migrations automáticas
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<PublicDbContext>();
    await db.Database.MigrateAsync();
}

app.Run();

public partial class Program { }
```

- [ ] **Step 3: Gerar migration inicial**

```bash
cd src/Atendefy.API
dotnet ef migrations add InitialCreate --output-dir Infrastructure/Database/Migrations
cd ../..
```

Expected: `Build succeeded. Done.`

- [ ] **Step 4: Rodar build completo**

```bash
dotnet build Atendefy.sln
```

Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 5: Rodar todos os testes**

```bash
dotnet test Atendefy.sln
```

Expected: todos os testes passam (ResultTests + TenantResolverTests + RedisServiceTests + JwtServiceTests + AuthServiceTests + TenantServiceTests)

- [ ] **Step 6: Commit**

```bash
git add src/Atendefy.API/Program.cs src/Atendefy.API/appsettings*.json
git add src/Atendefy.API/Infrastructure/Database/Migrations/
git commit -m "feat: wire up Program.cs, Serilog, Swagger and EF Core initial migration"
```

---

## Task 9: CI/CD — GitHub Actions

**Files:**
- Create: `.github/workflows/ci.yml`
- Create: `.github/workflows/deploy.yml`

- [ ] **Step 1: Criar workflow de CI**

Criar `.github/workflows/ci.yml`:

```yaml
name: CI

on:
  push:
    branches: [main, develop]
  pull_request:
    branches: [main]

jobs:
  test:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4

      - name: Setup .NET 8
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: "8.0.x"

      - name: Restore
        run: dotnet restore Atendefy.sln

      - name: Build
        run: dotnet build Atendefy.sln --no-restore -c Release

      - name: Test
        run: dotnet test Atendefy.sln --no-build -c Release --logger trx --collect:"XPlat Code Coverage"

      - name: Upload coverage
        uses: codecov/codecov-action@v4
        with:
          token: ${{ secrets.CODECOV_TOKEN }}
          fail_ci_if_error: false
```

- [ ] **Step 2: Criar workflow de deploy**

Criar `.github/workflows/deploy.yml`:

```yaml
name: Deploy

on:
  push:
    tags:
      - "v*"

env:
  REGISTRY: ghcr.io
  IMAGE_NAME: ${{ github.repository }}/api

jobs:
  build-push:
    runs-on: ubuntu-latest
    permissions:
      contents: read
      packages: write
    steps:
      - uses: actions/checkout@v4

      - name: Login to GHCR
        uses: docker/login-action@v3
        with:
          registry: ${{ env.REGISTRY }}
          username: ${{ github.actor }}
          password: ${{ secrets.GITHUB_TOKEN }}

      - name: Extract metadata
        id: meta
        uses: docker/metadata-action@v5
        with:
          images: ${{ env.REGISTRY }}/${{ env.IMAGE_NAME }}
          tags: |
            type=semver,pattern={{version}}
            type=semver,pattern={{major}}.{{minor}}
            type=raw,value=latest

      - name: Build and push
        uses: docker/build-push-action@v5
        with:
          context: .
          file: src/Atendefy.API/Dockerfile
          push: true
          tags: ${{ steps.meta.outputs.tags }}

  deploy:
    needs: build-push
    runs-on: ubuntu-latest
    steps:
      - name: Deploy via SSH
        uses: appleboy/ssh-action@v1
        with:
          host: ${{ secrets.VPS_HOST }}
          username: ${{ secrets.VPS_USER }}
          key: ${{ secrets.VPS_SSH_KEY }}
          script: |
            cd /opt/atendefy
            export API_VERSION=${{ github.ref_name }}
            docker compose pull atendefy-api
            docker compose up -d atendefy-api
            docker image prune -f
```

- [ ] **Step 3: Adicionar secrets necessários ao repositório GitHub**

No painel do repositório → Settings → Secrets → Actions, adicionar:

| Secret | Valor |
|---|---|
| `VPS_HOST` | IP público do servidor Hetzner |
| `VPS_USER` | usuário SSH (ex: `root` ou `deploy`) |
| `VPS_SSH_KEY` | conteúdo da chave privada SSH (`~/.ssh/id_rsa`) |
| `CODECOV_TOKEN` | token do codecov.io (opcional, para coverage) |

- [ ] **Step 4: Commit**

```bash
git add .github/
git commit -m "feat: add GitHub Actions CI and tag-based deploy to Hetzner VPS"
```

---

## Verificação Final do Plano 1

Após completar as 9 tasks, verificar:

- [ ] `dotnet test Atendefy.sln` — todos os testes passam
- [ ] `docker compose -f infra/docker-compose.yml -f infra/docker-compose.override.yml up -d` — todos os containers sobem sem erro
- [ ] `curl http://localhost:8080/health` retorna `{"status":"healthy"}`
- [ ] `POST http://localhost:8080/tenants/register` cria tenant e schema no PostgreSQL
- [ ] `POST http://localhost:8080/auth/login` retorna JWT válido
- [ ] Swagger acessível em `http://localhost:8080/swagger`

---

## Próximos Planos

Após aprovação do Plano 1:

**Plano 2 — WhatsApp Gateway + AI + Conversation Engine**
- `IWhatsAppProvider` + `MetaCloudProvider` + `EvolutionProvider`
- Webhook handler com validação HMAC-SHA256
- `IAIProvider` + `OpenAIProvider` + `AnthropicProvider` + `GeminiProvider`
- `ConversationWorker` (IHostedService consumindo Redis Stream)
- Rate limiting por tenant

**Plano 3 — Billing Module**
- Entidades `Plan`, `Subscription`, `Invoice`
- Limites via `limits_json`
- Webhooks Asaas + Stripe
- Suspensão automática por inadimplência

**Plano 4 — Frontend React**
- Projeto React + Vite + TypeScript + shadcn/ui
- Todas as telas: Dashboard, WhatsApp, Chatbot, IA, Conversas, Billing
- Painel Super Admin
- Onboarding wizard
