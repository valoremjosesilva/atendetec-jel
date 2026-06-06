# Plano 4: Frontend React SPA — Atendefy

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Construir um SPA React que consome a API REST Atendefy (Planos 1–3) — login multi-tenant, onboarding, dashboard, WhatsApp accounts, AI config, billing e conversas.

**Architecture:** React 19 + Vite em `src/Atendefy.Web/`. Axios com interceptors Bearer + X-Tenant-Key para autenticação; Zustand com persist middleware para auth state; TanStack Query v5 para server state; React Router v7 com protected routes. Em dev, Vite proxy `/api/*` → `http://localhost:8080/*`. Em prod, nginx multi-stage build serve assets estáticos e faz proxy para o container da API.

**Tech Stack:** React 19, Vite, TypeScript, shadcn/ui, Tailwind CSS, TanStack Query v5, React Router v7, Zustand v5, Axios, Nginx (prod)

---

## API Reference (Backend Planos 1–3)

| Método | Rota | Auth | Notas |
|--------|------|------|-------|
| POST | `/tenants/register` | Público | Body: `{companyName, subdomain, ownerName, ownerEmail, ownerPassword}` → `201 {id, subdomain, name}` |
| POST | `/auth/login` | Público (requer `X-Tenant-Key`) | Body: `{email, password}` → `{accessToken, refreshToken, expiresAt, tenantId, userId, role}` |
| GET | `/whatsapp/accounts` | JWT | Retorna array de contas |
| POST | `/whatsapp/accounts` | JWT | Body: `{provider, phone, configJson}` |
| GET | `/ai/config` | JWT | Retorna `{provider, model, systemPrompt}` ou 404 |
| PUT | `/ai/config` | JWT | Body: `{provider, apiKey, model, systemPrompt}` |
| GET | `/billing/plans` | Público | Retorna array de planos |
| POST | `/billing/subscribe` | JWT | Body: `{planId, provider, billingType, billingCycle, cpfCnpj?, paymentMethodId?}` |
| GET | `/billing/subscription` | JWT | Retorna assinatura atual + plano + última fatura |
| DELETE | `/billing/subscription` | JWT | Cancela assinatura |
| GET | `/health` | Público | `{status: "healthy"}` |

**JWT claims:** `tenant_id` (Guid), `sub` (userId), `role`
**Tenant resolution:** header `X-Tenant-Key: {subdomain}` (ou subdomain no Host)
**CORS:** `http://localhost:5173` permitido em dev

---

## File Map

```
src/Atendefy.Web/
├── public/
├── src/
│   ├── api/
│   │   └── client.ts           # Axios instance — Bearer + X-Tenant-Key interceptors, 401 redirect
│   ├── hooks/
│   │   ├── useWhatsApp.ts      # queries e mutations para /whatsapp
│   │   ├── useAIConfig.ts      # query e mutation para /ai/config
│   │   └── useBilling.ts       # plans, subscription, subscribe, cancel
│   ├── stores/
│   │   └── authStore.ts        # Zustand persist — token, subdomain, userId, role
│   ├── types/
│   │   └── api.ts              # TypeScript interfaces para todas as respostas da API
│   ├── components/
│   │   ├── layout/
│   │   │   ├── AppLayout.tsx   # Sidebar + Outlet
│   │   │   └── Sidebar.tsx     # NavLinks com ícones + logout
│   │   └── PrivateRoute.tsx    # Redirect /login se sem token
│   ├── pages/
│   │   ├── LoginPage.tsx       # Subdomain + email + password
│   │   ├── RegisterPage.tsx    # Criação de tenant + auto-login
│   │   ├── DashboardPage.tsx   # Cards de métricas + health check
│   │   ├── WhatsAppPage.tsx    # Lista + dialog de criação de conta
│   │   ├── AIConfigPage.tsx    # Provider + API key + modelo + system prompt
│   │   ├── BillingPage.tsx     # Planos + subscribe modal + status da assinatura
│   │   └── ConversationsPage.tsx  # Placeholder
│   ├── App.tsx                  # createBrowserRouter com todas as rotas
│   ├── main.tsx                 # QueryClientProvider + RouterProvider
│   └── index.css                # Tailwind / shadcn directives
├── nginx.conf                   # Prod: SPA fallback + proxy /api
├── Dockerfile                   # Multi-stage: node build → nginx serve
├── .env.example
└── vite.config.ts               # @tailwindcss/vite + proxy /api + alias @
```

---

## Task 1: Project Scaffold

**Files:**
- Create: `src/Atendefy.Web/` (projeto completo)
- Create: `src/Atendefy.Web/vite.config.ts`
- Create: `src/Atendefy.Web/.env.example`

- [ ] **Step 1: Scaffold the project**

Execute a partir de `C:\Projetos\JEL\JEL\Atendefy\src\`:

```powershell
cd C:\Projetos\JEL\JEL\Atendefy\src
npm create vite@latest Atendefy.Web -- --template react-ts
cd Atendefy.Web
npm install
```

- [ ] **Step 2: Install runtime dependencies**

```powershell
npm install react-router-dom @tanstack/react-query axios zustand
npm install -D @types/node
```

- [ ] **Step 3: Init shadcn/ui (configures Tailwind + aliases automatically)**

```powershell
npx shadcn@latest init
```

Quando solicitado, responda:
- Style: **Default**
- Base color: **Neutral**
- Use CSS variables: **yes**

O comando instala Tailwind, configura `components.json`, cria `src/lib/utils.ts` e atualiza `src/index.css` e `vite.config.ts`.

- [ ] **Step 4: Install shadcn components**

```powershell
npx shadcn@latest add button card input label select textarea dialog badge tabs separator avatar form
```

- [ ] **Step 5: Write `vite.config.ts`**

Abra `vite.config.ts` e adicione o bloco `resolve.alias` e `server.proxy` — **sem remover** o que o shadcn já escreveu (especialmente o plugin `tailwindcss()`). O resultado final deve ser:

```typescript
import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import tailwindcss from '@tailwindcss/vite';
import path from 'path';

export default defineConfig({
  plugins: [react(), tailwindcss()],
  resolve: {
    alias: {
      '@': path.resolve(__dirname, './src'),
    },
  },
  server: {
    proxy: {
      '/api': {
        target: 'http://localhost:8080',
        changeOrigin: true,
        rewrite: (p) => p.replace(/^\/api/, ''),
      },
    },
  },
});
```

Se o shadcn já adicionou `tailwindcss()` aos plugins, não duplique. Se usou `@tailwindcss/vite`, mantenha a mesma importação. Apenas garanta que `resolve.alias` e `server.proxy` estejam presentes.

- [ ] **Step 6: Write `.env.example`**

```
# Variáveis devem ter prefixo VITE_ para serem acessíveis no browser
VITE_APP_TITLE=Atendefy
```

- [ ] **Step 7: Clean up boilerplate**

- Delete `src/App.css`
- Delete `src/assets/react.svg` (se existir)
- Não altere `src/index.css` — o shadcn já escreveu as diretivas corretas

- [ ] **Step 8: Verify dev server starts**

```powershell
npm run dev
```

Esperado: Vite inicia em `http://localhost:5173` sem erros de compilação. A página pode mostrar o boilerplate do Vite — isso é esperado agora.

- [ ] **Step 9: Commit**

```powershell
git -C C:\Projetos\JEL\JEL\Atendefy add src/Atendefy.Web
git -C C:\Projetos\JEL\JEL\Atendefy commit -m "feat: scaffold Atendefy.Web — React 19 + Vite + shadcn/ui"
```

---

## Task 2: TypeScript Types + API Client + Auth Store

**Files:**
- Create: `src/Atendefy.Web/src/types/api.ts`
- Create: `src/Atendefy.Web/src/api/client.ts`
- Create: `src/Atendefy.Web/src/stores/authStore.ts`

- [ ] **Step 1: Write `src/types/api.ts`**

```typescript
// Auth
export interface LoginRequest {
  email: string;
  password: string;
}

export interface AuthResponse {
  accessToken: string;
  refreshToken: string;
  expiresAt: string;
  tenantId: string;
  userId: string;
  role: string;
}

export interface RegisterRequest {
  companyName: string;
  subdomain: string;
  ownerName: string;
  ownerEmail: string;
  ownerPassword: string;
}

export interface RegisterResponse {
  id: string;
  subdomain: string;
  name: string;
}

// WhatsApp
export interface WhatsAppAccount {
  id: string;
  provider: string;
  phone: string;
  status: string;
  createdAt: string;
}

export interface CreateWhatsAppAccountRequest {
  provider: string;
  phone: string;
  configJson: string;
}

// AI
export interface AIConfigResponse {
  provider: string;
  model: string;
  systemPrompt: string;
}

export interface AIConfigRequest {
  provider: string;
  apiKey: string;
  model: string;
  systemPrompt: string;
}

// Billing
export interface Plan {
  id: string;
  name: string;
  priceMonthly: number;
  priceYearly: number;
  limitsJson: string;
}

export interface PlanLimits {
  messages_per_month: number;
  whatsapp_accounts: number;
  team_members: number;
}

export interface CreateSubscriptionRequest {
  planId: string;
  provider: string;       // "asaas" | "stripe"
  billingType: string;    // "BOLETO" | "PIX" | "CREDIT_CARD"
  billingCycle: string;   // "monthly" | "yearly"
  cpfCnpj?: string;
  paymentMethodId?: string;
}

export interface InvoiceResult {
  id: string;
  status: string;
  boletoUrl?: string;
  boletoBarcode?: string;
  pixCopyPaste?: string;
  clientSecret?: string;
  dueDate: string;
}

export interface SubscriptionResponse {
  id: string;
  status: string;          // pending | active | past_due | suspended | cancelled
  billingCycle: string;    // monthly | yearly
  provider: string;        // asaas | stripe
  currentPeriodStart?: string;
  currentPeriodEnd?: string;
  plan?: { id: string; name: string };
  lastInvoice?: {
    id: string;
    status: string;
    amount: number;
    dueDate: string;
    paidAt?: string;
  };
}
```

- [ ] **Step 2: Write `src/stores/authStore.ts`**

```typescript
import { create } from 'zustand';
import { persist } from 'zustand/middleware';

interface AuthState {
  accessToken: string | null;
  refreshToken: string | null;
  tenantId: string | null;
  userId: string | null;
  role: string | null;
  subdomain: string | null;
  setAuth: (data: {
    accessToken: string;
    refreshToken: string;
    tenantId: string;
    userId: string;
    role: string;
    subdomain: string;
  }) => void;
  clear: () => void;
}

export const useAuthStore = create<AuthState>()(
  persist(
    (set) => ({
      accessToken: null,
      refreshToken: null,
      tenantId: null,
      userId: null,
      role: null,
      subdomain: null,
      setAuth: (data) => set(data),
      clear: () =>
        set({
          accessToken: null,
          refreshToken: null,
          tenantId: null,
          userId: null,
          role: null,
          subdomain: null,
        }),
    }),
    { name: 'atendefy-auth' }
  )
);
```

- [ ] **Step 3: Write `src/api/client.ts`**

```typescript
import axios from 'axios';
import { useAuthStore } from '@/stores/authStore';

export const apiClient = axios.create({
  baseURL: '/api',
});

apiClient.interceptors.request.use((config) => {
  const { accessToken, subdomain } = useAuthStore.getState();
  if (accessToken) {
    config.headers.Authorization = `Bearer ${accessToken}`;
  }
  if (subdomain) {
    config.headers['X-Tenant-Key'] = subdomain;
  }
  return config;
});

apiClient.interceptors.response.use(
  (res) => res,
  (error) => {
    if (error.response?.status === 401) {
      useAuthStore.getState().clear();
      window.location.href = '/login';
    }
    return Promise.reject(error);
  }
);
```

Nota: o interceptor de request usa `useAuthStore.getState()` (não o hook React) porque interceptors rodam fora de componentes. Isso é correto e seguro com Zustand.

- [ ] **Step 4: Verify TypeScript**

```powershell
npx tsc --noEmit
```

Esperado: 0 erros (os arquivos novos ainda não importam nada que não existe).

- [ ] **Step 5: Commit**

```powershell
git -C C:\Projetos\JEL\JEL\Atendefy add src/Atendefy.Web/src/types src/Atendefy.Web/src/api src/Atendefy.Web/src/stores
git -C C:\Projetos\JEL\JEL\Atendefy commit -m "feat: add API types, axios client with interceptors and Zustand auth store"
```

---

## Task 3: Auth Pages + Route Guard

**Files:**
- Create: `src/Atendefy.Web/src/pages/LoginPage.tsx`
- Create: `src/Atendefy.Web/src/pages/RegisterPage.tsx`
- Create: `src/Atendefy.Web/src/components/PrivateRoute.tsx`

- [ ] **Step 1: Write `src/pages/LoginPage.tsx`**

O login precisa do subdomain para o header `X-Tenant-Key` — esse campo não existe no auth store ainda (a store está vazia no primeiro acesso), então passamos o header manualmente na requisição.

```tsx
import { useState } from 'react';
import { useNavigate, Link } from 'react-router-dom';
import { apiClient } from '@/api/client';
import { useAuthStore } from '@/stores/authStore';
import type { AuthResponse } from '@/types/api';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';

export default function LoginPage() {
  const navigate = useNavigate();
  const setAuth = useAuthStore((s) => s.setAuth);
  const [subdomain, setSubdomain] = useState('');
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(false);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError('');
    setLoading(true);
    try {
      const { data } = await apiClient.post<AuthResponse>(
        '/auth/login',
        { email, password },
        { headers: { 'X-Tenant-Key': subdomain } }
      );
      setAuth({ ...data, subdomain });
      navigate('/dashboard');
    } catch {
      setError('Credenciais inválidas ou empresa não encontrada.');
    } finally {
      setLoading(false);
    }
  }

  return (
    <div className="min-h-screen flex items-center justify-center bg-background p-4">
      <Card className="w-full max-w-sm">
        <CardHeader>
          <CardTitle className="text-2xl text-center">Atendefy</CardTitle>
        </CardHeader>
        <CardContent>
          <form onSubmit={handleSubmit} className="space-y-4">
            <div className="space-y-1">
              <Label htmlFor="subdomain">Empresa (subdomínio)</Label>
              <Input
                id="subdomain"
                placeholder="minha-empresa"
                value={subdomain}
                onChange={(e) => setSubdomain(e.target.value)}
                required
              />
            </div>
            <div className="space-y-1">
              <Label htmlFor="email">E-mail</Label>
              <Input
                id="email"
                type="email"
                value={email}
                onChange={(e) => setEmail(e.target.value)}
                required
              />
            </div>
            <div className="space-y-1">
              <Label htmlFor="password">Senha</Label>
              <Input
                id="password"
                type="password"
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                required
              />
            </div>
            {error && <p className="text-sm text-destructive">{error}</p>}
            <Button type="submit" className="w-full" disabled={loading}>
              {loading ? 'Entrando…' : 'Entrar'}
            </Button>
            <p className="text-sm text-center text-muted-foreground">
              Sem conta?{' '}
              <Link to="/register" className="underline">
                Criar empresa
              </Link>
            </p>
          </form>
        </CardContent>
      </Card>
    </div>
  );
}
```

- [ ] **Step 2: Write `src/pages/RegisterPage.tsx`**

Após criar o tenant, faz auto-login e navega para /dashboard.

```tsx
import { useState } from 'react';
import { useNavigate, Link } from 'react-router-dom';
import { apiClient } from '@/api/client';
import { useAuthStore } from '@/stores/authStore';
import type { AuthResponse, RegisterRequest } from '@/types/api';
import { Button } from '@/components/ui/button';
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from '@/components/ui/card';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';

export default function RegisterPage() {
  const navigate = useNavigate();
  const setAuth = useAuthStore((s) => s.setAuth);
  const [form, setForm] = useState<RegisterRequest>({
    companyName: '',
    subdomain: '',
    ownerName: '',
    ownerEmail: '',
    ownerPassword: '',
  });
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(false);

  function update(field: keyof RegisterRequest, value: string) {
    setForm((prev) => ({ ...prev, [field]: value }));
  }

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError('');
    setLoading(true);
    try {
      await apiClient.post('/tenants/register', form);
      const { data } = await apiClient.post<AuthResponse>(
        '/auth/login',
        { email: form.ownerEmail, password: form.ownerPassword },
        { headers: { 'X-Tenant-Key': form.subdomain } }
      );
      setAuth({ ...data, subdomain: form.subdomain });
      navigate('/dashboard');
    } catch (err: unknown) {
      const msg =
        (err as { response?: { data?: { error?: string } } })?.response?.data?.error ??
        'Erro ao criar empresa.';
      setError(msg);
    } finally {
      setLoading(false);
    }
  }

  return (
    <div className="min-h-screen flex items-center justify-center bg-background p-4">
      <Card className="w-full max-w-md">
        <CardHeader>
          <CardTitle>Criar conta</CardTitle>
          <CardDescription>Comece o seu teste grátis agora.</CardDescription>
        </CardHeader>
        <CardContent>
          <form onSubmit={handleSubmit} className="space-y-4">
            <div className="space-y-1">
              <Label htmlFor="companyName">Nome da empresa</Label>
              <Input
                id="companyName"
                value={form.companyName}
                onChange={(e) => update('companyName', e.target.value)}
                required
              />
            </div>
            <div className="space-y-1">
              <Label htmlFor="subdomain">Subdomínio</Label>
              <Input
                id="subdomain"
                placeholder="minha-empresa"
                value={form.subdomain}
                onChange={(e) =>
                  update('subdomain', e.target.value.toLowerCase().replace(/[^a-z0-9-]/g, ''))
                }
                required
              />
              <p className="text-xs text-muted-foreground">
                Seu acesso será em{' '}
                <strong>{form.subdomain || '<subdomínio>'}.atendefy.com.br</strong>
              </p>
            </div>
            <div className="space-y-1">
              <Label htmlFor="ownerName">Seu nome</Label>
              <Input
                id="ownerName"
                value={form.ownerName}
                onChange={(e) => update('ownerName', e.target.value)}
                required
              />
            </div>
            <div className="space-y-1">
              <Label htmlFor="ownerEmail">E-mail</Label>
              <Input
                id="ownerEmail"
                type="email"
                value={form.ownerEmail}
                onChange={(e) => update('ownerEmail', e.target.value)}
                required
              />
            </div>
            <div className="space-y-1">
              <Label htmlFor="ownerPassword">Senha</Label>
              <Input
                id="ownerPassword"
                type="password"
                value={form.ownerPassword}
                onChange={(e) => update('ownerPassword', e.target.value)}
                required
                minLength={8}
              />
            </div>
            {error && <p className="text-sm text-destructive">{error}</p>}
            <Button type="submit" className="w-full" disabled={loading}>
              {loading ? 'Criando conta…' : 'Criar conta'}
            </Button>
            <p className="text-sm text-center text-muted-foreground">
              Já tem conta?{' '}
              <Link to="/login" className="underline">
                Entrar
              </Link>
            </p>
          </form>
        </CardContent>
      </Card>
    </div>
  );
}
```

- [ ] **Step 3: Write `src/components/PrivateRoute.tsx`**

```tsx
import { Navigate, Outlet } from 'react-router-dom';
import { useAuthStore } from '@/stores/authStore';

export default function PrivateRoute() {
  const accessToken = useAuthStore((s) => s.accessToken);
  return accessToken ? <Outlet /> : <Navigate to="/login" replace />;
}
```

- [ ] **Step 4: Commit**

```powershell
git -C C:\Projetos\JEL\JEL\Atendefy add src/Atendefy.Web/src/pages/LoginPage.tsx src/Atendefy.Web/src/pages/RegisterPage.tsx src/Atendefy.Web/src/components/PrivateRoute.tsx
git -C C:\Projetos\JEL\JEL\Atendefy commit -m "feat: add Login, Register pages and PrivateRoute guard"
```

---

## Task 4: App Layout + Routing

**Files:**
- Create: `src/Atendefy.Web/src/components/layout/Sidebar.tsx`
- Create: `src/Atendefy.Web/src/components/layout/AppLayout.tsx`
- Modify: `src/Atendefy.Web/src/App.tsx`
- Modify: `src/Atendefy.Web/src/main.tsx`

`lucide-react` é instalado como dependência transitiva do shadcn/ui — já está disponível.

- [ ] **Step 1: Write `src/components/layout/Sidebar.tsx`**

```tsx
import { NavLink } from 'react-router-dom';
import {
  LayoutDashboard,
  MessageSquare,
  Bot,
  CreditCard,
  Wifi,
  LogOut,
} from 'lucide-react';
import { useAuthStore } from '@/stores/authStore';
import { cn } from '@/lib/utils';

const navItems = [
  { to: '/dashboard', label: 'Dashboard', icon: LayoutDashboard },
  { to: '/whatsapp', label: 'WhatsApp', icon: Wifi },
  { to: '/ai-config', label: 'IA', icon: Bot },
  { to: '/conversations', label: 'Conversas', icon: MessageSquare },
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
```

- [ ] **Step 2: Write `src/components/layout/AppLayout.tsx`**

```tsx
import { Outlet } from 'react-router-dom';
import Sidebar from './Sidebar';

export default function AppLayout() {
  return (
    <div className="flex h-screen bg-background">
      <Sidebar />
      <main className="flex-1 overflow-y-auto p-6">
        <Outlet />
      </main>
    </div>
  );
}
```

- [ ] **Step 3: Create page stubs** (as páginas completas vêm nas Tasks 5–9; stubs permitem que o router compile agora)

Crie cada arquivo com o conteúdo abaixo:

`src/pages/DashboardPage.tsx`:
```tsx
export default function DashboardPage() {
  return <h1 className="text-2xl font-bold">Dashboard</h1>;
}
```

`src/pages/WhatsAppPage.tsx`:
```tsx
export default function WhatsAppPage() {
  return <h1 className="text-2xl font-bold">WhatsApp</h1>;
}
```

`src/pages/AIConfigPage.tsx`:
```tsx
export default function AIConfigPage() {
  return <h1 className="text-2xl font-bold">IA</h1>;
}
```

`src/pages/BillingPage.tsx`:
```tsx
export default function BillingPage() {
  return <h1 className="text-2xl font-bold">Billing</h1>;
}
```

`src/pages/ConversationsPage.tsx`:
```tsx
export default function ConversationsPage() {
  return <h1 className="text-2xl font-bold">Conversas</h1>;
}
```

- [ ] **Step 4: Write `src/App.tsx`**

```tsx
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
```

- [ ] **Step 5: Write `src/main.tsx`**

```tsx
import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import './index.css';
import App from './App.tsx';

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      retry: 1,
      staleTime: 30_000,
    },
  },
});

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <App />
    </QueryClientProvider>
  </StrictMode>
);
```

- [ ] **Step 6: Verify dev server**

```powershell
npm run dev
```

Acesse `http://localhost:5173`. Esperado: redirect para `/login`. A página de login deve renderizar com os campos de subdomínio, e-mail e senha. Sem erros no console do browser.

- [ ] **Step 7: Commit**

```powershell
git -C C:\Projetos\JEL\JEL\Atendefy add src/Atendefy.Web/src/
git -C C:\Projetos\JEL\JEL\Atendefy commit -m "feat: add AppLayout, Sidebar, routing and page stubs"
```

---

## Task 5: Dashboard Page

**Files:**
- Modify: `src/Atendefy.Web/src/pages/DashboardPage.tsx`

- [ ] **Step 1: Write `src/pages/DashboardPage.tsx`**

```tsx
import { useQuery } from '@tanstack/react-query';
import { Link } from 'react-router-dom';
import { apiClient } from '@/api/client';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { Bot, CreditCard, MessageSquare, Wifi } from 'lucide-react';

function useApiHealth() {
  return useQuery({
    queryKey: ['health'],
    queryFn: () =>
      apiClient.get<{ status: string }>('/health').then((r) => r.data),
  });
}

export default function DashboardPage() {
  const { data: health, isError } = useApiHealth();

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-bold">Dashboard</h1>
        <Badge variant={isError ? 'destructive' : 'default'}>
          API {isError ? 'offline' : (health?.status ?? '…')}
        </Badge>
      </div>

      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
        <MetricCard
          title="WhatsApp"
          description="Conecte contas e receba mensagens"
          icon={<Wifi className="h-5 w-5" />}
          to="/whatsapp"
          linkLabel="Gerenciar"
        />
        <MetricCard
          title="IA"
          description="Configure provedor e system prompt"
          icon={<Bot className="h-5 w-5" />}
          to="/ai-config"
          linkLabel="Configurar"
        />
        <MetricCard
          title="Conversas"
          description="Histórico de atendimentos"
          icon={<MessageSquare className="h-5 w-5" />}
          to="/conversations"
          linkLabel="Ver"
        />
        <MetricCard
          title="Billing"
          description="Planos e assinaturas"
          icon={<CreditCard className="h-5 w-5" />}
          to="/billing"
          linkLabel="Gerenciar"
        />
      </div>
    </div>
  );
}

function MetricCard({
  title,
  description,
  icon,
  to,
  linkLabel,
}: {
  title: string;
  description: string;
  icon: React.ReactNode;
  to: string;
  linkLabel: string;
}) {
  return (
    <Card>
      <CardHeader className="flex flex-row items-center justify-between space-y-0 pb-2">
        <CardTitle className="text-sm font-medium">{title}</CardTitle>
        {icon}
      </CardHeader>
      <CardContent>
        <p className="text-xs text-muted-foreground mb-3">{description}</p>
        <Button asChild size="sm" variant="outline">
          <Link to={to}>{linkLabel}</Link>
        </Button>
      </CardContent>
    </Card>
  );
}
```

- [ ] **Step 2: Verify manually**

Com o Docker stack rodando (`docker compose -f infra/docker-compose.yml -f infra/docker-compose.override.yml up -d` na raiz do projeto), faça login e navegue para `/dashboard`.

Esperado:
- 4 cards com links corretos
- Badge "API healthy" no canto superior direito

- [ ] **Step 3: Commit**

```powershell
git -C C:\Projetos\JEL\JEL\Atendefy add src/Atendefy.Web/src/pages/DashboardPage.tsx
git -C C:\Projetos\JEL\JEL\Atendefy commit -m "feat: add Dashboard page with API health check and quick-links"
```

---

## Task 6: WhatsApp Accounts Page

**Files:**
- Create: `src/Atendefy.Web/src/hooks/useWhatsApp.ts`
- Modify: `src/Atendefy.Web/src/pages/WhatsAppPage.tsx`

- [ ] **Step 1: Write `src/hooks/useWhatsApp.ts`**

```typescript
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiClient } from '@/api/client';
import type { CreateWhatsAppAccountRequest, WhatsAppAccount } from '@/types/api';

export function useWhatsAppAccounts() {
  return useQuery({
    queryKey: ['whatsapp-accounts'],
    queryFn: () =>
      apiClient.get<WhatsAppAccount[]>('/whatsapp/accounts').then((r) => r.data),
  });
}

export function useCreateWhatsAppAccount() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (req: CreateWhatsAppAccountRequest) =>
      apiClient.post<WhatsAppAccount>('/whatsapp/accounts', req).then((r) => r.data),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['whatsapp-accounts'] }),
  });
}
```

- [ ] **Step 2: Write `src/pages/WhatsAppPage.tsx`**

```tsx
import { useState } from 'react';
import { useCreateWhatsAppAccount, useWhatsAppAccounts } from '@/hooks/useWhatsApp';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from '@/components/ui/dialog';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { Textarea } from '@/components/ui/textarea';
import { Plus } from 'lucide-react';

type Provider = 'meta' | 'evolution';

const CONFIG_PLACEHOLDER: Record<Provider, string> = {
  meta: JSON.stringify({ phoneNumberId: '1234567890', accessToken: 'EAAxxxxxxx' }, null, 2),
  evolution: JSON.stringify(
    { baseUrl: 'http://evolution-api:8080', instance: 'my-instance', apiKey: 'your-api-key' },
    null,
    2
  ),
};

export default function WhatsAppPage() {
  const { data: accounts, isLoading } = useWhatsAppAccounts();
  const createAccount = useCreateWhatsAppAccount();

  const [open, setOpen] = useState(false);
  const [provider, setProvider] = useState<Provider>('meta');
  const [phone, setPhone] = useState('');
  const [configJson, setConfigJson] = useState(CONFIG_PLACEHOLDER.meta);
  const [error, setError] = useState('');

  function handleProviderChange(v: string) {
    const p = v as Provider;
    setProvider(p);
    setConfigJson(CONFIG_PLACEHOLDER[p]);
  }

  async function handleCreate() {
    setError('');
    try {
      JSON.parse(configJson);
    } catch {
      setError('configJson inválido — verifique o JSON.');
      return;
    }
    try {
      await createAccount.mutateAsync({ provider, phone, configJson });
      setOpen(false);
      setPhone('');
      setConfigJson(CONFIG_PLACEHOLDER[provider]);
    } catch (err: unknown) {
      const msg =
        (err as { response?: { data?: { error?: string } } })?.response?.data?.error ??
        'Erro ao criar conta.';
      setError(msg);
    }
  }

  function statusVariant(status: string): 'default' | 'secondary' | 'outline' {
    if (status === 'connected') return 'default';
    if (status === 'disconnected') return 'secondary';
    return 'outline';
  }

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-bold">Contas WhatsApp</h1>
        <Dialog open={open} onOpenChange={setOpen}>
          <DialogTrigger asChild>
            <Button>
              <Plus className="h-4 w-4 mr-2" />
              Nova conta
            </Button>
          </DialogTrigger>
          <DialogContent className="max-w-lg">
            <DialogHeader>
              <DialogTitle>Conectar conta WhatsApp</DialogTitle>
            </DialogHeader>
            <div className="space-y-4">
              <div className="space-y-1">
                <Label>Provedor</Label>
                <Select value={provider} onValueChange={handleProviderChange}>
                  <SelectTrigger>
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value="meta">Meta (WhatsApp Cloud API)</SelectItem>
                    <SelectItem value="evolution">Evolution API</SelectItem>
                  </SelectContent>
                </Select>
              </div>
              <div className="space-y-1">
                <Label htmlFor="phone">Número (com DDI, ex: +5511999999999)</Label>
                <Input
                  id="phone"
                  value={phone}
                  onChange={(e) => setPhone(e.target.value)}
                  placeholder="+5511999999999"
                />
              </div>
              <div className="space-y-1">
                <Label htmlFor="configJson">Configuração (JSON)</Label>
                <Textarea
                  id="configJson"
                  className="font-mono text-xs"
                  rows={6}
                  value={configJson}
                  onChange={(e) => setConfigJson(e.target.value)}
                />
              </div>
              {error && <p className="text-sm text-destructive">{error}</p>}
              <Button
                className="w-full"
                onClick={handleCreate}
                disabled={createAccount.isPending}
              >
                {createAccount.isPending ? 'Salvando…' : 'Salvar'}
              </Button>
            </div>
          </DialogContent>
        </Dialog>
      </div>

      {isLoading && <p className="text-muted-foreground">Carregando…</p>}

      {!isLoading && accounts?.length === 0 && (
        <p className="text-muted-foreground">Nenhuma conta conectada ainda.</p>
      )}

      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
        {accounts?.map((acc) => (
          <Card key={acc.id}>
            <CardHeader className="flex flex-row items-center justify-between space-y-0 pb-2">
              <CardTitle className="text-sm font-medium capitalize">{acc.provider}</CardTitle>
              <Badge variant={statusVariant(acc.status)}>{acc.status}</Badge>
            </CardHeader>
            <CardContent>
              <p className="text-sm">{acc.phone}</p>
              <p className="text-xs text-muted-foreground mt-1">
                {new Date(acc.createdAt).toLocaleDateString('pt-BR')}
              </p>
            </CardContent>
          </Card>
        ))}
      </div>
    </div>
  );
}
```

- [ ] **Step 3: Verify manually**

Com a API rodando e logado, navegue para `/whatsapp`.

Esperado:
- Mensagem "Nenhuma conta" quando não há contas
- Clicar em "Nova conta" abre o dialog
- Preencher o form e salvar cria um card novo na lista (invalidação de cache automática via `onSuccess`)

- [ ] **Step 4: Commit**

```powershell
git -C C:\Projetos\JEL\JEL\Atendefy add src/Atendefy.Web/src/hooks/useWhatsApp.ts src/Atendefy.Web/src/pages/WhatsAppPage.tsx
git -C C:\Projetos\JEL\JEL\Atendefy commit -m "feat: add WhatsApp accounts page — list and create dialog"
```

---

## Task 7: AI Config Page

**Files:**
- Create: `src/Atendefy.Web/src/hooks/useAIConfig.ts`
- Modify: `src/Atendefy.Web/src/pages/AIConfigPage.tsx`

- [ ] **Step 1: Write `src/hooks/useAIConfig.ts`**

```typescript
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiClient } from '@/api/client';
import type { AIConfigRequest, AIConfigResponse } from '@/types/api';

export function useAIConfig() {
  return useQuery({
    queryKey: ['ai-config'],
    queryFn: () =>
      apiClient
        .get<AIConfigResponse>('/ai/config')
        .then((r) => r.data)
        .catch((err: { response?: { status?: number } }) => {
          if (err?.response?.status === 404) return null;
          throw err;
        }),
  });
}

export function useSaveAIConfig() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (req: AIConfigRequest) =>
      apiClient.put<AIConfigResponse>('/ai/config', req).then((r) => r.data),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['ai-config'] }),
  });
}
```

- [ ] **Step 2: Write `src/pages/AIConfigPage.tsx`**

```tsx
import { useEffect, useState } from 'react';
import { useAIConfig, useSaveAIConfig } from '@/hooks/useAIConfig';
import { Button } from '@/components/ui/button';
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from '@/components/ui/card';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { Textarea } from '@/components/ui/textarea';

const MODELS: Record<string, string[]> = {
  openai: ['gpt-4o', 'gpt-4o-mini', 'gpt-4-turbo'],
  anthropic: ['claude-opus-4-8', 'claude-sonnet-4-6', 'claude-haiku-4-5-20251001'],
};

export default function AIConfigPage() {
  const { data: config, isLoading } = useAIConfig();
  const saveConfig = useSaveAIConfig();

  const [provider, setProvider] = useState('openai');
  const [apiKey, setApiKey] = useState('');
  const [model, setModel] = useState(MODELS.openai[0]);
  const [systemPrompt, setSystemPrompt] = useState('');
  const [success, setSuccess] = useState(false);
  const [error, setError] = useState('');

  useEffect(() => {
    if (config) {
      setProvider(config.provider);
      setModel(config.model);
      setSystemPrompt(config.systemPrompt ?? '');
    }
  }, [config]);

  function handleProviderChange(v: string) {
    setProvider(v);
    setModel(MODELS[v]?.[0] ?? '');
  }

  async function handleSave(e: React.FormEvent) {
    e.preventDefault();
    setError('');
    setSuccess(false);
    try {
      await saveConfig.mutateAsync({ provider, apiKey, model, systemPrompt });
      setSuccess(true);
      setApiKey('');
    } catch (err: unknown) {
      const msg =
        (err as { response?: { data?: { error?: string } } })?.response?.data?.error ??
        'Erro ao salvar configuração.';
      setError(msg);
    }
  }

  if (isLoading) return <p className="text-muted-foreground">Carregando…</p>;

  return (
    <div className="space-y-6 max-w-2xl">
      <h1 className="text-2xl font-bold">Configuração de IA</h1>
      <Card>
        <CardHeader>
          <CardTitle>Provedor de IA</CardTitle>
          <CardDescription>
            A API key é criptografada antes de ser armazenada. Deixe em branco para manter a chave
            atual.
          </CardDescription>
        </CardHeader>
        <CardContent>
          <form onSubmit={handleSave} className="space-y-4">
            <div className="space-y-1">
              <Label>Provedor</Label>
              <Select value={provider} onValueChange={handleProviderChange}>
                <SelectTrigger>
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="openai">OpenAI</SelectItem>
                  <SelectItem value="anthropic">Anthropic</SelectItem>
                </SelectContent>
              </Select>
            </div>

            <div className="space-y-1">
              <Label htmlFor="apiKey">
                API Key{config ? ' (deixe em branco para manter a atual)' : ''}
              </Label>
              <Input
                id="apiKey"
                type="password"
                placeholder={config ? '••••••••' : 'sk-…'}
                value={apiKey}
                onChange={(e) => setApiKey(e.target.value)}
                required={!config}
              />
            </div>

            <div className="space-y-1">
              <Label>Modelo</Label>
              <Select value={model} onValueChange={setModel}>
                <SelectTrigger>
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {(MODELS[provider] ?? []).map((m) => (
                    <SelectItem key={m} value={m}>
                      {m}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>

            <div className="space-y-1">
              <Label htmlFor="systemPrompt">Prompt do sistema</Label>
              <Textarea
                id="systemPrompt"
                rows={5}
                placeholder="Você é um assistente de atendimento ao cliente da [empresa]…"
                value={systemPrompt}
                onChange={(e) => setSystemPrompt(e.target.value)}
              />
            </div>

            {success && (
              <p className="text-sm text-green-600">Configuração salva com sucesso.</p>
            )}
            {error && <p className="text-sm text-destructive">{error}</p>}

            <Button type="submit" disabled={saveConfig.isPending}>
              {saveConfig.isPending ? 'Salvando…' : 'Salvar'}
            </Button>
          </form>
        </CardContent>
      </Card>
    </div>
  );
}
```

- [ ] **Step 3: Verify manually**

Navegue para `/ai-config`.

Esperado:
- Form vazio se não há config; preenchido com provider + modelo + system prompt se já existe
- Mudar provedor atualiza a lista de modelos no select
- Salvar exibe "Configuração salva com sucesso" e limpa o campo de API key

- [ ] **Step 4: Commit**

```powershell
git -C C:\Projetos\JEL\JEL\Atendefy add src/Atendefy.Web/src/hooks/useAIConfig.ts src/Atendefy.Web/src/pages/AIConfigPage.tsx
git -C C:\Projetos\JEL\JEL\Atendefy commit -m "feat: add AI Config page — provider, model, API key, system prompt"
```

---

## Task 8: Billing Page

**Files:**
- Create: `src/Atendefy.Web/src/hooks/useBilling.ts`
- Modify: `src/Atendefy.Web/src/pages/BillingPage.tsx`

- [ ] **Step 1: Write `src/hooks/useBilling.ts`**

```typescript
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiClient } from '@/api/client';
import type {
  CreateSubscriptionRequest,
  InvoiceResult,
  Plan,
  SubscriptionResponse,
} from '@/types/api';

export function usePlans() {
  return useQuery({
    queryKey: ['billing-plans'],
    queryFn: () => apiClient.get<Plan[]>('/billing/plans').then((r) => r.data),
  });
}

export function useSubscription() {
  return useQuery({
    queryKey: ['billing-subscription'],
    queryFn: () =>
      apiClient
        .get<SubscriptionResponse>('/billing/subscription')
        .then((r) => r.data)
        .catch((err: { response?: { status?: number } }) => {
          if (err?.response?.status === 404) return null;
          throw err;
        }),
  });
}

export function useSubscribe() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (req: CreateSubscriptionRequest) =>
      apiClient.post<InvoiceResult>('/billing/subscribe', req).then((r) => r.data),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['billing-subscription'] }),
  });
}

export function useCancelSubscription() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: () => apiClient.delete('/billing/subscription').then((r) => r.data),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['billing-subscription'] }),
  });
}
```

- [ ] **Step 2: Write `src/pages/BillingPage.tsx`**

```tsx
import { useState } from 'react';
import {
  useCancelSubscription,
  usePlans,
  useSubscribe,
  useSubscription,
} from '@/hooks/useBilling';
import type {
  CreateSubscriptionRequest,
  InvoiceResult,
  Plan,
  PlanLimits,
} from '@/types/api';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from '@/components/ui/card';
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs';

type BillingCycle = 'monthly' | 'yearly';

function parseLimits(json: string): PlanLimits {
  try {
    return JSON.parse(json) as PlanLimits;
  } catch {
    return { messages_per_month: 0, whatsapp_accounts: 0, team_members: 0 };
  }
}

function subStatusVariant(status: string): 'default' | 'destructive' | 'secondary' {
  if (status === 'active') return 'default';
  if (status === 'past_due' || status === 'suspended') return 'destructive';
  return 'secondary';
}

export default function BillingPage() {
  const { data: plans, isLoading: loadingPlans } = usePlans();
  const { data: subscription, isLoading: loadingSub } = useSubscription();
  const subscribe = useSubscribe();
  const cancelSub = useCancelSubscription();

  const [cycle, setCycle] = useState<BillingCycle>('monthly');
  const [selectedPlan, setSelectedPlan] = useState<Plan | null>(null);
  const [provider, setProvider] = useState('asaas');
  const [billingType, setBillingType] = useState('BOLETO');
  const [cpfCnpj, setCpfCnpj] = useState('');
  const [paymentMethodId, setPaymentMethodId] = useState('');
  const [invoiceResult, setInvoiceResult] = useState<InvoiceResult | null>(null);
  const [dialogError, setDialogError] = useState('');

  function openSubscribeDialog(plan: Plan) {
    setSelectedPlan(plan);
    setProvider('asaas');
    setBillingType('BOLETO');
    setCpfCnpj('');
    setPaymentMethodId('');
    setDialogError('');
    setInvoiceResult(null);
  }

  function handleProviderChange(v: string) {
    setProvider(v);
    setBillingType(v === 'stripe' ? 'CREDIT_CARD' : 'BOLETO');
  }

  async function handleSubscribe() {
    if (!selectedPlan) return;
    setDialogError('');
    const req: CreateSubscriptionRequest = {
      planId: selectedPlan.id,
      provider,
      billingType,
      billingCycle: cycle,
      cpfCnpj: provider === 'asaas' ? cpfCnpj : undefined,
      paymentMethodId: billingType === 'CREDIT_CARD' ? paymentMethodId : undefined,
    };
    try {
      const result = await subscribe.mutateAsync(req);
      setInvoiceResult(result);
    } catch (err: unknown) {
      const msg =
        (err as { response?: { data?: { error?: string } } })?.response?.data?.error ??
        'Erro ao processar assinatura.';
      setDialogError(msg);
    }
  }

  async function handleCancel() {
    if (!confirm('Cancelar assinatura? O acesso continua até o fim do período atual.')) return;
    try {
      await cancelSub.mutateAsync();
    } catch {
      // subscription query will refresh; error shown implicitly
    }
  }

  function closeInvoiceDialog() {
    setInvoiceResult(null);
    setSelectedPlan(null);
  }

  if (loadingPlans || loadingSub) return <p className="text-muted-foreground">Carregando…</p>;

  return (
    <div className="space-y-6 max-w-4xl">
      <h1 className="text-2xl font-bold">Billing</h1>

      {/* Current subscription */}
      {subscription && (
        <Card>
          <CardHeader>
            <div className="flex items-center justify-between">
              <CardTitle>Assinatura atual</CardTitle>
              <Badge variant={subStatusVariant(subscription.status)}>
                {subscription.status}
              </Badge>
            </div>
          </CardHeader>
          <CardContent className="space-y-2">
            <p className="text-sm">
              Plano: <strong>{subscription.plan?.name ?? '—'}</strong> · Ciclo:{' '}
              <strong>{subscription.billingCycle}</strong> · Provedor:{' '}
              <strong>{subscription.provider}</strong>
            </p>
            {subscription.currentPeriodEnd && (
              <p className="text-sm text-muted-foreground">
                Período atual até{' '}
                {new Date(subscription.currentPeriodEnd).toLocaleDateString('pt-BR')}
              </p>
            )}
            {subscription.lastInvoice && (
              <p className="text-sm">
                Última fatura: R$ {subscription.lastInvoice.amount.toFixed(2)} —{' '}
                <Badge
                  variant={
                    subscription.lastInvoice.status === 'paid' ? 'default' : 'secondary'
                  }
                >
                  {subscription.lastInvoice.status}
                </Badge>
              </p>
            )}
            {subscription.status !== 'cancelled' && (
              <Button
                variant="destructive"
                size="sm"
                onClick={handleCancel}
                disabled={cancelSub.isPending}
              >
                {cancelSub.isPending ? 'Cancelando…' : 'Cancelar assinatura'}
              </Button>
            )}
          </CardContent>
        </Card>
      )}

      {/* Plans */}
      <div>
        <h2 className="text-xl font-semibold mb-4">Planos disponíveis</h2>
        <Tabs value={cycle} onValueChange={(v) => setCycle(v as BillingCycle)}>
          <TabsList>
            <TabsTrigger value="monthly">Mensal</TabsTrigger>
            <TabsTrigger value="yearly">Anual</TabsTrigger>
          </TabsList>

          {(['monthly', 'yearly'] as const).map((c) => (
            <TabsContent key={c} value={c}>
              {(!plans || plans.length === 0) && (
                <p className="text-muted-foreground mt-4">
                  Nenhum plano disponível. Insira planos via Swagger ou diretamente no banco.
                </p>
              )}
              <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3 mt-4">
                {plans?.map((plan) => {
                  const limits = parseLimits(plan.limitsJson);
                  const price = c === 'monthly' ? plan.priceMonthly : plan.priceYearly;
                  const isActive = subscription?.plan?.id === plan.id;
                  return (
                    <Card key={plan.id} className={isActive ? 'ring-2 ring-primary' : ''}>
                      <CardHeader>
                        <CardTitle>{plan.name}</CardTitle>
                        <CardDescription>
                          R$ {price.toFixed(2)}/{c === 'monthly' ? 'mês' : 'ano'}
                        </CardDescription>
                      </CardHeader>
                      <CardContent className="space-y-2">
                        <ul className="text-sm space-y-1 text-muted-foreground">
                          <li>
                            {limits.messages_per_month.toLocaleString('pt-BR')} mensagens/mês
                          </li>
                          <li>{limits.whatsapp_accounts} conta(s) WhatsApp</li>
                          <li>{limits.team_members} membro(s) na equipe</li>
                        </ul>
                        <Button
                          className="w-full mt-2"
                          variant={isActive ? 'secondary' : 'default'}
                          disabled={isActive}
                          onClick={() => openSubscribeDialog(plan)}
                        >
                          {isActive ? 'Plano atual' : 'Assinar'}
                        </Button>
                      </CardContent>
                    </Card>
                  );
                })}
              </div>
            </TabsContent>
          ))}
        </Tabs>
      </div>

      {/* Subscribe dialog */}
      <Dialog
        open={!!selectedPlan && !invoiceResult}
        onOpenChange={(o) => !o && setSelectedPlan(null)}
      >
        <DialogContent className="max-w-md">
          <DialogHeader>
            <DialogTitle>Assinar {selectedPlan?.name}</DialogTitle>
          </DialogHeader>
          <div className="space-y-4">
            <div className="space-y-1">
              <Label>Provedor de pagamento</Label>
              <Select value={provider} onValueChange={handleProviderChange}>
                <SelectTrigger>
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="asaas">Asaas (Boleto / PIX)</SelectItem>
                  <SelectItem value="stripe">Stripe (Cartão de crédito)</SelectItem>
                </SelectContent>
              </Select>
            </div>

            {provider === 'asaas' && (
              <div className="space-y-1">
                <Label>Forma de pagamento</Label>
                <Select value={billingType} onValueChange={setBillingType}>
                  <SelectTrigger>
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value="BOLETO">Boleto bancário</SelectItem>
                    <SelectItem value="PIX">PIX</SelectItem>
                    <SelectItem value="CREDIT_CARD">Cartão de crédito</SelectItem>
                  </SelectContent>
                </Select>
              </div>
            )}

            {provider === 'asaas' && (
              <div className="space-y-1">
                <Label htmlFor="cpfCnpj">CPF ou CNPJ</Label>
                <Input
                  id="cpfCnpj"
                  placeholder="000.000.000-00"
                  value={cpfCnpj}
                  onChange={(e) => setCpfCnpj(e.target.value)}
                  required
                />
              </div>
            )}

            {billingType === 'CREDIT_CARD' && (
              <div className="space-y-1">
                <Label htmlFor="paymentMethodId">
                  {provider === 'stripe' ? 'Stripe Payment Method ID' : 'Token do cartão (Asaas)'}
                </Label>
                <Input
                  id="paymentMethodId"
                  placeholder={provider === 'stripe' ? 'pm_xxx' : 'tokenCreditCard_xxx'}
                  value={paymentMethodId}
                  onChange={(e) => setPaymentMethodId(e.target.value)}
                  required
                />
                {provider === 'stripe' && (
                  <p className="text-xs text-muted-foreground">
                    Em produção, use Stripe.js Elements para obter o paymentMethodId de forma
                    segura sem passar os dados do cartão pelo servidor.
                  </p>
                )}
              </div>
            )}

            {dialogError && <p className="text-sm text-destructive">{dialogError}</p>}

            <Button
              className="w-full"
              onClick={handleSubscribe}
              disabled={subscribe.isPending}
            >
              {subscribe.isPending ? 'Processando…' : 'Confirmar assinatura'}
            </Button>
          </div>
        </DialogContent>
      </Dialog>

      {/* Invoice result dialog */}
      <Dialog open={!!invoiceResult} onOpenChange={(o) => !o && closeInvoiceDialog()}>
        <DialogContent className="max-w-md">
          <DialogHeader>
            <DialogTitle>Assinatura criada!</DialogTitle>
          </DialogHeader>
          {invoiceResult && (
            <div className="space-y-3">
              <p className="text-sm">
                Status: <Badge>{invoiceResult.status}</Badge> · Vencimento:{' '}
                {new Date(invoiceResult.dueDate).toLocaleDateString('pt-BR')}
              </p>

              {invoiceResult.boletoUrl && (
                <div className="space-y-1">
                  <p className="text-sm font-medium">Boleto bancário</p>
                  <a
                    href={invoiceResult.boletoUrl}
                    target="_blank"
                    rel="noopener noreferrer"
                    className="text-sm underline text-primary break-all"
                  >
                    Abrir boleto
                  </a>
                  {invoiceResult.boletoBarcode && (
                    <p className="text-xs font-mono bg-muted p-2 rounded break-all">
                      {invoiceResult.boletoBarcode}
                    </p>
                  )}
                </div>
              )}

              {invoiceResult.pixCopyPaste && (
                <div className="space-y-2">
                  <p className="text-sm font-medium">PIX Copia e Cola</p>
                  <p className="text-xs font-mono bg-muted p-2 rounded break-all">
                    {invoiceResult.pixCopyPaste}
                  </p>
                  <Button
                    size="sm"
                    variant="outline"
                    onClick={() =>
                      navigator.clipboard.writeText(invoiceResult.pixCopyPaste!)
                    }
                  >
                    Copiar código PIX
                  </Button>
                </div>
              )}

              {invoiceResult.clientSecret && (
                <p className="text-sm text-muted-foreground">
                  Pagamento Stripe iniciado. Use Stripe.js com o client_secret para confirmar
                  o cartão.
                </p>
              )}

              <Button className="w-full" onClick={closeInvoiceDialog}>
                Fechar
              </Button>
            </div>
          )}
        </DialogContent>
      </Dialog>
    </div>
  );
}
```

- [ ] **Step 3: Seed a plan in the database** (se não houver planos ainda)

Insira via Swagger (`http://localhost:8080/swagger`) ou SQL direto:

```sql
INSERT INTO plans (id, name, price_monthly, price_yearly, limits_json, is_active, created_at, updated_at)
VALUES (
  gen_random_uuid(), 'Starter', 49.90, 499.00,
  '{"messages_per_month":1000,"whatsapp_accounts":1,"team_members":3}',
  true, now(), now()
);
```

- [ ] **Step 4: Verify manually**

Navegue para `/billing`.

Esperado:
- Cards de planos com toggle mensal/anual
- Botão "Assinar" abre o dialog de pagamento
- Seleção de Asaas mostra campos de forma de pagamento e CPF/CNPJ
- Seleção de Stripe mostra campo de paymentMethodId
- Assinatura existente aparece no card "Assinatura atual" com status e período

- [ ] **Step 5: Commit**

```powershell
git -C C:\Projetos\JEL\JEL\Atendefy add src/Atendefy.Web/src/hooks/useBilling.ts src/Atendefy.Web/src/pages/BillingPage.tsx
git -C C:\Projetos\JEL\JEL\Atendefy commit -m "feat: add Billing page — plans, subscribe modal, subscription management"
```

---

## Task 9: Conversations Page (placeholder)

**Files:**
- Modify: `src/Atendefy.Web/src/pages/ConversationsPage.tsx`

- [ ] **Step 1: Write `src/pages/ConversationsPage.tsx`**

```tsx
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { MessageSquare } from 'lucide-react';

export default function ConversationsPage() {
  return (
    <div className="space-y-6">
      <h1 className="text-2xl font-bold">Conversas</h1>
      <Card className="max-w-md">
        <CardHeader>
          <div className="flex items-center gap-2">
            <MessageSquare className="h-5 w-5" />
            <CardTitle>Em breve</CardTitle>
          </div>
        </CardHeader>
        <CardContent>
          <p className="text-sm text-muted-foreground">
            O histórico de conversas por contato será implementado no Plano 5. As mensagens já
            estão sendo processadas e armazenadas pelo ConversationWorker no backend.
          </p>
        </CardContent>
      </Card>
    </div>
  );
}
```

- [ ] **Step 2: Commit**

```powershell
git -C C:\Projetos\JEL\JEL\Atendefy add src/Atendefy.Web/src/pages/ConversationsPage.tsx
git -C C:\Projetos\JEL\JEL\Atendefy commit -m "feat: add Conversations placeholder page"
```

---

## Task 10: Docker — Nginx Static Build

**Files:**
- Create: `src/Atendefy.Web/nginx.conf`
- Create: `src/Atendefy.Web/Dockerfile`
- Modify: `infra/docker-compose.yml`
- Modify: `infra/docker-compose.override.yml`
- Modify: `infra/.env`

- [ ] **Step 1: Write `src/Atendefy.Web/nginx.conf`**

```nginx
server {
    listen 80;
    root /usr/share/nginx/html;
    index index.html;

    # SPA fallback — React Router lida com todas as rotas no cliente
    location / {
        try_files $uri $uri/ /index.html;
    }

    # Proxy /api/* para o container da API (remove o prefixo /api)
    location /api/ {
        proxy_pass http://atendefy-api:8080/;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
    }
}
```

- [ ] **Step 2: Write `src/Atendefy.Web/Dockerfile`**

```dockerfile
# Build
FROM node:22-alpine AS builder
WORKDIR /app
COPY package*.json ./
RUN npm ci
COPY . .
RUN npm run build

# Serve
FROM nginx:alpine
COPY --from=builder /app/dist /usr/share/nginx/html
COPY nginx.conf /etc/nginx/conf.d/default.conf
EXPOSE 80
```

- [ ] **Step 3: Update `infra/docker-compose.yml` — add web service**

Adicione o serviço `atendefy-web` após `atendefy-api`:

```yaml
  atendefy-web:
    image: ghcr.io/atendefy/web:${WEB_VERSION:-latest}
    restart: unless-stopped
    depends_on:
      - atendefy-api
    networks:
      - atendefy
```

- [ ] **Step 4: Update `infra/docker-compose.override.yml` — build locally**

Adicione o serviço `atendefy-web` (build local, porta 3000):

```yaml
  atendefy-web:
    build:
      context: ../src/Atendefy.Web
      dockerfile: Dockerfile
    ports:
      - "3000:80"
    depends_on:
      - atendefy-api
```

O arquivo final completo deve ficar:

```yaml
services:
  postgres:
    ports:
      - "5432:5432"

  redis:
    ports:
      - "6379:6379"

  atendefy-api:
    build:
      context: ..
      dockerfile: src/Atendefy.API/Dockerfile
    environment:
      - ASPNETCORE_ENVIRONMENT=Development
      - ASPNETCORE_URLS=http://+:8080
    ports:
      - "8080:8080"
    volumes:
      - ../logs:/app/logs

  atendefy-web:
    build:
      context: ../src/Atendefy.Web
      dockerfile: Dockerfile
    ports:
      - "3000:80"
    depends_on:
      - atendefy-api
```

- [ ] **Step 5: Update `infra/.env` — add WEB_VERSION**

Adicione no final do arquivo:

```
WEB_VERSION=latest
```

- [ ] **Step 6: Test Docker build**

```powershell
cd C:\Projetos\JEL\JEL\Atendefy
docker compose -f infra/docker-compose.yml -f infra/docker-compose.override.yml build atendefy-web
```

Esperado: Build completa sem erros. A imagem nginx com os assets estáticos é criada.

- [ ] **Step 7: Smoke test full stack**

```powershell
docker compose -f infra/docker-compose.yml -f infra/docker-compose.override.yml up -d
```

Acesse `http://localhost:3000`.

Esperado:
- Página de login carrega (assets servidos pelo nginx)
- Login funciona (nginx proxy `/api` → `http://atendefy-api:8080`)
- Dashboard mostra badge "API healthy"
- Navegação entre páginas funciona (SPA fallback via `try_files`)

- [ ] **Step 8: Commit**

```powershell
git -C C:\Projetos\JEL\JEL\Atendefy add src/Atendefy.Web/nginx.conf src/Atendefy.Web/Dockerfile infra/docker-compose.yml infra/docker-compose.override.yml infra/.env
git -C C:\Projetos\JEL\JEL\Atendefy commit -m "feat: add Nginx Dockerfile and docker-compose wiring for Atendefy.Web"
```

---

## Self-Review

### Spec coverage

| Requisito | Task |
|-----------|------|
| React 19 + Vite + TypeScript | Task 1 |
| shadcn/ui + Tailwind CSS | Task 1 |
| TanStack Query v5 | Tasks 1, 5, 6, 7, 8 |
| React Router v7 | Task 4 |
| Zustand auth store com persist | Task 2 |
| Axios — Bearer + X-Tenant-Key interceptors | Task 2 |
| Login multi-tenant (subdomain + email + password) | Task 3 |
| Register/Onboarding (criar tenant + auto-login) | Task 3 |
| Protected routes → redirect /login | Tasks 3, 4 |
| Dashboard com cards | Task 5 |
| WhatsApp Accounts (list + create) | Task 6 |
| AI Config (provider + key + model + prompt) | Task 7 |
| Billing (plans + subscribe + subscription status + cancel) | Task 8 |
| Conversas (placeholder) | Task 9 |
| Vite proxy /api → localhost:8080 em dev | Task 1 |
| Docker Nginx static build | Task 10 |
| Sem testes automatizados (verificação manual) | Todas |

Todos os requisitos cobertos. ✅

### Placeholder scan

- Nenhum "TBD", "TODO" ou "implement later" ✅
- Todos os steps com mudanças de código mostram o código completo ✅
- Tipos usados nas Tasks 5-10 definidos na Task 2 ✅
- Nenhuma referência "similar à Task N" ✅

### Type consistency

- `AuthResponse.accessToken` (camelCase) — JSON do backend serializa em camelCase ✅
- Login passa `X-Tenant-Key` manualmente (não via interceptor) porque o store está vazio no primeiro acesso ✅
- `useAuthStore.setAuth({ ...data, subdomain })` — o tipo `AuthState.setAuth` aceita `subdomain` além dos campos de `AuthResponse` ✅
- `useAIConfig` retorna `null` em 404 — `AIConfigPage` trata `config` como `AIConfigResponse | null` ✅
- `useSubscription` retorna `null` em 404 — `BillingPage` trata `subscription` como `SubscriptionResponse | null` ✅
- `Plan.id` é `string` (Guid serializado) — usado corretamente em `usePlans` e `BillingPage` ✅
