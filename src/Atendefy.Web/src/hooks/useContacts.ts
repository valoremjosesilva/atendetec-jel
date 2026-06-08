import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiClient } from '@/api/client';
import type { ContactsListResponse } from '@/types/api';

export function useContacts(page = 1, pageSize = 50) {
  return useQuery({
    queryKey: ['contacts', page, pageSize],
    queryFn: () =>
      apiClient
        .get<ContactsListResponse>('/contacts', { params: { page, pageSize } })
        .then((r) => r.data),
  });
}

export function useUpdateContactName() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ phone, name }: { phone: string; name: string }) =>
      apiClient
        .patch(`/contacts/${encodeURIComponent(phone)}`, { name })
        .then((r) => r.data),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['contacts'] }),
  });
}
