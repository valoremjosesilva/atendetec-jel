import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiClient } from '@/api/client';
import type { QuickRepliesListResponse } from '@/types/api';

export function useQuickReplies() {
  return useQuery({
    queryKey: ['quick-replies'],
    queryFn: () =>
      apiClient.get<QuickRepliesListResponse>('/quick-replies').then((r) => r.data),
  });
}

export function useCreateQuickReply() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ title, body }: { title: string; body: string }) =>
      apiClient.post('/quick-replies', { title, body }).then((r) => r.data),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['quick-replies'] }),
  });
}

export function useUpdateQuickReply() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, title, body }: { id: string; title?: string; body?: string }) =>
      apiClient.patch(`/quick-replies/${id}`, { title, body }).then((r) => r.data),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['quick-replies'] }),
  });
}

export function useDeleteQuickReply() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) =>
      apiClient.delete(`/quick-replies/${id}`).then((r) => r.data),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['quick-replies'] }),
  });
}
