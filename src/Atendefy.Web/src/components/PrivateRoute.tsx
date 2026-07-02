import { Navigate, Outlet } from 'react-router-dom';
import { useAuthStore } from '@/stores/authStore';

export default function PrivateRoute() {
  const authenticated = useAuthStore((s) => s.authenticated);
  return authenticated ? <Outlet /> : <Navigate to="/login" replace />;
}
