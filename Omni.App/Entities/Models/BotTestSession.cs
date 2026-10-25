namespace Omni.App.Entities.Models;

/// <summary>A test conversation with a bot on the bot editor (crm.bot_test_sessions). It runs the real workflow (n8n) with
/// the bot's real instructions, Knowledge Base and tools, but write actions are simulated: the one booking the test contact
/// "has" lives here, never in scheduling.bookings. Older sessions are deleted when a new one starts (Bots:TestSessionHours).
/// </summary>
public class BotTestSession
{
    public Guid Id { get; set; }
    public Guid ClientId { get; set; }
    public Guid BotId { get; set; }

    /// <summary>The bot handed the conversation to the team: the test is over until it is reset.</summary>
    public bool HandedOff { get; set; }

    /// <summary>The test contact's simulated upcoming booking (null = none).</summary>
    public DateTimeOffset? SimulatedBookingStart { get; set; }
    public DateTimeOffset? SimulatedBookingEnd { get; set; }
    public Guid? SimulatedServiceId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
