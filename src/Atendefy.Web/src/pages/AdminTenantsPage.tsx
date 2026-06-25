import { useState } from 'react';
import {
  useAdminTenants,
  usePlans,
  useAssignPlan,
  useActivateTenant,
  type AdminTenant,
} from '@/hooks/useAdmin';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Card, CardContent } from '@/components/ui/card';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';

function statusVariant(status: string): 'default' | 'secondary' | 'outline' {
  if (status === 'active') return 'default';
  if (status === 'pending') return 'outline';
  return 'secondary';
}

function TenantRow({ tenant }: { tenant: AdminTenant }) {
  const { data: plans } = usePlans();
  const assign = useAssignPlan();
  const activate = useActivateTenant();
  const [feedback, setFeedback] = useState('');

  async function handleAssign(planId: string) {
    setFeedback('');
    try {
      await assign.mutateAsync({ subdomain: tenant.subdomain, planId });
      setFeedback('Plano atualizado.');
    } catch {
      setFeedback('Erro ao atribuir plano.');
    }
  }

  return (
    <Card>
      <CardContent className="flex flex-wrap items-center gap-4 py-4">
        <div className="min-w-48 flex-1">
          <p className="font-medium">{tenant.name}</p>
          <p className="text-xs text-muted-foreground">{tenant.subdomain}</p>
        </div>

        <Badge variant={statusVariant(tenant.status)}>{tenant.status}</Badge>

        <div className="w-48">
          <Select
            value={tenant.planId ?? ''}
            onValueChange={(v) => v && handleAssign(v)}
          >
            <SelectTrigger>
              <SelectValue placeholder="Sem plano" />
            </SelectTrigger>
            <SelectContent>
              {plans?.map((p) => (
                <SelectItem key={p.id} value={p.id}>
                  {p.name}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>

        {tenant.status === 'pending' && (
          <Button
            size="sm"
            variant="outline"
            disabled={activate.isPending}
            onClick={() => activate.mutate(tenant.subdomain)}
          >
            Aprovar
          </Button>
        )}

        {feedback && <span className="text-xs text-muted-foreground">{feedback}</span>}
      </CardContent>
    </Card>
  );
}

export default function AdminTenantsPage() {
  const { data: tenants, isLoading } = useAdminTenants();

  return (
    <div className="space-y-6 max-w-4xl">
      <h1 className="text-2xl font-bold">Empresas</h1>
      <p className="text-sm text-muted-foreground">
        Defina o plano de uso de cada empresa. O plano controla contas de WhatsApp, Agenda, IA e
        limites. A mudança vale imediatamente (a empresa pode precisar recarregar o painel).
      </p>

      {isLoading && <p className="text-muted-foreground">Carregando…</p>}
      {!isLoading && tenants?.length === 0 && (
        <p className="text-muted-foreground">Nenhuma empresa cadastrada.</p>
      )}

      <div className="space-y-3">
        {tenants?.map((t) => (
          <TenantRow key={t.id} tenant={t} />
        ))}
      </div>
    </div>
  );
}
