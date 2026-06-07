import { Link } from 'react-router-dom';
import { Bot, CreditCard, MessageSquare, Wifi } from 'lucide-react';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { useDashboardStats } from '@/hooks/useDashboard';

export default function DashboardPage() {
  const { data: stats, isLoading, isError } = useDashboardStats();
  const waConnected = stats?.whatsAppStatus === 'open';

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-bold">Dashboard</h1>
        <Badge variant={waConnected ? 'default' : 'secondary'}>
          WhatsApp {isLoading ? '…' : (waConnected ? 'conectado' : (stats?.whatsAppStatus ?? 'none'))}
        </Badge>
      </div>
      {isError && (
        <p className="text-sm text-destructive">Falha ao carregar estatísticas.</p>
      )}

      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
        <StatCard
          title="Conversas este mês"
          value={isLoading ? '…' : String(stats?.conversationsThisMonth ?? 0)}
          sub={isLoading ? undefined : `${stats?.totalConversations ?? 0} no total`}
          icon={<MessageSquare className="h-4 w-4 text-muted-foreground" />}
        />
        <StatCard
          title="Mensagens este mês"
          value={isLoading ? '…' : String(stats?.messagesThisMonth ?? 0)}
          icon={<MessageSquare className="h-4 w-4 text-muted-foreground" />}
        />
        <StatCard
          title="Tokens consumidos"
          value={isLoading ? '…' : (stats?.tokensThisMonth ?? 0).toLocaleString('pt-BR')}
          icon={<Bot className="h-4 w-4 text-muted-foreground" />}
        />
        <StatCard
          title="Custo estimado (USD)"
          value={isLoading ? '…' : `$${(stats?.costThisMonth ?? 0).toFixed(4)}`}
          icon={<CreditCard className="h-4 w-4 text-muted-foreground" />}
        />
      </div>

      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
        <ActionCard title="WhatsApp" description="Contas e webhooks" icon={<Wifi className="h-5 w-5" />} to="/whatsapp" />
        <ActionCard title="IA" description="Provedor e system prompt" icon={<Bot className="h-5 w-5" />} to="/ai-config" />
        <ActionCard title="Conversas" description="Histórico de atendimentos" icon={<MessageSquare className="h-5 w-5" />} to="/conversations" />
        <ActionCard title="Billing" description="Planos e assinaturas" icon={<CreditCard className="h-5 w-5" />} to="/billing" />
      </div>
    </div>
  );
}

function StatCard({
  title,
  value,
  sub,
  icon,
}: {
  title: string;
  value: string;
  sub?: string;
  icon: React.ReactNode;
}) {
  return (
    <Card>
      <CardHeader className="flex flex-row items-center justify-between space-y-0 pb-2">
        <CardTitle className="text-sm font-medium">{title}</CardTitle>
        {icon}
      </CardHeader>
      <CardContent>
        <div className="text-2xl font-bold">{value}</div>
        {sub && <p className="text-xs text-muted-foreground mt-1">{sub}</p>}
      </CardContent>
    </Card>
  );
}

function ActionCard({
  title,
  description,
  icon,
  to,
}: {
  title: string;
  description: string;
  icon: React.ReactNode;
  to: string;
}) {
  return (
    <Card>
      <CardHeader className="flex flex-row items-center justify-between space-y-0 pb-2">
        <CardTitle className="text-sm font-medium">{title}</CardTitle>
        {icon}
      </CardHeader>
      <CardContent>
        <p className="text-xs text-muted-foreground mb-3">{description}</p>
        <Button size="sm" variant="outline" render={<Link to={to} />}>
          Acessar
        </Button>
      </CardContent>
    </Card>
  );
}
