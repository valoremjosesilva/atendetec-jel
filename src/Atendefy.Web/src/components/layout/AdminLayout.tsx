import { NavLink, Navigate, Outlet, useNavigate } from 'react-router-dom';
import { Building2, LayoutGrid, LogOut } from 'lucide-react';
import { useAdminStore } from '@/stores/adminStore';
import { cn } from '@/lib/utils';

const navItems = [
  { to: '/admin/tenants', label: 'Empresas', icon: Building2 },
  { to: '/admin/plans', label: 'Planos', icon: LayoutGrid },
];

export default function AdminLayout() {
  const adminKey = useAdminStore((s) => s.adminKey);
  const clear = useAdminStore((s) => s.clear);
  const navigate = useNavigate();

  if (!adminKey) return <Navigate to="/admin/login" replace />;

  function handleLogout() {
    clear();
    navigate('/admin/login');
  }

  return (
    <div className="flex h-screen">
      <aside className="w-56 flex flex-col h-full border-r bg-card">
        <div className="p-4 border-b">
          <p className="font-bold text-lg">Mensagee</p>
          <p className="text-xs text-muted-foreground">Superadmin</p>
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
            onClick={handleLogout}
            className="flex items-center gap-3 px-3 py-2 rounded-md text-sm w-full hover:bg-accent hover:text-accent-foreground transition-colors"
          >
            <LogOut className="h-4 w-4" />
            Sair
          </button>
        </div>
      </aside>
      <main className="flex-1 overflow-auto p-6">
        <Outlet />
      </main>
    </div>
  );
}
