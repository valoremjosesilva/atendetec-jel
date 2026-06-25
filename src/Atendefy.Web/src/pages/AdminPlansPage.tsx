import { useState } from 'react';
import {
  usePlans,
  useCreatePlan,
  useUpdatePlan,
  type AdminPlan,
  type PlanInput,
} from '@/hooks/useAdmin';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
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
import { Plus, Pencil } from 'lucide-react';

const EMPTY: PlanInput = {
  name: '',
  priceMonthly: 0,
  priceYearly: 0,
  isActive: true,
  whatsAppAccounts: 1,
  messagesPerMonth: 1000,
  teamMembers: 1,
  aiEnabled: true,
  schedulingEnabled: false,
};

function PlanForm({
  initial,
  planId,
  onDone,
}: {
  initial: PlanInput;
  planId?: string;
  onDone: () => void;
}) {
  const create = useCreatePlan();
  const update = useUpdatePlan();
  const [form, setForm] = useState<PlanInput>(initial);
  const [error, setError] = useState('');
  const pending = create.isPending || update.isPending;

  function set<K extends keyof PlanInput>(key: K, value: PlanInput[K]) {
    setForm((f) => ({ ...f, [key]: value }));
  }

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError('');
    try {
      if (planId) await update.mutateAsync({ id: planId, input: form });
      else await create.mutateAsync(form);
      onDone();
    } catch {
      setError('Erro ao salvar plano.');
    }
  }

  return (
    <form onSubmit={handleSubmit} className="space-y-4">
      <div className="space-y-1">
        <Label htmlFor="name">Nome do plano</Label>
        <Input id="name" value={form.name} onChange={(e) => set('name', e.target.value)} required />
      </div>

      <div className="grid grid-cols-2 gap-3">
        <div className="space-y-1">
          <Label htmlFor="priceMonthly">Preço mensal (R$)</Label>
          <Input
            id="priceMonthly"
            type="number"
            min={0}
            step="0.01"
            value={form.priceMonthly}
            onChange={(e) => set('priceMonthly', Number(e.target.value))}
          />
        </div>
        <div className="space-y-1">
          <Label htmlFor="priceYearly">Preço anual (R$)</Label>
          <Input
            id="priceYearly"
            type="number"
            min={0}
            step="0.01"
            value={form.priceYearly}
            onChange={(e) => set('priceYearly', Number(e.target.value))}
          />
        </div>
      </div>

      <div className="grid grid-cols-2 gap-3">
        <div className="space-y-1">
          <Label htmlFor="whatsAppAccounts">Contas WhatsApp</Label>
          <Input
            id="whatsAppAccounts"
            type="number"
            min={0}
            value={form.whatsAppAccounts}
            onChange={(e) => set('whatsAppAccounts', Number(e.target.value))}
          />
        </div>
        <div className="space-y-1">
          <Label htmlFor="teamMembers">Usuários da equipe</Label>
          <Input
            id="teamMembers"
            type="number"
            min={1}
            value={form.teamMembers}
            onChange={(e) => set('teamMembers', Number(e.target.value))}
          />
        </div>
      </div>

      <div className="space-y-1">
        <Label htmlFor="messagesPerMonth">Mensagens de IA por mês</Label>
        <Input
          id="messagesPerMonth"
          type="number"
          min={0}
          value={form.messagesPerMonth}
          onChange={(e) => set('messagesPerMonth', Number(e.target.value))}
        />
      </div>

      <div className="grid grid-cols-2 gap-3">
        <div className="space-y-1">
          <Label>IA (atendimento)</Label>
          <Select
            value={form.aiEnabled ? 'on' : 'off'}
            onValueChange={(v) => v && set('aiEnabled', v === 'on')}
          >
            <SelectTrigger>
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="on">Habilitada</SelectItem>
              <SelectItem value="off">Desabilitada</SelectItem>
            </SelectContent>
          </Select>
        </div>
        <div className="space-y-1">
          <Label>Agenda</Label>
          <Select
            value={form.schedulingEnabled ? 'on' : 'off'}
            onValueChange={(v) => v && set('schedulingEnabled', v === 'on')}
          >
            <SelectTrigger>
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="on">Habilitada</SelectItem>
              <SelectItem value="off">Desabilitada</SelectItem>
            </SelectContent>
          </Select>
        </div>
      </div>

      <div className="space-y-1">
        <Label>Plano ativo</Label>
        <Select
          value={form.isActive ? 'on' : 'off'}
          onValueChange={(v) => v && set('isActive', v === 'on')}
        >
          <SelectTrigger>
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value="on">Sim</SelectItem>
            <SelectItem value="off">Não</SelectItem>
          </SelectContent>
        </Select>
      </div>

      {error && <p className="text-sm text-destructive">{error}</p>}
      <Button type="submit" className="w-full" disabled={pending}>
        {pending ? 'Salvando…' : 'Salvar'}
      </Button>
    </form>
  );
}

function planToInput(p: AdminPlan): PlanInput {
  const { id: _id, ...rest } = p;
  return rest;
}

function PlanCard({ plan }: { plan: AdminPlan }) {
  const [open, setOpen] = useState(false);
  return (
    <Card>
      <CardHeader className="flex flex-row items-center justify-between space-y-0 pb-2">
        <CardTitle className="text-base">
          {plan.name}
          {!plan.isActive && (
            <span className="ml-2 text-xs font-normal text-muted-foreground">(inativo)</span>
          )}
        </CardTitle>
        <Dialog open={open} onOpenChange={setOpen}>
          <DialogTrigger render={<Button size="sm" variant="outline" />}>
            <Pencil className="h-4 w-4 mr-1" />
            Editar
          </DialogTrigger>
          <DialogContent className="max-w-lg">
            <DialogHeader>
              <DialogTitle>Editar plano</DialogTitle>
            </DialogHeader>
            <PlanForm initial={planToInput(plan)} planId={plan.id} onDone={() => setOpen(false)} />
          </DialogContent>
        </Dialog>
      </CardHeader>
      <CardContent className="text-sm space-y-1">
        <p>R$ {plan.priceMonthly.toFixed(2)}/mês · R$ {plan.priceYearly.toFixed(2)}/ano</p>
        <ul className="text-muted-foreground space-y-0.5">
          <li>{plan.whatsAppAccounts} conta(s) WhatsApp</li>
          <li>{plan.teamMembers} usuário(s)</li>
          <li>IA: {plan.aiEnabled ? `sim (${plan.messagesPerMonth}/mês)` : 'não'}</li>
          <li>Agenda: {plan.schedulingEnabled ? 'sim' : 'não'}</li>
        </ul>
      </CardContent>
    </Card>
  );
}

export default function AdminPlansPage() {
  const { data: plans, isLoading } = usePlans();
  const [open, setOpen] = useState(false);

  return (
    <div className="space-y-6 max-w-4xl">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-bold">Planos</h1>
        <Dialog open={open} onOpenChange={setOpen}>
          <DialogTrigger render={<Button />}>
            <Plus className="h-4 w-4 mr-2" />
            Novo plano
          </DialogTrigger>
          <DialogContent className="max-w-lg">
            <DialogHeader>
              <DialogTitle>Novo plano</DialogTitle>
            </DialogHeader>
            <PlanForm initial={EMPTY} onDone={() => setOpen(false)} />
          </DialogContent>
        </Dialog>
      </div>

      {isLoading && <p className="text-muted-foreground">Carregando…</p>}

      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
        {plans?.map((p) => (
          <PlanCard key={p.id} plan={p} />
        ))}
      </div>
    </div>
  );
}
