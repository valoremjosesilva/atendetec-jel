import type { ReactNode } from 'react';
import { Navigate } from 'react-router-dom';
import { useEntitlements, type Entitlements } from '@/hooks/useEntitlements';

// Bloqueia rotas de features não incluídas no plano do tenant (acesso direto por URL).
export default function FeatureGuard({
  feature,
  children,
}: {
  feature: keyof Pick<Entitlements, 'aiEnabled' | 'schedulingEnabled'>;
  children: ReactNode;
}) {
  const { data, isLoading } = useEntitlements();

  if (isLoading) return <p className="text-muted-foreground">Carregando…</p>;
  // Falha ao carregar /me não deve trancar o usuário fora: libera por padrão.
  if (data && !data.entitlements[feature]) return <Navigate to="/dashboard" replace />;

  return <>{children}</>;
}
