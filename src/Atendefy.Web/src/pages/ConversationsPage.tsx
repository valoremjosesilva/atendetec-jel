import { useEffect, useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { MessageSquare, Phone } from 'lucide-react';
import { Badge } from '@/components/ui/badge';
import { cn } from '@/lib/utils';
import { useConversations, useConversationMessages } from '@/hooks/useConversations';
import { useAuthStore } from '@/stores/authStore';

function formatTime(dateStr: string): string {
  const d = new Date(dateStr);
  const now = new Date();
  const isToday = d.toDateString() === now.toDateString();
  if (isToday) return d.toLocaleTimeString('pt-BR', { hour: '2-digit', minute: '2-digit' });
  return d.toLocaleDateString('pt-BR', { day: '2-digit', month: '2-digit' });
}

export default function ConversationsPage() {
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const { data, isLoading, isError } = useConversations();
  const { data: detail, isLoading: loadingMessages, isError: messagesError } =
    useConversationMessages(selectedId);

  const queryClient = useQueryClient();

  useEffect(() => {
    const token = useAuthStore.getState().accessToken;
    if (!token) return;

    const url = `/api/conversations/stream?token=${encodeURIComponent(token)}`;
    const es = new EventSource(url);
    let failures = 0;

    es.onmessage = (e) => {
      try {
        const { conversationId } = JSON.parse(e.data) as { conversationId: string };
        queryClient.invalidateQueries({ queryKey: ['conversations'] });
        queryClient.invalidateQueries({ queryKey: ['conversations', conversationId, 'messages'] });
        queryClient.invalidateQueries({ queryKey: ['dashboard-stats'] });
        failures = 0;
      } catch {
        console.warn('[SSE] malformed event data:', e.data);
      }
    };

    es.onerror = () => {
      failures++;
      if (failures >= 5) { es.close(); return; }
    };

    return () => es.close();
  }, [queryClient]);

  return (
    <div className="flex h-[calc(100vh-8rem)] gap-4">
      {/* Painel esquerdo: lista de conversas */}
      <div className="w-80 shrink-0 flex flex-col border rounded-lg overflow-hidden bg-card">
        <div className="p-4 border-b">
          <h1 className="text-lg font-semibold">Conversas</h1>
          {data && (
            <p className="text-xs text-muted-foreground mt-0.5">{data.total} conversa(s)</p>
          )}
        </div>

        <div className="flex-1 overflow-y-auto">
          {isLoading && (
            <p className="p-4 text-sm text-muted-foreground">Carregando...</p>
          )}
          {isError && (
            <p className="p-4 text-sm text-destructive">Erro ao carregar conversas.</p>
          )}
          {!isLoading && !isError && data?.conversations.length === 0 && (
            <div className="p-6 text-center text-sm text-muted-foreground">
              <MessageSquare className="h-8 w-8 mx-auto mb-2 opacity-30" />
              <p>Nenhuma conversa ainda.</p>
              <p className="mt-1 text-xs">Envie uma mensagem via WhatsApp para começar.</p>
            </div>
          )}
          {data?.conversations.map((conv) => (
            <button
              key={conv.id}
              type="button"
              className={cn(
                'w-full text-left px-4 py-3 border-b hover:bg-accent transition-colors',
                selectedId === conv.id && 'bg-accent'
              )}
              onClick={() => setSelectedId(conv.id)}
            >
              <div className="flex items-center justify-between mb-1">
                <span className="text-sm font-medium truncate">{conv.contactPhone}</span>
                <span className="text-xs text-muted-foreground shrink-0 ml-2">
                  {formatTime(conv.lastMessageAt)}
                </span>
              </div>
              <Badge variant="outline" className="text-xs py-0 h-5">
                {conv.messageCount} msgs
              </Badge>
            </button>
          ))}
        </div>
      </div>

      {/* Painel direito: mensagens */}
      <div className="flex-1 flex flex-col border rounded-lg overflow-hidden bg-card">
        {!selectedId ? (
          <div className="flex-1 flex items-center justify-center text-muted-foreground">
            <div className="text-center">
              <MessageSquare className="h-12 w-12 mx-auto mb-3 opacity-20" />
              <p className="text-sm">Selecione uma conversa</p>
            </div>
          </div>
        ) : (
          <>
            <div className="px-4 py-3 border-b flex items-center gap-2 shrink-0">
              <Phone className="h-4 w-4 text-muted-foreground" />
              <span className="font-medium text-sm">{detail?.contactPhone ?? '…'}</span>
              {detail && (
                <span className="text-xs text-muted-foreground ml-auto">
                  desde {new Date(detail.startedAt).toLocaleDateString('pt-BR')}
                </span>
              )}
            </div>

            <div className="flex-1 overflow-y-auto p-4 space-y-3">
              {loadingMessages && (
                <p className="text-sm text-center text-muted-foreground py-4">
                  Carregando mensagens…
                </p>
              )}
              {messagesError && (
                <p className="text-sm text-center text-destructive py-4">
                  Erro ao carregar mensagens.
                </p>
              )}
              {detail?.messages.map((msg) => (
                <div
                  key={msg.id}
                  className={cn('flex', msg.role === 'user' ? 'justify-start' : 'justify-end')}
                >
                  <div
                    className={cn(
                      'max-w-[75%] rounded-2xl px-4 py-2 text-sm',
                      msg.role === 'user'
                        ? 'bg-muted rounded-tl-sm'
                        : 'bg-primary text-primary-foreground rounded-tr-sm'
                    )}
                  >
                    <p className="whitespace-pre-wrap break-words">{msg.content}</p>
                    <p
                      className={cn(
                        'text-xs mt-1',
                        msg.role === 'user'
                          ? 'text-muted-foreground'
                          : 'text-primary-foreground/70'
                      )}
                    >
                      {formatTime(msg.createdAt)}
                    </p>
                  </div>
                </div>
              ))}
            </div>
          </>
        )}
      </div>
    </div>
  );
}
