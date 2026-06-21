# Guia: ativar Agendamento (Cal.com) no Mensagee

Este guia mostra, passo a passo, como o dono do negócio conecta a própria agenda
(Google ou Apple) e habilita o agendamento pelo WhatsApp. Quando ativo, o assistente
de IA envia automaticamente o seu link de agendamento sempre que um cliente quiser
marcar um horário — e o cliente escolhe o horário direto na sua agenda, sem você
precisar responder manualmente.

> **Como funciona, em uma frase:** o Mensagee guarda **um link de agendamento** e o bot
> o oferece na conversa. O cal.com cuida de disponibilidade, fuso horário, confirmação
> e lembrete. Sua agenda do Google/Apple fica sempre sincronizada — nada de horário
> dobrado.

---

## Pré-requisito
Uma conta no **Cal.com** (o plano **gratuito** já atende). Acesse https://cal.com e
crie a conta com seu e-mail.

---

## Passo 1 — Criar a conta no Cal.com
1. Em https://cal.com, clique em **Sign up** e crie a conta (e-mail ou Google).
2. Escolha um **nome de usuário** — ele aparece no seu link (ex.: `cal.com/clinica-abc`).
3. Defina seu **fuso horário** (ex.: *America/Sao_Paulo*) e seus **horários de
   atendimento** (ex.: seg–sex, 09:00–18:00). Isso vira a sua disponibilidade.

## Passo 2 — Conectar sua agenda (Google ou Apple)
Conectar a agenda é o que evita horário dobrado: o cal.com lê seus compromissos
existentes e só oferece horários livres, e grava os novos agendamentos na sua agenda.

**Google Agenda:**
1. No cal.com, vá em **Settings → Apps** (ou **Calendars**) → **Add** → **Google Calendar**.
2. Autorize o acesso com a conta Google da agenda que você usa.
3. Em **Adicionar à agenda**, escolha em qual agenda os agendamentos serão criados.

**Apple / iCloud:**
1. No iPhone/Mac, gere uma **senha de app** em https://appleid.apple.com → *Login e
   Segurança* → *Senhas específicas de app*.
2. No cal.com, **Settings → Calendars → Add → Apple Calendar** e informe seu Apple ID +
   a senha de app gerada.

> Pode conectar mais de uma agenda. A "agenda de verificação de conflitos" é a que o
> cal.com lê para saber quando você está ocupado.

## Passo 3 — Criar um tipo de evento (o "serviço")
O **event type** é o serviço que o cliente vai agendar (ex.: *Consulta 30 min*,
*Avaliação*, *Corte de cabelo*).
1. Em **Event Types → + New**, defina:
   - **Título** (ex.: "Consulta 30 min") e **duração**.
   - **Disponibilidade** (use o horário do Passo 1 ou um específico).
   - **Antecedência mínima** e **buffers** (folga antes/depois), se quiser.
   - **Local**: presencial (endereço), telefone, ou link de vídeo.
2. (Recomendado) Adicione uma pergunta de **telefone** no formulário de reserva
   (**Advanced → Booking questions → Add → Phone**). Isso é importante para a Fase de
   *write-back* (ver os    agendamentos dentro do painel) — guarde para depois.

## Passo 4 — Copiar o link de agendamento
No event type criado, clique em **Copy link** (ou no ícone de corrente). O link tem o
formato:
```
https://cal.com/seu-usuario/seu-evento
```
Ex.: `https://cal.com/clinica-abc/consulta-30min`. **É esse link que você vai colar no
Mensagee.**

## Passo 5 — Ativar no painel Mensagee
1. Entre no painel → menu **Agenda**.
2. **Status:** Ativado.
3. **Serviço:** Cal.com.
4. **Link de agendamento:** cole o link do Passo 4.
5. **Instruções para o assistente** (opcional): oriente o bot, ex.:
   *"Ofereça o link apenas para marcação de consultas. Atendimento de seg a sex."*
6. **Salvar.**

## Passo 6 — Testar
De um WhatsApp qualquer, mande para o número conectado algo como **"quero agendar"** ou
**"tem horário essa semana?"**. O assistente deve responder com o seu link de
agendamento. Abra o link, escolha um horário e confirme — o evento aparece na sua agenda
do Google/Apple e o cal.com manda a confirmação/lembrete ao cliente.

---

## Dicas e limites
- **Disponibilidade real:** mantenha sua agenda do Google/Apple atualizada — bloqueie
  feriados e compromissos pessoais para não receber agendamento em cima.
- **Lembretes:** configure lembretes por e-mail no cal.com (**Workflows**) para reduzir
  faltas. (Lembrete por SMS/WhatsApp pelo cal.com pode exigir plano pago.)
- **Um serviço por enquanto:** o Mensagee usa **um link por empresa** nesta versão. Se
  você tem vários serviços, crie um event type "guarda-chuva" ou um link de perfil
  (`cal.com/seu-usuario`) que lista todos os seus tipos de evento.
- **Fuso horário:** confira o fuso no cal.com — os horários oferecidos seguem o fuso da
  sua conta; o cliente vê no fuso dele.

## Problemas comuns
- **O bot não envia o link:** confirme que o **Status** está *Ativado* e o link salvo na
  tela Agenda; e que a IA está configurada para esse número.
- **Link errado/quebrado:** copie de novo pelo botão **Copy link** do event type (evite
  digitar à mão).
- **Horários não aparecem / tudo ocupado:** verifique a **disponibilidade** do event type
  e se a agenda conectada não está marcando o dia todo como ocupado.

---

## Próximo nível (opcional, já preparado)
O Mensagee tem o **write-back via webhook** já preparado: ao confirmar um agendamento, o
cal.com avisa o sistema e o **agendamento aparece dentro do painel**. Para ligar, é só
configurar um webhook no cal.com apontando para a URL que aparece na tela Agenda
(quando habilitado) e adicionar a pergunta de **telefone** no event type (Passo 3).
Fale com o suporte para ativar.
