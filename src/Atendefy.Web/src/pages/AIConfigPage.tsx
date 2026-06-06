import { useEffect, useState } from 'react';
import { useAIConfig, useSaveAIConfig } from '@/hooks/useAIConfig';
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

const MODELS: Record<string, string[]> = {
  openai: ['gpt-4o', 'gpt-4o-mini', 'gpt-4-turbo'],
  anthropic: ['claude-opus-4-8', 'claude-sonnet-4-6', 'claude-haiku-4-5-20251001'],
};

export default function AIConfigPage() {
  const { data: config, isLoading } = useAIConfig();
  const saveConfig = useSaveAIConfig();

  const [provider, setProvider] = useState('openai');
  const [apiKey, setApiKey] = useState('');
  const [model, setModel] = useState(MODELS.openai[0]);
  const [systemPrompt, setSystemPrompt] = useState('');
  const [success, setSuccess] = useState(false);
  const [error, setError] = useState('');

  useEffect(() => {
    if (config) {
      setProvider(config.provider);
      setModel(config.model);
      setSystemPrompt(config.systemPrompt ?? '');
    }
  }, [config]);

  function handleProviderChange(v: string) {
    setProvider(v);
    setModel(MODELS[v]?.[0] ?? '');
  }

  async function handleSave(e: React.FormEvent) {
    e.preventDefault();
    setError('');
    setSuccess(false);
    try {
      await saveConfig.mutateAsync({
        provider,
        ...(apiKey ? { apiKey } : {}),
        model,
        systemPrompt,
      });
      setSuccess(true);
      setApiKey('');
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
      <h1 className="text-2xl font-bold">Configuração de IA</h1>
      <Card>
        <CardHeader>
          <CardTitle>Provedor de IA</CardTitle>
          <CardDescription>
            A API key é criptografada antes de ser armazenada. Deixe em branco para manter a chave
            atual.
          </CardDescription>
        </CardHeader>
        <CardContent>
          <form onSubmit={handleSave} className="space-y-4">
            <div className="space-y-1">
              <Label>Provedor</Label>
              <Select value={provider} onValueChange={(v) => v && handleProviderChange(v)}>
                <SelectTrigger>
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="openai">OpenAI</SelectItem>
                  <SelectItem value="anthropic">Anthropic</SelectItem>
                </SelectContent>
              </Select>
            </div>

            <div className="space-y-1">
              <Label htmlFor="apiKey">
                API Key{config ? ' (deixe em branco para manter a atual)' : ''}
              </Label>
              <Input
                id="apiKey"
                type="password"
                placeholder={config ? '••••••••' : 'sk-…'}
                value={apiKey}
                onChange={(e) => setApiKey(e.target.value)}
                required={!config}
              />
            </div>

            <div className="space-y-1">
              <Label>Modelo</Label>
              <Select value={model} onValueChange={(v) => v && setModel(v)}>
                <SelectTrigger>
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {(MODELS[provider] ?? []).map((m) => (
                    <SelectItem key={m} value={m}>
                      {m}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>

            <div className="space-y-1">
              <Label htmlFor="systemPrompt">Prompt do sistema</Label>
              <Textarea
                id="systemPrompt"
                rows={5}
                placeholder="Você é um assistente de atendimento ao cliente da [empresa]…"
                value={systemPrompt}
                onChange={(e) => setSystemPrompt(e.target.value)}
              />
            </div>

            {success && (
              <p className="text-sm text-green-600">Configuração salva com sucesso.</p>
            )}
            {error && <p className="text-sm text-destructive">{error}</p>}

            <Button type="submit" disabled={saveConfig.isPending}>
              {saveConfig.isPending ? 'Salvando…' : 'Salvar'}
            </Button>
          </form>
        </CardContent>
      </Card>
    </div>
  );
}
