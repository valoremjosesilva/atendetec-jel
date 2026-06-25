import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { adminClient } from '@/api/adminClient';
import { useAdminStore } from '@/stores/adminStore';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';

export default function AdminLoginPage() {
  const navigate = useNavigate();
  const setAdminKey = useAdminStore((s) => s.setAdminKey);
  const [key, setKey] = useState('');
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(false);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError('');
    setLoading(true);
    // Valida a chave fazendo uma chamada real ao /admin/plans com o header.
    try {
      await adminClient.get('/admin/plans', { headers: { 'X-Admin-Key': key } });
      setAdminKey(key);
      navigate('/admin/tenants');
    } catch {
      setError('Chave de administrador inválida.');
    } finally {
      setLoading(false);
    }
  }

  return (
    <div className="min-h-screen flex items-center justify-center bg-muted/30 p-4">
      <Card className="w-full max-w-sm">
        <CardHeader>
          <CardTitle>Superadmin · Mensagee</CardTitle>
          <CardDescription>Acesso restrito. Informe a chave de administrador.</CardDescription>
        </CardHeader>
        <CardContent>
          <form onSubmit={handleSubmit} className="space-y-4">
            <div className="space-y-1">
              <Label htmlFor="adminKey">Chave de administrador</Label>
              <Input
                id="adminKey"
                type="password"
                value={key}
                onChange={(e) => setKey(e.target.value)}
                autoFocus
              />
            </div>
            {error && <p className="text-sm text-destructive">{error}</p>}
            <Button type="submit" className="w-full" disabled={loading || !key}>
              {loading ? 'Entrando…' : 'Entrar'}
            </Button>
          </form>
        </CardContent>
      </Card>
    </div>
  );
}
