import { useState } from 'react';
import { loadStripe } from '@stripe/stripe-js';
import { Elements, CardElement, useStripe, useElements } from '@stripe/react-stripe-js';
import {
  useCancelSubscription,
  usePlans,
  useSubscribe,
  useSubscription,
} from '@/hooks/useBilling';
import type {
  CreateSubscriptionRequest,
  InvoiceResult,
  Plan,
  PlanLimits,
} from '@/types/api';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from '@/components/ui/card';
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs';
import { BillingProvider, BillingType, SubscriptionStatus } from '@/lib/constants';

// ── Stripe setup ─────────────────────────────────────────────────────────────

const stripeKey = import.meta.env.VITE_STRIPE_PUBLISHABLE_KEY as string | undefined;
const stripePromise = stripeKey ? loadStripe(stripeKey) : null;

// ── Helpers ───────────────────────────────────────────────────────────────────

type BillingCycle = 'monthly' | 'yearly';

function parseLimits(json: string): PlanLimits {
  try {
    return JSON.parse(json) as PlanLimits;
  } catch {
    return { messages_per_month: 0, whatsapp_accounts: 0, team_members: 0 };
  }
}

function subStatusVariant(status: string): 'default' | 'destructive' | 'secondary' {
  if (status === SubscriptionStatus.ACTIVE) return 'default';
  if (status === SubscriptionStatus.PAST_DUE || status === SubscriptionStatus.SUSPENDED) return 'destructive';
  return 'secondary';
}

function maskCpfCnpj(raw: string): string {
  const d = raw.replace(/\D/g, '').slice(0, 14);
  const n = d.length;
  if (n <= 3) return d;
  if (n <= 6) return `${d.slice(0, 3)}.${d.slice(3)}`;
  if (n <= 9) return `${d.slice(0, 3)}.${d.slice(3, 6)}.${d.slice(6)}`;
  if (n <= 11) return `${d.slice(0, 3)}.${d.slice(3, 6)}.${d.slice(6, 9)}-${d.slice(9)}`;
  if (n <= 12) return `${d.slice(0, 2)}.${d.slice(2, 5)}.${d.slice(5, 8)}/${d.slice(8)}`;
  return `${d.slice(0, 2)}.${d.slice(2, 5)}.${d.slice(5, 8)}/${d.slice(8, 12)}-${d.slice(12)}`;
}

function validateCpf(d: string): boolean {
  if (/^(\d)\1{10}$/.test(d)) return false;
  const calc = (len: number) => {
    const sum = Array.from({ length: len }, (_, i) => +d[i] * (len + 1 - i)).reduce((a, b) => a + b, 0);
    const r = (sum * 10) % 11;
    return r >= 10 ? 0 : r;
  };
  return calc(9) === +d[9] && calc(10) === +d[10];
}

function validateCnpj(d: string): boolean {
  if (/^(\d)\1{13}$/.test(d)) return false;
  const w1 = [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];
  const w2 = [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];
  const calc = (weights: number[]) => {
    const sum = weights.reduce((acc, w, i) => acc + +d[i] * w, 0);
    const r = sum % 11;
    return r < 2 ? 0 : 11 - r;
  };
  return calc(w1) === +d[12] && calc(w2) === +d[13];
}

function isValidCpfCnpj(value: string): boolean {
  const d = value.replace(/\D/g, '');
  if (d.length === 11) return validateCpf(d);
  if (d.length === 14) return validateCnpj(d);
  return false;
}

// ── Stripe checkout form (must live inside <Elements>) ────────────────────────

function StripeCheckoutForm({
  plan,
  cycle,
  subscribe,
  onSuccess,
  onError,
}: {
  plan: Plan;
  cycle: BillingCycle;
  subscribe: ReturnType<typeof useSubscribe>;
  onSuccess: () => void;
  onError: (msg: string) => void;
}) {
  const stripe = useStripe();
  const elements = useElements();
  const [confirming, setConfirming] = useState(false);

  async function handleConfirm() {
    if (!stripe || !elements) return;
    setConfirming(true);
    try {
      const result = await subscribe.mutateAsync({
        planId: plan.id,
        provider: BillingProvider.STRIPE,
        billingType: BillingType.CREDIT_CARD,
        billingCycle: cycle,
      });

      if (result.clientSecret) {
        const cardElement = elements.getElement(CardElement);
        if (!cardElement) throw new Error('Elemento de cartão não encontrado.');
        const { error } = await stripe.confirmCardPayment(result.clientSecret, {
          payment_method: { card: cardElement },
        });
        if (error) throw new Error(error.message ?? 'Erro ao confirmar pagamento.');
      }

      onSuccess();
    } catch (err: unknown) {
      const msg =
        err instanceof Error
          ? err.message
          : ((err as { response?: { data?: { error?: string } } })?.response?.data?.error ??
            'Erro ao processar assinatura.');
      onError(msg);
    } finally {
      setConfirming(false);
    }
  }

  return (
    <div className="space-y-3">
      <div className="rounded-md border bg-background px-3 py-2.5">
        <CardElement
          options={{
            style: {
              base: {
                fontSize: '14px',
                fontFamily: 'inherit',
                color: 'inherit',
                '::placeholder': { color: '#9ca3af' },
              },
              invalid: { color: '#ef4444' },
            },
          }}
        />
      </div>
      <Button
        className="w-full"
        onClick={handleConfirm}
        disabled={!stripe || confirming || subscribe.isPending}
      >
        {confirming || subscribe.isPending ? 'Processando…' : 'Confirmar pagamento'}
      </Button>
    </div>
  );
}

// ── Main page ─────────────────────────────────────────────────────────────────

export default function BillingPage() {
  const { data: plans, isLoading: loadingPlans } = usePlans();
  const { data: subscription, isLoading: loadingSub } = useSubscription();
  const subscribe = useSubscribe();
  const cancelSub = useCancelSubscription();

  const [cancelError, setCancelError] = useState('');
  const [cycle, setCycle] = useState<BillingCycle>('monthly');
  const [selectedPlan, setSelectedPlan] = useState<Plan | null>(null);
  const [provider, setProvider] = useState<string>(BillingProvider.ASAAS);
  const [billingType, setBillingType] = useState<string>(BillingType.BOLETO);
  const [cpfCnpj, setCpfCnpj] = useState('');
  const [paymentMethodId, setPaymentMethodId] = useState('');
  const [invoiceResult, setInvoiceResult] = useState<InvoiceResult | null>(null);
  const [dialogError, setDialogError] = useState('');
  const [stripeSuccess, setStripeSuccess] = useState(false);

  const showStripeCard =
    provider === BillingProvider.STRIPE && billingType === BillingType.CREDIT_CARD;

  function openSubscribeDialog(plan: Plan) {
    setSelectedPlan(plan);
    setProvider(BillingProvider.ASAAS);
    setBillingType(BillingType.BOLETO);
    setCpfCnpj('');
    setPaymentMethodId('');
    setDialogError('');
    setInvoiceResult(null);
    setStripeSuccess(false);
  }

  function handleProviderChange(v: string) {
    setProvider(v);
    setBillingType(v === BillingProvider.STRIPE ? BillingType.CREDIT_CARD : BillingType.BOLETO);
    setDialogError('');
  }

  async function handleSubscribe() {
    if (!selectedPlan) return;
    setDialogError('');
    if (provider === BillingProvider.ASAAS && !isValidCpfCnpj(cpfCnpj)) {
      setDialogError('CPF ou CNPJ inválido. Verifique o número digitado.');
      return;
    }
    const req: CreateSubscriptionRequest = {
      planId: selectedPlan.id,
      provider,
      billingType,
      billingCycle: cycle,
      cpfCnpj: provider === BillingProvider.ASAAS ? cpfCnpj : undefined,
      paymentMethodId: billingType === BillingType.CREDIT_CARD ? paymentMethodId : undefined,
    };
    try {
      const result = await subscribe.mutateAsync(req);
      setInvoiceResult(result);
    } catch (err: unknown) {
      const msg =
        (err as { response?: { data?: { error?: string } } })?.response?.data?.error ??
        'Erro ao processar assinatura.';
      setDialogError(msg);
    }
  }

  async function handleCancel() {
    if (!confirm('Cancelar assinatura? O acesso continua até o fim do período atual.')) return;
    setCancelError('');
    try {
      await cancelSub.mutateAsync();
    } catch {
      setCancelError('Erro ao cancelar assinatura. Tente novamente.');
    }
  }

  function closeInvoiceDialog() {
    setInvoiceResult(null);
    setSelectedPlan(null);
  }

  if (loadingPlans || loadingSub) return <p className="text-muted-foreground">Carregando…</p>;

  return (
    <div className="space-y-6 max-w-4xl">
      <h1 className="text-2xl font-bold">Billing</h1>

      {subscription && (
        <Card>
          <CardHeader>
            <div className="flex items-center justify-between">
              <CardTitle>Assinatura atual</CardTitle>
              <Badge variant={subStatusVariant(subscription.status)}>
                {subscription.status}
              </Badge>
            </div>
          </CardHeader>
          <CardContent className="space-y-2">
            <p className="text-sm">
              Plano: <strong>{subscription.plan?.name ?? '—'}</strong> · Ciclo:{' '}
              <strong>{subscription.billingCycle}</strong> · Provedor:{' '}
              <strong>{subscription.provider}</strong>
            </p>
            {subscription.currentPeriodEnd && (
              <p className="text-sm text-muted-foreground">
                Período atual até{' '}
                {new Date(subscription.currentPeriodEnd).toLocaleDateString('pt-BR')}
              </p>
            )}
            {subscription.lastInvoice && (
              <p className="text-sm">
                Última fatura: R$ {subscription.lastInvoice.amount.toFixed(2)} —{' '}
                <Badge
                  variant={
                    subscription.lastInvoice.status === 'paid' ? 'default' : 'secondary'
                  }
                >
                  {subscription.lastInvoice.status}
                </Badge>
              </p>
            )}
            {cancelError && <p className="text-sm text-destructive">{cancelError}</p>}
            {subscription.status !== SubscriptionStatus.CANCELLED && (
              <Button
                variant="destructive"
                size="sm"
                onClick={handleCancel}
                disabled={cancelSub.isPending}
              >
                {cancelSub.isPending ? 'Cancelando…' : 'Cancelar assinatura'}
              </Button>
            )}
          </CardContent>
        </Card>
      )}

      <div>
        <h2 className="text-xl font-semibold mb-4">Planos disponíveis</h2>
        <Tabs value={cycle} onValueChange={(v) => setCycle(v as BillingCycle)}>
          <TabsList>
            <TabsTrigger value="monthly">Mensal</TabsTrigger>
            <TabsTrigger value="yearly">Anual</TabsTrigger>
          </TabsList>

          {(['monthly', 'yearly'] as const).map((c) => (
            <TabsContent key={c} value={c}>
              {(!plans || plans.length === 0) && (
                <p className="text-muted-foreground mt-4">
                  Nenhum plano disponível. Insira planos via Swagger ou diretamente no banco.
                </p>
              )}
              <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3 mt-4">
                {plans?.map((plan) => {
                  const limits = parseLimits(plan.limitsJson);
                  const price = c === 'monthly' ? plan.priceMonthly : plan.priceYearly;
                  const isActive = subscription?.plan?.id === plan.id;
                  return (
                    <Card key={plan.id} className={isActive ? 'ring-2 ring-primary' : ''}>
                      <CardHeader>
                        <CardTitle>{plan.name}</CardTitle>
                        <CardDescription>
                          R$ {price.toFixed(2)}/{c === 'monthly' ? 'mês' : 'ano'}
                        </CardDescription>
                      </CardHeader>
                      <CardContent className="space-y-2">
                        <ul className="text-sm space-y-1 text-muted-foreground">
                          <li>
                            {limits.messages_per_month.toLocaleString('pt-BR')} mensagens/mês
                          </li>
                          <li>{limits.whatsapp_accounts} conta(s) WhatsApp</li>
                          <li>{limits.team_members} membro(s) na equipe</li>
                        </ul>
                        <Button
                          className="w-full mt-2"
                          variant={isActive ? 'secondary' : 'default'}
                          disabled={isActive}
                          onClick={() => openSubscribeDialog(plan)}
                        >
                          {isActive ? 'Plano atual' : 'Assinar'}
                        </Button>
                      </CardContent>
                    </Card>
                  );
                })}
              </div>
            </TabsContent>
          ))}
        </Tabs>
      </div>

      {/* Subscribe dialog */}
      <Dialog
        open={!!selectedPlan && !invoiceResult && !stripeSuccess}
        onOpenChange={(o) => !o && setSelectedPlan(null)}
      >
        <DialogContent className="max-w-md">
          <DialogHeader>
            <DialogTitle>Assinar {selectedPlan?.name}</DialogTitle>
          </DialogHeader>
          <div className="space-y-4">
            {/* Provider */}
            <div className="space-y-1">
              <Label>Provedor de pagamento</Label>
              <Select value={provider} onValueChange={(v) => v && handleProviderChange(v)}>
                <SelectTrigger>
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value={BillingProvider.ASAAS}>Asaas (Boleto / PIX)</SelectItem>
                  <SelectItem value={BillingProvider.STRIPE}>Stripe (Cartão de crédito)</SelectItem>
                </SelectContent>
              </Select>
            </div>

            {/* Billing type — Asaas only */}
            {provider === BillingProvider.ASAAS && (
              <div className="space-y-1">
                <Label>Forma de pagamento</Label>
                <Select value={billingType} onValueChange={(v) => v && setBillingType(v)}>
                  <SelectTrigger>
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value={BillingType.BOLETO}>Boleto bancário</SelectItem>
                    <SelectItem value={BillingType.PIX}>PIX</SelectItem>
                    <SelectItem value={BillingType.CREDIT_CARD}>Cartão de crédito</SelectItem>
                  </SelectContent>
                </Select>
              </div>
            )}

            {/* CPF/CNPJ — Asaas only */}
            {provider === BillingProvider.ASAAS && (
              <div className="space-y-1">
                <Label htmlFor="cpfCnpj">CPF ou CNPJ</Label>
                <Input
                  id="cpfCnpj"
                  placeholder="000.000.000-00 ou 00.000.000/0000-00"
                  value={cpfCnpj}
                  onChange={(e) => setCpfCnpj(maskCpfCnpj(e.target.value))}
                  inputMode="numeric"
                />
              </div>
            )}

            {/* Asaas credit card token */}
            {provider === BillingProvider.ASAAS && billingType === BillingType.CREDIT_CARD && (
              <div className="space-y-1">
                <Label htmlFor="paymentMethodId">Token do cartão (Asaas)</Label>
                <Input
                  id="paymentMethodId"
                  placeholder="tokenCreditCard_xxx"
                  value={paymentMethodId}
                  onChange={(e) => setPaymentMethodId(e.target.value)}
                />
              </div>
            )}

            {/* Error (always visible above submit) */}
            {dialogError && <p className="text-sm text-destructive">{dialogError}</p>}

            {/* Stripe Elements card form */}
            {showStripeCard ? (
              stripePromise ? (
                <Elements stripe={stripePromise}>
                  <StripeCheckoutForm
                    plan={selectedPlan!}
                    cycle={cycle}
                    subscribe={subscribe}
                    onSuccess={() => setStripeSuccess(true)}
                    onError={(msg) => setDialogError(msg)}
                  />
                </Elements>
              ) : (
                <p className="text-xs text-amber-600 border border-amber-200 rounded-md p-2">
                  Stripe não configurado. Adicione{' '}
                  <code className="font-mono">VITE_STRIPE_PUBLISHABLE_KEY</code> ao .env.
                </p>
              )
            ) : (
              <Button
                className="w-full"
                onClick={handleSubscribe}
                disabled={subscribe.isPending}
              >
                {subscribe.isPending ? 'Processando…' : 'Confirmar assinatura'}
              </Button>
            )}
          </div>
        </DialogContent>
      </Dialog>

      {/* Stripe success dialog */}
      <Dialog open={stripeSuccess} onOpenChange={(o) => !o && (setStripeSuccess(false), setSelectedPlan(null))}>
        <DialogContent className="max-w-sm text-center">
          <DialogHeader>
            <DialogTitle>Pagamento enviado!</DialogTitle>
          </DialogHeader>
          <div className="space-y-3 py-2">
            <p className="text-4xl">✓</p>
            <p className="text-sm text-muted-foreground">
              Seu pagamento foi confirmado. A assinatura será ativada em instantes após a
              confirmação bancária.
            </p>
            <Button
              className="w-full"
              onClick={() => { setStripeSuccess(false); setSelectedPlan(null); }}
            >
              Fechar
            </Button>
          </div>
        </DialogContent>
      </Dialog>

      {/* Asaas invoice result dialog */}
      <Dialog open={!!invoiceResult} onOpenChange={(o) => !o && closeInvoiceDialog()}>
        <DialogContent className="max-w-md">
          <DialogHeader>
            <DialogTitle>Assinatura criada!</DialogTitle>
          </DialogHeader>
          {invoiceResult && (
            <div className="space-y-3">
              <p className="text-sm">
                Status: <Badge>{invoiceResult.status}</Badge> · Vencimento:{' '}
                {new Date(invoiceResult.dueDate).toLocaleDateString('pt-BR')}
              </p>

              {invoiceResult.boletoUrl && (
                <div className="space-y-1">
                  <p className="text-sm font-medium">Boleto bancário</p>
                  <a
                    href={invoiceResult.boletoUrl}
                    target="_blank"
                    rel="noopener noreferrer"
                    className="text-sm underline text-primary break-all"
                  >
                    Abrir boleto
                  </a>
                  {invoiceResult.boletoBarcode && (
                    <p className="text-xs font-mono bg-muted p-2 rounded break-all">
                      {invoiceResult.boletoBarcode}
                    </p>
                  )}
                </div>
              )}

              {invoiceResult.pixCopyPaste && (
                <div className="space-y-2">
                  <p className="text-sm font-medium">PIX Copia e Cola</p>
                  <p className="text-xs font-mono bg-muted p-2 rounded break-all">
                    {invoiceResult.pixCopyPaste}
                  </p>
                  <Button
                    size="sm"
                    variant="outline"
                    onClick={() =>
                      navigator.clipboard.writeText(invoiceResult.pixCopyPaste!)
                    }
                  >
                    Copiar código PIX
                  </Button>
                </div>
              )}

              <Button className="w-full" onClick={closeInvoiceDialog}>
                Fechar
              </Button>
            </div>
          )}
        </DialogContent>
      </Dialog>
    </div>
  );
}
