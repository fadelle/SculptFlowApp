using Omni.App.Business.Contracts.Services.Ai;
using Omni.App.Business.Contracts.Services.Billing;
using Omni.App.Common.Enums;
using Omni.App.Common.Helpers;
using Omni.App.Entities.Dtos.Ai;
using Omni.App.Entities.Models;
using Omni.App.Entities.Requests.Contacts;
using Omni.App.Persistence.Contracts.Ai;

namespace Omni.App.Business.Services.Ai;

public class BotRuntimeService : IBotRuntimeService
{
    private readonly IBotRepository _bots;
    private readonly IBotTestRepository _tests;
    private readonly IEntitlementService _entitlements;

    public BotRuntimeService(IBotRepository bots, IBotTestRepository tests, IEntitlementService entitlements)
    {
        _bots = bots;
        _tests = tests;
        _entitlements = entitlements;
    }

    public async Task<AiBotConfig?> ConfigAsync(Guid clientId, Guid botId, CancellationToken ct = default)
    {
        var bot = (await _bots.ListForClientReadOnlyAsync(clientId, ct)).FirstOrDefault(b => b.Id == botId);
        if (bot is null) return null;

        var capabilities = await UsableCapabilitiesAsync(clientId, bot, ct);
        var instructions = BotInstructions.Compose(bot.Name, bot.Description, BotRules.Instructions(bot) ?? string.Empty, capabilities,
            bot.HandoffPolicy, bot.Language);
        return new AiBotConfig(bot.Id, bot.Name, bot.Template, instructions, capabilities, bot.HandoffPolicy, bot.Language);
    }

    public async Task<BotToolCheck> AuthorizeAsync(Guid clientId, Guid? botId, Guid? testSessionId, string? capability, CancellationToken ct = default)
    {
        if (clientId == Guid.Empty || botId is not { } id) return BotToolCheck.Denied(BotToolCheck.UnknownBot);
        var bots = await _bots.ListForClientReadOnlyAsync(clientId, ct);
        // Client isolation: the bot must be the calling client's own, so a tool call can never reach another client's data.
        var bot = bots.FirstOrDefault(b => b.Id == id);
        if (bot is null) return BotToolCheck.Denied(BotToolCheck.UnknownBot);

        if (testSessionId is { } sessionId)
        {
            // A test conversation may run a draft or inactive bot, but only the bot it was started with.
            var session = await _tests.GetSessionAsync(clientId, sessionId, ct);
            if (session is null || session.BotId != bot.Id) return BotToolCheck.Denied(BotToolCheck.UnknownTestSession);
        }
        else
        {
            // A live call: the bot must still answer (plan with the AI assistant, active, within max_ai_bots).
            var entitlements = await _entitlements.GetAsync(clientId, ct);
            if (!entitlements.CanUseAiAssistant || !BotRules.AnsweringBotIds(bots, entitlements.MaximumAiBots).Contains(bot.Id))
                return BotToolCheck.Denied(BotToolCheck.BotNotActive);
        }

        var capabilities = BotRules.Capabilities(bot).ToHashSet(StringComparer.Ordinal);
        if (capability is not null && !capabilities.Contains(capability)) return BotToolCheck.Denied(BotToolCheck.NotAllowed);
        return BotToolCheck.Allowed(new BotRunContext(clientId, bot.Id, capabilities, BotRules.KnowledgeFilter(bot), bot.HandoffMessage, testSessionId));
    }

    public (UpdateContactContextRequest Request, string? MissingCapability) CheckContactUpdate(BotRunContext run, UpdateContactContextRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var clean = request with
        {
            PreferredLanguage = Blank(request.PreferredLanguage),
            City = Blank(request.City),
            DesiredTimeline = Blank(request.DesiredTimeline),
            Notes = Blank(request.Notes),
            Qualification = Blank(request.Qualification),
            Stage = Blank(request.Stage)
        };
        var details = clean.ServiceId is not null || clean.City is not null || clean.DesiredTimeline is not null || clean.Notes is not null
            || clean.NextFollowupAt is not null || clean.Qualification is not null;
        var missing = details && !run.Allows(BotCapability.ContactUpdate) ? BotCapability.ContactUpdate
            : clean.Stage is not null && !run.Allows(BotCapability.Pipeline) ? BotCapability.Pipeline
            : null;
        return (clean, missing);
    }

    /// <summary>The bot's actions the plan lets it use right now: availability and bookings need the bookings feature.</summary>
    private async Task<IReadOnlyList<string>> UsableCapabilitiesAsync(Guid clientId, Bot bot, CancellationToken ct)
    {
        var capabilities = BotRules.Capabilities(bot);
        if (!capabilities.Any(BotCapability.NeedBookings.Contains)) return capabilities;
        var bookings = (await _entitlements.GetAsync(clientId, ct)).CanUseBookings;
        return bookings ? capabilities : capabilities.Where(c => !BotCapability.NeedBookings.Contains(c)).ToList();
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
