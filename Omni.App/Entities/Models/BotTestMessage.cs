namespace Omni.App.Entities.Models;

/// <summary>One line of a bot test conversation (crm.bot_test_messages). Role is a BotTestRole.</summary>
public class BotTestMessage
{
    public Guid Id { get; set; }
    public Guid SessionId { get; set; }
    public Guid ClientId { get; set; }
    public string Role { get; set; } = Common.Enums.BotTestRole.Contact;
    public string Content { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}
