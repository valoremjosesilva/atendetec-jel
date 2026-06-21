import { useAppointments } from '@/hooks/useScheduling';
import { Badge } from '@/components/ui/badge';
import { CalendarCheck } from 'lucide-react';

function formatDateTime(dateStr: string | null): string {
  if (!dateStr) return '—';
  return new Date(dateStr).toLocaleString('pt-BR', {
    day: '2-digit',
    month: '2-digit',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  });
}

function statusVariant(status: string): 'default' | 'secondary' | 'outline' {
  if (status === 'cancelled') return 'secondary';
  if (status === 'rescheduled') return 'outline';
  return 'default';
}

function statusLabel(status: string): string {
  if (status === 'cancelled') return 'cancelado';
  if (status === 'rescheduled') return 'remarcado';
  if (status === 'confirmed') return 'confirmado';
  return status;
}

export default function AppointmentsPage() {
  const { data, isLoading, isError } = useAppointments();

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-bold">Agendamentos</h1>
        {data && <p className="text-sm text-muted-foreground">{data.length} agendamento(s)</p>}
      </div>

      {isLoading && <p className="text-muted-foreground">Carregando…</p>}
      {isError && <p className="text-destructive">Erro ao carregar agendamentos.</p>}

      {!isLoading && data?.length === 0 && (
        <div className="text-center py-12 text-muted-foreground">
          <CalendarCheck className="h-10 w-10 mx-auto mb-3 opacity-30" />
          <p className="text-sm">Nenhum agendamento ainda.</p>
          <p className="text-xs mt-1">
            Os agendamentos aparecem aqui automaticamente quando o cliente confirma um horário —
            desde que o webhook esteja configurado no Cal.com (veja a tela Agenda).
          </p>
        </div>
      )}

      {data && data.length > 0 && (
        <div className="border rounded-lg overflow-hidden">
          <table className="w-full text-sm">
            <thead className="border-b bg-muted/50">
              <tr>
                <th className="text-left px-4 py-3 font-medium">Início</th>
                <th className="text-left px-4 py-3 font-medium">Serviço</th>
                <th className="text-left px-4 py-3 font-medium">Cliente</th>
                <th className="text-left px-4 py-3 font-medium">Telefone</th>
                <th className="text-left px-4 py-3 font-medium">Status</th>
              </tr>
            </thead>
            <tbody>
              {data.map((appt) => (
                <tr key={appt.id} className="border-b last:border-0 hover:bg-muted/30">
                  <td className="px-4 py-3 whitespace-nowrap">{formatDateTime(appt.startTime)}</td>
                  <td className="px-4 py-3">{appt.title ?? '—'}</td>
                  <td className="px-4 py-3">
                    <span className={appt.attendeeName ? '' : 'text-muted-foreground italic'}>
                      {appt.attendeeName ?? 'sem nome'}
                    </span>
                  </td>
                  <td className="px-4 py-3 font-mono text-xs">{appt.attendeePhone ?? '—'}</td>
                  <td className="px-4 py-3">
                    <Badge variant={statusVariant(appt.status)}>{statusLabel(appt.status)}</Badge>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}
