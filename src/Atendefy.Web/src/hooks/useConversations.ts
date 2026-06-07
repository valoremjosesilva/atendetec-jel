import { useQuery } from '@tanstack/react-query';
import { apiClient } from '@/api/client';
import type { ConversationsListResponse, ConversationDetail } from '@/types/api';

export function useConversations(page = 1, pageSize = 20) {
  return useQuery({
    queryKey: ['conversations', page, pageSize],
    queryFn: () =>
      apiClient
        .get<ConversationsListResponse>('/conversations', { params: { page, pageSize } })
        .then((r) => r.data),
  });
}

export function useConversationMessages(id: string | null) {
  return useQuery({
    queryKey: ['conversations', id, 'messages'],
    queryFn: () =>
      apiClient
        .get<ConversationDetail>(`/conversations/${id}/messages`)
        .then((r) => r.data),
    enabled: !!id,
  });
}
