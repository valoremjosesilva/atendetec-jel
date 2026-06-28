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
import Turnstile from '@/components/Turnstile';

export default function RegisterPage() {
  const [form, setForm] = useState<RegisterRequest>({
    companyName: '',
    subdomain: '',
    ownerName: '',
    ownerEmail: '',
    ownerPassword: '',
  });
  const [captchaToken, setCaptchaToken] = useState('');
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(false);
  const [submitted, setSubmitted] = useState(false);
  const [resent, setResent] = useState(false);

  function update(field: keyof RegisterRequest, value: string) {
    setForm((prev) => ({ ...prev, [field]: value }));
  }

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError('');
    setLoading(true);
    try {
      // A empresa nasce "pendente" e o e-mail precisa ser confirmado antes da aprovação.
      await apiClient.post('/tenants/register', { ...form, captchaToken });
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

  async function handleResend() {
    setResent(false);
    try {
      await apiClient.post('/tenants/resend-verification', {
        subdomain: form.subdomain,
        email: form.ownerEmail,
      });
      setResent(true);
    } catch {
      setResent(true); // resposta genérica de qualquer forma
    }
  }

  if (submitted) {
    return (
      <div className="min-h-screen flex items-center justify-center bg-background p-4">
        <Card className="w-full max-w-md text-center">
          <CardHeader>
            <CardTitle>Confirme seu e-mail 📧</CardTitle>
            <CardDescription>
              Enviamos um link de confirmação para <strong>{form.ownerEmail}</strong>. Clique no link
              para validar seu e-mail. Depois disso, sua conta entra em análise e avisaremos quando for
              liberada.
            </CardDescription>
          </CardHeader>
          <CardContent className="space-y-3">
            <Button variant="outline" className="w-full" onClick={handleResend}>
              Reenviar e-mail de confirmação
            </Button>
            {resent && (
              <p className="text-sm text-green-600">Se houver um cadastro pendente, o e-mail foi reenviado.</p>
            )}
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
            <Turnstile onToken={setCaptchaToken} />
            {error && <p className="text-sm text-destructive">{error}</p>}
            <Button type="submit" className="w-full" disabled={loading || !captchaToken}>
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
