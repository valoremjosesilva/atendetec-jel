# Deploy no Google Cloud — Guia Passo a Passo

## Visão geral do que vamos fazer

```
Seu PC → GitHub → Google Cloud VM
         (código)   (servidor online)
```

O GitHub vai construir a aplicação e enviar para o servidor automaticamente toda vez que você criar uma versão nova.

---

## ETAPA 1 — Criar conta no Google Cloud

1. Acesse https://cloud.google.com
2. Clique em **"Comece gratuitamente"**
3. Faça login com uma conta Google
4. Preencha os dados de pagamento (não se preocupe: você tem **$300 de crédito grátis por 90 dias** e não cobra nada automaticamente)
5. Após criar a conta, você vai cair no **Console do Google Cloud**

---

## ETAPA 2 — Criar o servidor (VM)

1. No menu lateral esquerdo, clique em **"Compute Engine"** → **"Instâncias de VM"**
2. Se aparecer um botão **"Ativar"**, clique nele e aguarde ~1 minuto
3. Clique em **"Criar instância"**
4. Preencha assim:

| Campo | Valor |
|-------|-------|
| Nome | `atendefy-server` |
| Região | `southamerica-east1 (São Paulo)` |
| Zona | `southamerica-east1-b` |
| Série da máquina | `E2` |
| Tipo de máquina | `e2-medium (2 vCPU, 4 GB)` |
| Sistema operacional | `Ubuntu` → `Ubuntu 24.04 LTS` |
| Tamanho do disco | `20 GB` |

5. Em **"Firewall"**, marque as duas opções:
   - Permitir tráfego HTTP
   - Permitir tráfego HTTPS

6. Clique em **"Criar"** e aguarde ~1 minuto

---

## ETAPA 3 — Reservar um IP fixo

Sem isso, o IP do servidor muda toda vez que você reinicia a VM.

1. No menu lateral, vá em **"Rede VPC"** → **"Endereços IP externos"**
2. Clique em **"Reservar endereço estático externo"**
3. Preencha:
   - Nome: `atendefy-ip`
   - Região: `southamerica-east1`
   - Conectado a: selecione `atendefy-server`
4. Clique em **"Reservar"**
5. **Anote o IP que apareceu** — você vai precisar dele (ex: `34.95.123.45`)

---

## ETAPA 4 — Configurar o DNS do domínio

Acesse o painel onde seu domínio `atendefy.com.br` está registrado e crie os seguintes registros DNS do tipo **A**:

| Nome | Tipo | Valor |
|------|------|-------|
| `app` | A | `(seu IP do passo 3)` |
| `api` | A | `(seu IP do passo 3)` |
| `evolution` | A | `(seu IP do passo 3)` |
| `monitor` | A | `(seu IP do passo 3)` |

Resultado: `app.atendefy.com.br`, `api.atendefy.com.br`, etc. vão apontar para o seu servidor.

> A propagação do DNS pode levar de 5 minutos a 1 hora.

---

## ETAPA 5 — Acessar o servidor via SSH

1. Volte no Console do Google Cloud → **Compute Engine** → **Instâncias de VM**
2. Na linha do `atendefy-server`, clique no botão **"SSH"** (abre um terminal no navegador)
3. Um terminal preto vai abrir — é o terminal do seu servidor

---

## ETAPA 6 — Instalar o Docker no servidor

No terminal do servidor, cole estes comandos um por um:

```bash
# Instalar Docker
curl -fsSL https://get.docker.com | sh

# Permitir usar Docker sem sudo
sudo usermod -aG docker $USER

# Aplicar a permissão
newgrp docker
```

Teste se funcionou:
```bash
docker --version
```
Deve mostrar algo como `Docker version 27.x.x`.

---

## ETAPA 7 — Preparar os arquivos no servidor

No terminal do servidor:

```bash
# Criar a pasta do projeto
sudo mkdir -p /opt/atendefy
sudo chown $USER:$USER /opt/atendefy
cd /opt/atendefy
```

Agora crie os 3 arquivos abaixo (use `nano <nome>`, cole o conteúdo, salve com Ctrl+O → Enter → Ctrl+X):

### Arquivo 1: docker-compose.yml
Conteúdo: copie do arquivo `infra/docker-compose.yml` do projeto.

### Arquivo 2: Caddyfile
Conteúdo: copie do arquivo `infra/Caddyfile` do projeto.

### Arquivo 3: .env
Conteúdo: copie do arquivo `infra/.env` do projeto **com as senhas reais**.

---

## ETAPA 8 — Criar a chave SSH para o GitHub

No terminal do servidor:

```bash
# Criar a chave
ssh-keygen -t ed25519 -C "github-deploy" -f ~/.ssh/github_deploy -N ""

# Autorizar a chave no servidor
cat ~/.ssh/github_deploy.pub >> ~/.ssh/authorized_keys

# Ver a chave PRIVADA (copie tudo que aparecer)
cat ~/.ssh/github_deploy
```

Copie **todo** o conteúdo que aparece (começa com `-----BEGIN OPENSSH PRIVATE KEY-----`).

---

## ETAPA 9 — Configurar os Secrets no GitHub

1. Abra seu repositório no GitHub
2. Vá em **Settings** → **Secrets and variables** → **Actions**
3. Clique em **"New repository secret"** e crie os 3 secrets abaixo:

| Nome | Valor |
|------|-------|
| `VPS_HOST` | O IP do passo 3 (ex: `34.95.123.45`) |
| `VPS_USER` | Seu usuário no servidor (o que aparece antes do `@` no terminal) |
| `VPS_SSH_KEY` | A chave privada copiada no passo 8 |

---

## ETAPA 10 — Primeiro start manual dos serviços base

O deploy automático só atualiza a API e o frontend. Na primeira vez, suba tudo manualmente no terminal do servidor:

```bash
cd /opt/atendefy
docker compose --profile production up -d
```

Aguarde ~2 minutos e verifique:
```bash
docker compose ps
```
Todos os serviços devem aparecer como `running`.

---

## ETAPA 11 — Disparar o primeiro deploy pelo GitHub

No seu computador, no terminal do projeto:

```bash
git tag v0.1.0
git push origin v0.1.0
```

Acompanhe em tempo real no GitHub: **Actions** → clique no workflow que apareceu.

---

## Resultado final

Após tudo concluído, o projeto estará acessível em:

- https://app.atendefy.com.br — frontend
- https://api.atendefy.com.br — backend
- https://monitor.atendefy.com.br — monitoramento (Uptime Kuma)
- https://evolution.atendefy.com.br — WhatsApp API
