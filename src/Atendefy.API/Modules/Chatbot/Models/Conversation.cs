namespace Atendefy.API.Modules.Chatbot.Models;

public class Conversation
{
    public Guid Id { get; set; }
    public string ContactPhone { get; set; } = string.Empty;
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public int MessageCount { get; set; }
    public bool IsDeleted { get; set; }
    public bool BotPaused { get; set; }
    public Guid? AccountId { get; set; }
    public List<ConversationMessage> Messages { get; set; } = [];
}
