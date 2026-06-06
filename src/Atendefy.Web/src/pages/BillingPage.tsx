import { useState } from 'react';
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

type BillingCycle = 'monthly' | 'yearly';

function parseLimits(json: string): PlanLimits {
  try {
    return JSON.parse(json) as PlanLimits;
  } catch {
    return { messages_per_month: 0, whatsapp_accounts: 0, team_members: 0 };
  }
}

function subStatusVariant(status: string): 'default' | 'destructive' | 'secondary' {
  if (status === 'active') return 'default';
  if (status === 'past_due' || status === 'suspended') return 'destructive';
  return 'secondary';
}

export default function BillingPage() {
  const { data: plans, isLoading: loadingPlans } = usePlans();
  const { data: subscription, isLoading: loadingSub } = useSubscription();
  const subscribe = useSubscribe();
  const cancelSub = useCancelSubscription();

  const [cancelError, setCancelError] = useState('');
  const [cycle, setCycle] = useState<BillingCycle>('monthly');
  const [selectedPlan, setSelectedPlan] = useState<Plan | null>(null);
  const [provider, setProvider] = useState('asaas');
  const [billingType, setBillingType] = useState('BOLETO');
  const [cpfCnpj, setCpfCnpj] = useState('');
  const [paymentMethodId, setPaymentMethodId] = useState('');
  const [invoiceResult, setInvoiceResult] = useState<InvoiceResult | null>(null);
  const [dialogError, setDialogError] = useState('');

  function openSubscribeDialog(plan: Plan) {
    setSelectedPlan(plan);
    setProvider('asaas');
    setBillingType('BOLETO');
    setCpfCnpj('');
    setPaymentMethodId('');
    setDialogError('');
    setInvoiceResult(null);
  }

  function handleProviderChange(v: string) {
    setProvider(v);
    setBillingType(v === 'stripe' ? 'CREDIT_CARD' : 'BOLETO');
  }

  async function handleSubscribe() {
    if (!selectedPlan) return;
    setDialogError('');
    const req: CreateSubscriptionRequest = {
      planId: selectedPlan.id,
      provider,
      billingType,
      billingCycle: cycle,
      cpfCnpj: provider === 'asaas' ? cpfCnpj : undefined,
      paymentMethodId: billingType === 'CREDIT_CARD' ? paymentMethodId : undefined,
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
            {subscription.status !== 'cancelled' && (
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
        open={!!selectedPlan && !invoiceResult}
        onOpenChange={(o) => !o && setSelectedPlan(null)}
      >
        <DialogContent className="max-w-md">
          <DialogHeader>
            <DialogTitle>Assinar {selectedPlan?.name}</DialogTitle>
          </DialogHeader>
          <div className="space-y-4">
            <div className="space-y-1">
              <Label>Provedor de pagamento</Label>
              <Select value={provider} onValueChange={(v) => v && handleProviderChange(v)}>
                <SelectTrigger>
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="asaas">Asaas (Boleto / PIX)</SelectItem>
                  <SelectItem value="stripe">Stripe (Cartão de crédito)</SelectItem>
                </SelectContent>
              </Select>
            </div>

            {provider === 'asaas' && (
              <div className="space-y-1">
                <Label>Forma de pagamento</Label>
                <Select value={billingType} onValueChange={(v) => v && setBillingType(v)}>
                  <SelectTrigger>
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value="BOLETO">Boleto bancário</SelectItem>
                    <SelectItem value="PIX">PIX</SelectItem>
                    <SelectItem value="CREDIT_CARD">Cartão de crédito</SelectItem>
                  </SelectContent>
                </Select>
              </div>
            )}

            {provider === 'asaas' && (
              <div className="space-y-1">
                <Label htmlFor="cpfCnpj">CPF ou CNPJ</Label>
                <Input
                  id="cpfCnpj"
                  placeholder="000.000.000-00"
                  value={cpfCnpj}
                  onChange={(e) => setCpfCnpj(e.target.value)}
                />
              </div>
            )}

            {billingType === 'CREDIT_CARD' && (
              <div className="space-y-1">
                <Label htmlFor="paymentMethodId">
                  {provider === 'stripe' ? 'Stripe Payment Method ID' : 'Token do cartão (Asaas)'}
                </Label>
                <Input
                  id="paymentMethodId"
                  placeholder={provider === 'stripe' ? 'pm_xxx' : 'tokenCreditCard_xxx'}
                  value={paymentMethodId}
                  onChange={(e) => setPaymentMethodId(e.target.value)}
                />
                {provider === 'stripe' && (
                  <p className="text-xs text-muted-foreground">
                    Em produção, use Stripe.js Elements para obter o paymentMethodId de forma
                    segura.
                  </p>
                )}
              </div>
            )}

            {dialogError && <p className="text-sm text-destructive">{dialogError}</p>}

            <Button
              className="w-full"
              onClick={handleSubscribe}
              disabled={subscribe.isPending}
            >
              {subscribe.isPending ? 'Processando…' : 'Confirmar assinatura'}
            </Button>
          </div>
        </DialogContent>
      </Dialog>

      {/* Invoice result dialog */}
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

              {invoiceResult.clientSecret && (
                <p className="text-sm text-muted-foreground">
                  Pagamento Stripe iniciado. Use Stripe.js com o client_secret para confirmar.
                </p>
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
