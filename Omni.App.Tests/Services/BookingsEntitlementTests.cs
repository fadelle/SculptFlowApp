using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Omni.App.Business.Services.Bookings;
using Omni.App.Controllers.Filters;
using Omni.App.Entities.Requests.Ai;
using Omni.App.Entities.Requests.Bookings;
using Omni.App.Entities.Responses.Bookings;
using Omni.App.Entities.Responses.Calendars;
using static Omni.App.Tests.Pages.PageKit;

namespace Omni.App.Tests.Services;

/// <summary>The plan's "bookings" feature: Inbox-only plans get no Bookings pages, bookings API, AI booking tools or calendar
/// sync; billing off (or a plan with bookings = true) changes nothing.</summary>
public class BookingsEntitlementTests
{
    private readonly Guid _clientId = Guid.NewGuid();
    private readonly Guid _contactId = Guid.NewGuid();

    private static readonly ClientEntitlements WithoutBookings =
        new(true, true, SubscriptionStatus.Active, "inbox", "Inbox", null, new Dictionary<string, string> { [EntitlementKeys.Campaigns] = "true" });

    private static readonly ClientEntitlements WithBookings =
        new(true, true, SubscriptionStatus.Active, "booking", "Inbox + Booking", null, new Dictionary<string, string> { [EntitlementKeys.Bookings] = "true" });

    // ---------------------------------------------------------------- catalog and entitlements

    [Fact]
    public void Bookings_is_a_plan_feature_off_unless_the_plan_sets_it()
    {
        var definition = EntitlementCatalog.Find(EntitlementKeys.Bookings);
        Assert.Equal((EntitlementKind.Feature, "Bookings"), (definition!.Kind, definition.Label));
        Assert.Equal("true", EntitlementCatalog.NormalizeValue(EntitlementKeys.Bookings, " TRUE "));
        Assert.Throws<ArgumentException>(() => EntitlementCatalog.NormalizeValue(EntitlementKeys.Bookings, "5"));

        Assert.False(WithoutBookings.CanUseBookings);
        Assert.True(WithBookings.CanUseBookings);
        Assert.True(ClientEntitlements.Unrestricted.CanUseBookings); // billing off: unchanged behaviour
        var expired = new ClientEntitlements(true, false, SubscriptionStatus.Expired, "booking", "B", null, new Dictionary<string, string> { [EntitlementKeys.Bookings] = "true" });
        Assert.False(expired.CanUseBookings);
    }

    // ---------------------------------------------------------------- RequireFeature filter

    private static ActionExecutingContext Context(IEntitlementService entitlements, Guid? signedInClient, Dictionary<string, object?> arguments)
    {
        var current = new Mock<ICurrentClientContext>();
        current.Setup(c => c.GetClientIdAsync(It.IsAny<CancellationToken>())).ReturnsAsync(signedInClient);
        var http = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddSingleton(entitlements).AddSingleton(current.Object).BuildServiceProvider()
        };
        return new ActionExecutingContext(new ActionContext(http, new RouteData(), new ControllerActionDescriptor()), [], arguments, new object());
    }

    private static async Task<(ActionExecutingContext Context, bool Ran)> RunAsync(RequireFeatureAttribute filter, ActionExecutingContext context)
    {
        var ran = false;
        await filter.OnActionExecutionAsync(context, () =>
        {
            ran = true;
            return Task.FromResult(new ActionExecutedContext(context, [], context.Controller));
        });
        return (context, ran);
    }

    [Fact]
    public async Task The_dashboard_bookings_apis_answer_422_without_bookings_in_the_plan()
    {
        var filter = new RequireFeatureAttribute(EntitlementKeys.Bookings);

        var denied = Context(EntitlementKit.With(WithoutBookings).Object, _clientId, []);
        var ex = await Assert.ThrowsAsync<EntitlementDeniedException>(() => RunAsync(filter, denied));
        Assert.Equal(EntitlementKeys.Bookings, ex.Entitlement);
        Assert.Equal(StatusCodes.Status422UnprocessableEntity, ApiErrorsAttribute.StatusFor(ex));

        Assert.True((await RunAsync(filter, Context(EntitlementKit.With(WithBookings).Object, _clientId, []))).Ran);
        Assert.True((await RunAsync(filter, Context(EntitlementKit.All().Object, _clientId, []))).Ran); // billing off
        // No client resolved: the action answers its own 403.
        Assert.True((await RunAsync(filter, Context(EntitlementKit.With(WithoutBookings).Object, null, []))).Ran);
    }

    [Fact]
    public async Task The_ai_booking_tools_answer_a_business_outcome_the_ai_can_follow()
    {
        var filter = new RequireFeatureAttribute(EntitlementKeys.Bookings) { AiTool = true };
        var entitlements = EntitlementKit.With(WithoutBookings);

        var (context, ran) = await RunAsync(filter, Context(entitlements.Object, null, new() { ["clientId"] = _clientId }));

        Assert.False(ran);
        var ok = Assert.IsType<OkObjectResult>(context.Result);
        var body = System.Text.Json.JsonSerializer.Serialize(ok.Value);
        Assert.Contains("\"success\":false", body);
        Assert.Contains(RequireFeatureAttribute.NotIncludedCode, body);
        Assert.Contains("handoff_to_human", body);
        entitlements.Verify(e => e.EnsureFeatureAsync(_clientId, EntitlementKeys.Bookings, It.IsAny<CancellationToken>()), Times.Once);

        Assert.True((await RunAsync(filter, Context(EntitlementKit.With(WithBookings).Object, null, new() { ["clientId"] = _clientId }))).Ran);
    }

    [Theory]
    [InlineData(typeof(Omni.App.Controllers.Client.BookingsController), null)]
    [InlineData(typeof(Omni.App.Controllers.Client.CalendarIntegrationsController), null)]
    [InlineData(typeof(Omni.App.Controllers.Integrations.AiController), "GetAvailableSlots")]
    [InlineData(typeof(Omni.App.Controllers.Integrations.AiController), "ScheduleBooking")]
    [InlineData(typeof(Omni.App.Controllers.Integrations.AiController), "BookSlot")]
    [InlineData(typeof(Omni.App.Controllers.Integrations.AiController), "GetMyBookings")]
    [InlineData(typeof(Omni.App.Controllers.Integrations.AiController), "RescheduleBooking")]
    [InlineData(typeof(Omni.App.Controllers.Integrations.AiController), "CancelBooking")]
    public void Every_bookings_endpoint_requires_the_feature(Type controller, string? action)
    {
        var attribute = action is null
            ? controller.GetCustomAttributes(typeof(RequireFeatureAttribute), true).Cast<RequireFeatureAttribute>().SingleOrDefault()
            : controller.GetMethod(action)!.GetCustomAttributes(typeof(RequireFeatureAttribute), true).Cast<RequireFeatureAttribute>().SingleOrDefault();
        Assert.NotNull(attribute);
        Assert.Equal(EntitlementKeys.Bookings, attribute!.FeatureKey);
        Assert.Equal(action is not null, attribute.AiTool);
    }

    [Theory]
    [InlineData("SearchKnowledge")]
    [InlineData("GetClientInfo")]
    [InlineData("HandoffToHuman")]
    public void The_other_ai_tools_stay_open(string action) =>
        Assert.Empty(typeof(Omni.App.Controllers.Integrations.AiController).GetMethod(action)!.GetCustomAttributes(typeof(RequireFeatureAttribute), true));

    // ---------------------------------------------------------------- BookingService

    private BookingService Bookings(Mock<IEntitlementService> entitlements, Mock<IUnitOfWork> uow, Mock<IBookingRepository> bookings) =>
        new(bookings.Object, new Mock<IContactRepository>().Object, new Mock<IClientRepository>().Object, new Mock<IServiceRepository>().Object,
            uow.Object, new Mock<IEventLogger>().Object, new Mock<ICatalogService>().Object, new Mock<IAvailabilityService>().Object,
            new Mock<IInboxNotifier>().Object, new Mock<INotificationService>().Object, new Mock<ICalendarIntegrationService>().Object,
            new Mock<ICampaignRepository>().Object, new Mock<IConfigManager>().Object, PipelineKit.Types().Object, TextKit.Localizer(), entitlements.Object);

    [Fact]
    public async Task Every_booking_change_is_refused_without_bookings_in_the_plan()
    {
        var uow = new Mock<IUnitOfWork>();
        var repo = new Mock<IBookingRepository>();
        var sut = Bookings(EntitlementKit.With(WithoutBookings), uow, repo);
        var request = new CreateBookingRequest(_clientId, _contactId, null, "", DateTimeOffset.UtcNow.AddDays(2), null, null, null, null);

        await Assert.ThrowsAsync<EntitlementDeniedException>(() => sut.CreateAsync(request));
        await Assert.ThrowsAsync<EntitlementDeniedException>(() => sut.BookAvailableSlotAsync(request));
        await Assert.ThrowsAsync<EntitlementDeniedException>(() => sut.UpdateStatusAsync(_clientId, Guid.NewGuid(), BookingStatus.Attended));
        await Assert.ThrowsAsync<EntitlementDeniedException>(() => sut.RescheduleAsync(_clientId, _contactId, null, DateTimeOffset.UtcNow.AddDays(3), null, null));
        await Assert.ThrowsAsync<EntitlementDeniedException>(() => sut.CancelAsync(_clientId, _contactId, null, null));
        await Assert.ThrowsAsync<EntitlementDeniedException>(() => sut.ScheduleAsync(_clientId, _contactId,
            new ScheduleBookingRequest(DateTimeOffset.UtcNow.AddDays(2))));
        await Assert.ThrowsAsync<EntitlementDeniedException>(() => sut.CancelBookingAsync(_clientId, _contactId, null, null));

        uow.Verify(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        repo.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Booking_reads_still_work_so_existing_data_stays_readable_where_it_is_shown()
    {
        var repo = new Mock<IBookingRepository>();
        var sut = Bookings(EntitlementKit.With(WithoutBookings), new Mock<IUnitOfWork>(), repo);
        Assert.Null(await sut.GetByIdAsync(_clientId, Guid.NewGuid()));
        repo.Verify(r => r.GetWithDetailsAsync(_clientId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ---------------------------------------------------------------- pages

    [Fact]
    public async Task The_bookings_pages_say_bookings_arent_included_instead_of_loading_them()
    {
        var client = SomeClient();
        var without = EntitlementKit.With(WithoutBookings);

        var calendarPage = new Omni.App.Pages.Dashboard.BookingsModel(ClientContext(client).Object, without.Object).Attach();
        await calendarPage.OnGetAsync(default);
        Assert.False(calendarPage.BookingsIncluded);

        var bookings = new Mock<IBookingService>();
        var detail = new Omni.App.Pages.Dashboard.BookingDetailModel(ClientContext(client).Object, bookings.Object, TextKit.Localizer(), without.Object) { Id = Guid.NewGuid() }.Attach();
        Assert.IsType<PageResult>(await detail.OnGetAsync(default)); // not a 404: the page explains why
        Assert.False(detail.BookingsIncluded);
        bookings.Verify(b => b.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);

        // A status change refused by the plan shows the message instead of failing.
        bookings.Setup(b => b.UpdateStatusAsync(client.Id, detail.Id, "attended", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new EntitlementDeniedException(EntitlementKeys.Bookings, EntitlementDeniedException.NotIncludedKey).WithArgs(TextArg.Of("billing.feature.bookings")));
        detail.Status = "attended";
        Assert.IsType<PageResult>(await detail.OnPostAsync(default));
        Assert.Equal("Bookings isn't included in your plan. See Settings → Billing.", detail.ErrorMessage);

        var calendar = new Mock<ICalendarIntegrationService>();
        var settings = new Omni.App.Pages.Settings.CalendarIntegrationsModel(ClientContext(client).Object, calendar.Object, TextKit.Localizer(), without.Object).Attach();
        await settings.OnGetAsync(default);
        Assert.False(settings.BookingsIncluded);
        calendar.Verify(c => c.ListAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);

        var with = new Omni.App.Pages.Dashboard.BookingsModel(ClientContext(client).Object, EntitlementKit.With(WithBookings).Object).Attach();
        await with.OnGetAsync(default);
        Assert.True(with.BookingsIncluded);
    }
}
