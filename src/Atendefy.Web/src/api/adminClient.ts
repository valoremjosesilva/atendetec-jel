import axios, { AxiosError } from 'axios';
import { useAdminStore } from '@/stores/adminStore';

// Cliente para os endpoints /admin/* — envia a X-Admin-Key no header (sem JWT de tenant).
export const adminClient = axios.create({
  baseURL: '/api',
});

adminClient.interceptors.request.use((config) => {
  const { adminKey } = useAdminStore.getState();
  if (adminKey) {
    config.headers['X-Admin-Key'] = adminKey;
  }
  return config;
});

adminClient.interceptors.response.use(
  (res) => res,
  (error: AxiosError) => {
    // 403 => chave inválida/ausente: limpa e volta para o login admin.
    if (error.response?.status === 403) {
      useAdminStore.getState().clear();
      if (!window.location.pathname.startsWith('/admin/login')) {
        window.location.href = '/admin/login';
      }
    }
    return Promise.reject(error);
  }
);
