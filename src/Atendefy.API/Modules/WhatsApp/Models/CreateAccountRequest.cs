namespace Atendefy.API.Modules.WhatsApp.Models;

public record CreateAccountRequest(
    string Provider,     // "meta" | "evolution"
    string Phone,
    string? ConfigJson   // credenciais do provider (obrigatório p/ meta; ignorado p/ evolution — montado no servidor)
);
