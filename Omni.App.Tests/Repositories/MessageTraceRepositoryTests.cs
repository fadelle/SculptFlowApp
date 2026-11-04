using Microsoft.EntityFrameworkCore;
using Omni.App.Persistence.Repositories.Monitoring;
using Omni.App.Persistence.Repositories.PlatformAdmin;

namespace Omni.App.Tests.Repositories;

/// <summary>activity.message_trace_steps and the trace view's reads. Trace ids are random per test, so rows never clash.</summary>
public class MessageTraceRepositoryTests(PostgresFixture fixture) : RepoTestBase(fixture)
{
    private static string NewTrace() => Guid.NewGuid().ToString("N");

    private static MessageTraceStep Step(string traceId, DateTimeOffset at, string step = TraceSteps.MessageSaved, string outcome = TraceOutcomes.Ok,
        Guid? clientId = null, Guid? conversationId = null, Guid? messageId = null, string? externalId = null, string detail = "{}") => new()
    {
        TraceId = traceId, OccurredAt = at, Step = step, Outcome = outcome, ClientId = clientId, ConversationId = conversationId,
        MessageId = messageId, ExternalMessageId = externalId, Channel = "whatsapp", Detail = detail
    };

    [PostgresFact]
    public async Task Purge_keeps_failed_traces_until_the_failed_retention()
    {
        var cutoff = new DateTimeOffset(1991, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var failedCutoff = cutoff.AddDays(-30);
        var (ok, failedRecent, failedOld) = (NewTrace(), NewTrace(), NewTrace());
        await SeedAsync(
            Step(ok, cutoff.AddDays(-1)),
            Step(ok, cutoff.AddDays(1)), // inside retention: stays
            Step(failedRecent, cutoff.AddDays(-2)),
            Step(failedRecent, cutoff.AddDays(-2), TraceSteps.SendProviderFailed, TraceOutcomes.Failed),
            Step(failedOld, failedCutoff.AddDays(-1)),
            Step(failedOld, failedCutoff.AddDays(-1), TraceSteps.BotTriggerFailed, TraceOutcomes.Failed));

        await using (var db = NewContext())
        {
            Assert.True(await new MessageTraceStepRepository(db).DeleteExpiredAsync(cutoff, failedCutoff) >= 3);
        }

        await using var read = NewContext();
        var left = await read.MessageTraceSteps.AsNoTracking().Where(s => s.TraceId == ok || s.TraceId == failedRecent || s.TraceId == failedOld).ToListAsync();
        Assert.Equal(1, left.Count(s => s.TraceId == ok));
        Assert.Equal(2, left.Count(s => s.TraceId == failedRecent));
        Assert.DoesNotContain(left, s => s.TraceId == failedOld);
    }

    [PostgresFact]
    public async Task A_trace_reads_its_steps_messages_events_and_logs_and_messages_resolve_to_it()
    {
        var client = MakeClient();
        var contact = MakeContact(client.Id);
        var conversation = MakeConversation(client.Id, contact.Id);
        var trace = NewTrace();
        var t = new DateTimeOffset(2098, 5, 1, 9, 0, 0, TimeSpan.Zero);
        var inbound = MakeMessage(conversation, createdAt: t, externalId: "wamid." + trace);
        inbound.TraceId = trace;
        var named = MakeMessage(conversation, MessageDirection.Outbound, MessageOrigin.Ai, createdAt: t.AddSeconds(5)); // no trace id, named by a step
        var other = MakeMessage(conversation, createdAt: t.AddSeconds(1));
        await SeedAsync(client, contact, conversation, inbound, named, other,
            Step(trace, t.AddSeconds(1), TraceSteps.SendProviderAccepted, clientId: client.Id, conversationId: conversation.Id, messageId: named.Id, externalId: "out." + trace),
            Step(trace, t, TraceSteps.MessageSaved, clientId: client.Id, conversationId: conversation.Id, messageId: inbound.Id),
            new EventLog { Id = Guid.NewGuid(), ClientId = client.Id, ConversationId = conversation.Id, EventType = "contact_message_received", Source = "whatsapp_customer", Metadata = "{}", CreatedAt = t },
            new EventLog { Id = Guid.NewGuid(), ClientId = client.Id, ConversationId = conversation.Id, EventType = "too_late", Metadata = "{}", CreatedAt = t.AddHours(2) },
            new AppLog { OccurredAt = t.AddSeconds(2), Level = "Error", Category = "X", Message = "boom", Fingerprint = "f", TraceId = trace, Properties = "{}" });

        await using var db = NewContext();
        var repo = new MessageTraceAdminRepository(db);

        var steps = await repo.StepsOfTraceReadOnlyAsync(trace);
        Assert.Equal([TraceSteps.MessageSaved, TraceSteps.SendProviderAccepted], steps.Select(s => s.Step));

        var messages = await repo.MessagesReadOnlyAsync(trace, [named.Id]);
        Assert.Equal([inbound.Id, named.Id], messages.Select(m => m.Id));
        Assert.Equal((trace, (string?)null), (messages[0].TraceId, messages[1].TraceId));

        var events = await repo.EventsReadOnlyAsync([conversation.Id], t.AddMinutes(-1), t.AddMinutes(10));
        Assert.Equal("contact_message_received", Assert.Single(events).EventType);
        Assert.Equal("boom", Assert.Single(await repo.LogsOfTraceReadOnlyAsync(trace)).Message);
        Assert.Empty(await repo.BillingReadOnlyAsync([inbound.Id]));

        Assert.Equal(trace, await repo.TraceIdOfMessageReadOnlyAsync(inbound.Id)); // its own column
        Assert.Equal(trace, await repo.TraceIdOfMessageReadOnlyAsync(named.Id));   // through a step
        Assert.Null(await repo.TraceIdOfMessageReadOnlyAsync(other.Id));
        Assert.Equal(trace, await repo.TraceIdOfExternalMessageReadOnlyAsync("wamid." + trace));
        Assert.Equal(trace, await repo.TraceIdOfExternalMessageReadOnlyAsync("out." + trace));
        Assert.Null(await repo.TraceIdOfExternalMessageReadOnlyAsync("nothing." + trace));
    }

    [PostgresFact]
    public async Task Each_trace_gets_its_outcome_and_the_list_names_its_conversation()
    {
        // QA t7-10: a dropped reply or a skipped photo looked successful; conversationId was always null.
        var client = MakeClient();
        var contact = MakeContact(client.Id);
        var conversation = MakeConversation(client.Id, contact.Id);
        var t = DateTimeOffset.UtcNow.AddMinutes(-30);
        var (replied, skipped, handoff, silent, failed, recent) = (NewTrace(), NewTrace(), NewTrace(), NewTrace(), NewTrace(), NewTrace());
        await SeedAsync(client, contact, conversation,
            Step(replied, t, TraceSteps.WebhookReceived),
            Step(replied, t, TraceSteps.MessageSaved, clientId: client.Id),
            Step(replied, t.AddSeconds(1), TraceSteps.BotTriggered, clientId: client.Id, conversationId: conversation.Id),
            Step(replied, t.AddSeconds(5), TraceSteps.SendProviderAccepted, clientId: client.Id, conversationId: conversation.Id),
            Step(skipped, t, TraceSteps.MessageSaved, clientId: client.Id, conversationId: conversation.Id),
            Step(skipped, t, TraceSteps.BotSkipped, TraceOutcomes.Skipped, client.Id, conversation.Id, detail: """{"reason":"message_type_not_supported","type":"image"}"""),
            Step(handoff, t, TraceSteps.BotRunStarted, clientId: client.Id, conversationId: conversation.Id),
            Step(handoff, t.AddSeconds(2), TraceSteps.BotActionResult, clientId: client.Id, conversationId: conversation.Id, detail: """{"action":"handoff","status":200}"""),
            Step(silent, t, TraceSteps.MessageSaved, clientId: client.Id, conversationId: conversation.Id),
            Step(silent, t.AddSeconds(1), TraceSteps.BotTriggered, clientId: client.Id, conversationId: conversation.Id),
            Step(failed, t, TraceSteps.BotRunStarted, clientId: client.Id, conversationId: conversation.Id),
            Step(failed, t.AddSeconds(3), TraceSteps.SendProviderFailed, TraceOutcomes.Failed, client.Id, conversation.Id),
            Step(recent, DateTimeOffset.UtcNow, TraceSteps.BotTriggered, clientId: client.Id, conversationId: conversation.Id));

        await using var db = NewContext();
        var repo = new MessageTraceAdminRepository(db);
        var outcomes = await repo.OutcomesReadOnlyAsync([replied, skipped, handoff, silent, failed, recent], DateTimeOffset.UtcNow);

        Assert.Equal((TraceResults.Replied, (string?)null), outcomes[replied]);
        Assert.Equal((TraceResults.Skipped, "message_type_not_supported"), outcomes[skipped]);
        Assert.Equal(TraceResults.HandedOff, outcomes[handoff].Outcome);
        Assert.Equal(TraceResults.NoReply, outcomes[silent].Outcome);
        Assert.Equal((TraceResults.Failed, TraceSteps.SendProviderFailed), outcomes[failed]);
        Assert.Equal(TraceResults.InProgress, outcomes[recent].Outcome);

        var (rows, _) = await repo.ListReadOnlyAsync(new MessageTraceFilter(t.AddMinutes(-1), client.Id, null, false), 0, 20);
        var row = rows.Single(r => r.TraceId == replied);
        Assert.Equal((conversation.Id, TraceResults.Replied), (row.ConversationId!.Value, row.Outcome));   // its first named step has no conversation
    }

    [PostgresFact]
    public async Task Conversations_left_without_an_answer_are_listed_with_what_their_last_message_came_to()
    {
        var client = MakeClient();
        var (waiting, answered, fresh) = (MakeContact(client.Id), MakeContact(client.Id), MakeContact(client.Id));
        var t = DateTimeOffset.UtcNow.AddMinutes(-40);
        Conversation Conv(Contact contact, string direction, DateTimeOffset at)
        {
            var c = MakeConversation(client.Id, contact.Id);
            c.LastMessageDirection = direction; c.LastMessageAt = at;
            return c;
        }
        var c1 = Conv(waiting, MessageDirection.Inbound, t);
        var c2 = Conv(answered, MessageDirection.Outbound, t.AddMinutes(1));
        var c3 = Conv(fresh, MessageDirection.Inbound, DateTimeOffset.UtcNow.AddMinutes(-2));   // still inside the 15 minutes
        var trace = NewTrace();
        var photo = MakeMessage(c1, createdAt: t);
        photo.MessageType = "image"; photo.TraceId = trace;
        await SeedAsync(client, waiting, answered, fresh, c1, c2, c3, photo,
            Step(trace, t, TraceSteps.BotSkipped, TraceOutcomes.Skipped, client.Id, c1.Id, detail: """{"reason":"message_type_not_supported"}"""));

        await using var db = NewContext();
        var (rows, total) = await new MessageTraceAdminRepository(db)
            .ListUnansweredReadOnlyAsync(t.AddHours(-1), DateTimeOffset.UtcNow.AddMinutes(-15), client.Id, 0, 20);

        Assert.Equal(1, total);
        var row = Assert.Single(rows);
        Assert.Equal((c1.Id, photo.Id, "image", trace, TraceResults.Skipped, "message_type_not_supported", client.Name),
            (row.ConversationId, row.MessageId!.Value, row.MessageType, row.TraceId, row.Outcome, row.OutcomeReason, row.ClientName));
    }

    [PostgresFact]
    public async Task Ai_usage_adds_up_runs_model_calls_and_tokens_per_client_and_the_ai_reply_revenue()
    {
        var client = MakeClient();
        var account = new BillingAccount { Id = Guid.NewGuid(), ClientId = client.Id, CreatedAt = Now, UpdatedAt = Now };
        var (run1, run2) = (NewTrace(), NewTrace());
        var t = DateTimeOffset.UtcNow.AddHours(-1);
        BillingUsageRecord Reply(string status, decimal amount, decimal refunded = 0) => new()
        {
            Id = Guid.NewGuid(), ClientId = client.Id, BillingAccountId = account.Id, IdempotencyKey = Guid.NewGuid().ToString("N"),
            EventType = BillableEventTypes.AiReply, Channel = "whatsapp", Quantity = 1, ChargeStatus = status, ProviderOutcome = ProviderOutcome.Billable,
            Amount = amount, RefundedAmount = refunded, WalletAmount = status == ChargeStatus.Settled ? amount : 0, Currency = "USD",
            ProviderBilling = ProviderBillingResponsibility.PlatformFunded, OccurredAt = t, CreatedAt = t, UpdatedAt = t
        };
        await SeedAsync(client, account,
            Step(run1, t, TraceSteps.BotRunStarted, clientId: client.Id),
            Step(run1, t, TraceSteps.BotModelCalled, clientId: client.Id, detail: """{"inputTokens":1000,"outputTokens":200}"""),
            Step(run1, t, TraceSteps.BotModelCalled, clientId: client.Id, detail: """{"inputTokens":500,"outputTokens":100}"""),
            Step(run2, t, TraceSteps.BotRunStarted, clientId: client.Id),
            Step(run2, t, TraceSteps.BotModelCalled, TraceOutcomes.Failed, client.Id, detail: """{"error":"timeout"}"""),
            Step(run2, t, TraceSteps.BotRunFailed, TraceOutcomes.Failed, client.Id),
            Reply(ChargeStatus.Settled, 0.02m), Reply(ChargeStatus.Settled, 0.02m, refunded: 0.02m), Reply(ChargeStatus.Released, 0.02m));

        await using var db = NewContext();
        var repo = new MessageTraceAdminRepository(db);
        var usage = Assert.Single(await repo.AiUsageReadOnlyAsync(t.AddMinutes(-1), client.Id));
        Assert.Equal((client.Name, 2L, 1L, 2L, 1500L, 300L), (usage.ClientName, usage.Runs, usage.FailedRuns, usage.ModelCalls, usage.InputTokens, usage.OutputTokens));
        var revenue = Assert.Single(await repo.AiReplyRevenueReadOnlyAsync(t.AddMinutes(-1), client.Id));
        Assert.Equal((0.02m, "USD"), (revenue.Amount, revenue.Currency.Trim()));
        Assert.Empty(await repo.AiUsageReadOnlyAsync(t.AddMinutes(-1), Guid.NewGuid()));
    }

    [PostgresFact]
    public async Task The_list_summarises_traces_and_filters_by_client_conversation_and_failure()
    {
        var clientId = Guid.NewGuid();
        var conversationId = Guid.NewGuid();
        var t = new DateTimeOffset(2099, 7, 1, 9, 0, 0, TimeSpan.Zero);
        var (good, failed, errored, otherClient) = (NewTrace(), NewTrace(), NewTrace(), NewTrace());
        await SeedAsync(
            Step(good, t, TraceSteps.WebhookReceived),
            Step(good, t.AddSeconds(1), clientId: clientId, conversationId: conversationId),
            Step(failed, t.AddMinutes(1), TraceSteps.SendRequested, clientId: clientId, conversationId: Guid.NewGuid()),
            Step(failed, t.AddMinutes(1).AddSeconds(1), TraceSteps.SendProviderFailed, TraceOutcomes.Failed, clientId, detail: "{\"error\":\"x\"}"),
            Step(errored, t.AddMinutes(2), clientId: clientId),
            new AppLog { OccurredAt = t.AddMinutes(2), Level = "Critical", Category = "X", Message = "boom", Fingerprint = "f", TraceId = errored, Properties = "{}" },
            new AppLog { OccurredAt = t.AddMinutes(2), Level = "Warning", Category = "X", Message = "meh", Fingerprint = "f", TraceId = good, Properties = "{}" },
            Step(otherClient, t.AddMinutes(3), clientId: Guid.NewGuid()));

        await using var db = NewContext();
        var repo = new MessageTraceAdminRepository(db);
        var from = t.AddSeconds(-1);

        var (rows, total) = await repo.ListReadOnlyAsync(new MessageTraceFilter(from, clientId), 0, 10);
        Assert.Equal(3, total);
        Assert.Equal([errored, failed, good], rows.Select(r => r.TraceId)); // latest activity first
        var g = rows.Single(r => r.TraceId == good);
        Assert.Equal((2, 0, 0, TraceSteps.WebhookReceived, clientId, conversationId, "whatsapp"), (g.Steps, g.FailedSteps, g.Errors, g.FirstStep, g.ClientId!.Value, g.ConversationId!.Value, g.Channel));
        var f = rows.Single(r => r.TraceId == failed);
        Assert.Equal((1, TraceSteps.SendProviderFailed, "{\"error\": \"x\"}"), (f.FailedSteps, f.FailedStep, f.FailedDetail));
        Assert.Equal(1, rows.Single(r => r.TraceId == errored).Errors);

        var (failedOnly, failedTotal) = await repo.ListReadOnlyAsync(new MessageTraceFilter(from, clientId, FailedOnly: true), 0, 10);
        Assert.Equal(2, failedTotal);
        Assert.Equal([errored, failed], failedOnly.Select(r => r.TraceId));

        var (byConversation, _) = await repo.ListReadOnlyAsync(new MessageTraceFilter(from, ConversationId: conversationId), 0, 10);
        Assert.Equal(good, Assert.Single(byConversation).TraceId);

        var (paged, pagedTotal) = await repo.ListReadOnlyAsync(new MessageTraceFilter(from, clientId), 1, 1);
        Assert.Equal((3, failed), (pagedTotal, Assert.Single(paged).TraceId));

        var (none, noneTotal) = await repo.ListReadOnlyAsync(new MessageTraceFilter(t.AddYears(5)), 0, 10);
        Assert.Equal((0, 0), (none.Count, noneTotal));

        var counts = await repo.CountSinceReadOnlyAsync(new DateTimeOffset(2099, 7, 1, 0, 0, 0, TimeSpan.Zero));
        Assert.True(counts.Traces >= 4);
        Assert.True(counts.FailedTraces >= 2);
        Assert.True(counts.LastFailedAt >= t.AddMinutes(1));
    }
}
