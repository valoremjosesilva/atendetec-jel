# CLAUDE.md — Guia do Repositório Atendefy / Mensagee

Este arquivo orienta agentes de IA e desenvolvedores sobre a estrutura, convenções e comandos
do repositório.

## Visão geral

**Atendefy** (marca externa: **Mensagee**) é um SaaS multi-tenant de atendimento via WhatsApp
com IA. O código interno usa o nome `Atendefy`; a marca visível ao cliente é `Mensagee`.

Stack: ASP.NET Core .NET 10 (backend) + React 19 + Vite + TypeScript (frontend) + PostgreSQL 16
+ Redis 7. Deploy via Docker Compose em VM Google Cloud. CI via GitHub Actions → imagem GHCR →
deploy por tag `v*`.

## Estrutura de pastas

```
src/Atendefy.API/          # Backend .NET 10 (minimal API, modular)
  Modules/                 # Funcionalidades: AI, Auth, Billing, Chatbot, Scheduling, Tenants, Webhooks, WhatsApp
  Infrastructure/          # Cache (Redis), Database (EF Core + Npgsql), Email, Messaging, RateLimiting, Security
  SharedKernel/            # Result<T>, BaseEntity, AppConstants
  Program.cs               # DI, middleware, registro de endpoints
src/Atendefy.Web/          # Frontend React 19 + Vite + TypeScript
  src/pages/               # Uma página por funcionalidade
  src/hooks/               # TanStack Query hooks (um por domínio)
  src/api/client.ts        # Axios com injeção de JWT
  src/stores/authStore.ts  # Zustand (auth state)
  src/types/api.ts         # Interfaces TypeScript dos responses da API
tests/Atendefy.Tests/      # xUnit + FluentAssertions + NSubstitute
  Integration/             # Testes de integração (WebApplicationFactory + InMemory DB)
  AI/, Auth/, Billing/...  # Testes unitários por módulo
infra/
  docker-compose.yml       # Produção
  docker-compose.override.yml  # Desenvolvimento local
  .env.example             # Variáveis de ambiente necessárias
plans/                     # Planos de melhoria gerados pelo advisor
```

## Comandos essenciais

### Backend (.NET)
```bash
dotnet restore Atendefy.slnx
dotnet build Atendefy.slnx -c Release
dotnet test Atendefy.slnx -c Release
dotnet test Atendefy.slnx -c Release --filter "FullyQualifiedName~NomeDoTeste"
```

### Frontend
```bash
cd src/Atendefy.Web
npm ci                          # instalar dependências
npm run dev                     # servidor de desenvolvimento (porta 3001 no override)
npm run build                   # typecheck (tsc) + bundle (vite)
npx tsc --noEmit                # apenas typecheck sem gerar arquivos
```

### Ambiente local
```bash
cd infra
cp .env.example .env            # editar com valores reais
docker compose up -d --build    # sobe API, Web, Postgres, Redis, Evolution, Caddy
```
Portas de desenvolvimento: API=8084, Web=3001, Postgres=5434, Redis=6381, Evolution=8085.

### Deploy
Push de tag `v*` → GitHub Actions builda imagens → push para GHCR → SSH na VM → `docker compose up -d`.

## Arquitetura crítica

### Multi-tenancy
Cada tenant tem um **schema PostgreSQL próprio** (ex.: `tenant_550e8400e29b...`). O schema é
provisionado na aprovação do cadastro. O `TenantDbContext` é criado dinamicamente via
`TenantDbContextFactory.Create(schemaName)`. O `schemaName` vem do JWT (`tenant_id` claim →
lookup na `PublicDbContext.Tenants`).

**Nunca** passar um `schemaName` de fonte externa sem antes fazer lookup no banco (`PublicDbContext`).

### Dois contextos de banco
- `PublicDbContext`: schema `public` — tenants, planos, assinaturas, usuários, webhook_routes
- `TenantDbContext`: schema per-tenant — conversas, mensagens, contatos, configs de IA, contas WhatsApp

### Processamento de mensagens (caminho crítico)
1. Webhook Meta/Evolution → `WebhookEndpoints` → publica `InboundMessage` no Redis Stream `messages.inbound`
2. `ConversationWorker` (BackgroundService) consome o stream → verifica rate limit → verifica entitlements → busca config IA → chama provider IA → persiste mensagem → envia resposta WhatsApp → emite evento SSE

### Padrão de retorno: Result<T>
Erros de negócio retornam `Result<T>.Fail("mensagem")` — não exceções. Exceções são para falhas
irrecuperáveis de infra. Ver `SharedKernel/Result.cs`.

### Provedores WhatsApp
Dois provedores: `EvolutionProvider` (Baileys — QR code, instância local) e `MetaCloudProvider`
(API oficial Meta — número verificado). Criados via `WhatsAppProviderFactory`. Config por tenant
em `WhatsAppAccount.ConfigJson` (encriptado com AES).

## Convenções de código

- **Endpoints**: minimal API via `MapGroup` + `IEndpointRouteBuilder`. Cada módulo tem `Map{Módulo}Endpoints()`.
- **Serviços**: injeção via primary constructor. Registrados em `Program.cs`.
- **Erros no endpoint**: `Results.Json(new { error = "mensagem" }, statusCode: 401/400/404)`
- **Nomes de chave Redis**: `"entidade:identificador"` — ex.: `"aiconfig:{schemaName}"`, `"ratelimit:{schema}:{janela}"`
- **Testes unitários**: `new ServiçoTestado(deps)` direto, sem WebApplicationFactory
- **Testes de integração**: usar `ApiFactory` com `[Collection("Integration")]`

## Problemas conhecidos / gotchas

- **Evolution API**: versão Baileys é frágil. A versão fixada em `docker-compose.yml` deve ser
  mantida. Não atualizar para `:latest` sem testar QR code.
- **NuGet packages**: pacotes Npgsql, JwtBearer e EFCore.NamingConventions estão na versão 8.x
  enquanto o runtime é .NET 10. Funciona mas é tecnicamente um mismatch. Upgrade planejado.
- **Startup lento**: `Program.cs` roda migrations de schema para todos os tenants no startup.
  Em produção com muitos tenants, isso pode ser lento.
- **Chaves SSH**: `ssh-key-2026-06-10.key` está na raiz — arquivo gitignored mas presente no
  histórico. Não commitar arquivos `.key` ou `.pem`.

## Variáveis de ambiente obrigatórias

Ver `infra/.env.example` para lista completa. As críticas:
- `JWT_SECRET` — mínimo 32 chars
- `ENCRYPTION_KEY` — mínimo 32 chars (usado para encriptar API keys de IA e WhatsApp)
- `POSTGRES_CONNECTION` — connection string PostgreSQL
- `REDIS_CONNECTION` — connection string Redis
- `EVOLUTION_API_KEY` — chave da instância Evolution
- `ADMIN_KEY` — chave para endpoints admin (`X-Admin-Key` header)
