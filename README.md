# Atendefy

Plataforma **SaaS multi-tenant** que permite a pequenas e médias empresas implantarem **chatbots inteligentes no WhatsApp** sem manter uma equipe de atendimento 24h. Cada cliente (tenant) conecta seu número de WhatsApp, configura uma IA (OpenAI/Anthropic) e gerencia as conversas por um painel web, com cobrança recorrente integrada.

- **Modelo de negócio:** SaaS multi-tenant (assinatura mensal, R$ 100–2.000/cliente)
- **Nichos-alvo:** clínicas, imobiliárias, escritórios contábeis, advogados, oficinas
- **Status atual:** funcionalidades das 9 fases implementadas; pronto para rodar local. Deploy em produção (Google Cloud) ainda não executado.

---

## Sumário

- [Stack](#stack)
- [Arquitetura](#arquitetura)
- [Estrutura do repositório](#estrutura-do-repositório)
- [Rodando localmente](#rodando-localmente)
- [Configuração (variáveis de ambiente)](#configuração-variáveis-de-ambiente)
- [Referência da API](#referência-da-api)
- [Frontend](#frontend)
- [Testes](#testes)
- [Deploy (Google Cloud)](#deploy-google-cloud)
- [Problemas conhecidos / gotchas](#problemas-conhecidos--gotchas)
- [Roadmap](#roadmap)

---

## Stack

| Componente | Tecnologia |
|---|---|
| API | ASP.NET Core **.NET 10** (minimal API, modular) |
| Frontend | React 19 + Vite + TypeScript (SPA estática) |
| Banco de dados | PostgreSQL 16 — **schema por tenant** |
| Cache / Fila | Redis 7 (sessões, Redis Streams para mensagens, rate limit) |
| Reverse proxy | Caddy 2 (TLS automático via Let's Encrypt) |
| WhatsApp (não-oficial) | Evolution API (Baileys, via QR Code) |
| WhatsApp (oficial) | Meta Cloud API (webhook + Graph API) |
| IA | OpenAI, Anthropic, ou provider Mock |
| Pagamentos | Asaas (PIX/boleto/cartão) e Stripe |
| Monitoramento | Uptime Kuma |
| UI | shadcn/ui + Base UI, TanStack Query, Zustand, React Hook Form + Zod |
| Auth | JWT (BCrypt para hash de senha) |
| Logs | Serilog (console + arquivo em `logs/`) |

### Pacotes principais (API)
`Npgsql.EntityFrameworkCore.PostgreSQL`, `EFCore.NamingConventions`, `StackExchange.Redis`, `Microsoft.AspNetCore.Authentication.JwtBearer`, `BCrypt.Net-Next`, `Serilog.AspNetCore`, `Swashbuckle.AspNetCore`.

---

## Arquitetura

### Multi-tenancy
- **`PublicDbContext`** — schema `public`: tabelas globais (`tenants`, `users`, `webhook_routes`, planos, assinaturas, faturas). Migrações via **EF Core** (aplicadas automaticamente no startup).
- **`TenantDbContext`** (via `TenantDbContextFactory`) — um **schema PostgreSQL por tenant** (`tenant_<guid>`), com `conversations`, `messages`, `contacts`, `quick_replies`, `whatsapp_accounts`, `ai_config`. O provisionamento e as migrações de schema do tenant usam **SQL bruto idempotente** (`TenantProvisioner` + bloco `ALTER/CREATE ... IF NOT EXISTS` no `Program.cs`, re-executado a cada boot).
- **`TenantResolver`** identifica o tenant pelo subdomínio (`<tenant>.atendefy.com.br`); nas rotas autenticadas o `tenant_id` vem do JWT.

### Fluxo de mensagens
```
WhatsApp → Webhook (Meta/Evolution) → grava em Redis Stream
        → ConversationWorker (HostedService) consome a stream
        → checa BotPaused / IsResolved / rate limit
        → chama o provider de IA do tenant
        → responde via WhatsAppProvider (Evolution/Meta)
        → emite evento SSE para o painel (/conversations/stream)
```

### Background workers
- **`ConversationWorker`** — consome o Redis Stream, orquestra a IA e o envio das respostas.
- **`SuspensionWorker`** — suspende tenants com assinatura vencida/inadimplente.

### Diagrama de infraestrutura (produção)
```
                 ┌─────────── Caddy (TLS, reverse proxy) ───────────┐
 app.dominio  ─▶ │  atendefy-web (nginx, build estático do React)   │
 api.dominio  ─▶ │  atendefy-api (.NET)                             │
 evolution.   ─▶ │  evolution-api (WhatsApp não-oficial)            │
 monitor.     ─▶ │  uptime-kuma                                     │
                 └──────────────┬──────────────┬───────────────────┘
                          postgres:16      redis:7
```

---

## Estrutura do repositório

```
Atendefy/
├── Atendefy.slnx                 # solução (.NET)
├── README.md
├── src/
│   ├── Atendefy.API/             # backend .NET 10
│   │   ├── Program.cs            # composição: DI, auth, CORS, mapeamento de endpoints, migrações
│   │   ├── Infrastructure/
│   │   │   ├── Cache/            # RedisService
│   │   │   ├── Database/         # Public/TenantDbContext, Migrations EF, TenantResolver
│   │   │   ├── Messaging/        # RedisStreamService
│   │   │   └── RateLimiting/     # TenantRateLimiter, ApiRateLimitFilter
│   │   ├── Modules/
│   │   │   ├── Auth/             # login, JWT, hashing
│   │   │   ├── Tenants/          # registro + provisionamento de schema
│   │   │   ├── WhatsApp/         # contas, QR (Evolution), Meta Cloud, factory de provider
│   │   │   ├── AI/               # config por tenant, providers OpenAI/Anthropic/Mock
│   │   │   ├── Chatbot/          # conversas, mensagens, contatos, quick replies, dashboard, worker
│   │   │   ├── Webhooks/         # entrada Meta + Evolution
│   │   │   └── Billing/          # planos, assinaturas, gateways Asaas/Stripe, worker de suspensão
│   │   └── SharedKernel/         # Result<T>, BaseEntity, constantes, extensions
│   └── Atendefy.Web/             # frontend React + Vite
│       └── src/
│           ├── pages/            # Login, Register, Dashboard, Conversations, WhatsApp, AIConfig,
│           │                     #   Contacts, QuickReplies, Billing
│           ├── hooks/            # React Query hooks por domínio
│           ├── api/client.ts     # axios + injeção de JWT
│           ├── stores/           # authStore (Zustand)
│           └── components/       # layout (Sidebar/AppLayout) + ui (shadcn)
├── tests/Atendefy.Tests/         # xUnit — unitários + integração (~78 testes)
├── infra/                        # docker-compose, Caddyfile, .env, guia de deploy
└── docs/superpowers/             # specs (design) + plans (9 fases de implementação)
```

---

## Rodando localmente

### Pré-requisitos
- **Docker Desktop** (com daemon ativo) — é o único requisito obrigatório para subir tudo.
- Opcional para desenvolvimento fora do container: **.NET SDK 10** e **Node 22+**.

### Subir o stack completo
O `docker-compose.override.yml` é carregado automaticamente e builda as imagens de API e Web, além de habilitar a Evolution em modo dev. As credenciais vêm de `infra/.env`.

```bash
cd infra
docker compose up -d --build      # primeira vez (builda imagens)
docker compose ps                 # conferir que tudo está "healthy"/"running"
docker compose logs -f atendefy-api
docker compose down               # parar tudo
```

### Portas e URLs (dev)

| Serviço | URL / porta | Observação |
|---|---|---|
| Frontend | http://localhost:3001 | painel React |
| API | http://localhost:8084 | use `/swagger` e `/health` (a raiz `/` retorna 404, é esperado) |
| Evolution API | http://localhost:8085 | WhatsApp não-oficial |
| PostgreSQL | `localhost:5434` | db `atendefy`, user `atendefy` |
| Redis | `localhost:6381` | — |
| Uptime Kuma | interno (rede docker) | — |

### Primeiro uso
1. Acesse http://localhost:3001 e **registre um tenant** (cria schema + usuário admin).
2. Faça login → você cai no **Dashboard**.
3. Em **IA**, configure o provider (use `mock` para testar sem chave de API).
4. Em **Contas WhatsApp**, crie uma conta `evolution` (o placeholder já vem com `base_url: http://evolution-api:8080`, `instance: atendefy-dev`, `api_key: dev_evolution_key`) e clique **Conectar** para escanear o QR Code.
   - ⚠️ Se aparecer "Erro ao gerar QR code", veja [Problemas conhecidos](#problemas-conhecidos--gotchas).

---

## Configuração (variáveis de ambiente)

### Backend
Em produção/Docker as configurações vêm de variáveis de ambiente (ver `infra/.env`); em desenvolvimento local fora do container, de `appsettings.Development.json` (não versionado). Chaves principais (`appsettings.json`):

| Chave | Descrição |
|---|---|
| `ConnectionStrings:Postgres` | string de conexão do Postgres |
| `ConnectionStrings:Redis` | string de conexão do Redis |
| `Jwt:Secret` / `Issuer` / `Audience` | assinatura/validação do token (mín. 32 chars no secret) |
| `Encryption:Key` | chave para criptografar segredos do tenant (ex: API keys de IA) |
| `App:BaseDomain` | domínio base para resolução de tenant por subdomínio |
| `Meta:AppSecret` / `WebhookVerifyToken` | integração WhatsApp Cloud (Meta) |
| `Asaas:ApiKey` / `WebhookToken` / `Sandbox` | gateway Asaas |
| `Stripe:SecretKey` / `WebhookSecret` | gateway Stripe |
| `RateLimit:MessagesPerMinute` | limite por tenant (default 60) |

### `infra/.env` (Docker)
`DOMAIN`, `POSTGRES_USER/PASSWORD/CONNECTION`, `REDIS_PASSWORD/CONNECTION`, `JWT_SECRET`, `ENCRYPTION_KEY`, `EVOLUTION_API_KEY`, `API_VERSION`, `WEB_VERSION`. Modelo em `infra/.env.example`.

> **Atenção:** os valores em `infra/.env` são placeholders (`change_me_...`). Troque-os por segredos reais antes de qualquer deploy.

### Frontend
`src/Atendefy.Web/.env.example`: `VITE_APP_TITLE`, `VITE_STRIPE_PUBLISHABLE_KEY`.

---

## Referência da API

Base local: `http://localhost:8084`. Documentação interativa em **`/swagger`** (apenas em Development). Rotas autenticadas exigem `Authorization: Bearer <jwt>`.

| Método | Rota | Auth | Descrição |
|---|---|---|---|
| GET | `/health` | — | Health check |
| POST | `/tenants/register` | — | Registrar novo tenant (cria schema + admin) |
| POST | `/auth/login` | — | Login → retorna JWT |
| GET/PUT | `/ai/config` | ✓ | Obter/salvar config de IA do tenant |
| POST | `/whatsapp/accounts` | ✓ | Criar conta (provider `meta` ou `evolution`) |
| GET | `/whatsapp/accounts` | ✓ | Listar contas |
| POST | `/whatsapp/accounts/{id}/connect` | ✓ | Gerar QR Code (Evolution) |
| GET | `/whatsapp/accounts/{id}/status` | ✓ | Status da conexão |
| GET | `/conversations` | ✓ | Listar conversas (`?status=open\|resolved\|all`) |
| GET | `/conversations/{id}/messages` | ✓ | Mensagens de uma conversa |
| GET | `/conversations/stream` | ✓ | SSE de eventos em tempo real (token via query) |
| POST | `/conversations/{id}/messages` | ✓ | Enviar mensagem manual (modo humano) |
| PATCH | `/conversations/{id}/takeover` \| `/release` | ✓ | Assumir / devolver atendimento ao bot |
| PATCH | `/conversations/{id}/resolve` \| `/reopen` | ✓ | Resolver / reabrir conversa |
| GET | `/contacts` | ✓ | Listar contatos |
| PATCH | `/contacts/{phone}` | ✓ | Editar nome do contato |
| GET/POST | `/quick-replies` | ✓ | Listar / criar resposta rápida |
| PATCH/DELETE | `/quick-replies/{id}` | ✓ | Editar / remover resposta rápida |
| GET | `/dashboard/stats` | ✓ | Métricas do painel |
| GET | `/billing/plans` | ✓ | Planos disponíveis |
| POST/GET/DELETE | `/billing/subscribe` \| `/subscription` | ✓ | Assinar / consultar / cancelar |
| GET/POST | `/webhooks/meta` | — | Verificação + recebimento (Meta Cloud) |
| POST | `/webhooks/evolution` | — | Recebimento (Evolution) |
| POST | `/billing/webhooks/asaas` \| `/stripe` | — | Notificações de pagamento |

---

## Frontend

SPA em React + Vite. Rotas protegidas por `PrivateRoute` (JWT no `authStore`/Zustand). Dados via TanStack Query (hooks em `src/hooks`). Páginas:

- **Login / Register** — autenticação e cadastro de tenant.
- **Dashboard** — métricas (`/dashboard/stats`).
- **Conversas** — lista com filtro de status, takeover/release, resolver/reopen, envio manual, popover de respostas rápidas, atualização em tempo real via SSE.
- **Contas WhatsApp** — criar conta e conectar via QR Code.
- **IA** — configurar provider e prompt.
- **Contatos** — edição inline de nomes.
- **Respostas Rápidas** — CRUD de templates.
- **Cobrança** — planos e assinatura (Stripe Elements).

```bash
cd src/Atendefy.Web
npm install
npm run dev      # http://localhost:5173 (aponta para a API)
npm run build    # build de produção em dist/
```

---

## Testes

Projeto **xUnit** em `tests/Atendefy.Tests` — ~78 testes (`[Fact]`/`[Theory]`) cobrindo Auth, Billing, Chatbot, WhatsApp, AI, Tenants, Infrastructure e cenários de **Integração** (perfil `Testing`, ver `appsettings.Testing.json`).

```bash
dotnet test                          # roda toda a suíte
dotnet build Atendefy.slnx           # apenas compilar
```

---

## Deploy (Google Cloud)

Guia passo a passo em **`infra/DEPLOY-GOOGLE-CLOUD.md`**. Resumo: VM Ubuntu 24.04 (e2-medium) + IP fixo → DNS (`app`, `api`, `evolution`, `monitor`) → Docker → `docker compose --profile production up -d` → deploy contínuo via GitHub Actions (`.github/workflows/deploy.yml`) disparado por tags `v*`.

Workflows:
- **`ci.yml`** — build + testes em cada push.
- **`deploy.yml`** — build das imagens e deploy na VM via SSH ao criar uma tag de versão.

> O ambiente de deploy **ainda não foi criado** — nenhuma VM/DNS/secret configurado até o momento.

---

## Problemas conhecidos / gotchas

### "Erro ao gerar QR code" (Evolution API)
A API pede o QR à Evolution (`GET /instance/connect/{instance}`) e espera o campo `base64`. A imagem `atendai/evolution-api:latest` (v2.2.3) anuncia uma **versão antiga do WhatsApp Web** que o WhatsApp passou a rejeitar — o socket Baileys cai antes de gerar o QR e a Evolution retorna `{"count":0}`, fazendo a API responder 400.

**Correção (já aplicada em `infra/docker-compose.override.yml`):**
- `CONFIG_SESSION_PHONE_VERSION` fixado numa versão vigente (ex. `2.3000.1035194821`; consulte a [referência do Baileys](https://raw.githubusercontent.com/WhiskeySockets/Baileys/master/src/Defaults/baileys-version.json) — esse número envelhece com o tempo).
- `depends_on: postgres → service_healthy` para a Evolution não subir antes do banco (evita a falha de migração no boot).

Resetar uma instância travada:
```bash
curl -X DELETE -H "apikey: dev_evolution_key" http://localhost:8085/instance/delete/atendefy-dev
```

### Segredos versionados
- `ssh-key-2026-06-10.key` (**chave privada**) está commitada no histórico do git — **remover do tracking e rotacionar** antes de expor o repositório/deploy.
- `infra/.env`, `SECRET_GITUHUB.txt` e `appsettings.Development.json` estão corretamente no `.gitignore`.

### Aviso de pacote
`Microsoft.Extensions.Caching.Memory 8.0.0` gera warning de vulnerabilidade (NU1903) no build dos testes — atualizar quando conveniente.

---

## Roadmap

Fases concluídas (em `docs/superpowers/plans`):

1. ✅ Foundation (multi-tenant, auth, schema por tenant)
2. ✅ WhatsApp + IA (Evolution/Meta, providers de IA, worker de conversas)
3. ✅ Billing (planos, assinaturas, Asaas/Stripe, suspensão)
4. ✅ Frontend (painel React completo)
5. ✅ Evolution + histórico de conversas
6. ✅ Dashboard em tempo real (SSE)
7. ✅ QR Code + configuração de IA
8. ✅ Takeover + contatos
9. ✅ Resolver conversas + respostas rápidas

**Próximos passos sugeridos:**
- Criar e validar o ambiente de produção no Google Cloud.
- Remover/rotacionar a chave SSH versionada.
- Fase 10 (agendamento, propostas e follow-up estão previstos no design como fases futuras do produto).
