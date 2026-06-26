import { useEffect, useState } from 'react';
import { useScheduling, useSaveScheduling, useTestHorafy } from '@/hooks/useScheduling';
import type { SchedulingConfigRequest } from '@/types/api';
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
  const test = useTestHorafy();

  const [enabled, setEnabled] = useState(false);
  const [provider, setProvider] = useState('calcom');
  const [bookingUrl, setBookingUrl] = useState('');
  const [instructions, setInstructions] = useState('');
  // Horafy
  const [apiBaseUrl, setApiBaseUrl] = useState('');
  const [tenantSlug, setTenantSlug] = useState('');
  const [apiKey, setApiKey] = useState('');
  const [success, setSuccess] = useState(false);
  const [error, setError] = useState('');
  const [testMsg, setTestMsg] = useState('');

  const isHorafy = provider === 'horafy';

  useEffect(() => {
    if (config) {
      setEnabled(config.enabled);
      setProvider(config.provider ?? 'calcom');
      setBookingUrl(config.bookingUrl ?? '');
      setInstructions(config.instructions ?? '');
      setApiBaseUrl(config.apiBaseUrl ?? '');
      setTenantSlug(config.tenantSlug ?? '');
    }
  }, [config]);

  async function handleSave(e: React.FormEvent) {
    e.preventDefault();
    setError('');
    setSuccess(false);
    setTestMsg('');
    try {
      const req: SchedulingConfigRequest = isHorafy
        ? {
            provider,
            enabled,
            instructions,
            apiBaseUrl,
            tenantSlug,
            // só envia a chave quando o usuário digitou uma nova
            ...(apiKey ? { apiKey } : {}),
          }
        : { provider, bookingUrl, enabled, instructions };
      await save.mutateAsync(req);
      setApiKey('');
      setSuccess(true);
    } catch (err: unknown) {
      const msg =
        (err as { response?: { data?: { error?: string } } })?.response?.data?.error ??
        'Erro ao salvar configuração.';
      setError(msg);
    }
  }

  async function handleTest() {
    setTestMsg('');
    try {
      const r = await test.mutateAsync();
      setTestMsg(
        r.ok
          ? `Conexão OK — ${r.servicesCount ?? 0} serviço(s) encontrado(s).`
          : `Falha: ${r.error ?? 'erro desconhecido'}`,
      );
    } catch (err: unknown) {
      const msg =
        (err as { response?: { data?: { error?: string } } })?.response?.data?.error ??
        'Erro ao testar conexão.';
      setTestMsg(`Falha: ${msg}`);
    }
  }

  if (isLoading) return <p className="text-muted-foreground">Carregando…</p>;

  return (
    <div className="space-y-6 max-w-2xl">
      <h1 className="text-2xl font-bold">Agenda</h1>
      <Card>
        <CardHeader>
          <CardTitle>Agendamento</CardTitle>
          <CardDescription>
            Conecte uma agenda externa por link (Cal.com, Calendly) ou a sua agenda própria do
            Horafy via API, para agendar dentro da conversa do WhatsApp.
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
                  <SelectItem value="horafy">Horafy (agenda própria)</SelectItem>
                  <SelectItem value="calcom">Cal.com</SelectItem>
                  <SelectItem value="calendly">Calendly</SelectItem>
                  <SelectItem value="other">Outro</SelectItem>
                </SelectContent>
              </Select>
            </div>

            {isHorafy ? (
              <>
                <div className="space-y-1">
                  <Label htmlFor="apiBaseUrl">URL da API do Horafy</Label>
                  <Input
                    id="apiBaseUrl"
                    type="url"
                    placeholder="https://sua-empresa.horafy.com.br"
                    value={apiBaseUrl}
                    onChange={(e) => setApiBaseUrl(e.target.value)}
                    required={enabled}
                  />
                </div>
                <div className="space-y-1">
                  <Label htmlFor="tenantSlug">Slug do tenant</Label>
                  <Input
                    id="tenantSlug"
                    placeholder="sua-empresa"
                    value={tenantSlug}
                    onChange={(e) => setTenantSlug(e.target.value)}
                    required={enabled}
                  />
                </div>
                <div className="space-y-1">
                  <Label htmlFor="apiKey">Chave de API</Label>
                  <Input
                    id="apiKey"
                    type="password"
                    placeholder={config?.hasApiKey ? '•••••••• (já configurada)' : 'htf_live_…'}
                    value={apiKey}
                    onChange={(e) => setApiKey(e.target.value)}
                  />
                  <p className="text-xs text-muted-foreground">
                    Gere em <strong>Horafy → Integrações → API keys</strong>. Deixe em branco para
                    manter a chave atual.
                  </p>
                </div>

                <div className="flex items-center gap-3">
                  <Button type="button" variant="outline" onClick={handleTest} disabled={test.isPending}>
                    {test.isPending ? 'Testando…' : 'Testar conexão'}
                  </Button>
                  {testMsg && <span className="text-sm text-muted-foreground">{testMsg}</span>}
                </div>
              </>
            ) : (
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
            )}

            <div className="space-y-1">
              <Label htmlFor="instructions">Instruções para o assistente (opcional)</Label>
              <Textarea
                id="instructions"
                rows={3}
                placeholder="Ex.: atendimento de seg a sex; confirme o serviço antes de agendar."
                value={instructions}
                onChange={(e) => setInstructions(e.target.value)}
              />
            </div>

            {!isHorafy && config?.webhookUrl && (
              <div className="space-y-1 rounded-md border border-dashed p-3">
                <Label htmlFor="webhookUrl">URL do webhook (opcional — ver agendamentos no painel)</Label>
                <Input
                  id="webhookUrl"
                  readOnly
                  value={config.webhookUrl}
                  onFocus={(e) => e.currentTarget.select()}
                />
                <p className="text-xs text-muted-foreground">
                  Cole esta URL em <strong>Cal.com → Settings → Webhooks</strong> (evento{' '}
                  <strong>BOOKING_CREATED</strong>) para que os agendamentos confirmados apareçam no
                  painel.
                </p>
              </div>
            )}

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
