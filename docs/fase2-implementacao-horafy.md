# Fase 2 — Atendefy: agendamento conversacional no WhatsApp

> Data: 2026-06-26 · Plano: `docs/plano-execucao-integracao-horafy.md`
> Pré-requisito: Fase 1 do Atendefy (provider horafy + `HorafyClient`) e Fases 1–2 do Horafy.
> Status: **código escrito**. Falta compilar/testar (sem SDK .NET no ambiente de geração).

## Escopo entregue

- **A3** — Estado do fluxo (`BookingFlowState`) por (tenant, telefone) no Redis, TTL 15 min.
- **A4** — Orquestrador `BookingFlowService`: máquina de estados serviço → profissional → dia →
  horário → confirmação → criação no Horafy.
- **A5** — Detecção de intenção (regex existente) e resolução da escolha: número → texto → **IA**.
- **A10** — Bordas: sem serviços/profissionais/dias/horários, conflito 409 (reoferta horários),
  erros transitórios, expiração da sessão e cancelamento (`cancelar/parar/...`).

## Fluxo (texto numerado)

```
Cliente: "quero agendar"
Bot:  Vamos agendar! Qual serviço você deseja?
      1. Corte (30min)
      2. Barba (20min)
Cliente: 1
Bot:  Com qual profissional você prefere?
      1. Ana — Cabeleireira
      2. Bruno
Cliente: ana
Bot:  Para qual dia?
      1. seg 30/06
      2. ter 01/07
Cliente: 2
Bot:  Horários livres em ter 01/07:
      1. 09:00   2. 09:30   3. 14:00
Cliente: 14:00
Bot:  Confirma o agendamento?
      • Serviço: Corte • Profissional: Ana • Dia: ter 01/07 • Horário: 14:00
      Responda SIM para confirmar ou NÃO para cancelar.
Cliente: sim
Bot:  Pronto! Agendamento confirmado: Corte com Ana em ter 01/07 às 14:00. ✅
```

A resolução de cada resposta aceita **número**, **texto** (ex.: "ana", "de tarde") por
correspondência, e, em último caso, **IA** (mapeia a frase para a opção). O `externalId`
(`atendefy:{telefone}:{aaaaMMddHHmm}`) garante idempotência no Horafy.

## Integração no `ConversationWorker`

Antes do caminho da IA genérica, quando o tenant tem provider `horafy` ativo e há **fluxo ativo**
ou **intenção de agendar**, o worker monta a `HorafyConnection` (descriptografando a chave) e
delega ao `BookingFlowService`. A resposta é entregue por `DeliverReplyAsync` (persiste a conversa,
notifica o painel e envia pelo WhatsApp) e o processamento encerra — sem acionar a IA genérica.

- Não conta no teto mensal de IA (mensagens transacionais do fluxo).
- A IA é usada só pontualmente, para interpretar respostas livres.

## Arquivos criados

```
src/Atendefy.API/Modules/Scheduling/Horafy/BookingFlowState.cs    (estado + opções + passos)
src/Atendefy.API/Modules/Scheduling/Horafy/BookingFlowService.cs  (máquina de estados)
```

## Arquivos alterados

```
src/Atendefy.API/Modules/Chatbot/ConversationWorker.cs   (entrada do fluxo + DeliverReplyAsync + ctor)
src/Atendefy.API/Program.cs                              (registro BookingFlowService + ctor do worker)
```

## Banco de dados / infra

- **Nada novo.** Usa o Redis já existente (estado do fluxo) e o `HorafyClient` da Fase 1.
- Não há migração nem novas tabelas.

## Como testar (manual)

```
1. Tenant com plano que permite Agenda (SchedulingEnabled) e provider "horafy" salvo+testado.
2. No WhatsApp do tenant, enviar "quero agendar".
3. Seguir os passos (responder com números). Conferir no Horafy que a reserva foi criada
   (source=atendefy) e que o Horafy NÃO enviou notificação (supressão da Fase 2 do Horafy).
4. Reenviar o mesmo fluxo até o mesmo horário → idempotência (não duplica).
5. Enviar "cancelar" no meio → fluxo encerra educadamente.
```

## Checklist de verificação

- [ ] `dotnet build` (API) sem erros.
- [ ] Intenção ("quero agendar") inicia o fluxo; mensagens fora do fluxo seguem na IA normal.
- [ ] Serviço único / `DefaultServiceId` pula o passo de serviço.
- [ ] Profissional único pula o passo de profissional.
- [ ] Dia sem horários reoferta dias; conflito 409 reoferta horários.
- [ ] Confirmação cria no Horafy; resposta de sucesso no WhatsApp.
- [ ] Sessão expira em 15 min; "cancelar" limpa o estado.
- [ ] Reserva criada NÃO gera notificação duplicada (número do Horafy).

## Limitações / follow-ups

- **Listas interativas** (botões/listas nativas do WhatsApp) — Fase 3 (A6). Hoje é texto numerado.
- **Write-back** (`/webhooks/horafy`) — Fase 4 (A8): refletir no painel/cliente mudanças feitas
  fora do WhatsApp (portal/staff no Horafy).
- **"Voltar" um passo** não implementado (só "cancelar"); pode ser adicionado no `BookingFlowService`.
- Fuso horário: os horários são exibidos como o Horafy os retorna (mesma convenção do Horafy).

## Próximas fases (Atendefy)

- **Fase 3** — A6: mensagens interativas (listas/botões) no `IWhatsAppProvider` (Evolution/Meta).
- **Fase 4** — A8: intake do webhook `/webhooks/horafy` (HMAC) → `Appointment` + painel.
