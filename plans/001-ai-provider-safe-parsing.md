# Plano 001: Tornar os providers de IA resilientes a respostas malformadas

> **Instruções para o executor**: Siga este plano passo a passo. Execute cada comando de
> verificação e confirme o resultado esperado antes de avançar. Se alguma condição de PARE
> ocorrer, pare e reporte — não improvise.
>
> **Verificação de drift (execute primeiro)**:
> `git diff --stat e805859..HEAD -- src/Atendefy.API/Modules/AI/OpenAIProvider.cs src/Atendefy.API/Modules/AI/AnthropicProvider.cs`
> Se algum arquivo em escopo mudou desde que este plano foi escrito, compare os trechos em
> "Estado atual" com o código ao vivo antes de continuar; em caso de divergência, trate como PARE.

## Status

- **Prioridade**: P1
- **Esforço**: P (Pequeno — horas)
- **Risco**: BAIXO
- **Depende de**: nenhum
- **Categoria**: correção de bug
- **Escrito no commit**: `e805859`, 2026-06-28

## Por que isso importa

`OpenAIProvider` e `AnthropicProvider` acessam propriedades do JSON de resposta da IA com
`GetProperty(...)` encadeado e indexação `[0]` sem verificar se as propriedades existem ou se
o array tem elementos. Quando uma API de IA retorna erro HTTP 200 com corpo de erro (ex.: limite
de contexto excedido, resposta de rate-limit em formato alternativo, campo deprecated renomeado),
o código lança `KeyNotFoundException` ou `IndexOutOfRangeException`. Essa exceção sobe até o
`ConversationWorker`, que a captura no loop externo, aguarda 2 s e reprocessa — o que pode criar
um loop infinito para aquele tenant até o serviço ser reiniciado. O fix correto é retornar um
resultado vazio em vez de lançar exceção.

## Estado atual

**`src/Atendefy.API/Modules/AI/OpenAIProvider.cs` — linha 33-38:**
```csharp
var json = await response.Content.ReadFromJsonAsync<JsonElement>();
var content = json.GetProperty("choices")[0]
                  .GetProperty("message")
                  .GetProperty("content")
                  .GetString() ?? string.Empty;
var tokens = json.GetProperty("usage").GetProperty("completion_tokens").GetInt32();
```

**`src/Atendefy.API/Modules/AI/AnthropicProvider.cs` — linha 28-32:**
```csharp
var json = await response.Content.ReadFromJsonAsync<JsonElement>();
var content = json.GetProperty("content")[0]
                  .GetProperty("text")
                  .GetString() ?? string.Empty;
var tokens = json.GetProperty("usage").GetProperty("output_tokens").GetInt32();
```

**Tipo de retorno:** `AICompletionResult(string Content, int TokensUsed)`
(`src/Atendefy.API/Modules/AI/Models/AICompletionResult.cs`)

**Convenção do projeto:** erros são tratados localmente com log + fallback seguro; exceções são
reservadas para falhas irrecuperáveis. Veja `ConversationWorker.cs:60-64` para o padrão de catch
do loop externo.

**Testes existentes para usar como padrão:**
`tests/Atendefy.Tests/AI/OpenAIProviderTests.cs` — usa `MockHttpMessageHandler.ReturnsJson(json)`
para simular respostas da API e verifica `result.Content` e `result.TokensUsed`.

## Comandos necessários

| Propósito   | Comando                                                              | Esperado       |
|-------------|----------------------------------------------------------------------|----------------|
| Build       | `dotnet build Atendefy.slnx -c Release --no-restore`                | exit 0         |
| Testes IA   | `dotnet test Atendefy.slnx -c Release --filter "FullyQualifiedName~AI"` | todos passam |
| Todos testes | `dotnet test Atendefy.slnx -c Release`                             | todos passam   |

## Escopo

**Em escopo** (únicos arquivos a modificar):
- `src/Atendefy.API/Modules/AI/OpenAIProvider.cs`
- `src/Atendefy.API/Modules/AI/AnthropicProvider.cs`
- `tests/Atendefy.Tests/AI/OpenAIProviderTests.cs`
- `tests/Atendefy.Tests/AI/AnthropicProviderTests.cs`

**Fora do escopo** (não tocar):
- `IAIProvider.cs` — assinatura da interface não muda
- `MockAIProvider.cs` — provider de teste, não afetado
- `ConversationWorker.cs` — o comportamento de retry do worker permanece inalterado
- `AICompletionResult.cs` — record permanece o mesmo

## Git workflow

- Branch: `advisor/001-ai-provider-safe-parsing`
- Commits: um commit por provider corrigido; mensagem no padrão do repo (ex.: `fix(ai): tratar resposta malformada no OpenAIProvider`)
- Não fazer push nem abrir PR, a menos que instruído.

## Passos

### Passo 1: Corrigir `OpenAIProvider.cs`

Substitua o bloco de leitura da resposta (linhas 33-38) pelo padrão seguro:

```csharp
var json = await response.Content.ReadFromJsonAsync<JsonElement>();

string content = string.Empty;
int tokens = 0;

if (json.TryGetProperty("choices", out var choices)
    && choices.GetArrayLength() > 0
    && choices[0].TryGetProperty("message", out var message)
    && message.TryGetProperty("content", out var contentEl))
{
    content = contentEl.GetString() ?? string.Empty;
}

if (json.TryGetProperty("usage", out var usage)
    && usage.TryGetProperty("completion_tokens", out var tokensEl))
{
    tokens = tokensEl.GetInt32();
}

return new AICompletionResult(content, tokens);
```

**Verificar**: `dotnet build Atendefy.slnx -c Release --no-restore` → exit 0

### Passo 2: Corrigir `AnthropicProvider.cs`

Substitua o bloco de leitura da resposta (linhas 28-32) pelo padrão seguro:

```csharp
var json = await response.Content.ReadFromJsonAsync<JsonElement>();

string content = string.Empty;
int tokens = 0;

if (json.TryGetProperty("content", out var contentArr)
    && contentArr.GetArrayLength() > 0
    && contentArr[0].TryGetProperty("text", out var textEl))
{
    content = textEl.GetString() ?? string.Empty;
}

if (json.TryGetProperty("usage", out var usage)
    && usage.TryGetProperty("output_tokens", out var tokensEl))
{
    tokens = tokensEl.GetInt32();
}

return new AICompletionResult(content, tokens);
```

**Verificar**: `dotnet build Atendefy.slnx -c Release --no-restore` → exit 0

### Passo 3: Adicionar testes de parsing defensivo

Em `tests/Atendefy.Tests/AI/OpenAIProviderTests.cs`, adicione dois testes seguindo o padrão
dos testes existentes no arquivo:

```csharp
[Fact]
public async Task CompleteAsync_WhenChoicesArrayIsEmpty_ReturnsEmptyContent()
{
    var responseJson = """{"choices":[],"usage":{"completion_tokens":0}}""";
    var handler = MockHttpMessageHandler.ReturnsJson(responseJson);
    var provider = new OpenAIProvider(new HttpClient(handler), "sk-test");

    var result = await provider.CompleteAsync(new AICompletionRequest(
        SystemPrompt: "Prompt.", Messages: [new ChatMessage("user", "oi")], Model: "gpt-4o-mini"));

    result.Content.Should().BeEmpty();
    result.TokensUsed.Should().Be(0);
}

[Fact]
public async Task CompleteAsync_WhenResponseIsMissingExpectedFields_ReturnsEmptyContent()
{
    var responseJson = """{"error":{"message":"Rate limit exceeded"}}""";
    var handler = MockHttpMessageHandler.ReturnsJson(responseJson);
    var provider = new OpenAIProvider(new HttpClient(handler), "sk-test");

    var result = await provider.CompleteAsync(new AICompletionRequest(
        SystemPrompt: "Prompt.", Messages: [new ChatMessage("user", "oi")], Model: "gpt-4o-mini"));

    result.Content.Should().BeEmpty();
    result.TokensUsed.Should().Be(0);
}
```

Em `tests/Atendefy.Tests/AI/AnthropicProviderTests.cs`, adicione os testes equivalentes:
- `CompleteAsync_WhenContentArrayIsEmpty_ReturnsEmptyContent`
- `CompleteAsync_WhenResponseIsMissingExpectedFields_ReturnsEmptyContent`

Use `"""{"content":[],"usage":{"output_tokens":0}}"""` e `"""{"type":"error","error":{"message":"overloaded"}}"""` como JSONs de teste.

**Verificar**: `dotnet test Atendefy.slnx -c Release --filter "FullyQualifiedName~AI"` → todos passam, incluindo os 4 novos testes

## Plano de testes

- Novos testes: 4 (2 por provider)
- Casos cobertos: array vazio (`choices: []`), campos ausentes, JSON de erro da API
- Padrão a seguir: `tests/Atendefy.Tests/AI/OpenAIProviderTests.cs` (usar `MockHttpMessageHandler.ReturnsJson`)
- Verificação final: `dotnet test Atendefy.slnx -c Release --filter "FullyQualifiedName~AI"` → todos passam

## Critérios de conclusão

- [ ] `dotnet build Atendefy.slnx -c Release --no-restore` exit 0
- [ ] `dotnet test Atendefy.slnx -c Release` exit 0, incluindo 4 novos testes
- [ ] Nenhum `GetProperty("choices")[0]` ou `GetProperty("content")[0]` nos dois providers
- [ ] Apenas os 4 arquivos em escopo foram modificados (`git status`)
- [ ] Status deste plano atualizado em `plans/README.md`

## Condições de PARE

- O código nas localizações de "Estado atual" não corresponde ao que está no arquivo (drift)
- Build falha após a mudança e a causa não é trivial (erro de digitação)
- O fix parece exigir mudança em `IAIProvider` ou `ConversationWorker`

## Notas de manutenção

- Se um terceiro provider for adicionado no futuro, aplicar o mesmo padrão `TryGetProperty` desde o início.
- Revisor do PR: confirmar que nenhum `GetProperty()` sem `Try` existe no novo código.
- Se no futuro o projeto adotar um cliente OpenAI SDK oficial, este parsing manual pode ser substituído pelo SDK — remover estes métodos na migração.
