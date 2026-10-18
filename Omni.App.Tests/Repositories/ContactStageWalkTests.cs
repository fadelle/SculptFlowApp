using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Omni.App.Business.Managers;
using Omni.App.Business.Providers.Caching;
using Omni.App.Business.Services.Bookings;
using Omni.App.Business.Services.BusinessTypes;
using Omni.App.Business.Services.Contacts;
using Omni.App.Business.Services.Orders;
using Omni.App.Persistence.Contexts;
using Omni.App.Persistence.Repositories;
using Omni.App.Persistence.Repositories.Bookings;
using Omni.App.Persistence.Repositories.BusinessTypes;
using Omni.App.Persistence.Repositories.Catalog;
using Omni.App.Persistence.Repositories.Clients;
using Omni.App.Persistence.Repositories.Contacts;
using Omni.App.Persistence.Repositories.Orders;

namespace Omni.App.Tests.Repositories;

/// <summary>A contact walks the whole pipeline through the real services and the stages schema.sql seeds, once per built-in
/// business type: created (first open stage) → booking made (booked) → attended → order confirmed (converted). The code never
/// names a stage key, so the same walk lands on each type's own stages.</summary>
public class ContactStageWalkTests(PostgresFixture fixture) : RepoTestBase(fixture)
{
    private readonly CacheManager _cache = new(new MemoryCacheAdapter(TimeProvider.System), NullLogger<CacheManager>.Instance);
    private readonly Mock<IEventLogger> _events = new();
    private readonly Mock<ICatalogService> _catalog = new();

    private BusinessTypeService Types(ApplicationDbContext db) => new(new BusinessTypeRepository(db, _cache), new ClientRepository(db, _cache),
        new ContactRepository(db), new UnitOfWork(db), TextKit.Context(), TimeProvider.System);

    private ContactService Contacts(ApplicationDbContext db) =>
        new(new ContactRepository(db), new UnitOfWork(db), _events.Object, _catalog.Object, new ClientRepository(db, _cache), Types(db));

    private BookingService Bookings(ApplicationDbContext db) => new(new BookingRepository(db), new ContactRepository(db),
        new ClientRepository(db, _cache), new ServiceRepository(db, _cache), new UnitOfWork(db), _events.Object, _catalog.Object,
        new Mock<IAvailabilityService>().Object, new Mock<IInboxNotifier>().Object, new Mock<INotificationService>().Object,
        new Mock<ICalendarIntegrationService>().Object, new Mock<ICampaignRepository>().Object, new Mock<IConfigManager>().Object, Types(db), TextKit.Localizer());

    private OrderService Orders(ApplicationDbContext db) =>
        new(new OrderRepository(db), new ContactRepository(db), new UnitOfWork(db), _events.Object, _catalog.Object, Types(db));

    [PostgresTheory]
    [InlineData(TextKit.Clinic)]
    [InlineData(TextKit.General)]
    public async Task A_contact_walks_open_booked_attended_converted(string businessType)
    {
        var client = MakeClient(businessType: businessType);
        var service = MakeService(client.Id);
        await SeedAsync(client, service);

        ClientPipeline pipeline;
        await using (var db = NewContext()) pipeline = await Types(db).GetPipelineAsync(client.Id);
        Assert.Equal(businessType == TextKit.General ? 10 : 11, pipeline.Stages.Count);

        Guid contactId;
        await using (var db = NewContext())
        {
            var (contact, created) = await Contacts(db).CreateOrGetAsync(new CreateContactRequest(client.Id, service.Id, "Walker", null, null,
                "961700000", null, "website", null, null, null, "en", null, null, null, true));
            Assert.True(created);
            contactId = contact.Id;
        }
        await AssertStageAsync(contactId, pipeline, StageCategory.Open);

        Guid bookingId;
        await using (var db = NewContext())
        {
            bookingId = (await Bookings(db).CreateAsync(new CreateBookingRequest(client.Id, contactId, service.Id, "", DateTimeOffset.UtcNow.AddDays(2),
                null, "in_person", null, null))).Id;
        }
        await AssertStageAsync(contactId, pipeline, StageCategory.Booked);

        await using (var db = NewContext()) await Bookings(db).UpdateStatusAsync(client.Id, bookingId, BookingStatus.Attended);
        await AssertStageAsync(contactId, pipeline, StageCategory.Attended);

        await using (var db = NewContext())
        {
            var orders = Orders(db);
            var order = await orders.CreateAsync(new CreateOrderRequest(client.Id, contactId, service.Id, bookingId, 1000m, "USD", null));
            await orders.UpdateAsync(client.Id, order.Id, new UpdateOrderRequest(OrderStatus.Confirmed, null, null, null, null, null, null));
        }
        await AssertStageAsync(contactId, pipeline, StageCategory.Converted);
    }

    /// <summary>The stored key is the type's first stage of <paramref name="category"/>.</summary>
    private async Task AssertStageAsync(Guid contactId, ClientPipeline pipeline, string category)
    {
        await using var db = NewContext();
        var key = (await db.Contacts.AsNoTracking().SingleAsync(c => c.Id == contactId)).StageKey;
        Assert.Equal((category, pipeline.FirstOf(category)!.Key), (pipeline.CategoryOf(key), key));
    }
}
