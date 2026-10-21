using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Omni.App.Business.Managers;
using Omni.App.Business.Services.Contacts;
using Omni.App.Persistence.Repositories;
using Omni.App.Persistence.Repositories.Clients;
using Omni.App.Persistence.Repositories.Contacts;
using Omni.App.Persistence.Repositories.Events;

namespace Omni.App.Tests.Repositories;

/// <summary>Milestone C S4 on Postgres: the export holds every related row; the delete removes the contact with its
/// conversations, messages, bookings and orders, deletes its notifications, keeps other contacts and unlinks events.</summary>
public class ContactDataFlowTests(PostgresFixture fixture) : RepoTestBase(fixture)
{
    [PostgresFact]
    public async Task Export_then_delete_a_contact_and_everything_about_them()
    {
        var client = MakeClient("Glow");
        var service = MakeService(client.Id, "Facial");
        var ann = MakeContact(client.Id, "Ann Lee", "+96170000001", service.Id);
        var bob = MakeContact(client.Id, "Bob", "+96170000002");
        var conversation = MakeConversation(client.Id, ann.Id);
        var otherConversation = MakeConversation(client.Id, bob.Id);
        var booking = MakeBooking(client.Id, ann.Id, Now.AddDays(2), serviceId: service.Id);
        await SeedAsync(client, service, ann, bob);
        await SeedAsync(conversation, otherConversation, booking,
            MakeMessage(conversation, content: "Hello"), MakeMessage(conversation, MessageDirection.Outbound, MessageOrigin.Dashboard, "Hi Ann", Now.AddMinutes(1)),
            MakeMessage(otherConversation, content: "Bob here"),
            new Order { Id = Guid.NewGuid(), ClientId = client.Id, ContactId = ann.Id, ServiceId = service.Id, Status = "quoted", QuotedAmount = 120m, CreatedAt = Now, UpdatedAt = Now },
            new EventLog { Id = Guid.NewGuid(), ClientId = client.Id, ContactId = ann.Id, EventType = EventTypes.ContactCreated, Metadata = "{}", CreatedAt = Now },
            new Notification { Id = Guid.NewGuid(), ClientId = client.Id, ContactId = ann.Id, Type = NotificationType.NewContact, Title = "notifications.newContact.title",
                TextArgs = ["Ann Lee"], CreatedAt = Now });

        await using (var db = NewContext())
        {
            var sut = new ContactDataService(new ContactRepository(db), new ContactDataRepository(db), new ClientRepository(db),
                new EventLogger(new EventLogRepository(db)), new UnitOfWork(db), TimeProvider.System, NullLogger<ContactDataService>.Instance);
            var owner = new UserAccess("owner", client.Id, UserRole.Client, null, null, null);

            var export = (await sut.ExportAsync(owner, ann.Id))!;
            Assert.Equal(("Glow", "Ann Lee"), (export.ClientName, export.Contact.FullName));
            Assert.Equal(["Hello", "Hi Ann"], export.Messages.Select(m => m.Content));
            Assert.Equal(conversation.Id, Assert.Single(export.Conversations).Id);
            Assert.Equal("Facial", Assert.Single(export.Bookings).ServiceName);
            Assert.Equal(120m, Assert.Single(export.Orders).QuotedAmount);
            Assert.Equal(EventTypes.ContactCreated, Assert.Single(export.Activity).EventType);

            Assert.True(await sut.DeleteAsync(owner, ann.Id, "ann lee"));
        }

        await using var check = NewContext();
        Assert.False(await check.Contacts.AnyAsync(c => c.Id == ann.Id));
        Assert.False(await check.Conversations.AnyAsync(c => c.ContactId == ann.Id));
        Assert.False(await check.Messages.AnyAsync(m => m.ContactId == ann.Id));
        Assert.False(await check.Bookings.AnyAsync(b => b.ContactId == ann.Id));
        Assert.False(await check.Orders.AnyAsync(o => o.ContactId == ann.Id));
        Assert.False(await check.Notifications.AnyAsync(n => n.ClientId == client.Id));
        Assert.True(await check.Contacts.AnyAsync(c => c.Id == bob.Id));
        Assert.Equal(1, await check.Messages.CountAsync(m => m.ConversationId == otherConversation.Id));
        var events = await check.Events.Where(e => e.ClientId == client.Id).ToListAsync();
        Assert.All(events, e => Assert.Null(e.ContactId));
        Assert.Contains(events, e => e.EventType == EventTypes.ContactDeleted);
    }
}
