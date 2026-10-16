using Microsoft.EntityFrameworkCore;
using Omni.App.Persistence.Repositories.Monitoring;
using Omni.App.Persistence.Repositories.PlatformAdmin;

namespace Omni.App.Tests.Repositories;

/// <summary>activity.message_trace_steps and the trace view's reads. Trace ids are random per test, so rows never clash.</summary>
public class MessageTraceRepositoryTests(PostgresFixture fixture) : RepoTestBase(fixture)
{
    private static string NewTrace() => Guid.NewGuid().ToString("N");

    private static MessageTraceStep Step(string traceId, DateTimeOffset at, string step = TraceSteps.MessageSaved, string outcome = TraceOutcomes.Ok,
        Guid? clinicId = null, Guid? conversationId = null, Guid? messageId = null, string? externalId = null, string detail = "{}") => new()
    {
        TraceId = traceId, OccurredAt = at, Step = step, Outcome = outcome, ClinicId = clinicId, ConversationId = conversationId,
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
        var clinic = MakeClinic();
        var lead = MakeLead(clinic.Id);
        var conversation = MakeConversation(clinic.Id, lead.Id);
        var trace = NewTrace();
        var t = new DateTimeOffset(2098, 5, 1, 9, 0, 0, TimeSpan.Zero);
        var inbound = MakeMessage(conversation, createdAt: t, externalId: "wamid." + trace);
        inbound.TraceId = trace;
        var named = MakeMessage(conversation, MessageDirection.Outbound, MessageOrigin.Ai, createdAt: t.AddSeconds(5)); // no trace id, named by a step
        var other = MakeMessage(conversation, createdAt: t.AddSeconds(1));
        await SeedAsync(clinic, lead, conversation, inbound, named, other,
            Step(trace, t.AddSeconds(1), TraceSteps.SendProviderAccepted, clinicId: clinic.Id, conversationId: conversation.Id, messageId: named.Id, externalId: "out." + trace),
            Step(trace, t, TraceSteps.MessageSaved, clinicId: clinic.Id, conversationId: conversation.Id, messageId: inbound.Id),
            new EventLog { Id = Guid.NewGuid(), ClinicId = clinic.Id, ConversationId = conversation.Id, EventType = "customer_message_received", Source = "whatsapp_customer", Metadata = "{}", CreatedAt = t },
            new EventLog { Id = Guid.NewGuid(), ClinicId = clinic.Id, ConversationId = conversation.Id, EventType = "too_late", Metadata = "{}", CreatedAt = t.AddHours(2) },
            new AppLog { OccurredAt = t.AddSeconds(2), Level = "Error", Category = "X", Message = "boom", Fingerprint = "f", TraceId = trace, Properties = "{}" });

        await using var db = NewContext();
        var repo = new MessageTraceAdminRepository(db);

        var steps = await repo.StepsOfTraceReadOnlyAsync(trace);
        Assert.Equal([TraceSteps.MessageSaved, TraceSteps.SendProviderAccepted], steps.Select(s => s.Step));

        var messages = await repo.MessagesReadOnlyAsync(trace, [named.Id]);
        Assert.Equal([inbound.Id, named.Id], messages.Select(m => m.Id));
        Assert.Equal((trace, (string?)null), (messages[0].TraceId, messages[1].TraceId));

        var events = await repo.EventsReadOnlyAsync([conversation.Id], t.AddMinutes(-1), t.AddMinutes(10));
        Assert.Equal("customer_message_received", Assert.Single(events).EventType);
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
    public async Task The_list_summarises_traces_and_filters_by_clinic_conversation_and_failure()
    {
        var clinicId = Guid.NewGuid();
        var conversationId = Guid.NewGuid();
        var t = new DateTimeOffset(2099, 7, 1, 9, 0, 0, TimeSpan.Zero);
        var (good, failed, errored, otherClinic) = (NewTrace(), NewTrace(), NewTrace(), NewTrace());
        await SeedAsync(
            Step(good, t, TraceSteps.WebhookReceived),
            Step(good, t.AddSeconds(1), clinicId: clinicId, conversationId: conversationId),
            Step(failed, t.AddMinutes(1), TraceSteps.SendRequested, clinicId: clinicId, conversationId: Guid.NewGuid()),
            Step(failed, t.AddMinutes(1).AddSeconds(1), TraceSteps.SendProviderFailed, TraceOutcomes.Failed, clinicId, detail: "{\"error\":\"x\"}"),
            Step(errored, t.AddMinutes(2), clinicId: clinicId),
            new AppLog { OccurredAt = t.AddMinutes(2), Level = "Critical", Category = "X", Message = "boom", Fingerprint = "f", TraceId = errored, Properties = "{}" },
            new AppLog { OccurredAt = t.AddMinutes(2), Level = "Warning", Category = "X", Message = "meh", Fingerprint = "f", TraceId = good, Properties = "{}" },
            Step(otherClinic, t.AddMinutes(3), clinicId: Guid.NewGuid()));

        await using var db = NewContext();
        var repo = new MessageTraceAdminRepository(db);
        var from = t.AddSeconds(-1);

        var (rows, total) = await repo.ListReadOnlyAsync(new MessageTraceFilter(from, clinicId), 0, 10);
        Assert.Equal(3, total);
        Assert.Equal([errored, failed, good], rows.Select(r => r.TraceId)); // latest activity first
        var g = rows.Single(r => r.TraceId == good);
        Assert.Equal((2, 0, 0, TraceSteps.WebhookReceived, clinicId, conversationId, "whatsapp"), (g.Steps, g.FailedSteps, g.Errors, g.FirstStep, g.ClinicId!.Value, g.ConversationId!.Value, g.Channel));
        var f = rows.Single(r => r.TraceId == failed);
        Assert.Equal((1, TraceSteps.SendProviderFailed, "{\"error\": \"x\"}"), (f.FailedSteps, f.FailedStep, f.FailedDetail));
        Assert.Equal(1, rows.Single(r => r.TraceId == errored).Errors);

        var (failedOnly, failedTotal) = await repo.ListReadOnlyAsync(new MessageTraceFilter(from, clinicId, FailedOnly: true), 0, 10);
        Assert.Equal(2, failedTotal);
        Assert.Equal([errored, failed], failedOnly.Select(r => r.TraceId));

        var (byConversation, _) = await repo.ListReadOnlyAsync(new MessageTraceFilter(from, ConversationId: conversationId), 0, 10);
        Assert.Equal(good, Assert.Single(byConversation).TraceId);

        var (paged, pagedTotal) = await repo.ListReadOnlyAsync(new MessageTraceFilter(from, clinicId), 1, 1);
        Assert.Equal((3, failed), (pagedTotal, Assert.Single(paged).TraceId));

        var (none, noneTotal) = await repo.ListReadOnlyAsync(new MessageTraceFilter(t.AddYears(5)), 0, 10);
        Assert.Equal((0, 0), (none.Count, noneTotal));

        var counts = await repo.CountSinceReadOnlyAsync(new DateTimeOffset(2099, 7, 1, 0, 0, 0, TimeSpan.Zero));
        Assert.True(counts.Traces >= 4);
        Assert.True(counts.FailedTraces >= 2);
        Assert.True(counts.LastFailedAt >= t.AddMinutes(1));
    }
}
