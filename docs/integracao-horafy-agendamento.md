# Integração Atendefy ↔ Horafy (Agendamento via API)

> Análise de viabilidade e plano técnico — 2026-06-25
> Objetivo: expor a agenda do **Horafy** por API para que o **Atendefy** (chatbot WhatsApp) consulte disponibilidade e crie agendamentos diretamente na conversa.

---

## 1. Veredito

**Viável, e com baixo atrito.** Os dois projetos são surpreendentemente compatíveis:

- O **Horafy já é um produto API-first** de agendamento (.NET 8, REST versionada, Clean Architecture/CQRS, multi-tenant por schema PostgreSQL, JWT). Ele **já expõe** quase todos os endpoints necessários — inclusive disponibilidade e criação de agendamento em nome de um cliente.
- O **Atendefy já tem um módulo `Scheduling`** desenhado para um provedor externo de agenda (hoje Cal.com): `CalendarConfig` (provider/bookingUrl/instructions/webhookToken), entrada de webhook (`WebhookRoutes`), modelo `Appointment` com upsert idempotente, e já faz chamadas HTTP de saída (Evolution/Meta) — ou seja, está **estruturalmente pronto para ganhar um provider "horafy"**.

A lacuna real é pequena e concentra-se em: (a) **autenticação máquina-a-máquina** no Horafy, e (b) **dar capacidade de "tool calling" à IA do Atendefy** para agendar dentro da conversa. O resto é reaproveitamento do que já existe.

---

## 2. O que já existe (não precisa construir)

### Horafy — endpoints prontos e relevantes

| Necessidade | Endpoint | Auth atual |
|---|---|---|
| Listar serviços agendáveis | `GET /api/v1/services?onlyActive=true` | **Anônimo** |
| Listar recursos (profissional/sala) | `GET /api/v1/resources?onlyActive=true&type=` | **Anônimo** |
| Consultar horários livres | `GET /api/v1/availability/resources/{resourceId}/slots?date=YYYY-MM-DD&serviceId=` | **Anônimo** |
| **Criar agendamento p/ um cliente** | `POST /api/v1/bookings/admin` (nome, e-mail, telefone, serviços, recurso, horário) | Role staff |
| Cancelar | `POST /api/v1/bookings/{id}/cancel` | Cliente/staff |
| Confirmar / concluir / no-show | `POST /api/v1/bookings/{id}/confirm` etc. | Role staff |
| Listar agendamentos (período) | `GET /api/v1/bookings?resourceId=&from=&to=` | Role staff |
| Login (emite JWT) | `POST /api/v1/auth/email` (email+senha+tenantSlug) → access+refresh | Anônimo |

- O endpoint **`POST /bookings/admin`** é o ideal para o bot: recebe `CustomerName/Email/Phone` + `ServiceIds` + `ResourceId` + `ScheduledAt`, faz **checagem de conflito** e cria o agendamento. Não exige um cliente logado.
- O `TenantMiddleware` **já aceita o header `X-Tenant-Slug`** explicitamente "para uso interno / mobile" — é por ele que o Atendefy vai indicar qual tenant Horafy está sendo agendado.
- O Horafy **já envia notificações** (WhatsApp via Evolution, e-mail, templates — Sprint 7) e tem eventos de domínio — base pronta para o write-back.

### Atendefy — peças prontas e relevantes

- Módulo `Scheduling`: `CalendarConfig`, `SchedulingService` (upsert idempotente de `Appointment` por `ExternalId`), endpoints de config, e geração de `WebhookToken` + rota de webhook (`WebhookRoutes`).
- Webhook de entrada já existente para Cal.com (`CalcomPayloadParser`) — molde direto para um `HorafyPayloadParser`.
- Saída HTTP já usada (`EvolutionProvider`/`MetaCloudProvider`) — molde para um `HorafyClient` tipado.
- Criptografia de segredos já usada para chaves de IA (`AesEncryption`) — reaproveitável para a credencial do Horafy.
- Trava por plano (`EntitlementsService.SchedulingEnabled`) já aplicada no fluxo do bot.

---

## 3. O que precisa ser feito

### 3A. No Horafy (expor a agenda com segurança)

1. **Autenticação máquina-a-máquina (M2M).** Hoje o JWT só é emitido por login interativo (Google/Apple/e-mail+senha). Duas opções:
   - **Rápida (zero código novo):** criar um usuário de serviço por tenant com role `TenantStaff`/`TenantAdmin`; o Atendefy faz `POST /auth/email` e mantém o token em cache, renovando pelo refresh (7 dias). Desvantagem: senha por tenant e token de vida curta.
   - **Recomendada:** adicionar **API Key por tenant** (ou OAuth2 *client_credentials*) que emite um token de serviço com escopo restrito. Mais limpo, revogável e auditável, sem senha humana. Ex.: um `ApiKeyAuthHandler` + `POST /api/v1/integrations/token`.
2. **Vínculo token ↔ tenant (hardening de segurança).** Hoje a autorização é só por *role* e o schema vem do `X-Tenant-Slug`. Sem amarração, um token de staff do tenant A poderia ser usado com `X-Tenant-Slug` do tenant B. Adicionar validação: para roles não-plataforma, o `tenant_id` do token deve bater com o tenant resolvido.
3. **Webhooks de saída (write-back).** Publicar `booking.created/confirmed/cancelled/rescheduled` para uma URL do Atendefy, com assinatura HMAC. Aproveitar os eventos de domínio já existentes. Mantém a lista de agendamentos e a conversa do Atendefy em sincronia quando algo muda fora do WhatsApp (portal do cliente, ajuste do staff).
4. **Idempotência + referência externa.** Aceitar um `external_id`/`source` ("atendefy") em `POST /bookings/admin` para evitar duplicidade em retries e correlacionar o write-back. Hoje não há chave de idempotência.
5. **Evitar notificação dupla.** Quando o agendamento nasce na thread WhatsApp do Atendefy, o Horafy **não** deve notificar o cliente por outro número. Criar flag por origem/tenant para suprimir a notificação do Horafy em bookings originados pelo bot (ou rotear a notificação via Atendefy).
6. **Dedupe de cliente por telefone (opcional).** `AdminCreate` gera um `customerId` aleatório a cada chamada; bookings repetidos do mesmo WhatsApp não se ligam a um cliente. Fazer upsert por telefone se quiser histórico unificado.
7. **Contrato + rate limit.** Documentar uma superfície "integração" estável (OpenAPI já existe via Swagger/Scalar), fixar `X-Api-Version: 1`, aplicar rate limit por integração e cuidar de **timezone** (passar/retornar em `America/Sao_Paulo`; há um teste de slots sensível a fuso citado no STATUS do Horafy).

### 3B. No Atendefy (consumir a agenda)

1. **Novo provider "horafy" no `Scheduling`.** Incluir `"horafy"` em `ValidProviders` e estender `CalendarConfig` com `ApiBaseUrl`, `TenantSlug` e `ApiKeyEncrypted` (criptografado como as chaves de IA).
2. **`HorafyClient` tipado (HttpClient/IHttpClientFactory),** no padrão dos providers existentes: `ListServices`, `ListResources`, `GetSlots(date, serviceId)`, `CreateBooking(...)`, `Cancel(...)`, com obtenção/renovação de token (ou header de API key).
3. **Nível de integração — escolher escopo:**
   - **Nível 1 — handoff por link (mínimo, ~0 código):** o bot só envia a URL pública de agendamento do tenant no Horafy (`https://{slug}.horafy.com.br/...`). Já funciona hoje com provider `"other"`. Valida a ponta-a-ponta, mas **não** agenda dentro do chat.
   - **Nível 2 — agendamento no chat (o pedido real):** o bot consulta disponibilidade e cria o agendamento dentro da conversa WhatsApp. Exige dar **tool/function-calling à IA**, que o módulo de IA do Atendefy **não tem hoje** (é completion de texto puro). Envolve: declarar ferramentas (`list_services`, `get_slots`, `create_booking`) nos providers Anthropic/OpenAI, um **loop de agente** no `ConversationWorker` que executa as tool calls contra o `HorafyClient`, e parsing de data/horário. **É o maior item de trabalho.**
4. **Webhook de entrada "horafy".** Um `HorafyPayloadParser` irmão do de Cal.com, rota `/webhooks/horafy?token=`, reusando `WebhookRoutes` + upsert de `Appointment`, validando a assinatura HMAC.
5. **UI de configuração** no painel do Atendefy: conectar um tenant Horafy (base URL, slug, chave), escolher recurso/serviço padrão e botão "testar conexão".

---

## 4. Fluxo alvo (Nível 2)

```
Cliente (WhatsApp) → Evolution/Meta → Redis Stream → ConversationWorker
   → IA com ferramentas:
        1. get_slots(serviço, data)      ──HTTP──▶ Horafy GET /availability/.../slots
        2. create_booking(nome, fone...) ──HTTP──▶ Horafy POST /bookings/admin
   → resposta no WhatsApp ("agendado p/ 3ª, 14h ✅")
Horafy (booking criado/alterado) ──webhook HMAC──▶ Atendefy /webhooks/horafy
   → upsert Appointment → painel sincronizado
Headers M2M: Authorization: Bearer <token> + X-Tenant-Slug: <slug> + X-Api-Version: 1
```

---

## 5. Faseamento sugerido

| Fase | Onde | Entrega | Esforço |
|---|---|---|---|
| 0 | Atendefy | Handoff por link p/ página pública do Horafy (provider "other") | ~nulo |
| 1 | Horafy | Auth M2M (API key) + vínculo token↔tenant + rate limit + doc | Pequeno/médio |
| 2 | Atendefy | `HorafyClient` + config + webhook de entrada (sem IA ainda) | Médio |
| 3 | Atendefy | Loop de agente com tool-calling (agendamento conversacional) | Médio/grande |
| 4 | Ambos | Webhooks de write-back + dedupe de notificação + observabilidade | Médio |

---

## 6. Riscos e pontos de atenção

- **Fuso horário:** o bot precisa enviar/exibir horário local (America/Sao_Paulo); há comportamento de slots sensível a fuso citado no STATUS do Horafy.
- **Concorrência/overbooking:** o Horafy já faz checagem de conflito; usar **chave de idempotência** para retries do bot.
- **Notificação dupla** entre dois números de WhatsApp (Horafy x Atendefy).
- **Ciclo de vida do token** se for pela opção "usuário de serviço" (tratar refresh).
- **Segurança multi-tenant:** evitar replay de token entre tenants via `X-Tenant-Slug` (item 3A.2).
- **Entitlements de plano** nos dois lados (Atendefy `SchedulingEnabled`; Horafy capacidades/limites) precisam estar habilitados de forma coerente.
- **IA sem tool-calling hoje:** o Nível 2 depende dessa evolução no módulo de IA do Atendefy.

---

## 7. Recomendação

Começar pela **Fase 0** (link handoff, valida tudo sem código), em paralelo iniciar a **Fase 1** no Horafy (API key + hardening). O diferencial de produto — agendar dentro do WhatsApp — vem nas Fases 2–3. As Fases 4 fecham a sincronização bidirecional. Nenhuma reescrita é necessária: ambos os sistemas já têm a arquitetura certa para isso.
