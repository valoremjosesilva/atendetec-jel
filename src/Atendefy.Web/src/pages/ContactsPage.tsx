import { useState } from 'react';
import { useContacts, useUpdateContactName } from '@/hooks/useContacts';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Check, Pencil, Phone, X } from 'lucide-react';

function formatDate(dateStr?: string): string {
  if (!dateStr) return '—';
  return new Date(dateStr).toLocaleDateString('pt-BR', {
    day: '2-digit',
    month: '2-digit',
    year: 'numeric',
  });
}

export default function ContactsPage() {
  const { data, isLoading, isError } = useContacts();
  const updateName = useUpdateContactName();
  const [editingPhone, setEditingPhone] = useState<string | null>(null);
  const [editValue, setEditValue] = useState('');

  function startEdit(phone: string, currentName?: string) {
    setEditingPhone(phone);
    setEditValue(currentName ?? '');
  }

  function cancelEdit() {
    setEditingPhone(null);
    setEditValue('');
  }

  async function saveEdit(phone: string) {
    await updateName.mutateAsync({ phone, name: editValue });
    setEditingPhone(null);
  }

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-bold">Contatos</h1>
        {data && <p className="text-sm text-muted-foreground">{data.total} contato(s)</p>}
      </div>

      {isLoading && <p className="text-muted-foreground">Carregando…</p>}
      {isError && <p className="text-destructive">Erro ao carregar contatos.</p>}

      {!isLoading && data?.contacts.length === 0 && (
        <div className="text-center py-12 text-muted-foreground">
          <Phone className="h-10 w-10 mx-auto mb-3 opacity-30" />
          <p className="text-sm">Nenhum contato ainda.</p>
          <p className="text-xs mt-1">
            Contatos aparecem automaticamente quando alguém envia uma mensagem pelo WhatsApp.
          </p>
        </div>
      )}

      {data && data.contacts.length > 0 && (
        <div className="border rounded-lg overflow-hidden">
          <table className="w-full text-sm">
            <thead className="border-b bg-muted/50">
              <tr>
                <th className="text-left px-4 py-3 font-medium">Telefone</th>
                <th className="text-left px-4 py-3 font-medium">Nome</th>
                <th className="text-left px-4 py-3 font-medium">Conversas</th>
                <th className="text-left px-4 py-3 font-medium">Última atividade</th>
                <th className="w-10 px-4 py-3" />
              </tr>
            </thead>
            <tbody>
              {data.contacts.map((contact) => (
                <tr key={contact.phone} className="border-b last:border-0 hover:bg-muted/30">
                  <td className="px-4 py-3 font-mono text-xs">{contact.phone}</td>
                  <td className="px-4 py-3">
                    {editingPhone === contact.phone ? (
                      <div className="flex items-center gap-2">
                        <Input
                          className="h-7 text-sm w-40"
                          value={editValue}
                          onChange={(e) => setEditValue(e.target.value)}
                          onKeyDown={(e) => {
                            if (e.key === 'Enter') void saveEdit(contact.phone);
                            if (e.key === 'Escape') cancelEdit();
                          }}
                          autoFocus
                        />
                        <Button
                          size="icon"
                          variant="ghost"
                          className="h-7 w-7"
                          onClick={() => void saveEdit(contact.phone)}
                          disabled={updateName.isPending}
                        >
                          <Check className="h-3 w-3" />
                        </Button>
                        <Button
                          size="icon"
                          variant="ghost"
                          className="h-7 w-7"
                          onClick={cancelEdit}
                        >
                          <X className="h-3 w-3" />
                        </Button>
                      </div>
                    ) : (
                      <span className={contact.name ? '' : 'text-muted-foreground italic'}>
                        {contact.name ?? 'sem nome'}
                      </span>
                    )}
                  </td>
                  <td className="px-4 py-3 text-muted-foreground">{contact.conversationCount}</td>
                  <td className="px-4 py-3 text-muted-foreground">{formatDate(contact.lastActivity)}</td>
                  <td className="px-4 py-3">
                    {editingPhone !== contact.phone && (
                      <Button
                        size="icon"
                        variant="ghost"
                        className="h-7 w-7"
                        onClick={() => startEdit(contact.phone, contact.name)}
                      >
                        <Pencil className="h-3 w-3" />
                      </Button>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}
