namespace Atendefy.API.Modules.WhatsApp.Models;

public record CreateAccountRequest(
    string Provider,    // "meta" | "evolution"
    string Phone,
    string ConfigJson   // JSON com credenciais do provider
);
