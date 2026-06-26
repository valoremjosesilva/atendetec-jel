# Fase 1 — Atendefy: conectar o tenant ao Horafy

> Data: 2026-06-26 · Plano: `docs/plano-execucao-integracao-horafy.md`
> Status: **código escrito**. Falta compilar/testar (sem SDK .NET/Node no ambiente de geração).

## Escopo entregue

- **A1** — Provider `"horafy"` na config de agenda: novos campos em `CalendarConfig`
  (`ApiBaseUrl`, `TenantSlug`, `ApiKeyEncrypted`, `DefaultServiceId`, `DefaultResourceId`),
  com a chave **criptografada** (AES, `Encryption:Key`). Provisionamento + migração idempotente.
- **A2** — `HorafyClient` tipado: token-exchange (API key → JWT) com **cache no Redis**, e os
  passos do fluxo: serviços, profissionais do serviço, dias, horários e criação de agendamento.
  Endpoint de **teste de conexão**.
- **A7** — Captura do nome do contato (`pushName` do Evolution) e persistência em `contacts.name`
  (usado depois como `CustomerName` no agendamento).
- **A9** — UI: opção **Horafy (agenda própria)** na página *Agenda*, com URL/slug/chave e botão
  **Testar conexão**.

## Endpoints (Atendefy)

| Método | Rota | Descrição |
|---|---|---|
| GET | `/scheduling/config` | Config atual (inclui `apiBaseUrl`, `tenantSlug`, `hasApiKey`) |
| PUT | `/scheduling/config` | Salva config (provider `horafy` aceita `apiBaseUrl`/`tenantSlug`/`apiKey`) |
| POST | `/scheduling/horafy/test` | Testa a conexão com o Horafy (token + lista serviços) |

## Arquivos criados

```
src/Atendefy.API/Modules/Scheduling/Horafy/HorafyModels.cs    (HorafyConnection + DTOs)
src/Atendefy.API/Modules/Scheduling/Horafy/HorafyClient.cs    (cliente HTTP + token cache)
```

## Arquivos alterados

```
src/Atendefy.API/Modules/Scheduling/Models/CalendarConfig.cs            (+ campos horafy + request)
src/Atendefy.API/Modules/Scheduling/SchedulingService.cs               (encrypt + horafy + GetHorafyConnectionAsync)
src/Atendefy.API/Modules/Scheduling/SchedulingEndpoints.cs             (+ /horafy/test + Shape)
src/Atendefy.API/Modules/Tenants/TenantProvisioner.cs                  (+ colunas em calendar_configs)
src/Atendefy.API/Program.cs                                           (DI SchedulingService+encryptionKey, HttpClient/HorafyClient, ALTER idempotente)
src/Atendefy.API/Modules/Webhooks/Models/EvolutionWebhookPayload.cs    (+ pushName)
src/Atendefy.API/Modules/Webhooks/WebhookEndpoints.cs                  (propaga contact_name)
src/Atendefy.API/Modules/Chatbot/Models/InboundMessage.cs             (+ ContactName)
src/Atendefy.API/Modules/Chatbot/ConversationWorker.cs               (lê contact_name → Contact.Name)
src/Atendefy.Web/src/types/api.ts                                     (tipos horafy + HorafyTestResponse)
src/Atendefy.Web/src/hooks/useScheduling.ts                          (+ useTestHorafy)
src/Atendefy.Web/src/pages/SchedulingPage.tsx                        (provider Horafy + teste)
```

## Banco de dados

**Sem `dotnet ef`** para o schema de tenant: o Atendefy provisiona via SQL idempotente. As novas
colunas de `calendar_configs` são criadas:
- para **novos tenants** no `TenantProvisioner`;
- para **tenants existentes** pelo bloco `ALTER TABLE ... ADD COLUMN IF NOT EXISTS` que roda no
  startup (`Program.cs`).

Basta subir a API que as colunas aparecem. (O `PublicDbContext` não foi alterado.)

## Como funciona o `HorafyClient`

- `GetTokenAsync`: `POST {base}/api/v1/integrations/token` com `X-Api-Key` → JWT; cacheado no Redis
  em `horafy:token:{slug}` até ~1 min antes de expirar. Em `401`, invalida e tenta uma vez.
- Chamadas autenticadas enviam `Authorization: Bearer` + `X-Tenant-Slug` + `X-Api-Version: 1`.
- Métodos: `GetServicesAsync`, `GetResourcesByServiceAsync`, `GetAvailableDaysAsync`,
  `GetSlotsAsync`, `CreateBookingAsync`, `TestConnectionAsync`.

## Como testar (manual)

```
1. No Horafy, gere uma API key (Fase 1 do Horafy): POST /api/v1/integrations/api-keys.
2. No painel Atendefy → Agenda: provider "Horafy", informe URL da API + slug + a API key, salve.
3. Clique "Testar conexão" → deve retornar "Conexão OK — N serviço(s)".
4. Envie uma mensagem no WhatsApp do tenant → confirme que contacts.name recebeu o pushName.
```

## Checklist de verificação

- [ ] `dotnet build` (API) sem erros.
- [ ] `npm run build` (Atendefy.Web) sem erros de tipos.
- [ ] Subir a API cria as colunas novas em `calendar_configs` (novos e existentes tenants).
- [ ] Salvar config horafy criptografa a chave (coluna `api_key_encrypted` não vem em claro).
- [ ] `GET /scheduling/config` retorna `hasApiKey: true` e **nunca** a chave.
- [ ] "Testar conexão" OK com credenciais válidas; erro claro com inválidas.
- [ ] `pushName` do Evolution grava/atualiza `contacts.name`.

## Limitações / follow-ups

- **Nome do contato no Meta Cloud**: o payload atual não traz `contacts[].profile.name`; capturado
  só no Evolution. Adicionar quando necessário (estender `MetaWebhookPayload`).
- **Pickers de serviço/recurso padrão** na UI: os campos `defaultServiceId/defaultResourceId`
  existem no backend, mas a UI ainda não tem seletor (o fluxo conversacional lista os serviços de
  qualquer forma). Pode virar um seletor na Fase 2.

## Próximo (Fase 2 — Atendefy)

Máquina de estados do agendamento no `ConversationWorker` (serviço → profissional → dia → horário →
confirmação → `CreateBookingAsync`), interpretação de texto livre via IA, e intake do webhook
`/webhooks/horafy` (write-back vindo do Horafy/Fase 3).
