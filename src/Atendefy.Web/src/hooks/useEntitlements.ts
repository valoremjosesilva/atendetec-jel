import { useQuery } from '@tanstack/react-query';
import { apiClient } from '@/api/client';

export interface Entitlements {
  aiEnabled: boolean;
  schedulingEnabled: boolean;
  whatsAppAccounts: number;
  messagesPerMonth: number;
  teamMembers: number;
}

export interface MeResponse {
  role: string | null;
  planName: string | null;
  entitlements: Entitlements;
  usage: { messagesUsed: number };
}

export function useEntitlements() {
  return useQuery({
    queryKey: ['me'],
    queryFn: () => apiClient.get<MeResponse>('/me').then((r) => r.data),
    staleTime: 60_000,
  });
}
