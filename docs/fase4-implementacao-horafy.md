# Fase 4 — Atendefy: write-back do Horafy (sincronia)

> Data: 2026-06-26 · Plano: `docs/plano-execucao-integracao-horafy.md`
> Pré-requisito: Fase 3 do Horafy (webhooks de saída) e Fase 1 do Atendefy (config).
> Status: **código escrito**. Falta compilar/testar (sem SDK .NET no ambiente de geração).

## Escopo entregue (A8)

- Endpoint **`POST /webhooks/horafy?token=`** que recebe os eventos do Horafy
  (`booking.created/confirmed/cancelled`), valida a **assinatura HMAC** e atualiza a lista de
  agendamentos do tenant (`Appointment`), notificando o painel.
- `HorafyWebhook`: parser do envelope + verificação HMAC.
- Config: token de roteamento por tenant (reusa `WebhookToken`) + **segredo do webhook**
  (`WebhookSecretEncrypted`, criptografado), com URL e campo de segredo na UI.

## Como funciona

```
Horafy (booking criado/confirmado/cancelado fora do WhatsApp)
   → POST {Atendefy}/webhooks/horafy?token=<token>
       headers: X-Horafy-Event, X-Horafy-Signature (HMAC), X-Horafy-Delivery
   → resolve tenant pelo token (webhook_routes, provider "horafy")
   → valida HMAC com o segredo do tenant (se configurado)
   → HorafyWebhook.Parse → Appointment (idempotente por id da reserva do Horafy)
   → SchedulingService.UpsertAppointmentAsync → painel atualizado (emit)
```

- **Idempotência:** `Appointment.ExternalId = id da reserva no Horafy` (estável entre eventos),
  então confirmar/cancelar atualizam o mesmo registro.
- **Segurança:** se o tenant configurou o segredo, assinaturas inválidas → `401`. Erros de parse
  não retornam erro (evita retry-storm).

## Configuração (tenant)

1. No Atendefy (Agenda → Horafy), salve a config; será exibida a **URL do webhook**
   (`https://api.{dominio}/webhooks/horafy?token=...`).
2. No Horafy (Integrações → Webhook, Fase 3 do Horafy), cole essa URL → o Horafy devolve um
   **segredo**.
3. Cole o segredo no campo **"Segredo do webhook (Horafy)"** no Atendefy e salve.

## Arquivos criados

```
src/Atendefy.API/Modules/Scheduling/HorafyWebhook.cs   (parser + verificação HMAC)
```

## Arquivos alterados

```
src/Atendefy.API/Modules/Scheduling/Models/CalendarConfig.cs    (+ WebhookSecretEncrypted + request.WebhookSecret)
src/Atendefy.API/Modules/Scheduling/SchedulingService.cs        (token horafy + cifra segredo + GetHorafyWebhookSecretAsync)
src/Atendefy.API/Modules/Scheduling/SchedulingEndpoints.cs      (rota/URL/route por provider + HasWebhookSecret)
src/Atendefy.API/Modules/Tenants/TenantProvisioner.cs           (+ coluna webhook_secret_encrypted)
src/Atendefy.API/Program.cs                                    (+ ALTER webhook_secret_encrypted)
src/Atendefy.API/Modules/Webhooks/WebhookEndpoints.cs          (+ POST /webhooks/horafy)
src/Atendefy.Web/src/types/api.ts                             (+ hasWebhookSecret / webhookSecret)
src/Atendefy.Web/src/pages/SchedulingPage.tsx                 (URL do webhook + campo segredo)
```

## Banco de dados / infra

- **Sem `dotnet ef`** (schema de tenant é provisionado por SQL idempotente): a coluna
  `webhook_secret_encrypted` é criada no provisioner (novos tenants) e no `ALTER` de startup
  (tenants existentes). Reusa `webhook_routes` (provider "horafy").

## Como testar (manual)

```
1. Configurar URL+segredo conforme acima (Horafy ↔ Atendefy).
2. No Horafy, cancelar/confirmar uma reserva (pelo portal ou staff).
3. Conferir que o Atendefy recebeu o POST e o agendamento aparece/atualiza em
   /scheduling/appointments (e o painel emite "appointment_updated").
4. Enviar um POST com assinatura errada → 401 (quando o segredo está configurado).
```

## Checklist de verificação

- [ ] `dotnet build` (API) + `npm run build` (Web) sem erros.
- [ ] Coluna `webhook_secret_encrypted` criada (novos e existentes tenants).
- [ ] PUT /scheduling/config (horafy, enabled) gera `WebhookToken` + `webhook_routes` (provider horafy).
- [ ] `GET /scheduling/config` retorna `webhookUrl` (path /webhooks/horafy) e `hasWebhookSecret`.
- [ ] Webhook válido cria/atualiza `Appointment` (idempotente por id da reserva).
- [ ] Assinatura inválida → 401; sem segredo → aceita (token-only) e processa.

## Limitações / follow-ups

- Sem segredo configurado, a validação cai para **token-only** (o token já restringe o acesso).
  Recomenda-se configurar o segredo para validar HMAC.
- O painel apenas registra o `Appointment`; uma notificação proativa ao cliente no WhatsApp sobre a
  mudança (ex.: cancelado pelo estabelecimento) pode ser adicionada depois.

---

## Integração concluída — visão geral

**Horafy (provedor da agenda)**
- Fase 1 ✅ API key → token (M2M), binding de tenant, profissionais por serviço.
- Fase 2 ✅ dias disponíveis, booking idempotente com origem, supressão de notificação.
- Fase 3 ✅ webhooks de saída assinados (HMAC) + config por tenant.

**Atendefy (conversa no WhatsApp)**
- Fase 1 ✅ provider horafy + `HorafyClient` + nome do contato + UI.
- Fase 2 ✅ agendamento conversacional (serviço→profissional→dia→horário→confirmar→criar).
- Fase 3 ✅ listas/botões interativos (Meta) com fallback de texto (Evolution).
- Fase 4 ✅ write-back (`/webhooks/horafy`) → painel sincronizado.

**Migrations pendentes (rodar no deploy):**
- Horafy: `AddIntegrationApiKeys` (global), `AddBookingSourceAndExternalId` (tenant),
  `AddIntegrationWebhooks` (global).
- Atendefy: nenhuma (schema de tenant via SQL idempotente no startup).
