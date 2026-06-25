import { create } from 'zustand';
import { persist } from 'zustand/middleware';

interface AdminState {
  adminKey: string | null;
  setAdminKey: (key: string) => void;
  clear: () => void;
}

// Guarda a X-Admin-Key do superadmin localmente (mesma chave usada pelos endpoints /admin).
export const useAdminStore = create<AdminState>()(
  persist(
    (set) => ({
      adminKey: null,
      setAdminKey: (key) => set({ adminKey: key }),
      clear: () => set({ adminKey: null }),
    }),
    { name: 'mensagee-admin' }
  )
);
