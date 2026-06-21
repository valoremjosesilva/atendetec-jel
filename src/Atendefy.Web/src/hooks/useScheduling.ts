import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiClient } from '@/api/client';
import type { SchedulingConfigRequest, SchedulingConfigResponse } from '@/types/api';

export function useScheduling() {
  return useQuery({
    queryKey: ['scheduling-config'],
    queryFn: () =>
      apiClient
        .get<SchedulingConfigResponse>('/scheduling/config')
        .then((r) => r.data)
        .catch((err: { response?: { status?: number } }) => {
          if (err?.response?.status === 404) return null;
          throw err;
        }),
  });
}

export function useSaveScheduling() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (req: SchedulingConfigRequest) =>
      apiClient.put<SchedulingConfigResponse>('/scheduling/config', req).then((r) => r.data),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['scheduling-config'] }),
  });
}
