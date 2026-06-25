import { NavLink } from 'react-router-dom';
import {
  Bot,
  CalendarCheck,
  CalendarClock,
  CreditCard,
  LayoutDashboard,
  LogOut,
  MessageSquare,
  Users,
  Wifi,
  Zap,
} from 'lucide-react';
import { useAuthStore } from '@/stores/authStore';
import { useEntitlements } from '@/hooks/useEntitlements';
import { cn } from '@/lib/utils';

// `feature` opcional: o item só aparece se o plano do tenant permitir.
const navItems = [
  { to: '/dashboard', label: 'Dashboard', icon: LayoutDashboard },
  { to: '/whatsapp', label: 'WhatsApp', icon: Wifi },
  { to: '/ai-config', label: 'IA', icon: Bot, feature: 'aiEnabled' as const },
  { to: '/scheduling', label: 'Agenda', icon: CalendarClock, feature: 'schedulingEnabled' as const },
  { to: '/appointments', label: 'Agendamentos', icon: CalendarCheck, feature: 'schedulingEnabled' as const },
  { to: '/conversations', label: 'Conversas', icon: MessageSquare },
  { to: '/contacts', label: 'Contatos', icon: Users },
  { to: '/quick-replies', label: 'Respostas Rápidas', icon: Zap },
  { to: '/billing', label: 'Billing', icon: CreditCard },
];

export default function Sidebar() {
  const clear = useAuthStore((s) => s.clear);
  const subdomain = useAuthStore((s) => s.subdomain);
  const { data: me } = useEntitlements();

  // Enquanto /me não carrega, não esconde nada (evita "piscar" o menu).
  const items = navItems.filter(
    (item) => !item.feature || !me || me.entitlements[item.feature]
  );

  return (
    <aside className="w-56 flex flex-col h-full border-r bg-card">
      <div className="p-4 border-b">
        <p className="font-bold text-lg">Mensagee</p>
        <p className="text-xs text-muted-foreground truncate">{subdomain}</p>
      </div>
      <nav className="flex-1 p-2 space-y-1">
        {items.map(({ to, label, icon: Icon }) => (
          <NavLink
            key={to}
            to={to}
            className={({ isActive }) =>
              cn(
                'flex items-center gap-3 px-3 py-2 rounded-md text-sm transition-colors',
                isActive
                  ? 'bg-primary text-primary-foreground'
                  : 'hover:bg-accent hover:text-accent-foreground'
              )
            }
          >
            <Icon className="h-4 w-4" />
            {label}
          </NavLink>
        ))}
      </nav>
      {me?.entitlements.aiEnabled && me.entitlements.messagesPerMonth > 0 && (
        <div className="px-3 py-2 border-t">
          <p className="text-[11px] text-muted-foreground">
            Mensagens IA: {me.usage.messagesUsed}/{me.entitlements.messagesPerMonth}
          </p>
          <div className="mt-1 h-1 w-full rounded-full bg-muted overflow-hidden">
            <div
              className={cn(
                'h-full rounded-full',
                me.usage.messagesUsed >= me.entitlements.messagesPerMonth
                  ? 'bg-destructive'
                  : 'bg-primary'
              )}
              style={{
                width: `${Math.min(
                  100,
                  (me.usage.messagesUsed / me.entitlements.messagesPerMonth) * 100
                )}%`,
              }}
            />
          </div>
        </div>
      )}
      <div className="p-2 border-t">
        <button
          onClick={clear}
          className="flex items-center gap-3 px-3 py-2 rounded-md text-sm w-full hover:bg-accent hover:text-accent-foreground transition-colors"
        >
          <LogOut className="h-4 w-4" />
          Sair
        </button>
      </div>
    </aside>
  );
}
