# Plan 014: Limpar dependências do frontend (remover shadcn CLI, atualizar axios)

> **Executor instructions**: Follow this plan step by step. Run every
> verification command and confirm the expected result before moving to the
> next step. If anything in the "STOP conditions" section occurs, stop and
> report — do not improvise. When done, update the status row for this plan
> in `plans/README.md`.
>
> **Drift check (run first)**: `git diff --stat f809720..HEAD -- src/Atendefy.Web/package.json src/Atendefy.Web/package-lock.json`
> If any in-scope file changed since this plan was written, compare the
> "Current state" excerpts against the live code before proceeding; on a
> mismatch, treat it as a STOP condition.

## Status

- **Priority**: P2
- **Effort**: S
- **Risk**: LOW
- **Depends on**: none
- **Category**: security | deps
- **Planned at**: commit `f809720`, 2026-07-02

## Why this matters

O pacote `shadcn` (a **CLI de scaffolding** do shadcn/ui, não uma biblioteca de
runtime) está em `dependencies` do frontend. Nenhum arquivo em `src/` a importa
(verificado: `grep "from 'shadcn'"` → zero). É dela que vem TODA a saída do
`npm audit`: a CLI depende de `@modelcontextprotocol/sdk` → `hono` (advisories
HIGH: path traversal, CORS, header handling) e infla o install. Além disso,
`axios ^1.17.0` resolve `form-data` 4.0.0–4.0.5 (advisory de CRLF injection —
só afeta uso em Node, não o browser bundle, mas suja o audit). Remover a CLI e
subir o axios zera o `npm audit` sem tocar em código de runtime.

## Current state

Arquivo: `src/Atendefy.Web/package.json` — `dependencies` hoje incluem:

```json
"axios": "^1.17.0",
...
"shadcn": "^4.10.0",
```

Fatos verificados no vetting:
- `grep -rn "from 'shadcn'" src/Atendefy.Web/src` → **zero matches** (a CLI nunca
  é importada; os componentes shadcn/ui já foram copiados para
  `src/Atendefy.Web/src/components/ui/` e são código próprio do repo).
- `npm audit` atual acusa `form-data` (via axios) e `hono` (via shadcn →
  @modelcontextprotocol/sdk) como HIGH.
- O fix do form-data está em axios ≥ 1.18.1 (resolve form-data ≥ 4.0.6).

Comandos do repo (frontend): `npm ci`, `npm run build` (tsc + vite),
`npx tsc --noEmit` para typecheck isolado. CI roda `npm ci && npm run build`
no job `build-frontend` (`.github/workflows/ci.yml`).

## Commands you will need

Todos executados DENTRO de `src/Atendefy.Web`:

| Purpose   | Command                    | Expected on success |
|-----------|----------------------------|---------------------|
| Instalar  | `npm install`              | exit 0, lockfile atualizado |
| Typecheck | `npx tsc --noEmit`         | exit 0 |
| Build     | `npm run build`            | exit 0, `vite build` conclui |
| Audit     | `npm audit --omit=dev`     | 0 vulnerabilities (high/critical) |

## Scope

**In scope**:
- `src/Atendefy.Web/package.json`
- `src/Atendefy.Web/package-lock.json` (regenerado pelo npm)

**Out of scope**:
- Qualquer arquivo em `src/Atendefy.Web/src/` — os componentes em
  `components/ui/` são código do repo, não dependem do pacote `shadcn`.
- Outras atualizações de versão (react-router, lucide, tailwind etc.) — a
  auditoria as classificou como higiene de baixa prioridade, fora deste plano.
- `package.json` de qualquer outro diretório.

## Git workflow

- Branch: `advisor/014-frontend-deps-cleanup`
- Conventional commit em português (ex.: `chore(web): remover shadcn CLI das dependências e atualizar axios`)
- Não fazer push nem abrir PR sem instrução do operador.

## Steps

### Step 1: Remover `shadcn` das dependencies

Em `src/Atendefy.Web/package.json`, apagar a linha `"shadcn": "^4.10.0",` de
`dependencies`. (Quando alguém precisar scaffoldar um componente novo, usa
`npx shadcn@latest add <componente>` sem instalar — anotar isso no commit.)

**Verify**: `grep -c "shadcn" src/Atendefy.Web/package.json` → 0.

### Step 2: Atualizar axios

Em `package.json`, mudar `"axios": "^1.17.0"` para `"axios": "^1.18.1"`.

**Verify**: `grep -n "axios" src/Atendefy.Web/package.json` → mostra `^1.18.1`.

### Step 3: Regenerar lockfile e validar

Dentro de `src/Atendefy.Web`:

```
npm install
npm audit --omit=dev
npx tsc --noEmit
npm run build
```

**Verify**:
- `npm audit --omit=dev` → `found 0 vulnerabilities` (ou, no mínimo, zero HIGH/CRITICAL;
  se sobrar algo LOW/MODERATE não relacionado a shadcn/form-data, apenas registre no commit)
- `npx tsc --noEmit` → exit 0
- `npm run build` → exit 0

### Step 4: Sanidade do axios 1.18 no código

O repo usa axios em `src/api/client.ts` e `src/api/adminClient.ts` (interceptors,
`withCredentials`). axios 1.18 é minor sem breaking changes — mas confirme que o
build tipa: o Step 3 (`tsc --noEmit`) já cobre. Nada a editar.

**Verify**: `npm run build` (já rodado) → exit 0.

## Test plan

- Não há testes de frontend no repo (finding separado, não selecionado).
- Gates: `tsc --noEmit` + `vite build` + `npm audit` limpos (Steps 3).
- Smoke manual opcional: `npm run dev` e logar no painel local.

## Done criteria

- [ ] `grep -c "shadcn" src/Atendefy.Web/package.json` → 0
- [ ] `grep -c "\"hono\"" src/Atendefy.Web/package-lock.json` → 0
- [ ] `npm audit --omit=dev` (em src/Atendefy.Web) → 0 HIGH/CRITICAL
- [ ] `npm run build` → exit 0
- [ ] `git status` — só package.json e package-lock.json modificados
- [ ] Linha do plano 014 atualizada em `plans/README.md`

## STOP conditions

- `grep "from 'shadcn'"` encontrar QUALQUER import real em `src/` (significa que
  o vetting envelheceu — não remover, reportar).
- `npm install` falhar por conflito de peer dependencies com axios 1.18.
- O build quebrar após a remoção por algum plugin/config que referencie o pacote
  `shadcn` (checar `vite.config.ts` e `components.json` — se `components.json`
  referenciar a CLI, tudo bem: ela roda via `npx`, não precisa estar instalada).

## Maintenance notes

- Documentar para o time: componentes novos do shadcn/ui via
  `npx shadcn@latest add <nome>` (a CLI não é mais dependência do projeto).
- `npm audit` limpo vira sinal útil: qualquer HIGH novo passa a ser acionável.
- Follow-up deferido: batch de minors do frontend (lucide, react-router etc.) —
  higiene, sem urgência.
