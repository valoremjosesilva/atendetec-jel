import { useEffect, useRef, useState } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { apiClient } from '@/api/client';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';

type State = 'loading' | 'ok' | 'error';

export default function VerifyEmailPage() {
  const [params] = useSearchParams();
  const token = params.get('token');
  const [state, setState] = useState<State>('loading');
  const ran = useRef(false);

  useEffect(() => {
    if (ran.current) return; // evita dupla execução no StrictMode
    ran.current = true;
    if (!token) {
      setState('error');
      return;
    }
    apiClient
      .post('/tenants/verify-email', { token })
      .then(() => setState('ok'))
      .catch(() => setState('error'));
  }, [token]);

  return (
    <div className="min-h-screen flex items-center justify-center bg-background p-4">
      <Card className="w-full max-w-md text-center">
        {state === 'loading' && (
          <CardHeader>
            <CardTitle>Confirmando seu e-mail…</CardTitle>
            <CardDescription>Aguarde um instante.</CardDescription>
          </CardHeader>
        )}

        {state === 'ok' && (
          <>
            <CardHeader>
              <CardTitle>E-mail confirmado! ✅</CardTitle>
              <CardDescription>
                Obrigado. Sua conta está <strong>em análise</strong>; avisaremos por e-mail assim que
                for liberada para acesso.
              </CardDescription>
            </CardHeader>
            <CardContent>
              <Button className="w-full" render={<Link to="/login">Ir para o login</Link>} />
            </CardContent>
          </>
        )}

        {state === 'error' && (
          <>
            <CardHeader>
              <CardTitle>Link inválido ou expirado</CardTitle>
              <CardDescription>
                Não foi possível confirmar este link. Ele pode ter expirado. Refaça o cadastro ou
                solicite um novo e-mail de confirmação.
              </CardDescription>
            </CardHeader>
            <CardContent>
              <Button className="w-full" render={<Link to="/register">Voltar ao cadastro</Link>} />
            </CardContent>
          </>
        )}
      </Card>
    </div>
  );
}
