import axios, { AxiosError, type InternalAxiosRequestConfig } from 'axios';
import { useAuthStore } from '@/stores/authStore';

// Auth via cookies HttpOnly setados pela API (same-origin através do proxy /api).
// Nenhum token transita por JavaScript — só injetamos o X-Tenant-Key.
export const apiClient = axios.create({
  baseURL: '/api',
  withCredentials: true,
});

apiClient.interceptors.request.use((config) => {
  const { subdomain } = useAuthStore.getState();
  if (subdomain) {
    config.headers['X-Tenant-Key'] = subdomain;
  }
  return config;
});

export function logout() {
  // Expira os cookies no servidor; limpa o estado local mesmo se a chamada falhar.
  void axios.post('/api/auth/logout', {}, { withCredentials: true }).catch(() => {});
  useAuthStore.getState().clear();
  window.location.href = '/login';
}

// ─── Auto-refresh on 401 ────────────────────────────────────────────────────
// Quando o access token (cookie de 15 min) expira, o backend troca o refresh
// cookie por um novo par e a request original é repetida. 401s concorrentes
// compartilham um único refresh em andamento para não disparar N refreshes.

let refreshPromise: Promise<void> | null = null;

function refreshSession(): Promise<void> {
  if (!refreshPromise) {
    refreshPromise = axios
      // bare axios (sem interceptors) para evitar recursão
      .post('/api/auth/refresh', {}, { withCredentials: true })
      .then(() => undefined)
      .finally(() => {
        refreshPromise = null;
      });
  }
  return refreshPromise;
}

apiClient.interceptors.response.use(
  (res) => res,
  async (error: AxiosError) => {
    const original = error.config as (InternalAxiosRequestConfig & { _retry?: boolean }) | undefined;
    const status = error.response?.status;
    const { authenticated } = useAuthStore.getState();

    const isAuthCall = original?.url?.includes('/auth/');
    const canRefresh = status === 401 && authenticated && original && !original._retry && !isAuthCall;

    if (canRefresh) {
      original._retry = true;
      try {
        await refreshSession();
        return apiClient(original);
      } catch {
        logout();
        return Promise.reject(error);
      }
    }

    if (status === 401 && !isAuthCall) {
      logout();
    }
    return Promise.reject(error);
  }
);
