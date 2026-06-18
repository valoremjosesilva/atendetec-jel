import axios, { AxiosError, type InternalAxiosRequestConfig } from 'axios';
import { useAuthStore } from '@/stores/authStore';
import type { AuthResponse } from '@/types/api';

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

// ─── Auto-refresh on 401 ────────────────────────────────────────────────────
// When the 15-min access token expires, transparently exchange the refresh token
// for a new pair and replay the original request. Concurrent 401s share a single
// in-flight refresh so we don't fire N refreshes at once.

let refreshPromise: Promise<string> | null = null;

function refreshAccessToken(): Promise<string> {
  if (!refreshPromise) {
    const { refreshToken } = useAuthStore.getState();
    refreshPromise = axios
      // bare axios (no interceptors) to avoid recursion
      .post<AuthResponse>('/api/auth/refresh', { refreshToken })
      .then(({ data }) => {
        useAuthStore.getState().setTokens({
          accessToken: data.accessToken,
          refreshToken: data.refreshToken,
        });
        return data.accessToken;
      })
      .finally(() => {
        refreshPromise = null;
      });
  }
  return refreshPromise;
}

function logout() {
  useAuthStore.getState().clear();
  window.location.href = '/login';
}

apiClient.interceptors.response.use(
  (res) => res,
  async (error: AxiosError) => {
    const original = error.config as (InternalAxiosRequestConfig & { _retry?: boolean }) | undefined;
    const status = error.response?.status;
    const { refreshToken } = useAuthStore.getState();

    const isAuthCall = original?.url?.includes('/auth/');
    const canRefresh = status === 401 && refreshToken && original && !original._retry && !isAuthCall;

    if (canRefresh) {
      original._retry = true;
      try {
        const newToken = await refreshAccessToken();
        original.headers.Authorization = `Bearer ${newToken}`;
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
