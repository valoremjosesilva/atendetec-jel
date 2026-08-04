#!/bin/bash
# ============================================================
#  MJML / JEL Consultoria — Setup Fase 1
#  Hetzner Cloud CPX41 (Ashburn, US-East) + Coolify
#
#  ESCOPO: este script prepara a VPS que hospeda a PLATAFORMA
#  INTEIRA — Atende, Agenda, Alugue, Delivery e Loja — não só o
#  Atendefy. Ele mora neste repositório por ser o único com uma
#  pasta infra/ estabelecida; se um dia existir um repo de
#  infraestrutura próprio, é para lá que ele deve ir.
#
#  Roda UMA VEZ por VPS, antes de qualquer deploy. Depois dele,
#  tudo é feito pela UI do Coolify.
#
#  Uso:
#    scp infra/fase1-setup.sh root@IP_DA_VPS:/root/
#    ssh root@IP_DA_VPS
#    chmod +x fase1-setup.sh && ./fase1-setup.sh
#
#  Pré-requisitos:
#    - VPS Ubuntu 24.04 LTS (Hetzner Cloud — Ashburn/US-East)
#    - Acesso root via chave SSH (senha desabilitada)
#    - DNS em modo DNS-only (nuvem CINZA no Cloudflare) apontando
#      para o IP da VPS. O Traefik do Coolify emite o certificado
#      via HTTP-01, que falha com o proxy do Cloudflare na frente.
#      Ligue a nuvem laranja só depois dos certificados emitidos.
#
#        painel.mjml.com.br → IP_DA_VPS
#        mjml.com.br        → IP_DA_VPS
#
#  O que este script NÃO faz (de propósito):
#    - não cria .env em disco: no Coolify as variáveis de ambiente
#      ficam na UI, junto ao recurso. Arquivo .env solto vira uma
#      segunda fonte de verdade que sai de sincronia.
#    - não gera segredos em texto puro: use o gerador do Coolify e
#      guarde no Bitwarden/1Password.
#    - não cria /opt/jel/: o Coolify gerencia os volumes.
# ============================================================

set -euo pipefail
RED='\033[0;31m'; GREEN='\033[0;32m'; YELLOW='\033[1;33m'; CYAN='\033[0;36m'; NC='\033[0m'
info()  { echo -e "${CYAN}[INFO]${NC}  $1"; }
ok()    { echo -e "${GREEN}[OK]${NC}    $1"; }
warn()  { echo -e "${YELLOW}[WARN]${NC}  $1"; }
error() { echo -e "${RED}[ERROR]${NC} $1"; exit 1; }

echo ""
echo "╔══════════════════════════════════════════════════╗"
echo "║   MJML — Setup Fase 1 · Hetzner + Coolify        ║"
echo "╚══════════════════════════════════════════════════╝"
echo ""

[[ $EUID -ne 0 ]] && error "Execute como root."

# ── 0. SANIDADE ─────────────────────────────────────────────
info "Verificando recursos da máquina..."
TOTAL_RAM_MB=$(free -m | awk '/^Mem:/{print $2}')
DISK_GB=$(df -BG --output=size / | tail -1 | tr -dc '0-9')

if (( TOTAL_RAM_MB < 7500 )); then
  warn "RAM detectada: ${TOTAL_RAM_MB} MB."
  warn "Os 3 serviços da Fase 1 + Coolify + Postgres/Redis/RabbitMQ pedem ~3,4 GB."
  warn "O plano assume CX33 (8 GB). Abaixo disso vai faltar folga."
  read -rp "Continuar mesmo assim? [s/N] " CONFIRMA
  [[ "${CONFIRMA,,}" == "s" ]] || error "Abortado. Redimensione a VPS e rode de novo."
else
  ok "RAM: ${TOTAL_RAM_MB} MB — suficiente."
fi
ok "Disco: ${DISK_GB} GB."

# ── 1. SISTEMA ──────────────────────────────────────────────
info "Atualizando sistema..."
export DEBIAN_FRONTEND=noninteractive
apt-get update -qq
apt-get upgrade -y -qq
apt-get install -y -qq curl git ufw fail2ban unattended-upgrades jq nmap
ok "Sistema atualizado."

# ── 2. FIREWALL ─────────────────────────────────────────────
# Porta 8000 (UI do Coolify) fica FECHADA. O primeiro acesso é por
# túnel SSH — ver instruções no fim. Isso evita expor o painel com
# a senha de admin ainda não definida.
info "Configurando firewall (UFW)..."
ufw default deny incoming
ufw default allow outgoing
ufw allow 22/tcp   comment 'SSH'
ufw allow 80/tcp   comment 'HTTP - Traefik/ACME'
ufw allow 443/tcp  comment 'HTTPS - Traefik'
ufw allow 443/udp  comment 'HTTP/3'
ufw --force enable
ok "Firewall ativo: 22, 80, 443. Porta 8000 fechada de propósito."

# ── 3. FAIL2BAN ─────────────────────────────────────────────
info "Ativando fail2ban..."
systemctl enable fail2ban --now
ok "Fail2ban ativo (proteção SSH)."

# ── 4. HARDENING SSH ────────────────────────────────────────
info "Endurecendo SSH..."
SSHD_CONF=/etc/ssh/sshd_config.d/99-mjml.conf
if [[ -f /root/.ssh/authorized_keys && -s /root/.ssh/authorized_keys ]]; then
  cat > "$SSHD_CONF" <<'EOF'
PasswordAuthentication no
PermitRootLogin prohibit-password
EOF
  # Em Ubuntu 24.04+ o sshd é ativado por socket (ssh.socket): não há
  # ssh.service ativo para recarregar, e cada conexão nova já lê a config.
  systemctl reload ssh 2>/dev/null || systemctl reload sshd 2>/dev/null \
    || warn "sshd sem reload (socket-activated) — config vale para novas conexões."
  ok "Login por senha desabilitado (só chave SSH)."
else
  warn "Nenhuma chave em /root/.ssh/authorized_keys — login por senha MANTIDO."
  warn "Instale sua chave e rode:  systemctl reload ssh"
fi

# ── 5. SWAP ─────────────────────────────────────────────────
# Rede de segurança para picos de build do .NET e do Next.js.
# Não substitui RAM: se o swap encostar, é sinal de subir para CPX51.
info "Configurando swap..."
if swapon --show | grep -q .; then
  ok "Swap já configurado."
else
  fallocate -l 4G /swapfile
  chmod 600 /swapfile
  mkswap -q /swapfile
  swapon /swapfile
  echo '/swapfile none swap sw 0 0' >> /etc/fstab
  sysctl -qw vm.swappiness=10
  echo 'vm.swappiness=10' > /etc/sysctl.d/99-swappiness.conf
  ok "Swap de 4 GB ativo (swappiness=10)."
fi

# ── 6. DOCKER ───────────────────────────────────────────────
info "Instalando Docker..."
if command -v docker &>/dev/null; then
  ok "Docker já instalado: $(docker --version)"
else
  curl -fsSL https://get.docker.com | sh
  systemctl enable docker --now
  ok "Docker instalado: $(docker --version)"
fi

# Sem limite, o log de um container enche o disco e derruba tudo.
info "Limitando log dos containers..."
if [[ ! -f /etc/docker/daemon.json ]]; then
  mkdir -p /etc/docker
  cat > /etc/docker/daemon.json <<'EOF'
{
  "log-driver": "json-file",
  "log-opts": { "max-size": "10m", "max-file": "3" }
}
EOF
  systemctl restart docker
  ok "Log rotacionado em 10 MB x 3 por container."
else
  warn "/etc/docker/daemon.json já existe — confira o log-opts à mão."
fi

# ── 7. COOLIFY ──────────────────────────────────────────────
info "Instalando Coolify..."
if docker ps --format '{{.Names}}' 2>/dev/null | grep -q '^coolify$'; then
  ok "Coolify já está rodando."
else
  curl -fsSL https://cdn.coollabs.io/coolify/install.sh | bash
  ok "Coolify instalado."
fi

VPS_IP=$(curl -fsS --max-time 10 https://ipv4.icanhazip.com 2>/dev/null || hostname -I | awk '{print $1}')

# ── 8. RESUMO ───────────────────────────────────────────────
cat <<EOF

╔════════════════════════════════════════════════════════════╗
║              ✅  SETUP FASE 1 CONCLUÍDO                    ║
╚════════════════════════════════════════════════════════════╝

  IP desta VPS: ${VPS_IP}

  ── 1. Abrir o Coolify ─────────────────────────────────────
  A porta 8000 está FECHADA no firewall. Crie a conta admin
  por túnel SSH, da SUA máquina:

      ssh -L 8000:localhost:8000 root@${VPS_IP}

  e acesse http://localhost:8000 no navegador.
  Crie a conta admin AGORA — o primeiro que abrir vira dono.

  ── 2. Dar um domínio ao painel ────────────────────────────
  DNS:  painel.mjml.com.br → ${VPS_IP}   (nuvem CINZA)
  No Coolify: Settings > Instance Domain > https://painel.mjml.com.br
  Confirme o cadeado antes de fechar o túnel.

  ── 3. Serviços compartilhados (Etapa 3 do plano) ──────────
  Criar como recursos do Coolify, SEM porta pública:
    PostgreSQL 16  → bancos: atendefy, horafy, evolution
    Redis 7        → índices: 0=atendefy 1=horafy
    RabbitMQ 3.13  → vhost: /horafy

  ── 4. Ordem de deploy — Fase 1 ────────────────────────────
    1º  site institucional em mjml.com.br  (valida SSL/proxy)
    2º  AGENDA (Horafy)   → agenda.mjml.com.br + wildcard *.agenda
    3º  ATENDE (Atendefy) → app./api./evolution.atende.mjml.com.br
    Fase 2 (depois): Alugue, Delify (delivery.) e Lojas (loja.)

  ── 5. Conferir ────────────────────────────────────────────
    ufw status verbose
    nmap -Pn -p 22,80,443,8000,5432,6379 ${VPS_IP}
    (só 22, 80 e 443 devem aparecer como open)

  Docs: https://coolify.io/docs

EOF
