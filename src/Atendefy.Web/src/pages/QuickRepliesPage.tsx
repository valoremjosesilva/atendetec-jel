import { useState } from 'react';
import {
  useQuickReplies,
  useCreateQuickReply,
  useUpdateQuickReply,
  useDeleteQuickReply,
} from '@/hooks/useQuickReplies';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Check, Pencil, Plus, Trash2, X, Zap } from 'lucide-react';

export default function QuickRepliesPage() {
  const { data, isLoading, isError } = useQuickReplies();
  const createReply = useCreateQuickReply();
  const updateReply = useUpdateQuickReply();
  const deleteReply = useDeleteQuickReply();

  const [creating, setCreating] = useState(false);
  const [newTitle, setNewTitle] = useState('');
  const [newBody, setNewBody] = useState('');

  const [editingId, setEditingId] = useState<string | null>(null);
  const [editTitle, setEditTitle] = useState('');
  const [editBody, setEditBody] = useState('');

  async function handleCreate() {
    if (!newTitle.trim() || !newBody.trim()) return;
    await createReply.mutateAsync({ title: newTitle.trim(), body: newBody.trim() });
    setNewTitle('');
    setNewBody('');
    setCreating(false);
  }

  function startEdit(id: string, title: string, body: string) {
    setEditingId(id);
    setEditTitle(title);
    setEditBody(body);
  }

  async function saveEdit(id: string) {
    if (!editTitle.trim() || !editBody.trim()) return;
    await updateReply.mutateAsync({ id, title: editTitle.trim(), body: editBody.trim() });
    setEditingId(null);
  }

  function cancelEdit() {
    setEditingId(null);
    setEditTitle('');
    setEditBody('');
  }

  return (
    <div className="space-y-6 max-w-3xl">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-bold">Respostas Rápidas</h1>
          <p className="text-sm text-muted-foreground mt-1">
            Templates de texto para agilizar o atendimento manual.
          </p>
        </div>
        {!creating && (
          <Button onClick={() => setCreating(true)} size="sm">
            <Plus className="h-4 w-4 mr-1" />
            Nova Resposta
          </Button>
        )}
      </div>

      {isLoading && <p className="text-muted-foreground text-sm">Carregando…</p>}
      {isError && <p className="text-destructive text-sm">Erro ao carregar respostas.</p>}

      {/* Create form */}
      {creating && (
        <div className="border rounded-lg p-4 space-y-3 bg-muted/30">
          <p className="text-sm font-medium">Nova resposta rápida</p>
          <Input
            placeholder="Título (ex: Saudação inicial)"
            value={newTitle}
            onChange={(e) => setNewTitle(e.target.value)}
            maxLength={100}
          />
          <textarea
            className="w-full resize-none rounded-md border bg-background px-3 py-2 text-sm min-h-[80px] focus:outline-none focus:ring-2 focus:ring-ring"
            placeholder="Texto completo da resposta…"
            value={newBody}
            onChange={(e) => setNewBody(e.target.value)}
            rows={3}
            maxLength={1000}
          />
          <div className="flex gap-2">
            <Button
              size="sm"
              onClick={() => void handleCreate()}
              disabled={!newTitle.trim() || !newBody.trim() || createReply.isPending}
            >
              <Check className="h-4 w-4 mr-1" />
              {createReply.isPending ? 'Salvando…' : 'Salvar'}
            </Button>
            <Button
              size="sm"
              variant="outline"
              onClick={() => { setCreating(false); setNewTitle(''); setNewBody(''); }}
            >
              <X className="h-4 w-4 mr-1" />
              Cancelar
            </Button>
          </div>
        </div>
      )}

      {/* Empty state */}
      {!isLoading && data?.quickReplies.length === 0 && !creating && (
        <div className="text-center py-12 text-muted-foreground">
          <Zap className="h-10 w-10 mx-auto mb-3 opacity-30" />
          <p className="text-sm">Nenhuma resposta rápida ainda.</p>
          <p className="text-xs mt-1">
            Crie templates de texto para usar ao atender clientes manualmente.
          </p>
        </div>
      )}

      {/* Replies list */}
      {data && data.quickReplies.length > 0 && (
        <div className="border rounded-lg divide-y overflow-hidden">
          {data.quickReplies.map((qr) =>
            editingId === qr.id ? (
              <div key={qr.id} className="p-4 space-y-2 bg-muted/30">
                <Input
                  value={editTitle}
                  onChange={(e) => setEditTitle(e.target.value)}
                  placeholder="Título"
                  maxLength={100}
                />
                <textarea
                  className="w-full resize-none rounded-md border bg-background px-3 py-2 text-sm min-h-[80px] focus:outline-none focus:ring-2 focus:ring-ring"
                  value={editBody}
                  onChange={(e) => setEditBody(e.target.value)}
                  rows={3}
                  maxLength={1000}
                  onKeyDown={(e) => {
                    if (e.key === 'Escape') cancelEdit();
                  }}
                />
                <div className="flex gap-2">
                  <Button
                    size="sm"
                    onClick={() => void saveEdit(qr.id)}
                    disabled={!editTitle.trim() || !editBody.trim() || updateReply.isPending}
                  >
                    <Check className="h-4 w-4 mr-1" />
                    Salvar
                  </Button>
                  <Button size="sm" variant="outline" onClick={cancelEdit}>
                    <X className="h-4 w-4 mr-1" />
                    Cancelar
                  </Button>
                </div>
              </div>
            ) : (
              <div key={qr.id} className="flex items-start gap-3 p-4 hover:bg-muted/30">
                <Zap className="h-4 w-4 text-muted-foreground mt-0.5 shrink-0" />
                <div className="flex-1 min-w-0">
                  <p className="text-sm font-medium">{qr.title}</p>
                  <p className="text-xs text-muted-foreground mt-0.5 whitespace-pre-wrap">{qr.body}</p>
                </div>
                <div className="flex gap-1 shrink-0">
                  <Button
                    size="icon"
                    variant="ghost"
                    className="h-8 w-8"
                    onClick={() => startEdit(qr.id, qr.title, qr.body)}
                  >
                    <Pencil className="h-3.5 w-3.5" />
                  </Button>
                  <Button
                    size="icon"
                    variant="ghost"
                    className="h-8 w-8 text-destructive hover:text-destructive"
                    onClick={() => void deleteReply.mutateAsync(qr.id)}
                    disabled={deleteReply.isPending}
                  >
                    <Trash2 className="h-3.5 w-3.5" />
                  </Button>
                </div>
              </div>
            )
          )}
        </div>
      )}
    </div>
  );
}
