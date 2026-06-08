import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiClient } from '@/api/client';
import type { ConversationsListResponse, ConversationDetail } from '@/types/api';

export function useConversations(
  page = 1,
  pageSize = 20,
  status: 'open' | 'resolved' | 'all' = 'open'
) {
  return useQuery({
    queryKey: ['conversations', page, pageSize, status],
    queryFn: () =>
      apiClient
        .get<ConversationsListResponse>('/conversations', { params: { page, pageSize, status } })
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

export function useTakeoverConversation() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) =>
      apiClient.patch(`/conversations/${id}/takeover`).then((r) => r.data),
    onSuccess: (_data, id) => {
      queryClient.invalidateQueries({ queryKey: ['conversations'] });
      queryClient.invalidateQueries({ queryKey: ['conversations', id, 'messages'] });
    },
  });
}

export function useReleaseConversation() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) =>
      apiClient.patch(`/conversations/${id}/release`).then((r) => r.data),
    onSuccess: (_data, id) => {
      queryClient.invalidateQueries({ queryKey: ['conversations'] });
      queryClient.invalidateQueries({ queryKey: ['conversations', id, 'messages'] });
    },
  });
}

export function useResolveConversation() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) =>
      apiClient.patch(`/conversations/${id}/resolve`).then((r) => r.data),
    onSuccess: (_data, id) => {
      queryClient.invalidateQueries({ queryKey: ['conversations'] });
      queryClient.invalidateQueries({ queryKey: ['conversations', id, 'messages'] });
    },
  });
}

export function useReopenConversation() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) =>
      apiClient.patch(`/conversations/${id}/reopen`).then((r) => r.data),
    onSuccess: (_data, id) => {
      queryClient.invalidateQueries({ queryKey: ['conversations'] });
      queryClient.invalidateQueries({ queryKey: ['conversations', id, 'messages'] });
    },
  });
}

export function useSendMessage() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, text }: { id: string; text: string }) =>
      apiClient
        .post(`/conversations/${id}/messages`, { text })
        .then((r) => r.data),
    onSuccess: (_data, { id }) => {
      queryClient.invalidateQueries({ queryKey: ['conversations', id, 'messages'] });
    },
  });
}
