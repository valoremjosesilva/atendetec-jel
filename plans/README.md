# Planos de Melhoria — Atendefy / Mensagee

Gerado pelo advisor (`/improve deep`) em 2026-06-28, commit `e805859`.
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

**Legenda de esforço:** P = Pequeno (horas) · M = Médio (~1 dia) · G = Grande (vários dias)

**Valores de status:** TODO | IN PROGRESS | DONE | BLOCKED (com motivo) | REJECTED (com motivo)

## Notas de dependência

Todos os planos são independentes entre si e podem ser executados em qualquer ordem ou em
paralelo por executores diferentes. A ordem acima reflete apenas a prioridade de impacto.

Sugestão de agrupamento para execução paralela:
- **Grupo A** (pequenos, backend): 001 + 002 + 007
- **Grupo B** (médios, backend): 003 + 004 + 005
- **Grupo C** (infraestrutura): 006 + 008

## Achados considerados e rejeitados (não valem plano)

- **PERF-01 (N+1 na lista de conversas)**: `c.Messages.Max()` em `.Select()` gera subquery SQL
  correlacionada — uma só query, não N round-trips. Não é N+1 real. Não vale plano.
- **CORRECTNESS-09 (estados de erro no React)**: optional chaining já previne crashes; é lacuna
  de UX, não bug crítico. Deferred.
- **PERF-11 (virtualização de lista de conversas)**: só relevante acima de ~500 conversas por
  tenant; maioria não chega lá cedo. Deferred.
- **DEPS-07 (updates menores de npm)**: manutenção rotineira; sem plano dedicado.
- **CORRECTNESS-05 (race condition na criação de conversa)**: arquitetura single-worker do
  ConversationWorker torna isso latente; baixa prioridade até escalar.

## Achados fora do escopo destes planos (ação manual ou sessão futura)

- **Credenciais na raiz do repo** (Security #1): `ssh-key-2026-06-10.key`, `SECRET_GITUHUB.txt`
  e outros arquivos de credencial precisam de **rotação imediata** + `git filter-repo` para
  limpar o histórico. Esta é uma ação manual, não um plano de código.
- **Upgrade NuGet para .NET 10** (Deps #8): Npgsql, JwtBearer, EFCore.NamingConventions na
  versão 8.x enquanto o runtime é .NET 10. Upgrade coordenado recomendado em sessão separada.
- **JWT em localStorage** (Security #12): migração para HttpOnly cookie é uma mudança de
  arquitetura de auth — sessão própria.
- **Evolution API `:latest`** (Deps #16): fixar versão em `docker-compose.yml` é uma linha de
  mudança; pode ser feito ad-hoc sem plano formal.
