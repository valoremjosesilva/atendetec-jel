import { NavLink } from 'react-router-dom';
import {
  Bot,
  CreditCard,
  LayoutDashboard,
  LogOut,
  MessageSquare,
  Users,
  Wifi,
  Zap,
} from 'lucide-react';
import { useAuthStore } from '@/stores/authStore';
import { cn } from '@/lib/utils';

const navItems = [
  { to: '/dashboard', label: 'Dashboard', icon: LayoutDashboard },
  { to: '/whatsapp', label: 'WhatsApp', icon: Wifi },
  { to: '/ai-config', label: 'IA', icon: Bot },
  { to: '/conversations', label: 'Conversas', icon: MessageSquare },
  { to: '/contacts', label: 'Contatos', icon: Users },
  { to: '/quick-replies', label: 'Respostas Rápidas', icon: Zap },
  { to: '/billing', label: 'Billing', icon: CreditCard },
];

export default function Sidebar() {
  const clear = useAuthStore((s) => s.clear);
  const subdomain = useAuthStore((s) => s.subdomain);

  return (
    <aside className="w-56 flex flex-col h-full border-r bg-card">
      <div className="p-4 border-b">
        <p className="font-bold text-lg">Atendefy</p>
        <p className="text-xs text-muted-foreground truncate">{subdomain}</p>
      </div>
      <nav className="flex-1 p-2 space-y-1">
        {navItems.map(({ to, label, icon: Icon }) => (
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
