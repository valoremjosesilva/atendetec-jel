# Plano 008: Criar CLAUDE.md com guia para agentes executores

> **Instruções para o executor**: Este plano cria um arquivo novo. Não há drift a verificar
> (o arquivo não existe). Apenas crie o arquivo conforme especificado e verifique que o projeto
> ainda builda.

## Status

- **Prioridade**: P2
- **Esforço**: P (Pequeno — horas)
- **Risco**: BAIXO (arquivo puramente aditivo)
- **Depende de**: nenhum (pode rodar em qualquer ordem)
- **Categoria**: DX / tooling
- **Escrito no commit**: `e805859`, 2026-06-28

## Por que isso importa

O Claude Code e outros agentes executores leem `CLAUDE.md` na raiz do repositório antes de
qualquer sessão de trabalho. Sem ele, cada agente começa do zero: precisa explorar a estrutura
de pastas, inferir convenções, descobrir os comandos de build/test e adivinhar onde a lógica
crítica está. Isso desperdiça tokens, aumenta a probabilidade de erros e faz com que instruções
deste advisor precisem repetir contexto básico a cada plano. Um bom `CLAUDE.md` é investimento
que amortiza em todos os planos subsequentes.

## Estado atual

Não existe `CLAUDE.md` na raiz do repositório. O arquivo `README.md` existe e contém informações
de produto/deploy, mas não está estruturado para consumo por agentes.

## Comandos necessários

| Propósito   | Comando                                              | Esperado |
|-------------|------------------------------------------------------|----------|
| Build .NET  | `dotnet build Atendefy.slnx -c Release --no-restore` | exit 0   |

## Escopo

**Em escopo**:
- `CLAUDE.md` (criar na raiz do repositório — junto com `README.md` e `Atendefy.slnx`)

**Fora do escopo**:
- `README.md` — não alterar
- Qualquer arquivo de código-fonte

## Git workflow

- Branch: `advisor/008-claude-md`
- Commit: `docs: adicionar CLAUDE.md com guia para agentes e desenvolvedores`
- Não fazer push nem abrir PR, a menos que instruído.

## Passos

### Passo 1: Criar `CLAUDE.md` na raiz do repositório

Crie o arquivo `CLAUDE.md` com o seguinte conteúdo (adapte qualquer informação que esteja
desatualizada em relação ao estado atual do projeto):

```markdown
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
  docker-compose.override.yml # Desenvolvimento local
  .env.example             # Variáveis de ambiente necessárias
docs/superpowers/plans/    # Planos das 9 fases de implementação (referência histórica)
plans/                     # Planos de melhoria gerados pelo advisor (este arquivo está aqui)
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

- **Endpoints**: minimal API via `MapGroup` + `IEndpointRouteBuilder`. Cada módulo tem
  `Map{Módulo}Endpoints()`.
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
```

**Verificar**: `dotnet build Atendefy.slnx -c Release --no-restore` → exit 0 (arquivo `.md`
não afeta build .NET)

## Critérios de conclusão

- [ ] `CLAUDE.md` existe na raiz do repositório (mesma pasta que `README.md`)
- [ ] `dotnet build Atendefy.slnx -c Release --no-restore` exit 0
- [ ] O arquivo tem as seções: visão geral, estrutura de pastas, comandos, arquitetura, convenções, gotchas
- [ ] Nenhum valor de secret está no arquivo (apenas descrições de variáveis)
- [ ] Apenas `CLAUDE.md` foi criado (`git status`)
- [ ] Status atualizado em `plans/README.md`

## Condições de PARE

- Qualquer informação de arquitetura parece ter mudado significativamente desde o commit
  `e805859` (ex.: módulos novos adicionados, porta diferente) — atualize o conteúdo antes de
  commitar

## Notas de manutenção

- Atualizar `CLAUDE.md` quando:
  - Novos módulos são adicionados em `src/Atendefy.API/Modules/`
  - Comandos de build/test mudam
  - Um gotcha novo é descoberto em produção
  - A versão da Evolution API é atualizada
- O arquivo deve ser lido no início de cada sessão de trabalho com agentes. Se estiver desatualizado,
  o custo é apenas confusão — não falha de build. Mas confusão tem custo real.
