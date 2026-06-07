namespace Atendefy.API.Modules.Chatbot.Models;

public class Contact
{
    public string Phone { get; set; } = string.Empty;
    public string? Name { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
