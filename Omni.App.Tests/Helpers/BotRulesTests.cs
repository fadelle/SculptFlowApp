using System.Text.Json;
using Omni.App.Business.Mappers.Ai;

namespace Omni.App.Tests.Helpers;

public class BotRulesTests
{
    private static Bot Bot(string template = BotTemplate.Custom, string status = BotStatus.Active, int minutesOld = 0, string? instructions = "Hi") => new()
    {
        Id = Guid.NewGuid(), Template = template, Status = status, Instructions = instructions, CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-minutesOld)
    };

    [Fact]
    public void A_template_runs_its_own_actions_and_instructions_and_a_custom_bot_its_known_choices()
    {
        var sales = Bot(BotTemplate.Sales, instructions: "ignored");
        sales.Capabilities = [BotCapability.Bookings];
        Assert.Equal(BotTemplates.Sales.Capabilities, BotRules.Capabilities(sales));
        Assert.Equal(BotTemplates.Sales.CoreInstructions, BotRules.Instructions(sales));

        var custom = Bot();
        custom.Capabilities = [BotCapability.Pipeline, "unknown", BotCapability.Knowledge];
        Assert.Equal([BotCapability.Knowledge, BotCapability.Pipeline], BotRules.Capabilities(custom));
        Assert.Null(BotRules.Instructions(Bot(instructions: " ")));
    }

    [Fact]
    public void Only_the_oldest_active_bots_within_the_limit_answer()
    {
        var old = Bot(minutesOld: 30);
        var middle = Bot(minutesOld: 20);
        var draft = Bot(status: BotStatus.Draft, minutesOld: 40);
        var young = Bot(minutesOld: 1);
        Bot[] bots = [young, draft, middle, old];
        Assert.Equal(new HashSet<Guid> { old.Id, middle.Id }, BotRules.AnsweringBotIds(bots, 2));
        Assert.Equal(3, BotRules.AnsweringBotIds(bots, null).Count);
        Assert.Empty(BotRules.AnsweringBotIds(bots, 0));
    }

    [Fact]
    public void The_knowledge_filter_and_readiness_follow_the_scope()
    {
        var bot = Bot();
        Assert.Null(BotRules.KnowledgeFilter(bot));
        Assert.Null(BotRules.NotReadyKey(bot));

        bot.KnowledgeScope = BotKnowledgeScope.Selected;
        Assert.Equal("bots.errors.sourcesRequired", BotRules.NotReadyKey(bot));
        var doc = Guid.NewGuid(); var site = Guid.NewGuid();
        bot.KnowledgeSources = [new() { KnowledgeDocumentId = doc }, new() { WebsiteSourceId = site }];
        var filter = BotRules.KnowledgeFilter(bot)!;
        Assert.Equal([doc], filter.DocumentIds);
        Assert.Equal([site], filter.WebsiteSourceIds);
        Assert.Null(BotRules.NotReadyKey(bot));

        Assert.Equal("bots.errors.instructionsRequired", BotRules.NotReadyKey(Bot(instructions: null)));
        Assert.Null(BotRules.NotReadyKey(Bot(BotTemplate.Support, instructions: null)));
        Assert.Equal("bots.errors.instructionsRequired", BotRules.NotReadyKey(BotMapper.ToDto(Bot(instructions: null), 0)));
        Assert.Null(BotRules.NotReadyKey(BotMapper.ToDto(Bot(BotTemplate.Booking, instructions: null), 0)));
    }

    [Fact]
    public void The_bot_instructions_name_the_allowed_actions_policy_and_language()
    {
        var text = BotInstructions.Compose("Front desk", "WhatsApp", "Be brief.", [BotCapability.Knowledge], BotHandoffPolicy.OnRequest, null);
        Assert.Contains("Your name: Front desk", text);
        Assert.Contains("search_knowledge", text);
        Assert.DoesNotContain("update_stage", text);
        Assert.Contains("never return action=schedule", text);
        Assert.Contains("only when the contact asks for a person", text);
        Assert.Contains("language and script of the contact's latest message", text);

        var booking = BotInstructions.Compose("B", null, "x", [BotCapability.Availability, BotCapability.Bookings], BotHandoffPolicy.OnRequestOrUnsure, "ar");
        Assert.DoesNotContain("never return action=schedule", booking);
        Assert.DoesNotContain("Your purpose", booking);
        Assert.Contains("can't confidently help", booking);
        Assert.Contains("Always reply in Arabic", booking);

        var slotsOnly = BotInstructions.Compose("B", null, "x", [BotCapability.Availability], BotHandoffPolicy.OnRequest, "xx");
        Assert.Contains("cannot book, move or cancel", slotsOnly);
        Assert.Contains("language and script", slotsOnly); // an unknown fixed language falls back to automatic
    }

    [Fact]
    public void Every_bot_word_has_a_text_key()
    {
        Assert.All(BotTemplate.All, t => Assert.StartsWith("bots.templates.", BotTextKeys.TemplateName(t)));
        Assert.Equal(BotTemplate.All.Count, BotTemplate.All.Select(BotTextKeys.TemplateText).Distinct().Count());
        Assert.Equal(BotCapability.All.Count, BotCapability.All.Select(BotTextKeys.Capability).Distinct().Count());
        Assert.Equal(BotStatus.All.Count, BotStatus.All.Select(BotTextKeys.Status).Distinct().Count());
        Assert.Equal(BotLanguages.Codes.Count(), BotLanguages.Codes.Select(BotTextKeys.Language).Distinct().Count());
        Assert.Equal(2, BotHandoffPolicy.All.Select(BotTextKeys.HandoffPolicy).Distinct().Count());
        Assert.Equal(("badge-green", "badge-gray", "badge-amber"),
            (BotTextKeys.StatusBadge(BotStatus.Active), BotTextKeys.StatusBadge(BotStatus.Inactive), BotTextKeys.StatusBadge(BotStatus.Draft)));
        Assert.Null(BotTextKeys.BlockReason(null));
        Assert.Equal(5, new[] { AiBotBlockReason.PlanWithoutAi, AiBotBlockReason.AiDisabled, AiBotBlockReason.NoBot, AiBotBlockReason.BotInactive, AiBotBlockReason.BotLimit }
            .Select(BotTextKeys.BlockReason).Distinct().Count());
    }

    [Fact]
    public void The_transcript_words_notes_for_the_reader_and_knows_when_the_bot_still_owes_a_reply()
    {
        var web = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        BotTestMessage Line(string role, string content) => new() { Id = Guid.NewGuid(), Role = role, Content = content };
        string Note(string key, params string[] args) => JsonSerializer.Serialize(BotTestNote.Of(key, args), web);
        var session = new BotTestSession { Id = Guid.NewGuid() };
        var text = TextKit.Localizer();

        var waiting = BotTestMapper.ToTranscript(session, [Line(BotTestRole.Contact, "Book me"), Line(BotTestRole.Note, Note(BotTestNote.BookedKey, "Mon"))], text);
        Assert.True(waiting.Waiting);
        Assert.Equal("Simulated: booked Mon", waiting.Lines[1].Text);

        Assert.False(BotTestMapper.ToTranscript(session, [Line(BotTestRole.Contact, "Hi"), Line(BotTestRole.Bot, "Hello")], text).Waiting);
        Assert.False(BotTestMapper.ToTranscript(session, [Line(BotTestRole.Contact, "Hi"), Line(BotTestRole.Note, Note(BotTestNote.NotReachableKey))], text).Waiting);
        Assert.False(BotTestMapper.ToTranscript(session, [Line(BotTestRole.Contact, "Hi"), Line(BotTestRole.Note, Note(BotTestNote.ReplyRefusedKey))], text).Waiting);
        Assert.Equal("{not json", BotTestMapper.ToTranscript(session, [Line(BotTestRole.Note, "{not json")], text).Lines[0].Text);

        session.HandedOff = true;
        var over = BotTestMapper.ToTranscript(session, [Line(BotTestRole.Contact, "Human please")], text);
        Assert.Equal((true, false), (over.HandedOff, over.Waiting));
    }
}
