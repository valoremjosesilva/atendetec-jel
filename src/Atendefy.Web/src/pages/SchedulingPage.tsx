import { useEffect, useState } from 'react';
import { useScheduling, useSaveScheduling } from '@/hooks/useScheduling';
import { Button } from '@/components/ui/button';
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from '@/components/ui/card';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { Textarea } from '@/components/ui/textarea';

export default function SchedulingPage() {
  const { data: config, isLoading } = useScheduling();
  const save = useSaveScheduling();

  const [enabled, setEnabled] = useState(false);
  const [provider, setProvider] = useState('calcom');
  const [bookingUrl, setBookingUrl] = useState('');
  const [instructions, setInstructions] = useState('');
  const [success, setSuccess] = useState(false);
  const [error, setError] = useState('');

  useEffect(() => {
    if (config) {
      setEnabled(config.enabled);
      setProvider(config.provider ?? 'calcom');
      setBookingUrl(config.bookingUrl ?? '');
      setInstructions(config.instructions ?? '');
    }
  }, [config]);

  async function handleSave(e: React.FormEvent) {
    e.preventDefault();
    setError('');
    setSuccess(false);
    try {
      await save.mutateAsync({ provider, bookingUrl, enabled, instructions });
      setSuccess(true);
    } catch (err: unknown) {
      const msg =
        (err as { response?: { data?: { error?: string } } })?.response?.data?.error ??
        'Erro ao salvar configuração.';
      setError(msg);
    }
  }

  if (isLoading) return <p className="text-muted-foreground">Carregando…</p>;

  return (
    <div className="space-y-6 max-w-2xl">
      <h1 className="text-2xl font-bold">Agenda</h1>
      <Card>
        <CardHeader>
          <CardTitle>Agendamento por link</CardTitle>
          <CardDescription>
            Quando ativado, o assistente envia o seu link de agendamento (Cal.com, Calendly…) sempre
            que o cliente quiser marcar um horário. Conecte sua agenda Google/Apple dentro do próprio
            serviço de agendamento.
          </CardDescription>
        </CardHeader>
        <CardContent>
          <form onSubmit={handleSave} className="space-y-4">
            <div className="space-y-1">
              <Label>Status</Label>
              <Select
                value={enabled ? 'on' : 'off'}
                onValueChange={(v) => v && setEnabled(v === 'on')}
              >
                <SelectTrigger>
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="on">Ativado</SelectItem>
                  <SelectItem value="off">Desativado</SelectItem>
                </SelectContent>
              </Select>
            </div>

            <div className="space-y-1">
              <Label>Serviço</Label>
              <Select value={provider} onValueChange={(v) => v && setProvider(v)}>
                <SelectTrigger>
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="calcom">Cal.com</SelectItem>
                  <SelectItem value="calendly">Calendly</SelectItem>
                  <SelectItem value="other">Outro</SelectItem>
                </SelectContent>
              </Select>
            </div>

            <div className="space-y-1">
              <Label htmlFor="bookingUrl">Link de agendamento</Label>
              <Input
                id="bookingUrl"
                type="url"
                placeholder="https://cal.com/sua-empresa/consulta"
                value={bookingUrl}
                onChange={(e) => setBookingUrl(e.target.value)}
                required={enabled}
              />
            </div>

            <div className="space-y-1">
              <Label htmlFor="instructions">Instruções para o assistente (opcional)</Label>
              <Textarea
                id="instructions"
                rows={3}
                placeholder="Ex.: ofereça o link apenas para consultas; atendimento de seg a sex."
                value={instructions}
                onChange={(e) => setInstructions(e.target.value)}
              />
            </div>

            {success && <p className="text-sm text-green-600">Configuração salva com sucesso.</p>}
            {error && <p className="text-sm text-destructive">{error}</p>}

            <Button type="submit" disabled={save.isPending}>
              {save.isPending ? 'Salvando…' : 'Salvar'}
            </Button>
          </form>
        </CardContent>
      </Card>
    </div>
  );
}
