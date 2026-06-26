# Plano de Execução — Atendefy: agendar no Horafy pelo WhatsApp

> Data: 2026-06-26
> Lado: **Atendefy** (consumidor da agenda + condução da conversa). Plano-irmão no Horafy: `Horafy/docs/superpowers/plans/2026-06-26-integracao-atendefy-agendamento.md`.
> Visão geral/viabilidade: `Atendefy/docs/integracao-horafy-agendamento.md`.

## Decisões travadas

- **Autenticação M2M:** **API Key por tenant** (o Horafy troca a key por um JWT curto). O Atendefy guarda a key criptografada e faz o token-exchange.
- **Orquestração:** **fluxo guiado determinístico** — máquina de estados no Atendefy + listas/botões do WhatsApp. A IA detecta intenção e interpreta texto livre; a sequência é fixa.

## Fluxo alvo (o pedido do cliente)

```
Cliente no WhatsApp conversa com o bot
   └─ "quero agendar"  → bot entra no FLUXO DE AGENDAMENTO
        1. Serviço        → bot lê os serviços do tenant no Horafy e oferece  → cliente escolhe
        2. Profissional   → bot consulta quais profissionais ATENDEM o serviço escolhido → cliente escolhe
        3. Dia            → bot lista próximos dias com vaga                  → cliente escolhe
        4. Horário        → bot lista horários livres do dia                 → cliente escolhe
        5. Confirmação    → resumo (serviço, profissional, dia, hora, duração/preço)
        6. Agenda         → POST cria o agendamento no Horafy → "Agendado ✅"
```

**O serviço é o passo 1 e define os passos seguintes:** os profissionais ofertados são apenas os que atendem aquele serviço (consulta dependente da seleção), e a duração do serviço afeta os horários livres.

Cada lista guarda os **IDs reais** (serviceId, resourceId, data, slot) no estado; a resposta do cliente (número, item da lista, ou texto livre interpretado pela IA) seleciona a opção e avança o passo.

---

## Tarefas

### A1 — Provider "horafy" na config de agenda
- Incluir `"horafy"` em `ValidProviders` (`Modules/Scheduling/SchedulingService.cs`).
- Estender `CalendarConfig` (`Modules/Scheduling/Models/CalendarConfig.cs`) com: `ApiBaseUrl`, `TenantSlug`, `ApiKeyEncrypted` (criptografar com `AesEncryption`, como as chaves de IA), `DefaultServiceId?`, `DefaultResourceId?`.
- Atualizar o provisioner de tenant (`Program.cs` bloco `CREATE/ALTER ... IF NOT EXISTS`) + `CalendarConfigRequest`/endpoints (`SchedulingEndpoints.cs`).

### A2 — `HorafyClient` tipado (HttpClient)
- Novo `Modules/Scheduling/Horafy/HorafyClient.cs` via `IHttpClientFactory`, no padrão de `EvolutionProvider`/`MetaCloudProvider`.
- **Auth:** `GetTokenAsync()` faz `POST /api/v1/integrations/token` com `X-Api-Key`, cacheia o JWT (Redis, TTL < expiração) e renova quando expira.
- Métodos:
  - `GetServicesAsync()` → `GET /services?onlyActive=true`
  - `GetResourcesByServiceAsync(serviceId)` → `GET /services/{id}/resources`
  - `GetAvailableDaysAsync(resourceId, from, to, serviceId)` → `GET /availability/resources/{id}/days` (fallback: loop em `/slots` por dia)
  - `GetSlotsAsync(resourceId, date, serviceId)` → `GET /availability/resources/{id}/slots`
  - `CreateBookingAsync(externalId, serviceIds, resourceId, scheduledAt, name, phone, notes)` → `POST /integrations/bookings`
  - `CancelBookingAsync(id, reason)`
- Headers padrão: `Authorization: Bearer`, `X-Tenant-Slug`, `X-Api-Version: 1`.

### A3 — Estado do fluxo de agendamento (Redis)
- `BookingFlowState` por `(tenantId, phone)`: `Step` (`Idle|Service|Professional|Day|Time|Confirm|Done`), seleções (`serviceId`, `resourceId`, `date`, `slot`) e as **opções oferecidas** (lista com IDs) para resolver a escolha.
- Guardar junto da sessão atual (`ConversationService`/Redis), TTL ex.: 15 min. Comando "cancelar"/"voltar" reseta/retrocede.

### A4 — Orquestrador do fluxo (`BookingFlowService`)
- Novo `Modules/Scheduling/Horafy/BookingFlowService.cs`, chamado pelo `ConversationWorker` **antes** do caminho de IA genérico quando o tenant tem provider `horafy` ativo.
- Lógica por passo (consulta Horafy → monta lista → envia → interpreta resposta → avança):
  - **Serviço (passo 1):** `GetServices` → bot lê os serviços do tenant e oferece; cliente escolhe. Otimização: se houver só 1 serviço ativo (ou `DefaultServiceId` configurado), seleciona automaticamente e segue.
  - **Profissional:** `GetResourcesByService(serviceId)` → lista **somente os profissionais que atendem o serviço escolhido**.
  - **Dia:** `GetAvailableDays(resourceId, serviceId)` (próximos ~14 dias com vaga) → lista.
  - **Horário:** `GetSlots(resourceId, dia, serviceId)` → lista horários (já considerando a duração do serviço).
  - **Confirmação:** resumo (serviço, profissional, dia, hora) + "responda SIM para confirmar".
  - **Criar:** `CreateBooking` com nome do contato + telefone; `externalId` determinístico (ex.: hash de tenant+phone+slot) p/ idempotência; responde confirmação e sai do fluxo.
- Mantém intacto o fluxo de IA atual para mensagens fora de agendamento.

### A5 — Detecção de intenção e interpretação de resposta
- Entrada no fluxo: regex já existente (`MentionsScheduling` no `ConversationWorker`) + checagem leve via IA para casos ambíguos.
- Seleção dentro do passo: aceitar número da lista, e **texto livre** ("pode ser quarta de manhã", "com a Dra. Ana") interpretado por uma função curta que pede à IA para mapear o texto → uma das opções oferecidas (retorna o ID ou "não entendi").

### A6 — Mensagens interativas no WhatsApp
- Estender `IWhatsAppProvider` (hoje só `SendMessageAsync` de texto) para suportar **listas/botões** do Evolution e Meta Cloud (interactive messages).
- Fallback automático para texto numerado quando o provider/conta não suportar.

### A7 — Captura do nome do contato
- Capturar `pushName`/profile name do webhook de entrada (Evolution/Meta) e persistir no `Contact` (hoje `UpsertContactAsync` só grava telefone) — usado como `CustomerName` no agendamento.

### A8 — Webhook de entrada "horafy" (write-back)
- `Modules/Scheduling/Horafy/HorafyPayloadParser.cs` (irmão de `CalcomPayloadParser`), rota `/webhooks/horafy?token=`.
- Validar **assinatura HMAC** (`X-Horafy-Signature`), resolver tenant por `WebhookRoutes` (provider `horafy`), e `SchedulingService.UpsertAppointmentAsync` (idempotente por `ExternalId`).
- Emitir evento p/ o painel (`emitter`) e, se desejado, avisar o cliente na thread quando o agendamento muda fora do WhatsApp.

### A9 — UI de configuração (Atendefy.Web)
- Tela "Agenda": escolher provider **Horafy**, informar `ApiBaseUrl` + `TenantSlug` + **API Key**, escolher serviço/recurso padrão (carregados via "testar conexão") e ativar.
- Trava por plano já existe (`EntitlementsService.SchedulingEnabled`).

### A10 — Tratamento de erros e bordas
- Sem profissionais/dias/horários → mensagem clara + oferecer link da página pública do Horafy como fallback.
- Conflito `409` no create → reconsultar horários e reoferecer.
- Timeout/erro do Horafy → fallback para link + log.
- Expiração da sessão (TTL) → reiniciar fluxo cordialmente.
- "cancelar"/"voltar"/trocar de profissional no meio do fluxo.

### A11 — Observabilidade e testes
- Logs por passo + métrica de conversão (iniciou → agendou).
- Testes unitários do `BookingFlowService` com `HorafyClient` mockado (cada transição de estado), e e2e do caminho feliz.

---

## Faseamento (Atendefy)

| Fase | Tarefas | Resultado |
|---|---|---|
| 0 — Handoff (hoje) | usar provider "other" com URL pública do Horafy | valida ponta-a-ponta sem código |
| 1 — Cliente & config | A1, A2, A7, A9 | conecta o tenant ao Horafy e lê catálogo |
| 2 — Fluxo (texto) | A3, A4, A5, A10 | agenda no chat com listas numeradas |
| 3 — Interativo | A6 | listas/botões nativos do WhatsApp |
| 4 — Sincronia | A8 | write-back do Horafy reflete no painel/chat |
| 5 — Qualidade | A11 | testes + métricas |

> Dependência: as Fases 1–2 do Atendefy dependem das Fases 1–2 do Horafy (token-exchange, `/services/{id}/resources`, `/integrations/bookings`). A Fase 4 depende de H9/H10.

## Critérios de aceite

- Cliente final agenda inteiramente pelo WhatsApp: serviço (se aplicável) → profissional → dia → horário → confirmação → "Agendado ✅".
- O agendamento aparece na agenda do tenant no Horafy e o cliente **não** recebe mensagem duplicada de outro número.
- Reenvio/retry do bot não cria agendamento duplicado (idempotência por `externalId`).
- Cancelamento fora do WhatsApp chega ao Atendefy (write-back) e atualiza o painel.
- Falhas do Horafy degradam para o link público sem travar a conversa.
