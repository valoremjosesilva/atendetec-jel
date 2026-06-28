# Plano 005: Paginar histórico de mensagens (backend + frontend)

> **Instruções para o executor**: Siga este plano passo a passo. Execute cada verificação antes
> de avançar. Em condições de PARE, pare e reporte — não improvise.
>
> **Verificação de drift (execute primeiro)**:
> `git diff --stat e805859..HEAD -- src/Atendefy.API/Modules/Chatbot/ConversationEndpoints.cs src/Atendefy.Web/src/hooks/useConversations.ts`
> Em caso de divergência, trate como PARE.

## Status

- **Prioridade**: P1
- **Esforço**: M (Médio — ~1 dia)
- **Risco**: MÉDIO
- **Depende de**: nenhum
- **Categoria**: performance
- **Escrito no commit**: `e805859`, 2026-06-28

## Por que isso importa

O endpoint `GET /conversations/{id}/messages` retorna **todo o histórico** da conversa sem
nenhum limite. Tenants ativos com suporte via WhatsApp acumulam centenas ou milhares de mensagens
por conversa. Uma consulta sem `LIMIT` carrega tudo na memória do servidor e transmite tudo pelo
wire — causando timeouts, OOM e lentidão visível na UI. A solução é paginação por cursor
(timestamp `before`): o frontend carrega as últimas 50 mensagens por padrão e oferece um botão
"Carregar mensagens anteriores" para buscar mais.

## Estado atual

**Backend — `src/Atendefy.API/Modules/Chatbot/ConversationEndpoints.cs:66-98`:**
```csharp
group.MapGet("/{id:guid}/messages", async (
    Guid id,
    TenantDbContextFactory dbFactory,
    PublicDbContext publicDb,
    HttpContext ctx) =>
{
    // ...
    var messages = await db.Messages
        .Where(m => m.ConversationId == id)
        .OrderBy(m => m.CreatedAt)
        .Select(m => new { m.Id, m.Role, m.Content, m.TokensUsed, m.CreatedAt })
        .ToListAsync();  // <-- sem Take(); carrega tudo

    return Results.Ok(new {
        conversation.Id, conversation.ContactPhone, conversation.StartedAt,
        conversation.MessageCount, conversation.BotPaused, conversation.IsResolved,
        conversation.ResolvedAt,
        messages
    });
});
```

**Frontend — `src/Atendefy.Web/src/hooks/useConversations.ts:19-28`:**
```typescript
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
```

**Tipo de resposta atual** (`src/Atendefy.Web/src/types/api.ts` — verificar o type
`ConversationDetail`; adicionar `hasMore: boolean` a ele).

**Onde a UI usa as mensagens:**
`src/Atendefy.Web/src/pages/ConversationsPage.tsx` — consome `useConversationMessages` e renderiza
a lista. Localizar o ponto de renderização para adicionar o botão "Carregar anteriores".

## Comandos necessários

| Propósito        | Comando                                                        | Esperado     |
|------------------|----------------------------------------------------------------|--------------|
| Build .NET       | `dotnet build Atendefy.slnx -c Release --no-restore`           | exit 0       |
| Testes integração | `dotnet test Atendefy.slnx -c Release --filter "FullyQualifiedName~Integration"` | todos passam |
| Build frontend   | `cd src/Atendefy.Web && npm run build`                         | exit 0       |
| Typecheck frontend | `cd src/Atendefy.Web && npx tsc --noEmit`                   | exit 0       |

## Escopo

**Em escopo**:
- `src/Atendefy.API/Modules/Chatbot/ConversationEndpoints.cs`
- `src/Atendefy.Web/src/hooks/useConversations.ts`
- `src/Atendefy.Web/src/types/api.ts` (adicionar `hasMore` ao tipo `ConversationDetail`)
- `src/Atendefy.Web/src/pages/ConversationsPage.tsx` (adicionar botão "Carregar anteriores")

**Fora do escopo** (não tocar):
- Schema do banco — sem nova migration
- `ConversationService.cs` — lógica de persistência não muda
- Outros endpoints do grupo `/conversations`
- `useConversationMessages` para queries de SSE / invalidação — a invalidação existente
  (`queryClient.invalidateQueries`) continua funcionando

## Git workflow

- Branch: `advisor/005-message-history-pagination`
- Commits: um para o backend, um para o frontend
- Mensagem: `feat(conversations): paginar histórico de mensagens por cursor`

## Passos

### Passo 1: Atualizar o endpoint de mensagens no backend

Substitua o handler `GET /{id:guid}/messages` com suporte a `?limit=<n>&before=<ISO>`:

```csharp
group.MapGet("/{id:guid}/messages", async (
    Guid id,
    [FromQuery] int limit,
    [FromQuery] DateTime? before,
    TenantDbContextFactory dbFactory,
    PublicDbContext publicDb,
    HttpContext ctx) =>
{
    var (_, schemaName, error) = await ResolveTenantAsync(ctx, publicDb);
    if (error is not null) return Results.Json(new { error }, statusCode: 401);

    if (limit <= 0 || limit > 100) limit = 50;

    await using var db = dbFactory.Create(schemaName);

    var conversation = await db.Conversations.FirstOrDefaultAsync(c => c.Id == id);
    if (conversation is null) return Results.NotFound();

    var query = db.Messages.Where(m => m.ConversationId == id);
    if (before.HasValue)
        query = query.Where(m => m.CreatedAt < before.Value);

    // Buscar (limit+1) para detectar se há mais sem query extra
    var raw = await query
        .OrderByDescending(m => m.CreatedAt)
        .Take(limit + 1)
        .Select(m => new { m.Id, m.Role, m.Content, m.TokensUsed, m.CreatedAt })
        .ToListAsync();

    var hasMore = raw.Count > limit;
    var messages = raw.Take(limit).OrderBy(m => m.CreatedAt).ToList();

    return Results.Ok(new
    {
        conversation.Id,
        conversation.ContactPhone,
        conversation.StartedAt,
        conversation.MessageCount,
        conversation.BotPaused,
        conversation.IsResolved,
        conversation.ResolvedAt,
        messages,
        hasMore
    });
});
```

**Verificar**: `dotnet build Atendefy.slnx -c Release --no-restore` → exit 0

**Verificar**: `dotnet test Atendefy.slnx -c Release --filter "FullyQualifiedName~Integration"` →
todos passam. Os testes existentes que chamam sem `?limit=` usarão o default de 50, o que é
correto.

### Passo 2: Atualizar o tipo `ConversationDetail` no frontend

Em `src/Atendefy.Web/src/types/api.ts`, localize a interface `ConversationDetail` e adicione
`hasMore`:

```typescript
export interface ConversationDetail {
  id: string;
  contactPhone: string;
  startedAt: string;
  messageCount: number;
  botPaused: boolean;
  isResolved: boolean;
  resolvedAt: string | null;
  messages: Message[];
  hasMore: boolean;   // <-- adicionar
}
```

**Verificar**: `cd src/Atendefy.Web && npx tsc --noEmit` → exit 0

### Passo 3: Atualizar o hook `useConversationMessages`

Em `src/Atendefy.Web/src/hooks/useConversations.ts`, substitua `useConversationMessages` para
suportar o parâmetro `before`:

```typescript
export function useConversationMessages(id: string | null, before?: string) {
  return useQuery({
    queryKey: ['conversations', id, 'messages', before],
    queryFn: () => {
      const params: Record<string, string> = { limit: '50' };
      if (before) params.before = before;
      return apiClient
        .get<ConversationDetail>(`/conversations/${id}/messages`, { params })
        .then((r) => r.data);
    },
    enabled: !!id,
    staleTime: 30_000,  // 30s: mensagens não mudam sem interação do usuário
  });
}
```

**Verificar**: `cd src/Atendefy.Web && npx tsc --noEmit` → exit 0

### Passo 4: Adicionar botão "Carregar mensagens anteriores" na UI

Em `src/Atendefy.Web/src/pages/ConversationsPage.tsx`:

1. Adicione estado local para rastrear o cursor de paginação:
```typescript
const [beforeCursor, setBeforeCursor] = useState<string | undefined>(undefined);
```

2. Atualize a chamada do hook para passar o cursor:
```typescript
const { data: detail } = useConversationMessages(selectedId, beforeCursor);
```

3. Na área de renderização das mensagens, **acima** da lista, adicione o botão condicional:
```typescript
{detail?.hasMore && (
  <button
    onClick={() => {
      const oldest = detail.messages[0]?.createdAt;
      if (oldest) setBeforeCursor(oldest);
    }}
    className="w-full py-2 text-sm text-muted-foreground hover:text-foreground"
  >
    Carregar mensagens anteriores
  </button>
)}
```

4. Quando `selectedId` mudar (usuário seleciona outra conversa), resetar o cursor:
```typescript
useEffect(() => {
  setBeforeCursor(undefined);
}, [selectedId]);
```

**Verificar**: `cd src/Atendefy.Web && npm run build` → exit 0

> **Nota:** Esta implementação carrega mensagens mais antigas como uma **query separada** (não
> acumula na mesma lista — essa é a forma segura para TanStack Query). Se o produto exigir
> acumulação de páginas em scroll infinito, isso requer `useInfiniteQuery` — registre como
> follow-up, não implemente agora.

## Critérios de conclusão

- [ ] `dotnet build Atendefy.slnx -c Release --no-restore` exit 0
- [ ] `dotnet test Atendefy.slnx -c Release` exit 0 (sem regressões)
- [ ] `cd src/Atendefy.Web && npm run build` exit 0
- [ ] `cd src/Atendefy.Web && npx tsc --noEmit` exit 0
- [ ] Endpoint retorna `hasMore: true` quando há mais de 50 mensagens
- [ ] Endpoint aceita `?limit=` e `?before=` sem erro
- [ ] `grep -n "ToListAsync()" src/Atendefy.API/Modules/Chatbot/ConversationEndpoints.cs` → a
  query de mensagens tem `.Take(limit + 1)` antes do `ToListAsync()`
- [ ] Apenas os 4 arquivos em escopo foram modificados (`git status`)
- [ ] Status atualizado em `plans/README.md`

## Condições de PARE

- O tipo `ConversationDetail` não existe em `types/api.ts` com esse nome — localize o tipo real e
  adapte
- O `ConversationsPage.tsx` usa a lista de mensagens de forma diferente do esperado (ex.: store
  Zustand) — reporte a estrutura antes de adicionar o botão
- Backend retorna erro 400 ao receber `?before=` como ISO string — verificar binding do
  parâmetro `DateTime?` no .NET

## Notas de manutenção

- O `staleTime: 30_000` foi adicionado ao hook. Isso significa que navegar para outra conversa
  e voltar dentro de 30s não refaz a query. Se houver casos de uso onde isso é problemático,
  reduza para `staleTime: 0`.
- A invalidação de `queryClient.invalidateQueries({ queryKey: ['conversations', id, 'messages'] })`
  existente nos mutations (`useSendMessage`, etc.) continuará funcionando, pois invalida pelo
  prefixo `['conversations', id, 'messages']` que cobre todas as variantes de cursor.
- Follow-up explicitamente adiado: scroll infinito com `useInfiniteQuery` (acumula todas as
  páginas). Só implementar se UX exigir.
