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
  setTokens: (tokens: { accessToken: string; refreshToken: string }) => void;
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
      setTokens: (tokens) => set(tokens),
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
