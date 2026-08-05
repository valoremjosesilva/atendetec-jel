import { createBrowserRouter, Navigate, RouterProvider } from 'react-router-dom';
import AppLayout from '@/components/layout/AppLayout';
import PrivateRoute from '@/components/PrivateRoute';
import LandingPage from '@/pages/LandingPage';
import LoginPage from '@/pages/LoginPage';
import RegisterPage from '@/pages/RegisterPage';
import VerifyEmailPage from '@/pages/VerifyEmailPage';
import DashboardPage from '@/pages/DashboardPage';
import WhatsAppPage from '@/pages/WhatsAppPage';
import AIConfigPage from '@/pages/AIConfigPage';
import SchedulingPage from '@/pages/SchedulingPage';
import AppointmentsPage from '@/pages/AppointmentsPage';
import BillingPage from '@/pages/BillingPage';
import ConversationsPage from '@/pages/ConversationsPage';
import ContactsPage from '@/pages/ContactsPage';
import QuickRepliesPage from '@/pages/QuickRepliesPage';
import AdminLayout from '@/components/layout/AdminLayout';
import AdminLoginPage from '@/pages/AdminLoginPage';
import AdminTenantsPage from '@/pages/AdminTenantsPage';
import AdminPlansPage from '@/pages/AdminPlansPage';
import FeatureGuard from '@/components/FeatureGuard';

const router = createBrowserRouter([
  { path: '/login', element: <LoginPage /> },
  { path: '/register', element: <RegisterPage /> },
  { path: '/verify-email', element: <VerifyEmailPage /> },
  { path: '/admin/login', element: <AdminLoginPage /> },
  {
    element: <AdminLayout />,
    children: [
      { path: '/admin/tenants', element: <AdminTenantsPage /> },
      { path: '/admin/plans', element: <AdminPlansPage /> },
    ],
  },
  {
    element: <PrivateRoute />,
    children: [
      {
        element: <AppLayout />,
        children: [
          { path: '/dashboard', element: <DashboardPage /> },
          { path: '/whatsapp', element: <WhatsAppPage /> },
          {
            path: '/ai-config',
            element: (
              <FeatureGuard feature="aiEnabled">
                <AIConfigPage />
              </FeatureGuard>
            ),
          },
          {
            path: '/scheduling',
            element: (
              <FeatureGuard feature="schedulingEnabled">
                <SchedulingPage />
              </FeatureGuard>
            ),
          },
          {
            path: '/appointments',
            element: (
              <FeatureGuard feature="schedulingEnabled">
                <AppointmentsPage />
              </FeatureGuard>
            ),
          },
          { path: '/conversations', element: <ConversationsPage /> },
          { path: '/contacts', element: <ContactsPage /> },
          { path: '/quick-replies', element: <QuickRepliesPage /> },
          { path: '/billing', element: <BillingPage /> },
        ],
      },
    ],
  },
  // Landing pública no apex (atende.mjml.com.br); o painel vive em app.* — o
  // mesmo bundle serve os dois hosts, então a raiz decide pelo hostname.
  // Em dev (localhost) a raiz segue para o painel; use /landing para ver a landing.
  { path: '/', element: isApexHost() ? <LandingPage /> : <Navigate to="/dashboard" replace /> },
  { path: '/landing', element: <LandingPage /> },
  { path: '*', element: <Navigate to="/login" replace /> },
]);

function isApexHost(): boolean {
  const host = window.location.hostname;
  return !host.startsWith('app.') && host !== 'localhost' && host !== '127.0.0.1';
}

export default function App() {
  return <RouterProvider router={router} />;
}
