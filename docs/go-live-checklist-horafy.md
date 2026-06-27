# Go-live — Checklist da integração Atendefy ↔ Horafy

> Data: 2026-06-26 · Cobre Horafy (Fases 1–3) + Atendefy (Fases 1–4).
> Marque cada item. Itens marcados com 🔴 são bloqueantes.

---

## 1. Pré-deploy — build, testes e migrations

- [ ] 🔴 **Horafy** — `dotnet build` sem erros.
- [ ] 🔴 **Horafy** — `dotnet test` verde.
- [ ] 🔴 **Atendefy (API)** — `dotnet build` sem erros.
- [ ] **Atendefy (API)** — `dotnet test` verde.
- [ ] 🔴 **Atendefy (Web)** — `npm run build` sem erros de tipo.
- [ ] 🔴 **Horafy / banco global** — migration `IntegracaoAtendefy` aplicada (✅ já validado em dev: tabelas `integration_api_keys` / `integration_webhooks` + índices).
- [ ] **Horafy / tenants existentes** — script `DO $$…$$` rodado (em dev não havia tenants; rodar em homolog/prod se houver).
- [ ] **Atendefy** — sem migration (schema de tenant é SQL idempotente no startup); validar que as colunas novas de `calendar_configs` aparecem ao subir a API.

---

## 2. Configuração no Horafy (provedor da agenda) — por tenant

- [ ] 🔴 Plano do tenant tem a **capacidade Agendamento** habilitada.
- [ ] 🔴 **Serviços** cadastrados (nome, duração, preço).
- [ ] 🔴 **Profissionais/recursos** cadastrados e **vinculados aos serviços** (serviço ↔ recurso).
- [ ] 🔴 **Horários de disponibilidade** definidos por recurso (senão `slots`/`days` voltam vazios).
- [ ] 🔴 **API key** criada: `POST /api/v1/integrations/api-keys` → guardar a chave (aparece **uma vez**).
- [ ] **Webhook de saída** criado: `PUT /api/v1/integrations/webhook` → guardar o **secret** (write-back).
- [ ] Confirmar versão da API fixada (`X-Api-Version: 1`).

## 3. Configuração no Atendefy (consumidor) — por tenant

- [ ] 🔴 Plano com **SchedulingEnabled** (Agenda liberada).
- [ ] 🔴 Conta **WhatsApp conectada** (Meta Cloud → listas/botões; Evolution → texto numerado).
- [ ] 🔴 Painel → **Agenda → Horafy**: informar **URL da API**, **slug** e **API key**; salvar.
- [ ] 🔴 Botão **"Testar conexão"** → retorna "Conexão OK — N serviço(s)".
- [ ] **Write-back:** copiar a **URL do webhook** exibida → colar no Horafy (passo 2) → copiar o **secret** do Horafy → colar em "Segredo do webhook" e salvar.
- [ ] (Opcional) Definir serviço/recurso padrão se quiser pular passos.

---

## 4. Validação ponta-a-ponta (smoke test)

- [ ] 🔴 Enviar "quero agendar" no WhatsApp → fluxo: **serviço → profissional → dia → horário → confirmação**.
- [ ] 🔴 Confirmar ("SIM") → mensagem de **agendamento confirmado**.
- [ ] 🔴 Reserva aparece na **agenda do Horafy** com `source = atendefy`.
- [ ] 🔴 O **Horafy não envia notificação** ao cliente (anti-duplicação — só o Atendefy fala com ele).
- [ ] **Idempotência:** refazer o fluxo até o **mesmo horário** → não duplica (Horafy responde reserva existente).
- [ ] **Conflito:** dois clientes no mesmo horário → o segundo recebe **reoferta de horários** (409 tratado).
- [ ] **Interativo:** no Meta, listas/botões aparecem; no Evolution, texto numerado (responder com número funciona).
- [ ] **Write-back:** cancelar/confirmar a reserva **no Horafy** → reflete em `/scheduling/appointments` do Atendefy (evento `appointment_updated`).
- [ ] **HMAC:** POST forjado em `/webhooks/horafy` com assinatura errada → **401**.
- [ ] **Cancelar fluxo:** enviar "cancelar" no meio → encerra educadamente e limpa o estado.

## 5. Segurança

- [ ] 🔴 **Tenant binding** (Horafy): token do tenant A + `X-Tenant-Slug: B` → **403**.
- [ ] 🔴 Segredos **criptografados**: API key e webhook secret no Atendefy (AES); secret/hash no Horafy. Conferir que **não vão em claro** em logs nem no `GET /scheduling/config` (só `hasApiKey`/`hasWebhookSecret`).
- [ ] 🔴 Todos os endpoints sob **TLS**; a URL do webhook é **https**.
- [ ] Token de integração de **curta duração** (cache no Redis com TTL).

## 6. Observabilidade e operação

- [ ] Logs cobrindo: token-exchange, entrega de webhook (Horafy), erros do fluxo (Atendefy).
- [ ] **Retry** do MassTransit ativo para webhooks de saída (endpoint do receptor pode cair).
- [ ] Alertas/monitoramento de **401** (assinatura/tenant) e **409** (conflito) acima do normal.
- [ ] Verificar **rate limit** / cota por integração (se aplicável).

## 7. Rollback / kill-switch

- [ ] **Desligar rápido:** Atendefy → Agenda → desativar provider Horafy (volta ao link/IA genérica).
- [ ] **Revogar acesso:** Horafy → `DELETE /integrations/api-keys/{id}` e/ou `DELETE /integrations/webhook`.
- [ ] Migration global tem `Down()` (drop tables) caso precise reverter o schema.

---

## 8. Pendências conhecidas (não bloqueantes)

- [ ] Nome do contato no **Meta Cloud** (hoje capturado só no Evolution).
- [ ] Evento **`booking.rescheduled`** no Horafy (não existe ainda) → webhook de remarcação.
- [ ] Flag **por tenant** para a supressão de notificação (hoje incondicional p/ origem de integração).
- [ ] Notificar o cliente no WhatsApp quando o estabelecimento altera a reserva no Horafy.
- [ ] Loop de re-provisionamento de tenants no startup (hoje só na criação) — relevante se houver muitos tenants legados.

---

### Ordem sugerida no deploy

1. Deploy **Horafy** → migrations globais aplicam no startup; validar tabelas.
2. (Se houver tenants legados) rodar o script `DO $$…$$`.
3. Configurar API key + webhook no Horafy (por tenant).
4. Deploy **Atendefy** → configurar provider Horafy + testar conexão + colar URL/secret do webhook.
5. Rodar o **smoke test** (seção 4) com um número de teste.
6. Acompanhar logs/alertas nas primeiras horas.
