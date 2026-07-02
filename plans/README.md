# Planos de Melhoria — Atendefy / Mensagee

Rodada 1 gerada pelo advisor (`/improve deep`) em 2026-06-28, commit `e805859`.
Rodada 2 gerada em 2026-07-02, commit `f809720`.
Execute na ordem abaixo, a menos que as dependências indiquem outra coisa.
Cada executor: leia o plano inteiro antes de começar, respeite as condições de PARE e atualize
a coluna de status ao terminar.

## Ordem de execução e status

| Plano | Título                                          | Prioridade | Esforço | Depende de | Status |
|-------|-------------------------------------------------|------------|---------|------------|--------|
| [001](001-ai-provider-safe-parsing.md) | Provider de IA: parsing seguro de JSON | P1 | P | —   | DONE |
| [002](002-aiconfig-redis-cache.md)     | Cachear AiConfig no Redis              | P1 | P | —   | DONE |
| [003](003-webhook-deduplication.md)    | Deduplicar mensagens de webhook        | P1 | M | —   | DONE |
| [004](004-billing-webhook-idempotency.md) | Cobrança: webhook idempotente       | P1 | M | —   | DONE |
| [005](005-message-history-pagination.md)  | Paginar histórico de mensagens      | P1 | M | —   | DONE |
| [006](006-frontend-ci-job.md)          | CI: job de build do frontend           | P1 | M | —   | DONE |
| [007](007-tenant-isolation-tests.md)   | Testes de isolamento entre tenants     | P1 | P | —   | DONE |
| [008](008-claude-md.md)               | Criar CLAUDE.md                         | P2 | P | —   | DONE |
| [009](009-stream-ack-reliability.md)  | Não perder mensagens em falha do worker (PEL + dead-letter) | P1 | M | — | TODO |
| [010](010-webhook-dedup-atomic.md)    | Dedup de webhook atômica (SET NX)       | P1 | S | —   | TODO |
| [011](011-hot-path-indexes.md)        | Índices do caminho quente (contact_phone, subscriptions) | P1 | S | — | TODO |
| [012](012-auth-rate-limiting.md)      | Rate-limit em login/refresh/verify-email | P1 | S | —  | TODO |
| [013](013-billing-subscription-atomic-save.md) | Subscription+Invoice num único SaveChanges | P2 | S | — | TODO |
| [014](014-frontend-deps-cleanup.md)   | Frontend: remover shadcn CLI + axios 1.18 (npm audit zero) | P2 | S | — | TODO |

**Legenda de esforço:** P/S = Pequeno (horas) · M = Médio (~1 dia) · G/L = Grande (vários dias)

**Valores de status:** TODO | IN PROGRESS | DONE | BLOCKED (com motivo) | REJECTED (com motivo)

## Notas de dependência

- 009–014 são independentes entre si; podem rodar em paralelo por executores diferentes.
- 009 e 010 tocam arquivos diferentes do mesmo domínio (worker vs endpoint) — merges tranquilos.
- 011 muda o hash do `TenantSchemaMigrator.PatchSqlTemplate`: o primeiro boot após o deploy
  reaplica o patch em todos os tenants (uma vez, esperado).

## Achados considerados e rejeitados (não valem plano / não re-auditar)

Rodada 1 (2026-06-28):
- **PERF-01 (N+1 na lista de conversas)**: `c.Messages.Max()` gera subquery correlacionada — uma query só. Não é N+1.
- **CORRECTNESS-09 (estados de erro no React)**: lacuna de UX, não bug. Deferred.
- **PERF-11 (virtualização da lista de conversas)**: só relevante acima de ~500 conversas/tenant. Deferred.
- **DEPS-07 (updates menores de npm)**: manutenção rotineira.
- **CORRECTNESS-05 (race na criação de conversa)**: latente com worker single-consumer.

Rodada 2 (2026-07-02):
- **Fallback interativo no EvolutionProvider**: `IWhatsAppProvider.SendInteractiveAsync` tem
  default interface implementation que degrada para texto numerado — já resolvido by-design.
- **"Horafy não compila"**: docs `docs/fase1-implementacao-horafy.md` estão desatualizadas;
  a solução compila e passa a suíte (117 testes) — o gap real é cobertura de testes (registrado abaixo).
- **setState em componente desmontado (VerifyEmailPage)**: React 18+ eliminou o warning; sem efeito real.
- **codecov `fail_ci_if_error: true`**: faria o CI falhar por instabilidade de serviço externo. Manter false.
- **Índice composto em webhook_routes (Provider, LookupKey)**: LookupKey já tem índice único; ganho marginal.
- **Credenciais untracked em `docs/`** (api-key-*.md etc.): ação manual do operador (mover para fora
  do repo), não plano de código. O histórico git já foi limpo e as chaves rotacionadas (2026-07-01).

## Achados registrados sem plano nesta rodada (candidatos a rodadas futuras)

- **Testes dos caminhos críticos** (ConversationWorker, BookingFlowService, HorafyClient,
  PersistAsync, EntitlementsService, IncrementWithTtl, webhook Meta e2e) — maior gap de qualidade; esforço M–L.
- **Cal.com webhook sem HMAC** (hoje só token-capability na query; Horafy no mesmo arquivo valida assinatura).
- **PII (telefone) em ≥8 pontos de log** do ConversationWorker/BookingFlow — LGPD/minimização.
- **Code-splitting por rota** no frontend (bundle único de 647KB).
- **EntitlementsService**: 2 queries públicas por mensagem → 1 join ou cache Redis.
- **CI sem cache de NuGet**; **deploy sem health-check pós-up**.
- **`ResolveTenantAsync` duplicado em 4 endpoints**; **merge PersistAsync/PersistUserOnlyAsync**.
- **README desatualizado** (diz que deploy nunca ocorreu; faltam ~10 endpoints na referência; portas 5173/3001).
- **Testcontainers** (Postgres/Redis reais) para a suíte de integração; **testes de frontend** (vitest).
- **Unificar migração de tenant em EF** (hoje: provisioner SQL + patch por hash); **OpenAPI→TS typegen**;
  **paginação nos endpoints admin**; **Serilog RequestId**; **TreatWarningsAsErrors**; **ESLint/Prettier**.
- **Direção** (opções de produto): Team Members (entitlement existe, feature não), dashboard analytics
  (taxa de resolução/tempo de resposta), broadcast/templates, tags de contatos.
