# Plano 006: Adicionar job de CI para o frontend (build + typecheck)

> **Instruções para o executor**: Siga este plano passo a passo. Execute cada verificação antes
> de avançar. Em condições de PARE, pare e reporte — não improvise.
>
> **Verificação de drift (execute primeiro)**:
> `git diff --stat e805859..HEAD -- .github/workflows/ci.yml`
> Em caso de divergência, trate como PARE.

## Status

- **Prioridade**: P1
- **Esforço**: M (Médio — ~1 dia, incluindo investigação e correção de erros TS latentes)
- **Risco**: MÉDIO — o typecheck pode revelar erros TypeScript já existentes no código; corrigi-los
  é parte do escopo deste plano
- **Depende de**: nenhum
- **Categoria**: DX / tooling
- **Escrito no commit**: `e805859`, 2026-06-28

## Por que isso importa

O CI atual (`.github/workflows/ci.yml`) roda apenas o pipeline .NET: restore, build, test. O
frontend React/TypeScript nunca é compilado nem tipado no CI. Isso significa que erros de
TypeScript, imports quebrados e problemas de build do Vite podem ser mergeados na `main` sem
qualquer alarme — só sendo descobertos no deploy (quando a imagem Docker é buildada). Adicionar
um job `build-frontend` que roda `tsc --noEmit` e `npm run build` fecha essa lacuna e garante
que `main` sempre tem um frontend buildável.

## Estado atual

**`.github/workflows/ci.yml` — arquivo completo:**
```yaml
name: CI

on:
  push:
    branches: [main, develop]
  pull_request:
    branches: [main]

jobs:
  test:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4

      - name: Setup .NET 10
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: "10.0.x"

      - name: Restore
        run: dotnet restore Atendefy.slnx

      - name: Build
        run: dotnet build Atendefy.slnx --no-restore -c Release

      - name: Test
        run: dotnet test Atendefy.slnx --no-build -c Release --logger trx --collect:"XPlat Code Coverage"

      - name: Upload coverage
        uses: codecov/codecov-action@v4
        with:
          token: ${{ secrets.CODECOV_TOKEN }}
          fail_ci_if_error: false
```

**`src/Atendefy.Web/package.json` — scripts disponíveis:**
```json
{
  "scripts": {
    "dev": "vite",
    "build": "tsc && vite build",
    "preview": "vite preview"
  }
}
```

> ⚠️ O script `build` já roda `tsc` antes de `vite build`. Isso significa que `npm run build`
> já é suficiente para verificar tipos E gerar o bundle. Não é necessário chamar `tsc --noEmit`
> separadamente.

**Versão do Node necessária:** o `package.json` usa React 19 e Vite 8. O ambiente `ubuntu-latest`
no GitHub Actions tem Node LTS disponível. Use `node-version: '22'` (LTS atual).

## Comandos necessários

| Propósito            | Comando                                             | Esperado     |
|----------------------|-----------------------------------------------------|--------------|
| Verificar build local | `cd src/Atendefy.Web && npm ci && npm run build`   | exit 0       |
| Verificar CI push    | Push para branch e observar GitHub Actions           | job verde    |

## Escopo

**Em escopo**:
- `.github/workflows/ci.yml`

**Fora do escopo** (não tocar):
- `package.json`, `tsconfig.json`, `vite.config.ts` — não alterar a configuração do projeto
- `.github/workflows/deploy.yml` — pipeline de deploy não muda
- Código fonte do frontend — se `npm run build` falhar por erros TS, corrija-os em commits
  separados **antes** de fazer merge deste job no CI (ver Passo 2)

## Git workflow

- Branch: `advisor/006-frontend-ci-job`
- Commits: um para o `ci.yml`, commits separados para correções de TS se necessário
- Mensagem: `ci: adicionar job de build do frontend (typecheck + vite)`

## Passos

### Passo 1: Verificar o build do frontend localmente

Antes de mudar o CI, confirme que o frontend builda localmente:

```bash
cd src/Atendefy.Web
npm ci
npm run build
```

**Verificar**: exit 0 e diretório `dist/` gerado

Se houver erros de TypeScript, corrija-os em commits separados antes de continuar. Erros comuns
esperados:
- Props não utilizadas que foram removidas
- Tipos `any` implícitos em versões mais novas do TypeScript
- Imports de módulos não encontrados

Documente cada correção em seu próprio commit com mensagem descritiva.

### Passo 2: Adicionar o job `build-frontend` ao `ci.yml`

Adicione o seguinte job ao final do arquivo `.github/workflows/ci.yml`, **após** o job `test`
existente:

```yaml
  build-frontend:
    runs-on: ubuntu-latest
    defaults:
      run:
        working-directory: src/Atendefy.Web
    steps:
      - uses: actions/checkout@v4

      - name: Setup Node.js
        uses: actions/setup-node@v4
        with:
          node-version: '22'
          cache: 'npm'
          cache-dependency-path: src/Atendefy.Web/package-lock.json

      - name: Install dependencies
        run: npm ci

      - name: Build (typecheck + bundle)
        run: npm run build
```

O arquivo final do `ci.yml` terá dois jobs independentes (`test` e `build-frontend`) que rodam
em paralelo. Isso é correto — nenhum depende do outro.

**Verificar**: o YAML é válido. Use um linter YAML online ou:
```bash
python3 -c "import yaml; yaml.safe_load(open('.github/workflows/ci.yml'))"
```

### Passo 3: Fazer push e confirmar no GitHub Actions

Faça commit e push da branch. Acesse a aba "Actions" do repositório e confirme que:
- O job `test` (existente) ainda passa
- O job `build-frontend` (novo) aparece e fica verde

**Verificar**: ambos os jobs passam no GitHub Actions

## Critérios de conclusão

- [ ] `.github/workflows/ci.yml` tem um job `build-frontend`
- [ ] O job usa `actions/setup-node@v4` com `node-version: '22'` e cache de npm
- [ ] O job roda `npm ci` e `npm run build` no diretório `src/Atendefy.Web`
- [ ] O job passa no GitHub Actions (push da branch confirma)
- [ ] O job `test` existente não regrediu
- [ ] Apenas `.github/workflows/ci.yml` foi modificado (+ eventuais correções de TS em commits separados)
- [ ] Status atualizado em `plans/README.md`

## Condições de PARE

- `npm run build` falha localmente com mais de ~10 erros de TypeScript — avalie se é melhor
  primeiro adicionar `"skipLibCheck": true` ao `tsconfig.json` como medida temporária, ou se
  os erros são simples de corrigir. Reporte antes de commitar qualquer mudança de configuração
- `package-lock.json` não existe — substituir `cache: 'npm'` por `cache: 'npm'` com
  `cache-dependency-path: src/Atendefy.Web/package.json` ou usar `npm install` em vez de `npm ci`
- GitHub Actions não tem acesso a `secrets.CODECOV_TOKEN` e isso estiver causando falha — esse
  problema é pré-existente no job `test`, não relacionado a este plano; reporte separadamente

## Notas de manutenção

- O `cache: 'npm'` com `cache-dependency-path` fará cache dos `node_modules` entre runs do CI.
  Se dependências forem atualizadas sem atualizar o `package-lock.json`, o cache pode ficar stale.
  Sempre commitar `package-lock.json` junto com mudanças de `package.json`.
- Follow-up sugerido: adicionar `npm run lint` ao job quando ESLint for configurado (Plano 008
  do conjunto de melhorias — ou veja achado DX-02).
- O job não faz upload do bundle `dist/` como artifact pois o deploy usa Docker (que roda
  `npm run build` internamente). Se no futuro o deploy mudar para usar o artifact do CI, ajustar.
