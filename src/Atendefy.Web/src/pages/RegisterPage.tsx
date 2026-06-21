import { useState } from 'react';
import { Link } from 'react-router-dom';
import { apiClient } from '@/api/client';
import type { RegisterRequest } from '@/types/api';
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

export default function RegisterPage() {
  const [form, setForm] = useState<RegisterRequest>({
    companyName: '',
    subdomain: '',
    ownerName: '',
    ownerEmail: '',
    ownerPassword: '',
  });
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(false);
  const [submitted, setSubmitted] = useState(false);

  function update(field: keyof RegisterRequest, value: string) {
    setForm((prev) => ({ ...prev, [field]: value }));
  }

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError('');
    setLoading(true);
    try {
      // A empresa nasce "pendente": não logamos aqui; o acesso libera após aprovação.
      await apiClient.post('/tenants/register', form);
      setSubmitted(true);
    } catch (err: unknown) {
      const msg =
        (err as { response?: { data?: { error?: string } } })?.response?.data?.error ??
        'Erro ao criar empresa.';
      setError(msg);
    } finally {
      setLoading(false);
    }
  }

  if (submitted) {
    return (
      <div className="min-h-screen flex items-center justify-center bg-background p-4">
        <Card className="w-full max-w-md text-center">
          <CardHeader>
            <CardTitle>Empresa cadastrada! 🎉</CardTitle>
            <CardDescription>
              Sua conta está <strong>em análise</strong>. Assim que for liberada, você poderá
              entrar com o e-mail e a senha que cadastrou.
            </CardDescription>
          </CardHeader>
          <CardContent>
            <Button className="w-full" render={<Link to="/login">Ir para o login</Link>} />
          </CardContent>
        </Card>
      </div>
    );
  }

  return (
    <div className="min-h-screen flex items-center justify-center bg-background p-4">
      <Card className="w-full max-w-md">
        <CardHeader>
          <CardTitle>Criar conta</CardTitle>
          <CardDescription>Comece o seu teste grátis agora.</CardDescription>
        </CardHeader>
        <CardContent>
          <form onSubmit={handleSubmit} className="space-y-4">
            <div className="space-y-1">
              <Label htmlFor="companyName">Nome da empresa</Label>
              <Input
                id="companyName"
                value={form.companyName}
                onChange={(e) => update('companyName', e.target.value)}
                required
              />
            </div>
            <div className="space-y-1">
              <Label htmlFor="subdomain">Subdomínio</Label>
              <Input
                id="subdomain"
                placeholder="minha-empresa"
                value={form.subdomain}
                onChange={(e) =>
                  update('subdomain', e.target.value.toLowerCase().replace(/[^a-z0-9-]/g, ''))
                }
                required
              />
              <p className="text-xs text-muted-foreground">
                Sua empresa será identificada por{' '}
                <strong>{form.subdomain || '<subdomínio>'}</strong>
              </p>
            </div>
            <div className="space-y-1">
              <Label htmlFor="ownerName">Seu nome</Label>
              <Input
                id="ownerName"
                value={form.ownerName}
                onChange={(e) => update('ownerName', e.target.value)}
                required
              />
            </div>
            <div className="space-y-1">
              <Label htmlFor="ownerEmail">E-mail</Label>
              <Input
                id="ownerEmail"
                type="email"
                value={form.ownerEmail}
                onChange={(e) => update('ownerEmail', e.target.value)}
                required
              />
            </div>
            <div className="space-y-1">
              <Label htmlFor="ownerPassword">Senha</Label>
              <Input
                id="ownerPassword"
                type="password"
                value={form.ownerPassword}
                onChange={(e) => update('ownerPassword', e.target.value)}
                required
                minLength={8}
              />
            </div>
            {error && <p className="text-sm text-destructive">{error}</p>}
            <Button type="submit" className="w-full" disabled={loading}>
              {loading ? 'Criando conta…' : 'Criar conta'}
            </Button>
            <p className="text-sm text-center text-muted-foreground">
              Já tem conta?{' '}
              <Link to="/login" className="underline">
                Entrar
              </Link>
            </p>
          </form>
        </CardContent>
      </Card>
    </div>
  );
}
