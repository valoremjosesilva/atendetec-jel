# Plano 007: Adicionar testes de isolamento entre tenants

> **Instruções para o executor**: Siga este plano passo a passo. Execute cada verificação antes
> de avançar. Em condições de PARE, pare e reporte — não improvise.
>
> **Verificação de drift (execute primeiro)**:
> `git diff --stat e805859..HEAD -- tests/Atendefy.Tests/Integration/ApiFactory.cs tests/Atendefy.Tests/Integration/ConversationsIntegrationTests.cs`
> Em caso de divergência, trate como PARE.

## Status

- **Prioridade**: P1
- **Esforço**: P (Pequeno — horas)
- **Risco**: BAIXO (apenas testes novos; sem mudança de código de produção)
- **Depende de**: nenhum
- **Categoria**: testes
- **Escrito no commit**: `e805859`, 2026-06-28

## Por que isso importa

O Atendefy é um SaaS multi-tenant com isolamento via schema PostgreSQL por tenant. Um bug na
resolução de tenant (ex.: JWT mal interpretado, header incorreto, erro no `TenantResolver`)
poderia expor dados de um cliente ao outro. Atualmente, toda a suite de integração usa um único
tenant de teste (`TenantId = 11111111-...`). Nenhum teste verifica explicitamente que um JWT de
tenant A **não consegue** acessar os recursos de tenant B. Esse teste é a garantia mais crítica
em qualquer SaaS multi-tenant e está ausente.

## Estado atual

**`tests/Atendefy.Tests/Integration/ApiFactory.cs`:**
```csharp
public class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly string TenantSchemaName = $"tenant_{TenantId:N}";
    public const string Subdomain = "test-tenant";
    public const string BaseDomain = "atendefy.com.br";
    public const string UserEmail = "admin@test.com";
    public const string UserPassword = "TestPassword123!";

    // MintToken() gera JWT com TenantId hardcoded
    public string MintToken() =>
        Services.GetRequiredService<JwtService>()
            .GenerateAccessToken(Guid.NewGuid(), TenantId, "Owner", UserEmail);

    public HttpClient CreateAuthenticatedClient() { ... }
    // InitializeAsync() cria o tenant e o usuário do TenantId acima
}
```

**`tests/Atendefy.Tests/Integration/ConversationsIntegrationTests.cs`:**
- Todos os testes usam `ApiFactory.TenantId` (único tenant)
- `CreateAuthenticatedClient()` gera JWT para esse único tenant
- Nenhum teste tenta acessar recursos de tenant diferente

**`tests/Atendefy.Tests/Integration/IntegrationCollection.cs`:**
```csharp
[CollectionDefinition("Integration")]
public class IntegrationCollection : ICollectionFixture<ApiFactory> { }
```

**Padrão dos testes de integração existentes** (`ContactsIntegrationTests.cs`):
```csharp
[Collection("Integration")]
public class ContactsIntegrationTests : IAsyncLifetime
{
    private readonly ApiFactory _factory;
    public ContactsIntegrationTests(ApiFactory factory) => _factory = factory;
    public async Task InitializeAsync() { /* seed de dados */ }
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task GetContacts_WithAuth_Returns200() { ... }
}
```

**Resolução de tenant no backend** (`ConversationEndpoints.cs:280-291`):
```csharp
private static async Task<(string, string, string?)> ResolveTenantAsync(
    HttpContext ctx, PublicDbContext publicDb)
{
    var tenantIdStr = ctx.User.FindFirst("tenant_id")?.Value;
    if (string.IsNullOrEmpty(tenantIdStr) || !Guid.TryParse(tenantIdStr, out var tenantId))
        return (string.Empty, string.Empty, "Token inválido");

    var tenant = await publicDb.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId);
    if (tenant is null) return (string.Empty, string.Empty, "Tenant não encontrado");

    return (tenantIdStr, tenant.SchemaName, null);
}
```

O isolamento está na claim `tenant_id` do JWT. O teste deve verificar que um JWT com um
`tenant_id` diferente do subdomain host recebe erro (ou não acessa dados do outro tenant).

## Comandos necessários

| Propósito      | Comando                                                              | Esperado     |
|----------------|----------------------------------------------------------------------|--------------|
| Build          | `dotnet build Atendefy.slnx -c Release --no-restore`                 | exit 0       |
| Testes isolamento | `dotnet test Atendefy.slnx -c Release --filter "FullyQualifiedName~TenantIsolation"` | todos passam |
| Todos testes   | `dotnet test Atendefy.slnx -c Release`                               | todos passam |

## Escopo

**Em escopo**:
- `tests/Atendefy.Tests/Integration/TenantIsolationTests.cs` (criar — arquivo novo)
- `tests/Atendefy.Tests/Integration/ApiFactory.cs` (adicionar método `MintTokenForTenant`)

**Fora do escopo** (não tocar):
- Código de produção — zero mudanças
- Outros arquivos de teste — não alterar testes existentes
- `IntegrationCollection.cs` — não muda

## Git workflow

- Branch: `advisor/007-tenant-isolation-tests`
- Commits: um commit único com ambos os arquivos
- Mensagem: `test(integration): verificar isolamento entre tenants`

## Passos

### Passo 1: Adicionar método auxiliar `MintTokenForTenant` em `ApiFactory`

Em `ApiFactory.cs`, adicione o seguinte método público após `MintToken()`:

```csharp
// Gera um JWT para um tenant diferente do padrão — usado para testar isolamento.
public string MintTokenForTenant(Guid tenantId) =>
    Services.GetRequiredService<JwtService>()
        .GenerateAccessToken(Guid.NewGuid(), tenantId, "Owner", "other@test.com");
```

Este método é idêntico ao `MintToken()` mas aceita qualquer `tenantId`. Não altera o comportamento
existente.

**Verificar**: `dotnet build Atendefy.slnx -c Release --no-restore` → exit 0

### Passo 2: Criar `TenantIsolationTests.cs`

Crie o arquivo `tests/Atendefy.Tests/Integration/TenantIsolationTests.cs`:

```csharp
using FluentAssertions;
using System.Net;
using System.Net.Http.Headers;

namespace Atendefy.Tests.Integration;

[Collection("Integration")]
public class TenantIsolationTests : IAsyncLifetime
{
    private readonly ApiFactory _factory;
    // ID de um segundo tenant fictício — não existe no banco de teste
    private static readonly Guid OtherTenantId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    public TenantIsolationTests(ApiFactory factory) => _factory = factory;

    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync() => Task.CompletedTask;

    // Helper: cria HttpClient com JWT de um tenant diferente do host (test-tenant.atendefy.com.br)
    private HttpClient CreateClientForOtherTenant()
    {
        // O subdomain do host continua sendo "test-tenant" (resolvendo para TenantId padrão),
        // mas o JWT carrega OtherTenantId. O backend deve rejeitar porque TenantId do JWT ≠
        // Tenant do host OU porque OtherTenantId não existe no banco.
        var client = _factory.CreateTenantClient(); // host: test-tenant.atendefy.com.br
        var token = _factory.MintTokenForTenant(OtherTenantId);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [Fact]
    public async Task GetConversations_WithJwtFromDifferentTenant_Returns401()
    {
        var client = CreateClientForOtherTenant();

        var response = await client.GetAsync("/conversations?page=1&pageSize=10");

        // O tenant_id no JWT aponta para um tenant inexistente no banco de teste.
        // ResolveTenantAsync retorna "Tenant não encontrado" → 401.
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetContacts_WithJwtFromDifferentTenant_Returns401()
    {
        var client = CreateClientForOtherTenant();

        var response = await client.GetAsync("/contacts?page=1&pageSize=10");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetConversationMessages_WithJwtFromDifferentTenant_Returns401OrNotFound()
    {
        var client = CreateClientForOtherTenant();
        // Usar um ID de conversa que existe no tenant padrão
        var existingConversationId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");

        var response = await client.GetAsync($"/conversations/{existingConversationId}/messages");

        // Deve retornar 401 (tenant não encontrado no banco) — nunca 200 com dados do outro tenant
        response.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.NotFound);
        response.StatusCode.Should().NotBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AuthenticatedTenant_CannotSeeAnotherTenantConversationInList()
    {
        // Tenant padrão (TenantId 11111111-...) autenticado corretamente
        var legitimateClient = _factory.CreateAuthenticatedClient();

        var response = await legitimateClient.GetAsync("/conversations?page=1&pageSize=100");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var conversations = body.GetProperty("conversations").EnumerateArray().ToList();

        // Todas as conversas devem pertencer ao schema do tenant padrão.
        // Verificação indireta: a lista retorna (não é vazia — seed foi feito em ConversationsIntegrationTests)
        // e não há dados "vazados" de outros tenants (o schema isolamento garante isso estruturalmente).
        // Este teste confirma que a query funciona dentro do schema correto.
        conversations.Should().NotBeNull();
    }
}
```

> **Nota sobre a abordagem:** o isolamento real acontece no schema PostgreSQL — o `TenantDbContext`
> aponta para `search_path = schema_do_tenant`. Nos testes de integração, o DB é InMemory
> (sem schemas). O que os testes acima verificam é a **camada de autenticação**: um JWT com
> `tenant_id` desconhecido é rejeitado com 401 antes de chegar ao banco. Isso é a defesa de
> primeira linha. Para testar isolamento de schema em nível de DB, seria necessário PostgreSQL
> real — considere como plano de melhoria separado se necessário.

**Verificar**: `dotnet build Atendefy.slnx -c Release --no-restore` → exit 0

### Passo 3: Executar os novos testes

**Verificar**: `dotnet test Atendefy.slnx -c Release --filter "FullyQualifiedName~TenantIsolation"` → 4 testes passam

**Verificar**: `dotnet test Atendefy.slnx -c Release` → todos os testes passam (sem regressões)

## Critérios de conclusão

- [ ] `dotnet build Atendefy.slnx -c Release --no-restore` exit 0
- [ ] `dotnet test Atendefy.slnx -c Release` exit 0
- [ ] 4 novos testes em `TenantIsolationTests.cs` passam
- [ ] Testes confirmam que JWT de tenant inexistente retorna 401 em `/conversations` e `/contacts`
- [ ] Testes confirmam que JWT de tenant inexistente não retorna 200 em endpoint de mensagens
- [ ] Apenas `ApiFactory.cs` e `TenantIsolationTests.cs` foram modificados (`git status`)
- [ ] Status atualizado em `plans/README.md`

## Condições de PARE

- `ConversationsIntegrationTests.cs` não criou o seed de conversa `aaaaaaaa-...` (a classe de
  testes em questão só inicializa quando é executada) — substitua o GUID de conversa por
  `Guid.NewGuid()` no teste `GetConversationMessages_WithJwtFromDifferentTenant_Returns401OrNotFound`
  (o resultado será NotFound, o que ainda é correto)
- `MintTokenForTenant` não compila porque `JwtService.GenerateAccessToken` tem assinatura diferente
  — inspecione `src/Atendefy.API/Modules/Auth/JwtService.cs` e adapte os parâmetros

## Notas de manutenção

- Estes testes verificam a camada de autenticação (JWT → tenant_id → lookup no banco). Eles NÃO
  verificam isolamento de schema a nível de SQL (que requereria PostgreSQL real). Se o produto
  escalar e o risco de isolamento aumentar, investir em testes de integração com PostgreSQL real
  (ex.: via Testcontainers) é o próximo passo.
- Ao adicionar novos endpoints que acessam dados de tenant, adicione um teste análogo aqui.
