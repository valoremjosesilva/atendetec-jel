import { useState } from 'react';
import { useCreateWhatsAppAccount, useWhatsAppAccounts, useConnectWhatsApp, useWhatsAppStatus } from '@/hooks/useWhatsApp';
import { Badge } from '@/components/ui/badge';
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
import { Textarea } from '@/components/ui/textarea';
import { Plus, QrCode } from 'lucide-react';

type Provider = 'meta' | 'evolution';

const CONFIG_PLACEHOLDER: Record<Provider, string> = {
  meta: JSON.stringify({ phoneNumberId: '1234567890', accessToken: 'EAAxxxxxxx' }, null, 2),
  evolution: JSON.stringify(
    { base_url: 'http://evolution-api:8080', instance: 'atendefy-dev', api_key: 'dev_evolution_key' },
    null,
    2
  ),
};

function statusVariant(status: string): 'default' | 'secondary' | 'outline' {
  if (status === 'connected' || status === 'open') return 'default';
  if (status === 'disconnected' || status === 'close') return 'secondary';
  return 'outline';
}

function statusLabel(status: string): string {
  if (status === 'connected' || status === 'open') return 'conectado';
  if (status === 'connecting') return 'conectando…';
  if (status === 'disconnected' || status === 'close') return 'desconectado';
  return status;
}

// ─── QR Code Dialog ───────────────────────────────────────────────────────────

function QrDialog({ accountId }: { accountId: string }) {
  const [open, setOpen] = useState(false);
  const connect = useConnectWhatsApp();
  const { data: statusData } = useWhatsAppStatus(accountId, open);

  const isConnected = statusData?.status === 'open' || statusData?.status === 'connected';
  const qrCode = connect.data?.qrCode ?? null;

  function handleOpen(value: boolean) {
    setOpen(value);
    if (value) {
      connect.reset();
      connect.mutate(accountId);
    }
  }

  return (
    <Dialog open={open} onOpenChange={handleOpen}>
      <DialogTrigger
        render={
          <Button size="sm" variant="outline">
            <QrCode className="h-4 w-4 mr-1" />
            Conectar
          </Button>
        }
      />
      <DialogContent className="max-w-sm text-center">
        <DialogHeader>
          <DialogTitle>Conectar WhatsApp</DialogTitle>
        </DialogHeader>

        {isConnected ? (
          <div className="py-6 space-y-2">
            <p className="text-2xl">✓</p>
            <p className="font-medium text-green-600">WhatsApp conectado!</p>
          </div>
        ) : connect.isPending ? (
          <p className="py-6 text-muted-foreground">Gerando QR code…</p>
        ) : connect.isError ? (
          <div className="py-6 space-y-3">
            <p className="text-sm text-destructive">Erro ao gerar QR code.</p>
            <Button size="sm" variant="outline" onClick={() => { connect.reset(); connect.mutate(accountId); }}>
              Tentar novamente
            </Button>
          </div>
        ) : qrCode ? (
          <div className="space-y-3">
            <img src={qrCode} alt="QR Code WhatsApp" className="mx-auto w-56 h-56 rounded-lg border" />
            <p className="text-sm text-muted-foreground">
              Abra o WhatsApp → Aparelhos conectados → Conectar um aparelho
            </p>
            <p className="text-xs text-muted-foreground">Verificando conexão a cada 3s…</p>
          </div>
        ) : null}
      </DialogContent>
    </Dialog>
  );
}

// ─── Main Page ────────────────────────────────────────────────────────────────

export default function WhatsAppPage() {
  const { data: accounts, isLoading } = useWhatsAppAccounts();
  const createAccount = useCreateWhatsAppAccount();

  const [open, setOpen] = useState(false);
  const [provider, setProvider] = useState<Provider>('meta');
  const [phone, setPhone] = useState('');
  const [configJson, setConfigJson] = useState(CONFIG_PLACEHOLDER.meta);
  const [error, setError] = useState('');

  function handleProviderChange(v: string) {
    const p = v as Provider;
    setProvider(p);
    setConfigJson(CONFIG_PLACEHOLDER[p]);
  }

  async function handleCreate() {
    setError('');
    try {
      JSON.parse(configJson);
    } catch {
      setError('configJson inválido — verifique o JSON.');
      return;
    }
    try {
      await createAccount.mutateAsync({ provider, phone, configJson });
      setOpen(false);
      setPhone('');
      setConfigJson(CONFIG_PLACEHOLDER[provider]);
    } catch (err: unknown) {
      const msg =
        (err as { response?: { data?: { error?: string } } })?.response?.data?.error ??
        'Erro ao criar conta.';
      setError(msg);
    }
  }

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-bold">Contas WhatsApp</h1>
        <Dialog open={open} onOpenChange={setOpen}>
          <DialogTrigger render={<Button />}>
            <Plus className="h-4 w-4 mr-2" />
            Nova conta
          </DialogTrigger>
          <DialogContent className="max-w-lg">
            <DialogHeader>
              <DialogTitle>Conectar conta WhatsApp</DialogTitle>
            </DialogHeader>
            <div className="space-y-4">
              <div className="space-y-1">
                <Label>Provedor</Label>
                <Select value={provider} onValueChange={(v) => v && handleProviderChange(v)}>
                  <SelectTrigger>
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value="meta">Meta (WhatsApp Cloud API)</SelectItem>
                    <SelectItem value="evolution">Evolution API</SelectItem>
                  </SelectContent>
                </Select>
              </div>
              <div className="space-y-1">
                <Label htmlFor="phone">Número (com DDI, ex: +5511999999999)</Label>
                <Input
                  id="phone"
                  value={phone}
                  onChange={(e) => setPhone(e.target.value)}
                  placeholder="+5511999999999"
                />
              </div>
              <div className="space-y-1">
                <Label htmlFor="configJson">Configuração (JSON)</Label>
                <Textarea
                  id="configJson"
                  className="font-mono text-xs"
                  rows={6}
                  value={configJson}
                  onChange={(e) => setConfigJson(e.target.value)}
                />
              </div>
              {error && <p className="text-sm text-destructive">{error}</p>}
              <Button
                className="w-full"
                onClick={handleCreate}
                disabled={createAccount.isPending}
              >
                {createAccount.isPending ? 'Salvando…' : 'Salvar'}
              </Button>
            </div>
          </DialogContent>
        </Dialog>
      </div>

      {isLoading && <p className="text-muted-foreground">Carregando…</p>}

      {!isLoading && accounts?.length === 0 && (
        <p className="text-muted-foreground">Nenhuma conta conectada ainda.</p>
      )}

      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
        {accounts?.map((acc) => (
          <Card key={acc.id}>
            <CardHeader className="flex flex-row items-center justify-between space-y-0 pb-2">
              <CardTitle className="text-sm font-medium capitalize">{acc.provider}</CardTitle>
              <Badge variant={statusVariant(acc.status)}>{statusLabel(acc.status)}</Badge>
            </CardHeader>
            <CardContent>
              <p className="text-sm">{acc.phone}</p>
              <p className="text-xs text-muted-foreground mt-1">
                {new Date(acc.createdAt).toLocaleDateString('pt-BR')}
              </p>
              {acc.provider === 'evolution' && acc.status !== 'connected' && acc.status !== 'open' && (
                <div className="mt-3">
                  <QrDialog accountId={acc.id} />
                </div>
              )}
            </CardContent>
          </Card>
        ))}
      </div>
    </div>
  );
}
