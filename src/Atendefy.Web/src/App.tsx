import { createBrowserRouter, Navigate, RouterProvider } from 'react-router-dom';
import AppLayout from '@/components/layout/AppLayout';
import PrivateRoute from '@/components/PrivateRoute';
import LoginPage from '@/pages/LoginPage';
import RegisterPage from '@/pages/RegisterPage';
import DashboardPage from '@/pages/DashboardPage';
import WhatsAppPage from '@/pages/WhatsAppPage';
import AIConfigPage from '@/pages/AIConfigPage';
import BillingPage from '@/pages/BillingPage';
import ConversationsPage from '@/pages/ConversationsPage';
import ContactsPage from '@/pages/ContactsPage';

const router = createBrowserRouter([
  { path: '/login', element: <LoginPage /> },
  { path: '/register', element: <RegisterPage /> },
  {
    element: <PrivateRoute />,
    children: [
      {
        element: <AppLayout />,
        children: [
          { path: '/dashboard', element: <DashboardPage /> },
          { path: '/whatsapp', element: <WhatsAppPage /> },
          { path: '/ai-config', element: <AIConfigPage /> },
          { path: '/conversations', element: <ConversationsPage /> },
          { path: '/contacts', element: <ContactsPage /> },
          { path: '/billing', element: <BillingPage /> },
        ],
      },
    ],
  },
  { path: '/', element: <Navigate to="/dashboard" replace /> },
  { path: '*', element: <Navigate to="/login" replace /> },
]);

export default function App() {
  return <RouterProvider router={router} />;
}
