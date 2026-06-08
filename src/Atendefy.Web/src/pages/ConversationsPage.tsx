import { useEffect, useRef, useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { Bot, CheckCircle, MessageSquare, Phone, UserCheck, Zap } from 'lucide-react';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { cn } from '@/lib/utils';
import {
  useConversations,
  useConversationMessages,
  useTakeoverConversation,
  useReleaseConversation,
  useResolveConversation,
  useReopenConversation,
  useSendMessage,
} from '@/hooks/useConversations';
import { useQuickReplies } from '@/hooks/useQuickReplies';
import { useAuthStore } from '@/stores/authStore';

type StatusFilter = 'open' | 'resolved' | 'all';

function formatTime(dateStr: string): string {
  const d = new Date(dateStr);
  const now = new Date();
  const isToday = d.toDateString() === now.toDateString();
  if (isToday) return d.toLocaleTimeString('pt-BR', { hour: '2-digit', minute: '2-digit' });
  return d.toLocaleDateString('pt-BR', { day: '2-digit', month: '2-digit' });
}

export default function ConversationsPage() {
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [statusFilter, setStatusFilter] = useState<StatusFilter>('open');
  const [messageText, setMessageText] = useState('');
  const [showQuickReplies, setShowQuickReplies] = useState(false);
  const messagesEndRef = useRef<HTMLDivElement>(null);

  const { data, isLoading, isError } = useConversations(1, 20, statusFilter);
  const { data: detail, isLoading: loadingMessages, isError: messagesError } =
    useConversationMessages(selectedId);
  const { data: quickRepliesData } = useQuickReplies();

  const takeover = useTakeoverConversation();
  const release = useReleaseConversation();
  const resolve = useResolveConversation();
  const reopen = useReopenConversation();
  const sendMessage = useSendMessage();
  const queryClient = useQueryClient();

  useEffect(() => {
    const token = useAuthStore.getState().accessToken;
    if (!token) return;
    const safeToken: string = token;

    let es: EventSource | null = null;
    let retryTimeout: ReturnType<typeof setTimeout> | null = null;
    let retries = 0;
    let destroyed = false;

    function connect() {
      if (destroyed) return;
      const url = `/api/conversations/stream?token=${encodeURIComponent(safeToken)}`;
      es = new EventSource(url);

      es.onmessage = (e) => {
        retries = 0;
        try {
          const { conversationId } = JSON.parse(e.data) as { conversationId: string };
          queryClient.invalidateQueries({ queryKey: ['conversations'] });
          queryClient.invalidateQueries({ queryKey: ['conversations', conversationId, 'messages'] });
          queryClient.invalidateQueries({ queryKey: ['dashboard-stats'] });
        } catch { /* malformed event, ignore */ }
      };

      es.onerror = () => {
        es?.close();
        if (destroyed) return;
        retries++;
        const delay = Math.min(1000 * 2 ** retries, 30_000);
        retryTimeout = setTimeout(connect, delay);
      };
    }

    connect();

    return () => {
      destroyed = true;
      es?.close();
      if (retryTimeout) clearTimeout(retryTimeout);
    };
  }, [queryClient]);

  useEffect(() => {
    messagesEndRef.current?.scrollIntoView({ behavior: 'smooth' });
  }, [detail?.messages.length]);

  const botPaused = detail?.botPaused ?? false;
  const isResolved = detail?.isResolved ?? false;

  function handleFilterChange(f: StatusFilter) {
    setStatusFilter(f);
    setSelectedId(null);
  }

  async function handleSend() {
    if (!selectedId || !messageText.trim()) return;
    const text = messageText.trim();
    setMessageText('');
    setShowQuickReplies(false);
    await sendMessage.mutateAsync({ id: selectedId, text });
  }

  function handleKeyDown(e: React.KeyboardEvent<HTMLTextAreaElement>) {
    if (e.key === 'Enter' && !e.shiftKey) {
      e.preventDefault();
      void handleSend();
    }
  }

  const filterLabels: Record<StatusFilter, string> = {
    open: 'Abertas',
    resolved: 'Resolvidas',
    all: 'Todas',
  };

  return (
    <div className="flex h-[calc(100vh-8rem)] gap-4">
      {/* Left panel */}
      <div className="w-80 shrink-0 flex flex-col border rounded-lg overflow-hidden bg-card">
        <div className="p-4 border-b">
          <h1 className="text-lg font-semibold">Conversas</h1>
          {data && (
            <p className="text-xs text-muted-foreground mt-0.5">{data.total} conversa(s)</p>
          )}
        </div>

        {/* Status filter tabs */}
        <div className="flex gap-1 p-2 border-b">
          {(['open', 'resolved', 'all'] as const).map((f) => (
            <button
              key={f}
              type="button"
              onClick={() => handleFilterChange(f)}
              className={cn(
                'flex-1 text-xs py-1.5 rounded transition-colors',
                statusFilter === f
                  ? 'bg-primary text-primary-foreground'
                  : 'hover:bg-accent text-muted-foreground'
              )}
            >
              {filterLabels[f]}
            </button>
          ))}
        </div>

        <div className="flex-1 overflow-y-auto">
          {isLoading && <p className="p-4 text-sm text-muted-foreground">Carregando...</p>}
          {isError && <p className="p-4 text-sm text-destructive">Erro ao carregar conversas.</p>}
          {!isLoading && !isError && data?.conversations.length === 0 && (
            <div className="p-6 text-center text-sm text-muted-foreground">
              <MessageSquare className="h-8 w-8 mx-auto mb-2 opacity-30" />
              <p>Nenhuma conversa {statusFilter === 'open' ? 'aberta' : statusFilter === 'resolved' ? 'resolvida' : ''} ainda.</p>
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
              <div className="flex items-center gap-1.5 flex-wrap">
                <Badge variant="outline" className="text-xs py-0 h-5">
                  {conv.messageCount} msgs
                </Badge>
                {conv.botPaused && !conv.isResolved && (
                  <Badge variant="secondary" className="text-xs py-0 h-5 gap-1">
                    <UserCheck className="h-3 w-3" />
                    humano
                  </Badge>
                )}
                {conv.isResolved && (
                  <Badge variant="outline" className="text-xs py-0 h-5 gap-1 text-green-600 border-green-200">
                    <CheckCircle className="h-3 w-3" />
                    encerrada
                  </Badge>
                )}
              </div>
            </button>
          ))}
        </div>
      </div>

      {/* Right panel */}
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
            {/* Header */}
            <div className="px-4 py-3 border-b flex items-center gap-2 shrink-0">
              <Phone className="h-4 w-4 text-muted-foreground" />
              <span className="font-medium text-sm">{detail?.contactPhone ?? '…'}</span>
              {detail && (
                <span className="text-xs text-muted-foreground">
                  desde {new Date(detail.startedAt).toLocaleDateString('pt-BR')}
                </span>
              )}
              <div className="ml-auto flex items-center gap-2">
                {isResolved ? (
                  <>
                    <Badge variant="outline" className="gap-1 text-green-600 border-green-200">
                      <CheckCircle className="h-3 w-3" />
                      Resolvida
                    </Badge>
                    <Button
                      size="sm"
                      variant="outline"
                      onClick={() => reopen.mutate(selectedId)}
                      disabled={reopen.isPending}
                    >
                      Reabrir
                    </Button>
                  </>
                ) : (
                  <>
                    {botPaused ? (
                      <>
                        <Badge variant="secondary" className="gap-1">
                          <UserCheck className="h-3 w-3" />
                          Modo humano
                        </Badge>
                        <Button
                          size="sm"
                          variant="outline"
                          onClick={() => release.mutate(selectedId)}
                          disabled={release.isPending}
                        >
                          <Bot className="h-4 w-4 mr-1" />
                          Liberar bot
                        </Button>
                      </>
                    ) : (
                      <>
                        <Badge variant="outline" className="gap-1 text-muted-foreground">
                          <Bot className="h-3 w-3" />
                          Modo bot
                        </Badge>
                        <Button
                          size="sm"
                          variant="outline"
                          onClick={() => takeover.mutate(selectedId)}
                          disabled={takeover.isPending}
                        >
                          <UserCheck className="h-4 w-4 mr-1" />
                          Assumir
                        </Button>
                      </>
                    )}
                    <Button
                      size="sm"
                      variant="ghost"
                      className="text-muted-foreground"
                      onClick={() => resolve.mutate(selectedId)}
                      disabled={resolve.isPending}
                    >
                      <CheckCircle className="h-4 w-4 mr-1" />
                      Resolver
                    </Button>
                  </>
                )}
              </div>
            </div>

            {/* Messages */}
            <div className="flex-1 overflow-y-auto p-4 space-y-3">
              {loadingMessages && (
                <p className="text-sm text-center text-muted-foreground py-4">Carregando mensagens…</p>
              )}
              {messagesError && (
                <p className="text-sm text-center text-destructive py-4">Erro ao carregar mensagens.</p>
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
                        : msg.role === 'agent'
                          ? 'bg-emerald-600 text-white rounded-tr-sm'
                          : 'bg-primary text-primary-foreground rounded-tr-sm'
                    )}
                  >
                    <p className="whitespace-pre-wrap break-words">{msg.content}</p>
                    <p className="text-xs mt-1 opacity-70">
                      {formatTime(msg.createdAt)}
                      {msg.role === 'agent' && ' · você'}
                    </p>
                  </div>
                </div>
              ))}
              <div ref={messagesEndRef} />
            </div>

            {/* Send footer — only when human has control and conversation is open */}
            {botPaused && !isResolved && (
              <div className="border-t p-3 flex flex-col gap-2 shrink-0">
                {/* Quick replies panel */}
                {showQuickReplies && (
                  <div className="max-h-48 overflow-y-auto border rounded-md bg-popover shadow-sm">
                    {!quickRepliesData?.quickReplies.length ? (
                      <p className="p-3 text-xs text-muted-foreground text-center">
                        Nenhuma resposta rápida configurada. Acesse{' '}
                        <a href="/quick-replies" className="underline">Respostas Rápidas</a>.
                      </p>
                    ) : (
                      quickRepliesData.quickReplies.map((qr) => (
                        <button
                          key={qr.id}
                          type="button"
                          className="w-full text-left px-3 py-2 text-sm hover:bg-accent transition-colors border-b last:border-0"
                          onClick={() => {
                            setMessageText(qr.body);
                            setShowQuickReplies(false);
                          }}
                        >
                          <p className="font-medium text-xs text-muted-foreground mb-0.5">{qr.title}</p>
                          <p className="truncate text-xs">{qr.body}</p>
                        </button>
                      ))
                    )}
                  </div>
                )}
                {/* Input row */}
                <div className="flex gap-2">
                  <textarea
                    className="flex-1 resize-none rounded-md border bg-background px-3 py-2 text-sm min-h-[40px] max-h-[120px] focus:outline-none focus:ring-2 focus:ring-ring"
                    placeholder="Digite uma mensagem… (Enter para enviar)"
                    rows={1}
                    value={messageText}
                    onChange={(e) => setMessageText(e.target.value)}
                    onKeyDown={handleKeyDown}
                  />
                  <div className="flex flex-col gap-1">
                    <Button
                      size="sm"
                      variant="outline"
                      type="button"
                      onClick={() => setShowQuickReplies((v) => !v)}
                      className={cn(showQuickReplies && 'bg-accent')}
                    >
                      <Zap className="h-4 w-4" />
                    </Button>
                    <Button
                      size="sm"
                      onClick={() => void handleSend()}
                      disabled={!messageText.trim() || sendMessage.isPending}
                    >
                      {sendMessage.isPending ? '…' : 'Enviar'}
                    </Button>
                  </div>
                </div>
              </div>
            )}
          </>
        )}
      </div>
    </div>
  );
}
