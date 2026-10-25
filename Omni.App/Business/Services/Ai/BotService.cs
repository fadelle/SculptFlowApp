using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Services.Ai;
using Omni.App.Business.Contracts.Services.Billing;
using Omni.App.Business.Contracts.Services.Knowledge;
using Omni.App.Business.Mappers.Ai;
using Omni.App.Common.Enums;
using Omni.App.Common.Exceptions;
using Omni.App.Common.Helpers;
using Omni.App.Common.Statics;
using Omni.App.Entities.Dtos.Ai;
using Omni.App.Entities.Models;
using Omni.App.Entities.Requests.Ai;
using Omni.App.Persistence.Contracts;
using Omni.App.Persistence.Contracts.Ai;
using Omni.App.Persistence.Contracts.Clients;
using Omni.App.Persistence.Contracts.Inbox;

namespace Omni.App.Business.Services.Ai;

public class BotService : IBotService
{
    private const int NameMaxLength = 100;        // crm.bots.name
    private const int DescriptionMaxLength = 500; // crm.bots.description

    private readonly IBotRepository _bots;
    private readonly IInboxSettingsRepository _inboxSettings;
    private readonly IClientRepository _clients;
    private readonly IEntitlementService _entitlements;
    private readonly IKnowledgeService _knowledge;
    private readonly IWebsiteSourceService _websites;
    private readonly IConfigManager _config;
    private readonly IUnitOfWork _unitOfWork;

    public BotService(IBotRepository bots, IInboxSettingsRepository inboxSettings, IClientRepository clients, IEntitlementService entitlements,
        IKnowledgeService knowledge, IWebsiteSourceService websites, IConfigManager config, IUnitOfWork unitOfWork)
    {
        _bots = bots;
        _inboxSettings = inboxSettings;
        _clients = clients;
        _entitlements = entitlements;
        _knowledge = knowledge;
        _websites = websites;
        _config = config;
        _unitOfWork = unitOfWork;
    }

    public async Task<BotListDto> ListAsync(Guid clientId, CancellationToken ct = default)
    {
        await EnsureClientAsync(clientId, ct);
        var bots = await _bots.ListForClientReadOnlyAsync(clientId, ct);
        var accounts = await AccountCountsAsync(clientId, ct);
        var entitlements = await _entitlements.GetAsync(clientId, ct);
        var limit = entitlements.MaximumAiBots;
        var answering = BotRules.AnsweringBotIds(bots, limit);
        var overLimit = bots.Where(b => b.Status == BotStatus.Active && !answering.Contains(b.Id)).Select(b => b.Id).ToHashSet();
        return new BotListDto(entitlements.CanUseAiAssistant, limit, bots.Count(b => b.Status == BotStatus.Active),
            bots.Select(b => BotMapper.ToDto(b, accounts.GetValueOrDefault(b.Id))).ToList(), overLimit);
    }

    public async Task<BotDto?> GetAsync(Guid clientId, Guid botId, CancellationToken ct = default)
    {
        var bot = (await _bots.ListForClientReadOnlyAsync(clientId, ct)).FirstOrDefault(b => b.Id == botId);
        return bot is null ? null : BotMapper.ToDto(bot, (await AccountCountsAsync(clientId, ct)).GetValueOrDefault(bot.Id));
    }

    public async Task<BotDto> CreateAsync(Guid clientId, string template, string name, CancellationToken ct = default)
    {
        if (!BotTemplate.All.Contains(template)) throw new ArgumentException("bots.errors.template");
        await EnsureClientAsync(clientId, ct);

        var now = DateTimeOffset.UtcNow;
        var bot = new Bot
        {
            Id = Guid.NewGuid(),
            ClientId = clientId,
            Name = CleanName(name),
            Template = template,
            Status = BotStatus.Draft,
            CreatedAt = now,
            UpdatedAt = now
        };
        _bots.Add(bot);
        await _unitOfWork.SaveChangesAsync(ct);
        return BotMapper.ToDto(bot, 0);
    }

    public async Task<BotDto> DuplicateAsCustomAsync(Guid clientId, Guid botId, string name, CancellationToken ct = default)
    {
        var source = await _bots.GetAsync(clientId, botId, ct) ?? throw new KeyNotFoundException("bots.errors.notFound");
        var now = DateTimeOffset.UtcNow;
        var copy = new Bot
        {
            Id = Guid.NewGuid(),
            ClientId = clientId,
            Name = CleanName(name),
            Description = source.Description,
            Template = BotTemplate.Custom,
            Instructions = BotRules.Instructions(source),
            Capabilities = BotRules.Capabilities(source).ToList(),
            KnowledgeScope = source.KnowledgeScope,
            Language = source.Language,
            HandoffPolicy = source.HandoffPolicy,
            HandoffMessage = source.HandoffMessage,
            Status = BotStatus.Draft,
            CreatedAt = now,
            UpdatedAt = now,
            KnowledgeSources = source.KnowledgeSources
                .Select(s => new BotKnowledgeSource { Id = Guid.NewGuid(), KnowledgeDocumentId = s.KnowledgeDocumentId, WebsiteSourceId = s.WebsiteSourceId })
                .ToList()
        };
        _bots.Add(copy);
        await _unitOfWork.SaveChangesAsync(ct);
        return BotMapper.ToDto(copy, 0);
    }

    public async Task<BotDto> SaveAsync(Guid clientId, Guid botId, SaveBotRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var bot = await _bots.GetAsync(clientId, botId, ct) ?? throw new KeyNotFoundException("bots.errors.notFound");

        var name = CleanName(request.Name);
        var description = Clean(request.Description);
        if ((description?.Length ?? 0) > DescriptionMaxLength) throw new ArgumentException("bots.errors.descriptionTooLong").WithArgs(DescriptionMaxLength);
        var scope = request.KnowledgeScope ?? BotKnowledgeScope.All;
        if (!BotKnowledgeScope.Values.Contains(scope)) throw new ArgumentException("bots.errors.knowledgeScope");
        var language = Clean(request.Language);
        if (language is not null && !BotLanguages.EnglishNames.ContainsKey(language)) throw new ArgumentException("bots.errors.language");
        var policy = request.HandoffPolicy ?? BotHandoffPolicy.OnRequestOrUnsure;
        if (!BotHandoffPolicy.All.Contains(policy)) throw new ArgumentException("bots.errors.handoffPolicy");
        var handoffMessage = Clean(request.HandoffMessage);
        var maxMessage = _config.BotsHandoffMessageMaxLength;
        if ((handoffMessage?.Length ?? 0) > maxMessage) throw new ArgumentException("bots.errors.handoffMessageTooLong").WithArgs(maxMessage);
        var status = request.Status ?? bot.Status;
        if (!BotStatus.All.Contains(status)) throw new ArgumentException("bots.errors.status");

        if (bot.Template == BotTemplate.Custom)
        {
            var instructions = Clean(request.Instructions) ?? throw new ArgumentException("bots.errors.instructionsRequired");
            var maxInstructions = _config.BotsInstructionsMaxLength;
            if (instructions.Length > maxInstructions) throw new ArgumentException("bots.errors.instructionsTooLong").WithArgs(maxInstructions);
            var capabilities = (request.Capabilities ?? []).Distinct(StringComparer.Ordinal).ToList();
            if (capabilities.Any(c => !BotCapability.All.Contains(c))) throw new ArgumentException("bots.errors.capability");
            bot.Instructions = instructions;
            bot.Capabilities = BotCapability.All.Where(capabilities.Contains).ToList();
        }

        await SetSourcesAsync(clientId, bot, scope, request.KnowledgeDocumentIds ?? [], request.WebsiteSourceIds ?? [], ct);
        bot.Name = name;
        bot.Description = description;
        bot.KnowledgeScope = scope;
        bot.Language = language;
        bot.HandoffPolicy = policy;
        bot.HandoffMessage = handoffMessage;
        await ApplyStatusAsync(clientId, bot, status, ct);
        bot.UpdatedAt = DateTimeOffset.UtcNow;

        // Tracked save: CacheInvalidationInterceptor drops the client's cached bots (and so every runtime decision about them).
        await _unitOfWork.SaveChangesAsync(ct);
        return BotMapper.ToDto(bot, (await AccountCountsAsync(clientId, ct)).GetValueOrDefault(bot.Id));
    }

    public async Task<BotDto> SetStatusAsync(Guid clientId, Guid botId, string status, CancellationToken ct = default)
    {
        if (!BotStatus.All.Contains(status)) throw new ArgumentException("bots.errors.status");
        var bot = await _bots.GetAsync(clientId, botId, ct) ?? throw new KeyNotFoundException("bots.errors.notFound");
        await ApplyStatusAsync(clientId, bot, status, ct);
        bot.UpdatedAt = DateTimeOffset.UtcNow;
        await _unitOfWork.SaveChangesAsync(ct);
        return BotMapper.ToDto(bot, (await AccountCountsAsync(clientId, ct)).GetValueOrDefault(bot.Id));
    }

    public async Task DeleteAsync(Guid clientId, Guid botId, CancellationToken ct = default)
    {
        var bot = await _bots.GetAsync(clientId, botId, ct) ?? throw new KeyNotFoundException("bots.errors.notFound");
        // The database would set bot_id null by itself; clearing it on tracked rows also clears the cached Inbox settings.
        var now = DateTimeOffset.UtcNow;
        foreach (var account in await _inboxSettings.ListForBotAsync(clientId, botId, ct))
        {
            account.BotId = null;
            account.UpdatedAt = now;
        }
        _bots.Remove(bot);
        await _unitOfWork.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<BotKnowledgeChoice>> ListKnowledgeChoicesAsync(Guid clientId, CancellationToken ct = default)
    {
        // Website pages are chosen through their website source, so their documents aren't offered one by one.
        var documents = (await _knowledge.ListAsync(clientId, ct))
            .Where(d => d.SourceType != KnowledgeSourceType.Website)
            .OrderBy(d => d.Title, StringComparer.CurrentCultureIgnoreCase)
            .Select(d => new BotKnowledgeChoice(d.Id, BotKnowledgeChoice.Document, d.Title, d.IsActive));
        var websites = (await _websites.ListAsync(clientId, ct))
            .OrderBy(w => w.Host, StringComparer.OrdinalIgnoreCase)
            .Select(w => new BotKnowledgeChoice(w.Id, BotKnowledgeChoice.Website, w.StartUrl, w.IsActive));
        return documents.Concat(websites).ToList();
    }

    /// <summary>Activation rules: the plan's AI assistant, a bot that is ready to run, and room under max_ai_bots.</summary>
    private async Task ApplyStatusAsync(Guid clientId, Bot bot, string status, CancellationToken ct)
    {
        if (status == BotStatus.Active && bot.Status != BotStatus.Active)
        {
            await _entitlements.EnsureFeatureAsync(clientId, EntitlementKeys.AiAssistant, ct);
            if (BotRules.NotReadyKey(bot) is { } notReady) throw new InvalidOperationException(notReady);
            var active = (await _bots.ListForClientReadOnlyAsync(clientId, ct)).Count(b => b.Status == BotStatus.Active && b.Id != bot.Id);
            await _entitlements.EnsureWithinLimitAsync(clientId, EntitlementKeys.MaxAiBots, active + 1, ct);
        }
        else if (status == BotStatus.Active && BotRules.NotReadyKey(bot) is { } notReady)
        {
            // An active bot saved without what it needs would answer badly; it must stay runnable.
            throw new InvalidOperationException(notReady);
        }
        bot.Status = status;
    }

    /// <summary>Replaces the selected sources (only the client's own documents and website sources are accepted).</summary>
    private async Task SetSourcesAsync(Guid clientId, Bot bot, string scope, IReadOnlyList<Guid> documentIds, IReadOnlyList<Guid> websiteIds,
        CancellationToken ct)
    {
        var wantedDocuments = scope == BotKnowledgeScope.Selected ? documentIds.Distinct().ToHashSet() : [];
        var wantedWebsites = scope == BotKnowledgeScope.Selected ? websiteIds.Distinct().ToHashSet() : [];
        if (scope == BotKnowledgeScope.Selected)
        {
            if (wantedDocuments.Count + wantedWebsites.Count == 0) throw new ArgumentException("bots.errors.sourcesRequired");
            var choices = await ListKnowledgeChoicesAsync(clientId, ct);
            var ownDocuments = choices.Where(c => c.Kind == BotKnowledgeChoice.Document).Select(c => c.Id).ToHashSet();
            var ownWebsites = choices.Where(c => c.Kind == BotKnowledgeChoice.Website).Select(c => c.Id).ToHashSet();
            if (!wantedDocuments.IsSubsetOf(ownDocuments) || !wantedWebsites.IsSubsetOf(ownWebsites))
                throw new ArgumentException("bots.errors.sourceNotFound");
        }

        foreach (var source in bot.KnowledgeSources.ToList())
        {
            var keep = source.KnowledgeDocumentId is { } d ? wantedDocuments.Remove(d) : source.WebsiteSourceId is { } w && wantedWebsites.Remove(w);
            if (keep) continue;
            bot.KnowledgeSources.Remove(source);
            _bots.RemoveSource(source);
        }
        bot.KnowledgeSources.AddRange(wantedDocuments.Select(d => new BotKnowledgeSource { Id = Guid.NewGuid(), BotId = bot.Id, KnowledgeDocumentId = d }));
        bot.KnowledgeSources.AddRange(wantedWebsites.Select(w => new BotKnowledgeSource { Id = Guid.NewGuid(), BotId = bot.Id, WebsiteSourceId = w }));
    }

    private async Task<Dictionary<Guid, int>> AccountCountsAsync(Guid clientId, CancellationToken ct) =>
        (await _inboxSettings.ListForClientReadOnlyAsync(clientId, ct))
            .Where(s => s.BotId is not null)
            .GroupBy(s => s.BotId!.Value)
            .ToDictionary(g => g.Key, g => g.Count());

    private async Task EnsureClientAsync(Guid clientId, CancellationToken ct) =>
        _ = await _clients.GetReadOnlyAsync(clientId, ct) ?? throw new KeyNotFoundException("clients.errors.notFound");

    private static string CleanName(string? name)
    {
        var clean = Clean(name) ?? throw new ArgumentException("bots.errors.nameRequired");
        if (clean.Length > NameMaxLength) throw new ArgumentException("bots.errors.nameTooLong").WithArgs(NameMaxLength);
        return clean;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
