import { useState } from 'react';
import type { CSSProperties, FormEvent } from 'react';
import axios from 'axios';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Textarea } from '@/components/ui/textarea';

/**
 * Landing pública servida no apex (atende.mjml.com.br). O painel continua em
 * app.atende.mjml.com.br — mesmo bundle, host decide (ver rota "/" no App).
 * O formulário posta em /api/leads: same-origin, o nginx do container faz o
 * proxy para a API igual faz para o painel.
 */

const APP_LOGIN_URL = 'https://app.atende.mjml.com.br/login';

/* Paleta própria da landing (petróleo — distinta do índigo do Agenda). */
const PALETTE: Record<string, string> = {
  '--brand': 'oklch(0.52 0.11 230)',
  '--brand-ink': 'oklch(0.42 0.10 233)',
  '--hero-bg': 'oklch(0.235 0.045 240)',
  '--hero-glow': 'oklch(0.36 0.09 230)',
  '--hero-fg': 'oklch(0.975 0.006 230)',
  '--hero-muted': 'oklch(0.815 0.04 230)',
  '--tint': 'oklch(0.955 0.018 225)',
  '--tint-ink': 'oklch(0.36 0.09 230)',
};

const BUSINESS_TYPES = [
  'Loja',
  'Clínica ou consultório',
  'Restaurante ou delivery',
  'Salão ou barbearia',
  'Assistência ou serviços',
];

const STEPS = [
  {
    title: 'Conte pra gente do seu negócio',
    text: 'Preencha o formulário aqui embaixo. Leva dois minutos.',
  },
  {
    title: 'Conectamos seu WhatsApp e treinamos a IA',
    text: 'Com as informações do seu negócio: serviços, preços, horários e o seu jeito de falar com o cliente.',
  },
  {
    title: 'Seus clientes são atendidos na hora',
    text: 'A IA responde, marca horário e te chama quando a conversa precisa de você.',
  },
];

const FEATURES = [
  {
    title: 'Resposta na hora, dia e noite',
    text: 'Cliente não fica no vácuo: a dúvida chega e a resposta sai, mesmo fora do horário.',
  },
  {
    title: 'Agenda pelo próprio chat',
    text: 'A IA oferece horários e marca ali mesmo na conversa, integrada à agenda do seu negócio.',
  },
  {
    title: 'Você no controle',
    text: 'Todas as conversas num painel: entre quando quiser, assuma o chat e veja o que a IA respondeu.',
  },
  {
    title: 'Respostas com a sua cara',
    text: 'A IA aprende seus serviços, seus preços e o seu jeito de falar com o cliente.',
  },
];

function ChatMock() {
  return (
    <div
      aria-hidden="true"
      className="w-[290px] select-none rounded-[2.4rem] bg-zinc-900/90 p-2.5 shadow-2xl shadow-black/40 sm:w-[310px]"
    >
      <div className="overflow-hidden rounded-[1.9rem] bg-[#e9e2d9]">
        {/* Barra da conversa */}
        <div className="flex items-center gap-2.5 bg-white px-4 pb-3 pt-4">
          <div className="flex size-9 items-center justify-center rounded-full bg-(--brand) text-xs font-bold text-white">
            OV
          </div>
          <div>
            <p className="text-[13px] font-semibold leading-tight text-zinc-900">Ótica da Vila</p>
            <p className="text-[11px] leading-tight text-emerald-600">online agora</p>
          </div>
        </div>

        {/* Conversa */}
        <div className="space-y-2 px-3 py-3">
          <div className="max-w-[85%] rounded-xl rounded-tl-sm bg-white px-3 py-2 shadow-sm">
            <p className="text-[12.5px] leading-snug text-zinc-800">
              Oi! Vocês têm horário amanhã de manhã pro exame de vista?
            </p>
            <p className="mt-0.5 text-right text-[10px] text-zinc-400">09:12</p>
          </div>

          <div className="ml-auto max-w-[85%] rounded-xl rounded-tr-sm bg-[#d7f5c8] px-3 py-2 shadow-sm">
            <p className="text-[12.5px] leading-snug text-zinc-800">
              Oi, Ana! 😊 Temos sim: amanhã às 9h30 ou às 11h. Qual fica melhor pra você?
            </p>
            <p className="mt-0.5 text-right text-[10px] text-zinc-500">09:12 ✓✓</p>
          </div>

          <div className="max-w-[85%] rounded-xl rounded-tl-sm bg-white px-3 py-2 shadow-sm">
            <p className="text-[12.5px] leading-snug text-zinc-800">9h30, por favor!</p>
            <p className="mt-0.5 text-right text-[10px] text-zinc-400">09:13</p>
          </div>

          <div className="ml-auto max-w-[85%] rounded-xl rounded-tr-sm bg-[#d7f5c8] px-3 py-2 shadow-sm">
            <p className="text-[12.5px] leading-snug text-zinc-800">
              Fechado! Agendei seu exame amanhã às 9h30. Te esperamos! 📅
            </p>
            <p className="mt-0.5 text-right text-[10px] text-zinc-500">09:13 ✓✓</p>
          </div>
        </div>

        {/* Selo da IA */}
        <div className="bg-white p-3">
          <div className="flex items-center gap-1.5 rounded-xl bg-emerald-50 px-3 py-2">
            <svg
              className="size-3.5 shrink-0 text-emerald-600"
              viewBox="0 0 16 16"
              fill="none"
              stroke="currentColor"
              strokeWidth="2"
              strokeLinecap="round"
              strokeLinejoin="round"
            >
              <path d="m2.5 8.5 3.5 3.5 7-8" />
            </svg>
            <p className="text-[11px] leading-tight text-emerald-800">
              Atendido pela IA · agendamento criado no seu painel
            </p>
          </div>
        </div>
      </div>
    </div>
  );
}

function LeadForm() {
  const [name, setName] = useState('');
  const [phone, setPhone] = useState('');
  const [businessType, setBusinessType] = useState('');
  const [message, setMessage] = useState('');
  const [website, setWebsite] = useState(''); // honeypot
  const [errors, setErrors] = useState<{ name?: string; phone?: string; businessType?: string }>({});
  const [error, setError] = useState('');
  const [sent, setSent] = useState(false);
  const [loading, setLoading] = useState(false);

  function validate(): boolean {
    const next: typeof errors = {};
    if (!name.trim()) next.name = 'Conta pra gente seu nome';
    const digits = (phone.match(/\d/g) ?? []).length;
    if (!phone.trim()) next.phone = 'Precisamos do seu WhatsApp pra te chamar';
    else if (digits < 10 || !/^[\d\s()+.-]+$/.test(phone))
      next.phone = 'Coloque o DDD junto, ex.: (11) 98888-7777';
    if (!businessType) next.businessType = 'Escolha o que mais parece com seu negócio';
    setErrors(next);
    return Object.keys(next).length === 0;
  }

  async function handleSubmit(e: FormEvent) {
    e.preventDefault();
    if (!validate()) return;
    if (website) return; // honeypot preenchido: finge sucesso, não envia
    setError('');
    setLoading(true);
    try {
      await axios.post('/api/leads', {
        name,
        phone,
        email: null,
        businessType,
        message: message || null,
      });
      setSent(true);
    } catch {
      setError('Não conseguimos enviar agora. Tenta de novo em instantes.');
    } finally {
      setLoading(false);
    }
  }

  if (sent) {
    return (
      <div role="status" className="rounded-2xl bg-background px-6 py-10 text-center shadow-sm">
        <p className="text-2xl font-semibold text-foreground">Recebemos! 🎉</p>
        <p className="mx-auto mt-3 max-w-md text-base leading-7 text-foreground/70">
          Em breve a gente te chama no WhatsApp pra conhecer seu negócio e colocar
          a IA pra trabalhar pra você. Fica de olho no celular!
        </p>
      </div>
    );
  }

  return (
    <form
      onSubmit={handleSubmit}
      noValidate
      className="space-y-5 rounded-2xl bg-background p-6 shadow-sm sm:p-8"
    >
      <div className="space-y-2">
        <Label htmlFor="lead-name" className="text-base">Seu nome</Label>
        <Input
          id="lead-name"
          autoComplete="name"
          placeholder="Como podemos te chamar?"
          className="h-11 text-base"
          aria-invalid={!!errors.name}
          value={name}
          onChange={(e) => setName(e.target.value)}
        />
        {errors.name && <p className="text-sm text-destructive">{errors.name}</p>}
      </div>

      <div className="space-y-2">
        <Label htmlFor="lead-phone" className="text-base">Seu WhatsApp</Label>
        <Input
          id="lead-phone"
          type="tel"
          inputMode="tel"
          autoComplete="tel-national"
          placeholder="(11) 98888-7777"
          className="h-11 text-base"
          aria-invalid={!!errors.phone}
          value={phone}
          onChange={(e) => setPhone(e.target.value)}
        />
        {errors.phone && <p className="text-sm text-destructive">{errors.phone}</p>}
      </div>

      <div className="space-y-2">
        <Label htmlFor="lead-business" className="text-base">Seu negócio</Label>
        <select
          id="lead-business"
          aria-invalid={!!errors.businessType}
          className="border-input h-11 w-full appearance-none rounded-lg border bg-background bg-[url('data:image/svg+xml;charset=utf-8,%3Csvg%20xmlns%3D%22http%3A%2F%2Fwww.w3.org%2F2000%2Fsvg%22%20width%3D%2216%22%20height%3D%2216%22%20fill%3D%22none%22%20stroke%3D%22%23737373%22%20stroke-width%3D%222%22%20stroke-linecap%3D%22round%22%20stroke-linejoin%3D%22round%22%3E%3Cpath%20d%3D%22m4%206%204%204%204-4%22%2F%3E%3C%2Fsvg%3E')] bg-[position:right_0.75rem_center] bg-no-repeat px-3 pr-10 text-base outline-none transition-colors focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50 aria-invalid:border-destructive"
          value={businessType}
          onChange={(e) => setBusinessType(e.target.value)}
        >
          <option value="" disabled>O que mais parece com o seu?</option>
          {BUSINESS_TYPES.map((t) => (
            <option key={t} value={t}>{t}</option>
          ))}
          <option value="Outro">Outro</option>
        </select>
        {errors.businessType && <p className="text-sm text-destructive">{errors.businessType}</p>}
      </div>

      <div className="space-y-2">
        <Label htmlFor="lead-message" className="text-base">
          Quer contar mais? <span className="font-normal text-muted-foreground">(opcional)</span>
        </Label>
        <Textarea
          id="lead-message"
          rows={3}
          placeholder="Ex.: hoje eu respondo tudo sozinho e vivo deixando cliente esperando…"
          className="text-base"
          value={message}
          onChange={(e) => setMessage(e.target.value)}
        />
      </div>

      <div aria-hidden="true" className="absolute -left-[9999px] top-auto h-px w-px overflow-hidden">
        <label htmlFor="lead-website">Não preencha este campo</label>
        <input
          id="lead-website"
          type="text"
          tabIndex={-1}
          autoComplete="off"
          value={website}
          onChange={(e) => setWebsite(e.target.value)}
        />
      </div>

      {error && <p className="text-sm text-destructive">{error}</p>}

      <Button
        type="submit"
        disabled={loading}
        className="h-12 w-full bg-(--brand-ink) text-base font-semibold text-white hover:bg-(--brand-ink) hover:opacity-90"
      >
        {loading ? 'Enviando…' : 'Quero a IA atendendo pra mim'}
      </Button>
      <p className="text-center text-sm leading-6 text-muted-foreground">
        Sem compromisso e sem cartão. A gente só te chama pra conversar.
      </p>
    </form>
  );
}

export default function LandingPage() {
  return (
    <div style={PALETTE as CSSProperties} className="min-h-screen bg-background">
      {/* ── Hero ─────────────────────────────────────────────── */}
      <div className="relative overflow-hidden bg-(--hero-bg) text-(--hero-fg)">
        <div
          aria-hidden="true"
          className="pointer-events-none absolute -right-40 -top-48 size-[34rem] rounded-full bg-(--hero-glow) opacity-50 blur-3xl"
        />
        <header className="relative mx-auto flex w-full max-w-6xl items-center justify-between px-5 py-5 sm:px-8">
          <p className="text-xl font-bold tracking-tight">Atende</p>
          <a
            href={APP_LOGIN_URL}
            className="rounded-lg border border-white/25 px-4 py-2 text-sm font-medium transition-colors hover:bg-white/10 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-white"
          >
            Entrar
          </a>
        </header>

        <section className="relative mx-auto grid w-full max-w-6xl items-center gap-12 px-5 pb-16 pt-10 sm:px-8 sm:pb-24 sm:pt-16 lg:grid-cols-[1.1fr_auto] lg:gap-8">
          <div className="max-w-xl">
            <h1 className="landing-rise text-balance text-4xl font-bold leading-[1.08] tracking-tight sm:text-5xl lg:text-6xl">
              Seu WhatsApp respondendo na hora, até quando você não pode.
            </h1>
            <p className="landing-rise-2 mt-6 max-w-[52ch] text-lg leading-8 text-(--hero-muted) sm:text-xl sm:leading-9">
              A IA do Atende conversa com seus clientes, tira dúvidas e marca
              horário — do jeito que você ensinou. E você acompanha tudo num
              painel simples.
            </p>
            <div className="landing-rise-3 mt-9 flex flex-wrap items-center gap-4">
              <a
                href="#interesse"
                className="rounded-xl bg-white px-6 py-3.5 text-base font-semibold text-(--brand-ink) shadow-lg transition-transform hover:scale-[1.02] focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-white"
              >
                Quero conhecer
              </a>
              <p className="text-sm text-(--hero-muted)">Grátis pra começar a conversa</p>
            </div>
          </div>
          <div className="landing-rise-4 justify-self-center lg:justify-self-end">
            <ChatMock />
          </div>
        </section>
      </div>

      {/* ── Como funciona (sequência real, por isso numerada) ── */}
      <section className="mx-auto w-full max-w-6xl px-5 py-16 sm:px-8 sm:py-24">
        <h2 className="landing-reveal text-balance text-3xl font-bold tracking-tight text-foreground sm:text-4xl">
          Como funciona
        </h2>
        <ol className="mt-10 grid gap-10 sm:grid-cols-3 sm:gap-8">
          {STEPS.map((step, i) => (
            <li key={step.title} className="landing-reveal flex gap-4 sm:block">
              <span className="flex size-9 shrink-0 items-center justify-center rounded-full bg-(--tint) text-base font-bold text-(--tint-ink) sm:mb-4">
                {i + 1}
              </span>
              <div>
                <h3 className="text-lg font-semibold text-foreground">{step.title}</h3>
                <p className="mt-2 max-w-[38ch] text-base leading-7 text-foreground/70">{step.text}</p>
              </div>
            </li>
          ))}
        </ol>
      </section>

      {/* ── O que já vem pronto ───────────────────────────────── */}
      <section className="border-y border-border bg-muted/40">
        <div className="mx-auto w-full max-w-6xl px-5 py-16 sm:px-8 sm:py-24">
          <h2 className="landing-reveal max-w-2xl text-balance text-3xl font-bold tracking-tight text-foreground sm:text-4xl">
            Tudo que o seu atendimento precisa já vem pronto
          </h2>
          <div className="mt-10 grid gap-x-12 gap-y-10 sm:grid-cols-2">
            {FEATURES.map((f) => (
              <div key={f.title} className="landing-reveal flex gap-4">
                <svg
                  aria-hidden="true"
                  className="mt-1 size-6 shrink-0 text-(--brand-ink)"
                  viewBox="0 0 24 24"
                  fill="none"
                  stroke="currentColor"
                  strokeWidth="2.5"
                  strokeLinecap="round"
                  strokeLinejoin="round"
                >
                  <path d="m4.5 12.5 5 5 10-11" />
                </svg>
                <div>
                  <h3 className="text-lg font-semibold text-foreground">{f.title}</h3>
                  <p className="mt-1.5 max-w-[44ch] text-base leading-7 text-foreground/70">{f.text}</p>
                </div>
              </div>
            ))}
          </div>
          <p className="landing-reveal mt-12 max-w-2xl text-base leading-7 text-foreground/60">
            Lojas, clínicas, restaurantes, salões, assistências, óticas — e o seu
            negócio também.
          </p>
        </div>
      </section>

      {/* ── Formulário de interesse ───────────────────────────── */}
      <section id="interesse" className="scroll-mt-8 bg-(--tint)">
        <div className="mx-auto w-full max-w-xl px-5 py-16 sm:px-8 sm:py-24">
          <h2 className="landing-reveal text-balance text-center text-3xl font-bold tracking-tight text-foreground sm:text-4xl">
            Bora responder todo mundo?
          </h2>
          <p className="landing-reveal mx-auto mt-4 max-w-md text-center text-base leading-7 text-foreground/70">
            Deixa seu contato que a gente te chama no WhatsApp, entende seu
            negócio e coloca a IA pra trabalhar com você.
          </p>
          <div className="landing-reveal mt-10">
            <LeadForm />
          </div>
        </div>
      </section>

      {/* ── Rodapé ────────────────────────────────────────────── */}
      <footer className="mx-auto flex w-full max-w-6xl flex-col items-center gap-3 px-5 py-10 text-center sm:flex-row sm:justify-between sm:text-left">
        <p className="text-sm text-muted-foreground">
          <span className="font-semibold text-foreground">Atende</span> — atendimento com IA no WhatsApp
        </p>
        <p className="text-sm text-muted-foreground">
          Já sou cliente:{' '}
          <a href={APP_LOGIN_URL} className="font-medium text-(--brand-ink) underline-offset-4 hover:underline">
            entrar no painel
          </a>
        </p>
      </footer>
    </div>
  );
}
