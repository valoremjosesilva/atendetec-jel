# Atendefy — Design Document

**Data:** 2026-06-04  
**Versão:** 1.0  
**Autor:** Jose Silva  
**Status:** Aprovado

---

## Visão Geral

O **Atendefy** é uma plataforma SaaS multi-tenant que permite a pequenas e médias empresas implantarem chatbots inteligentes no WhatsApp sem contratar funcionários para atendimento 24h. O produto é comercializado como assinatura mensal com planos flexíveis, atendendo nichos como clínicas, imobiliárias, escritórios contábeis, advogados e oficinas.

**Modelo de negócio:** SaaS multi-tenant  
**Potencial de receita:** R$ 100 a R$ 2.000/mês por cliente  
**MVP:** Chatbot + WhatsApp (agendamento, propostas e follow-up em fases posteriores)

---

## Seção 1: Arquitetura Geral

### Stack Principal

| Componente | Tecnologia | Função |
|---|---|---|
| API | ASP.NET Core 8 (.NET 8) | Backend principal com todos os módulos |
| Frontend | React + Vite + TypeScript | Painel SPA do cliente (static build) |
| Banco de dados | PostgreSQL 16 | Multi-schema por tenant |
| Cache / Fila | Redis 7 | Sessões, fila de mensagens, rate limit |
| Reverse Proxy | Caddy | TLS automático (Let's Encrypt), roteamento |
| WhatsApp não-oficial | Evolution API (Docker) | Conexão via QR Code |
| WhatsApp oficial | Meta Cloud API | Webhook HTTP + Graph API |
| UI Components | shadcn/ui + TanStack Query | Frontend do painel |

### Diagrama de Infraestrutura

```
┌─────────────────────────────────────────────────────────────┐
│                        VPS (Hetzner)                        │
│                                                             │
│  ┌──────────┐   ┌──────────────────────────────────────┐   │
│  │  Caddy   │   │         Atendefy API (.NET)           │   │
│  │ (reverse │──▶│                                      │   │
│  │  proxy + │   │  ┌──────────┐  ┌──────────────────┐  │   │
│  │   TLS)   │   │  │ Tenants  │  │    Chatbot /     │  │   │
│  └──────────┘   │  │ Module   │  │   Conversation   │  │   │
│                 │  └──────────┘  └──────────────────┘  │   │
│  ┌──────────┐   │  ┌──────────┐  ┌──────────────────┐  │   │
│  │ Frontend │   │  │ Billing  │  │   WhatsApp       │  │   │
│  │  (React  │   │  │ Module   │  │   Gateway        │  │   │
│  │  Static) │   │  └──────────┘  └──────────────────┘  │   │
│  └──────────┘   │  ┌──────────┐  ┌──────────────────┐  │   │
│                 │  │   AI     │  │   Webhooks       │  │   │
│                 │  │ Provider │  │   Handler        │  │   │
│                 │  └──────────┘  └──────────────────┘  │   │
│                 └──────────────────────────────────────┘   │
│                                                             │
│  ┌──────────────┐  ┌──────────┐  ┌───────────────────┐    │
│  │  PostgreSQL  │  │  Redis   │  │  Evolution API    │    │
│  │  (multi-     │  │ (cache + │  │  (container)      │    │
│  │   schema)    │  │  queue)  │  │                   │    │
│  └──────────────┘  └──────────┘  └───────────────────┘    │
└─────────────────────────────────────────────────────────────┘
         │                              │
         ▼                              ▼
  WhatsApp Business API          WhatsApp (via QR)
  (Meta oficial)                 (Evolution API)
```

### Multi-tenancy

Cada tenant recebe um schema PostgreSQL dedicado (`tenant_{id}`). A API resolve o tenant por subdomínio (`cliente.atendefy.com.br`) ou por API key no header `X-Tenant-Key`. Isolamento total de dados entre tenants.

### Domínios

```
atendefy.com.br          → landing page / marketing
app.atendefy.com.br      → painel do cliente (SPA)
api.atendefy.com.br      → API backend
```

---

## Seção 2: Estrutura de Módulos da API

A API segue arquitetura **Vertical Slice** — cada módulo é uma pasta autossuficiente com seus próprios endpoints, serviços e modelos.

```
Atendefy.API/
├── Modules/
│   ├── Tenants/          ← cadastro, configuração, onboarding
│   ├── Auth/             ← JWT, refresh token, permissões por tenant
│   ├── WhatsApp/         ← abstração sobre Meta API + Evolution API
│   ├── Chatbot/          ← sessões de conversa, contexto, histórico
│   ├── AI/               ← abstração multi-provider (OpenAI, Anthropic, Gemini)
│   ├── Billing/          ← planos, uso, faturas, webhooks de pagamento
│   └── Webhooks/         ← recebimento de eventos externos (Meta, Evolution)
├── Infrastructure/
│   ├── Database/         ← EF Core, migrations, tenant resolver
│   ├── Cache/            ← Redis helpers
│   └── Messaging/        ← fila interna via Redis Streams
└── SharedKernel/         ← Result pattern, domain events, base entities
```

### Abstração WhatsApp Gateway

```
IWhatsAppProvider
  ├── MetaCloudProvider    (oficial — webhook + send via HTTP/Graph API)
  └── EvolutionProvider    (não-oficial — QR Code, REST da Evolution API)
```

A troca de provider é configurada por tenant no banco. O restante do sistema não conhece qual provider está ativo.

### Abstração AI Provider

```
IAIProvider
  ├── OpenAIProvider       (GPT-4o, GPT-4o mini)
  ├── AnthropicProvider    (Claude Haiku, Sonnet)
  └── GeminiProvider       (Gemini Flash, Pro)
```

Cada tenant configura: provider preferido + API key própria (ou consome do pool da plataforma com custo repassado na fatura).

---

## Seção 3: Fluxo de uma Conversa

```
Cliente final (WhatsApp)
        │
        ▼
[1] Webhook recebido
        │  POST /webhooks/meta  ou  POST /webhooks/evolution
        ▼
[2] WebhookHandler
        │  - valida assinatura (HMAC-SHA256 Meta / token Evolution)
        │  - identifica tenant pelo número/token
        │  - publica evento no Redis Stream: "messages.inbound"
        ▼
[3] ConversationWorker (IHostedService / Background Service)
        │  - consome fila Redis
        │  - busca/cria sessão da conversa (Redis TTL 30min)
        │  - monta contexto: histórico + prompt do sistema do tenant
        ▼
[4] AI Provider
        │  - envia para OpenAI / Anthropic / Gemini
        │  - recebe resposta em streaming ou completa
        ▼
[5] WhatsApp Gateway
        │  - envia resposta via Meta API ou Evolution API
        ▼
[6] Persistência
        │  - salva mensagem + resposta no PostgreSQL (schema do tenant)
        │  - atualiza contadores de uso (Billing)
        ▼
Cliente final recebe a resposta
```

### Sessão de Conversa (Redis)

```json
{
  "sessionId": "whatsapp:5511999999999",
  "tenantId": "tenant_abc123",
  "messages": [
    { "role": "user", "content": "Oi, qual o horário?" },
    { "role": "assistant", "content": "Olá! Atendemos de seg a sex..." }
  ],
  "expiresAt": "30 minutos após última mensagem"
}
```

O **prompt do sistema** é configurado pelo tenant no painel (nome da empresa, tom de voz, horários, FAQ) e injetado no topo de toda conversa.

**Rate limiting por tenant** via Redis: limite de mensagens/minuto configurável por plano para evitar abuso e controlar custo de IA.

---

## Seção 4: Modelo de Dados e Billing

### Schema Público (plataforma)

```sql
public.tenants          (id, name, subdomain, status, plan_id, created_at)
public.plans            (id, name, price_monthly, price_setup, limits_json)
public.subscriptions    (id, tenant_id, plan_id, status, billing_cycle)
public.invoices         (id, tenant_id, amount, status, due_date)
```

### Schema por Tenant

```sql
tenant_{id}.whatsapp_accounts   (id, provider, phone, config_json, status)
tenant_{id}.ai_configs          (id, provider, api_key_encrypted, model)
tenant_{id}.conversations       (id, contact_phone, started_at, message_count)
tenant_{id}.messages            (id, conversation_id, role, content, tokens_used, created_at)
tenant_{id}.usage_counters      (month, messages_sent, tokens_consumed, cost_usd)
```

### Billing Flexível

O campo `plans.limits_json` permite configurar qualquer combinação de cobrança:

```json
{
  "max_whatsapp_numbers": 3,
  "max_messages_month": 5000,
  "ai_pool": true,
  "setup_fee": 150000,
  "monthly_fee": 49900,
  "overage_per_message": 5
}
```

*(valores em centavos)*

| Modelo de Cobrança | Configuração |
|---|---|
| Plano fixo mensal | `monthly_fee` fixo, `overage = 0` |
| Pay-as-you-go | `monthly_fee = 0`, `overage_per_message > 0` |
| Setup + mensalidade | `setup_fee > 0` + `monthly_fee > 0` |
| Plano customizado | qualquer combinação |

### Gateways de Pagamento

- **Asaas** — boleto, Pix, cartão (mercado brasileiro). Custo: 1,99% por transação
- **Stripe** — cartão internacional. Custo: 2,9% + R$1,50 por transação

Ambos integrados via webhook para atualizar status de subscription automaticamente.

---

## Seção 5: Infraestrutura e Custos

### Docker Compose (VPS)

```yaml
services:
  caddy:           # reverse proxy + TLS automático
  atendefy-api:    # ASP.NET Core 8
  atendefy-web:    # React SPA (servida pelo Caddy como static)
  postgres:        # PostgreSQL 16
  redis:           # Redis 7
  evolution-api:   # WhatsApp não-oficial
  uptime-kuma:     # monitoramento de disponibilidade
```

### Plano de Crescimento — Hetzner Cloud

| Fase | Servidor | vCPU | RAM | Disco | Custo/mês |
|---|---|---|---|---|---|
| MVP (0–50 tenants) | CAX21 (ARM) | 2 | 4 GB | 40 GB | ~€5 (~R$30) |
| Crescimento (50–200) | CAX31 | 4 | 8 GB | 80 GB | ~€15 (~R$90) |
| Escala (200–500) | CAX41 | 8 | 16 GB | 160 GB | ~€30 (~R$180) |

### Custos Operacionais Mensais (Fase MVP)

| Item | Custo/mês |
|---|---|
| VPS Hetzner CAX21 | ~R$30 |
| Domínio `.com.br` | ~R$5 |
| Backup Hetzner (20% do VPS) | ~R$6 |
| OpenAI API (pool plataforma) | variável — repassado ao cliente |
| Evolution API | grátis (self-hosted) |
| Asaas | 1,99% por transação |
| Stripe | 2,9% + R$1,50 por transação |
| **Total fixo** | **~R$41/mês** |

### CI/CD

```
GitHub (gratuito)
    │
    └── GitHub Actions
           ├── push main → build + testes automatizados
           └── tag v* → docker build → push ghcr.io → deploy SSH no VPS
```

Registry: **GitHub Container Registry (ghcr.io)** — gratuito.

---

## Seção 6: Painel do Cliente e Onboarding

### Fluxo de Onboarding

```
1. Cadastro em app.atendefy.com.br
        │  nome, email, senha, nome da empresa
        ▼
2. Escolha de plano
        │  exibe planos com preços e limites
        ▼
3. Pagamento (Asaas / Stripe)
        │  setup fee (se houver) + primeiro mês
        ▼
4. Provisionamento automático do tenant
        │  - schema PostgreSQL criado
        │  - email de boas-vindas enviado
        ▼
5. Wizard de configuração
        │  ├── Conectar WhatsApp (QR Code ou Meta API)
        │  ├── Configurar prompt do sistema
        │  └── Testar bot
        ▼
6. Bot ativo
```

### Páginas do Painel do Cliente

| Tela | Funcionalidade |
|---|---|
| Dashboard | métricas: conversas hoje, mensagens, tokens usados |
| WhatsApp | conectar/desconectar números, status da conexão |
| Chatbot | editar prompt do sistema, testar conversa ao vivo |
| IA | escolher provider, inserir API key própria ou usar pool |
| Conversas | histórico completo de conversas e mensagens |
| Billing | plano atual, uso do mês, histórico de faturas |
| Configurações | dados da empresa, gerenciar usuários da conta |

### Painel Super Admin (operador do Atendefy)

| Tela | Funcionalidade |
|---|---|
| Tenants | listar, ver status, impersonar, bloquear/suspender |
| Planos | criar/editar planos e limites via `limits_json` |
| Financeiro | MRR, inadimplência, próximas cobranças |
| Uso global | consumo de IA, mensagens, custo real vs receita |

---

## Seção 7: Segurança

### Autenticação e Autorização

- JWT com refresh token: access token expira em 15 min, refresh em 7 dias
- Roles por tenant: `Owner`, `Admin`, `Viewer`
- Super admin separado com IP allowlist configurável
- API keys para integrações externas: geradas por tenant, armazenadas como hash SHA-256

### Proteção de Dados Sensíveis

| Dado | Proteção |
|---|---|
| API keys de IA do cliente | AES-256 encrypted no banco |
| Senhas | bcrypt (cost factor 12) |
| Tokens WhatsApp (Evolution) | AES-256 encrypted at rest |
| Dados de conversa | isolados por schema PostgreSQL |

### Proteções Operacionais

- Rate limiting por tenant via Redis (mensagens/minuto por plano)
- Webhook signature validation (HMAC-SHA256 Meta / token Evolution)
- Tenant suspenso automaticamente após 7 dias de inadimplência
- Soft delete em todos os dados — tenant pode recuperar até 30 dias após cancelamento

### Observabilidade

- Logs estruturados com **Serilog** (arquivo local + futuro Seq/Loki)
- Health checks em `/health` para cada serviço
- **Uptime Kuma** (self-hosted, gratuito) monitorando endpoints críticos

---

## Roadmap Pós-MVP

| Feature | Quando adicionar |
|---|---|
| Agendamento automático via WhatsApp | Após 20 clientes pagantes |
| Geração de propostas | Após validar demanda com clientes |
| Follow-up automático de leads | Após feature de agendamento |
| App mobile para admin | Após R$10k MRR |
| Migração seletiva para microsserviços | Após 500 tenants ativos |

---

## Resumo de Decisões Arquiteturais

| Decisão | Escolha | Justificativa |
|---|---|---|
| Modelo de produto | SaaS multi-tenant | Escala sem crescer equipe operacional |
| Arquitetura backend | Monolito Modular | Entrega rápida, fácil manutenção com .NET, evoluível |
| Multi-tenancy | Schema por tenant | Isolamento real de dados, fácil de raciocinar |
| Hosting | VPS Hetzner | Custo ~R$30/mês vs centenas em cloud gerenciada |
| WhatsApp | Dual provider | Flexibilidade: oficial (seguro) + não-oficial (zero burocracia) |
| IA | Multi-provider | Evita lock-in, cliente usa sua própria key ou pool |
| Billing | Flexível via JSON | Permite negociar planos customizados sem mudar código |
| Pagamentos | Asaas + Stripe | Cobertura total: Pix/boleto BR + cartão internacional |
| CI/CD | GitHub Actions + ghcr.io | Gratuito, integrado, suficiente para fase inicial |
