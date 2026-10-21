using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Omni.App.Business.Managers;
using Omni.App.Business.Services.Bookings;
using Omni.App.Persistence.Contexts;
using Omni.App.Persistence.Repositories;
using Omni.App.Persistence.Repositories.Bookings;
using Omni.App.Persistence.Repositories.Clients;
using Omni.App.Persistence.Repositories.Contacts;
using Omni.App.Persistence.Repositories.Inbox;
using Omni.App.Persistence.Repositories.WhatsApp;

namespace Omni.App.Tests.Repositories;

/// <summary>Milestone C S5 on Postgres: which bookings are due, one row per (booking, reminder, start time) even when the
/// worker runs twice, the skip rules (made inside the window, superseded, opted out) and the WhatsApp / Telegram sends.</summary>
public class BookingReminderFlowTests(PostgresFixture fixture) : RepoTestBase(fixture)
{
    private readonly Mock<IMessageService> _messages = new();

    private BookingReminderService Sut(ApplicationDbContext db)
    {
        var config = new Mock<IConfigManager>();
        config.SetupGet(c => c.RemindersEnabled).Returns(true);
        config.SetupGet(c => c.RemindersBatchSize).Returns(50);
        config.SetupGet(c => c.LocalizationLanguages).Returns("en,ar,fr");
        var entitlements = new Mock<IEntitlementService>();
        entitlements.Setup(e => e.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(ClientEntitlements.Unrestricted);
        var terminology = new Mock<ITerminology>();
        terminology.Setup(t => t.Defaults(It.IsAny<string>())).Returns(TextKit.DefaultTerms());
        terminology.Setup(t => t.ForBusinessTypeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(TextKit.DefaultTerms());
        return new BookingReminderService(new BookingReminderRepository(db), new ContactRepository(db), new ConversationRepository(db),
            Mock.Of<IConversationService>(), new WhatsAppTemplateRepository(db), new ClientRepository(db), entitlements.Object, _messages.Object,
            new ReaderTextFactory(TextKit.Catalog, terminology.Object, config.Object), new UnitOfWork(db), config.Object, TimeProvider.System,
            NullLogger<BookingReminderService>.Instance);
    }

    private static Booking Book(Guid clientId, Guid contactId, TimeSpan from, TimeSpan made)
    {
        var start = DateTimeOffset.UtcNow.Add(from);
        var booking = MakeBooking(clientId, contactId, start);
        booking.CreatedAt = DateTimeOffset.UtcNow.Subtract(made);
        return booking;
    }

    private async Task<List<BookingReminder>> RowsAsync(Guid clientId)
    {
        await using var db = NewContext();
        return await db.BookingReminders.Where(r => r.ClientId == clientId).ToListAsync();
    }

    [PostgresFact]
    public async Task Due_reminders_are_sent_once_and_the_skip_rules_hold()
    {
        var client = MakeClient("Glow");
        client.Timezone = "Asia/Beirut";
        var ann = MakeContact(client.Id, "Ann", "+96170000001");
        var bob = MakeContact(client.Id, "Bob", "+96170000002");
        bob.MarketingOptIn = false;
        bob.OptedOutAt = DateTimeOffset.UtcNow;
        var template = new WhatsAppTemplate
        {
            Id = Guid.NewGuid(), ClientId = client.Id, Name = "booking_reminder", Category = "UTILITY", Language = "en", Status = WhatsAppTemplateStatus.Approved,
            Body = "Hi {{1}}, see you {{2}}.", CreatedAt = Now, UpdatedAt = Now
        };
        var annChat = MakeConversation(client.Id, ann.Id);
        var bobChat = MakeConversation(client.Id, bob.Id);
        var tomorrow = Book(client.Id, ann.Id, TimeSpan.FromHours(20), made: TimeSpan.FromDays(3));      // first due
        var soon = Book(client.Id, ann.Id, TimeSpan.FromHours(1), made: TimeSpan.FromDays(3));           // second due, first superseded
        var lastMinute = Book(client.Id, ann.Id, TimeSpan.FromHours(10), made: TimeSpan.FromHours(1));   // made inside the 24 h window
        var later = Book(client.Id, ann.Id, TimeSpan.FromDays(3), made: TimeSpan.FromDays(3));           // not due yet
        var optedOut = Book(client.Id, bob.Id, TimeSpan.FromHours(20), made: TimeSpan.FromDays(3));
        await SeedAsync(client, ann, bob, template);
        await SeedAsync(annChat, bobChat, tomorrow, soon, lastMinute, later, optedOut,
            new ReminderSettings { Id = Guid.NewGuid(), ClientId = client.Id, IsEnabled = true, FirstHoursBefore = 24, SecondHoursBefore = 2,
                TemplatesJson = $"{{\"en\":\"{template.Id}\"}}", CreatedAt = Now, UpdatedAt = Now });

        var sentValues = new List<IReadOnlyList<string>>();
        _messages.Setup(m => m.SendReminderTemplateAsync(client.Id, annChat.Id, template.Id, It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, Guid, Guid, IReadOnlyList<string>, CancellationToken>((_, _, _, values, _) => sentValues.Add(values))
            .ReturnsAsync((MessageResponse?)null);

        await using (var db = NewContext()) Assert.Equal(5, await Sut(db).SendDueAsync(client.Id));
        await using (var db = NewContext()) Assert.Equal(0, await Sut(db).SendDueAsync(client.Id)); // nothing twice

        var rows = await RowsAsync(client.Id);
        string? Outcome(Booking b, string kind) => rows.SingleOrDefault(r => r.BookingId == b.Id && r.Kind == kind) is { } r ? $"{r.Status}:{r.Reason}" : null;
        Assert.Equal("sent:", Outcome(tomorrow, ReminderKind.First));
        Assert.Null(Outcome(tomorrow, ReminderKind.Second));
        Assert.Equal("sent:", Outcome(soon, ReminderKind.Second));
        Assert.Equal($"skipped:{ReminderReason.Superseded}", Outcome(soon, ReminderKind.First));
        Assert.Equal($"skipped:{ReminderReason.MadeInsideWindow}", Outcome(lastMinute, ReminderKind.First));
        Assert.Null(Outcome(later, ReminderKind.First));
        Assert.Equal($"skipped:{ReminderReason.OptedOut}", Outcome(optedOut, ReminderKind.First));
        Assert.Equal(2, sentValues.Count);
        Assert.All(sentValues, v => Assert.Equal("Ann", v[0]));
        Assert.All(sentValues, v => Assert.Equal(2, v.Count)); // the template uses two values

        // A reschedule is a new start time: its reminder is due again.
        await using (var db = NewContext())
        {
            var moved = await db.Bookings.SingleAsync(b => b.Id == tomorrow.Id);
            moved.ScheduledStart = moved.ScheduledStart.AddHours(2);
            await db.SaveChangesAsync();
            Assert.Equal(1, await Sut(db).SendDueAsync(client.Id));
        }
        await using (var db = NewContext())
            Assert.Equal(2, (await new BookingReminderRepository(db).ListForBookingAsync(client.Id, tomorrow.Id)).Count);
    }

    [PostgresFact]
    public async Task Telegram_gets_text_in_the_contacts_language_and_missing_templates_are_explained()
    {
        var client = MakeClient("Glow");
        var leila = MakeContact(client.Id, "Leila", null);
        leila.PreferredLanguage = "ar";
        var sam = MakeContact(client.Id, "Sam", "+96170000003");
        var telegram = MakeConversation(client.Id, leila.Id, ConversationChannel.Telegram);
        var whatsApp = MakeConversation(client.Id, sam.Id);
        var a = Book(client.Id, leila.Id, TimeSpan.FromHours(5), made: TimeSpan.FromDays(2));
        var b = Book(client.Id, sam.Id, TimeSpan.FromHours(5), made: TimeSpan.FromDays(2));
        await SeedAsync(client, leila, sam);
        await SeedAsync(telegram, whatsApp, a, b,
            new ReminderSettings { Id = Guid.NewGuid(), ClientId = client.Id, IsEnabled = true, FirstHoursBefore = 24, SecondHoursBefore = 0, TemplatesJson = "{}",
                CreatedAt = Now, UpdatedAt = Now });
        string? text = null;
        _messages.Setup(m => m.SendSystemTextAsync(client.Id, telegram.Id, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, Guid, string, CancellationToken>((_, _, t, _) => text = t).ReturnsAsync((MessageResponse?)null);

        await using (var db = NewContext()) Assert.Equal(2, await Sut(db).SendDueAsync(client.Id));

        Assert.StartsWith("تذكير من Glow: موعدك يوم", text);
        var rows = await RowsAsync(client.Id);
        Assert.Equal((ReminderStatus.Sent, ConversationChannel.Telegram), (rows.Single(r => r.BookingId == a.Id).Status, rows.Single(r => r.BookingId == a.Id).Channel));
        Assert.Equal((ReminderStatus.Skipped, ReminderReason.NoTemplate), (rows.Single(r => r.BookingId == b.Id).Status, rows.Single(r => r.BookingId == b.Id).Reason));
    }

    [PostgresFact]
    public async Task A_failed_send_is_recorded_not_retried()
    {
        var client = MakeClient("Glow");
        var ann = MakeContact(client.Id, "Ann", null);
        var chat = MakeConversation(client.Id, ann.Id, ConversationChannel.Telegram);
        var booking = Book(client.Id, ann.Id, TimeSpan.FromHours(3), made: TimeSpan.FromDays(2));
        await SeedAsync(client, ann);
        await SeedAsync(chat, booking, new ReminderSettings { Id = Guid.NewGuid(), ClientId = client.Id, IsEnabled = true, FirstHoursBefore = 24,
            SecondHoursBefore = 0, TemplatesJson = "{}", CreatedAt = Now, UpdatedAt = Now });
        _messages.Setup(m => m.SendSystemTextAsync(client.Id, chat.Id, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ChannelSendException("Telegram said no"));

        await using (var db = NewContext()) Assert.Equal(1, await Sut(db).SendDueAsync(client.Id));
        await using (var db = NewContext()) Assert.Equal(0, await Sut(db).SendDueAsync(client.Id));

        var row = Assert.Single(await RowsAsync(client.Id));
        Assert.Equal((ReminderStatus.Failed, ReminderReason.SendFailed), (row.Status, row.Reason));
    }
}
