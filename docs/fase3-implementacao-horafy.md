# Fase 3 — Atendefy: mensagens interativas (listas/botões)

> Data: 2026-06-26 · Plano: `docs/plano-execucao-integracao-horafy.md`
> Pré-requisito: Fase 2 do Atendefy (fluxo de agendamento por texto).
> Status: **código escrito**. Falta compilar/testar (sem SDK .NET no ambiente de geração).

## Escopo entregue (A6)

- Abstração `InteractiveMessage` (lista/botões) + `IWhatsAppProvider.SendInteractiveAsync` com
  **fallback automático para texto numerado**.
- **Meta Cloud**: envio nativo de **lista** (serviços/profissionais/dias/horários) e **botões**
  (confirmação Sim/Não), e leitura das **respostas interativas** no webhook.
- O fluxo de agendamento agora devolve `BookingFlowReply` (texto + interativo); o `ConversationWorker`
  envia interativo quando há suporte.
- **Evolution/Baileys**: usa o fallback de texto numerado (o WhatsApp descontinuou listas/botões via
  Baileys). A experiência continua idêntica funcionalmente (responder com número).

## Como funciona

- Cada passo de escolha vira uma **lista** (até 9 linhas); a confirmação vira **2 botões** (Sim/Não).
- O `id` de cada opção é o id real (GUID do serviço/profissional, data `yyyy-MM-dd`, horário ISO).
  Ao tocar, o WhatsApp devolve esse `id`; o webhook o usa como "texto" e o `ResolveAsync` casa
  **por id** (antes de número/texto/IA).
- Limites do WhatsApp respeitados (lista: ≤10 linhas, título ≤24; botões: ≤3, título ≤20; corpo ≤1024).
- Em canais sem interativo, `InteractiveText.Render` (ou o `FallbackText` do próprio fluxo) entrega
  a versão numerada — e o cliente responde com o número normalmente.

## Arquivos criados

```
src/Atendefy.API/Modules/WhatsApp/Models/InteractiveMessage.cs   (modelo + renderer de fallback)
```

## Arquivos alterados

```
src/Atendefy.API/Modules/WhatsApp/IWhatsAppProvider.cs                 (+ SendInteractiveAsync default)
src/Atendefy.API/Modules/WhatsApp/MetaCloudProvider.cs                 (+ envio interativo nativo)
src/Atendefy.API/Modules/Webhooks/Models/MetaWebhookPayload.cs         (+ interactive/button_reply/list_reply)
src/Atendefy.API/Modules/Webhooks/WebhookEndpoints.cs                  (lê resposta interativa do Meta)
src/Atendefy.API/Modules/Scheduling/Horafy/BookingFlowState.cs         (+ BookingFlowReply)
src/Atendefy.API/Modules/Scheduling/Horafy/BookingFlowService.cs       (retorna texto+interativo; casa por id)
src/Atendefy.API/Modules/Chatbot/ConversationWorker.cs                 (DeliverReplyAsync envia interativo)
```

## Banco de dados / infra

- **Nada novo.** Sem migração, sem novas dependências.

## Como testar (manual)

```
1. Conta WhatsApp via Meta Cloud, tenant com Horafy ativo.
2. "quero agendar" → deve chegar uma LISTA de serviços (toque para escolher).
3. Seguir profissional/dia/horário por toque; confirmação com BOTÕES Sim/Não.
4. Conferir a reserva no Horafy (source=atendefy).
5. Repetir numa conta Evolution → as mesmas etapas chegam como TEXTO numerado (responder com número).
```

## Checklist de verificação

- [ ] `dotnet build` (API) sem erros.
- [ ] Meta: listas e botões renderizam; tocar devolve o id e avança o passo.
- [ ] Evolution: fallback de texto numerado; responder com número avança o passo.
- [ ] Confirmação por botão (id `sim`/`nao`) é interpretada corretamente.
- [ ] Respostas interativas não quebram o `ResolveAsync` (casa por id).
- [ ] Histórico/painel mostram a versão em texto da mensagem.

## Limitações / follow-ups

- **Evolution interativo**: não enviado de propósito (descontinuado pelo WhatsApp via Baileys).
  Se uma versão suportar no futuro, basta sobrescrever `SendInteractiveAsync` em `EvolutionProvider`.
- Listas longas são truncadas a 9 opções; paginação ("ver mais") pode ser adicionada se necessário.

## Próxima (Fase 4 — Atendefy)

A8 — intake do webhook `/webhooks/horafy` (HMAC) para refletir no painel/cliente as mudanças feitas
fora do WhatsApp (portal/staff no Horafy): parser + rota + `SchedulingService.UpsertAppointmentAsync`.
