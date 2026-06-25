import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { adminClient } from '@/api/adminClient';

export interface AdminPlan {
  id: string;
  name: string;
  priceMonthly: number;
  priceYearly: number;
  isActive: boolean;
  whatsAppAccounts: number;
  messagesPerMonth: number;
  teamMembers: number;
  aiEnabled: boolean;
  schedulingEnabled: boolean;
}

export type PlanInput = Omit<AdminPlan, 'id'>;

export interface AdminTenant {
  id: string;
  subdomain: string;
  name: string;
  status: string;
  planId: string | null;
  planName: string | null;
  createdAt: string;
}

export function usePlans() {
  return useQuery({
    queryKey: ['admin-plans'],
    queryFn: () => adminClient.get<AdminPlan[]>('/admin/plans').then((r) => r.data),
  });
}

export function useCreatePlan() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (input: PlanInput) =>
      adminClient.post<AdminPlan>('/admin/plans', input).then((r) => r.data),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['admin-plans'] }),
  });
}

export function useUpdatePlan() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, input }: { id: string; input: PlanInput }) =>
      adminClient.put<AdminPlan>(`/admin/plans/${id}`, input).then((r) => r.data),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['admin-plans'] }),
  });
}

export function useAdminTenants() {
  return useQuery({
    queryKey: ['admin-tenants'],
    queryFn: () => adminClient.get<AdminTenant[]>('/admin/tenants').then((r) => r.data),
  });
}

export function useAssignPlan() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ subdomain, planId }: { subdomain: string; planId: string }) =>
      adminClient
        .post(`/admin/tenants/${subdomain}/plan`, { planId })
        .then((r) => r.data),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['admin-tenants'] }),
  });
}

export function useActivateTenant() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (subdomain: string) =>
      adminClient.post(`/tenants/${subdomain}/activate`, {}).then((r) => r.data),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['admin-tenants'] }),
  });
}
