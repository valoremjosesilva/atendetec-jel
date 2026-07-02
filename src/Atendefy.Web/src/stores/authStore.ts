import { create } from 'zustand';
import { persist } from 'zustand/middleware';

// Os tokens JWT vivem em cookies HttpOnly setados pela API — nunca em
// localStorage. Aqui ficam apenas metadados não-sensíveis da sessão.
interface AuthState {
  authenticated: boolean;
  tenantId: string | null;
  userId: string | null;
  role: string | null;
  subdomain: string | null;
  setAuth: (data: {
    tenantId: string;
    userId: string;
    role: string;
    subdomain: string;
  }) => void;
  clear: () => void;
}

const emptyState = {
  authenticated: false,
  tenantId: null,
  userId: null,
  role: null,
  subdomain: null,
};

export const useAuthStore = create<AuthState>()(
  persist(
    (set) => ({
      ...emptyState,
      setAuth: (data) => set({ ...data, authenticated: true }),
      clear: () => set(emptyState),
    }),
    {
      name: 'atendefy-auth',
      // v1: tokens saíram do localStorage (migração para cookie HttpOnly).
      // A migração descarta o estado v0 — apaga os tokens persistidos pela
      // versão anterior e força um novo login.
      version: 1,
      migrate: () => ({ ...emptyState }) as AuthState,
    }
  )
);
