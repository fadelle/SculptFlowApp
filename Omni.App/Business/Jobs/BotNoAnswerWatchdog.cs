using System.Diagnostics;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Services.Inbox;
using Omni.App.Common.Enums;
using Omni.App.Common.Helpers;
using Omni.App.Common.Statics;
using Omni.App.Entities.Dtos.Monitoring;
using Omni.App.Persistence.Contracts.PlatformAdmin;

namespace Omni.App.Business.Jobs;

/// <summary>
/// Once a minute, finds conversations still with the AI whose last customer message the bot was asked about and nothing came
/// of it (the trace outcome no_reply: no reply, no hand-off, no failure, no skip) for Ai / NoAnswerHandoffMinutes, and hands
/// each to the team (ConversationService.HandOverUnansweredAiAsync: a notice for the members) with a bot.no_answer step in the
/// message's own trace (QA t9-04: a dead n8n workflow answered 200 to the trigger and the customer heard nothing, with no
/// sign of it anywhere). The hand-off ends the AI mode, so a conversation is handed over once. 0 minutes = off.
/// </summary>
public sealed class BotNoAnswerWatchdog : BackgroundService
{
    /// <summary>How far back a waiting message is still looked at (older ones are left to the members' unanswered list).</summary>
    private static readonly TimeSpan LookBack = TimeSpan.FromHours(2);
    private const int MaxPerPass = 200;

    private readonly IServiceScopeFactory _scopes;
    private readonly IConfigManager _config;
    private readonly TimeProvider _time;
    private readonly ILogger<BotNoAnswerWatchdog> _logger;

    public BotNoAnswerWatchdog(IServiceScopeFactory scopes, IConfigManager config, TimeProvider time, ILogger<BotNoAnswerWatchdog> logger)
    {
        _scopes = scopes;
        _config = config;
        _time = time;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1), _time);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await RunOnceAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
                {
                    _logger.LogWarning(ex, "The bot no-answer check failed; it runs again in a minute.");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // stopping
        }
    }

    /// <summary>One pass; the number of conversations handed to the team.</summary>
    internal async Task<int> RunOnceAsync(CancellationToken ct)
    {
        var minutes = _config.AiNoAnswerHandoffMinutes;
        if (minutes <= 0) return 0;
        var now = _time.GetUtcNow();

        await using var scope = _scopes.CreateAsyncScope();
        var traces = scope.ServiceProvider.GetRequiredService<IMessageTraceAdminRepository>();
        var (rows, _) = await traces.ListUnansweredReadOnlyAsync(now - LookBack, now.AddMinutes(-minutes), null, 0, MaxPerPass, ct);
        var handed = 0;
        foreach (var row in rows.Where(r => r.Outcome == TraceResults.NoReply && r.Mode == ConversationMode.Ai && r.TraceId is not null))
        {
            // In the message's trace, so the admin's trace page shows why the conversation moved.
            using var trace = TraceContext.Continue($"00-{row.TraceId}-{ActivitySpanId.CreateRandom().ToHexString()}-01", "bot.no_answer");
            await using var work = _scopes.CreateAsyncScope();
            var tracer = work.ServiceProvider.GetRequiredService<IMessageTracer>();
            tracer.Step(TraceSteps.BotNoAnswer, TraceOutcomes.Failed, new TraceIds(row.ClientId, row.ConversationId, row.MessageId, row.Channel),
                detail: new { waitedMinutes = minutes });
            if (await work.ServiceProvider.GetRequiredService<IConversationService>().HandOverUnansweredAiAsync(row.ClientId, row.ConversationId, ct) == true)
            {
                handed++;
                _logger.LogWarning("The bot never answered conversation {ConversationId} of client {ClientId} ({Minutes} min): handed to the team.",
                    row.ConversationId, row.ClientId, minutes);
            }
        }
        return handed;
    }
}
