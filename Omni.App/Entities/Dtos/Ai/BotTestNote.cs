namespace Omni.App.Entities.Dtos.Ai;

/// <summary>A "note" line of a bot test conversation (what happened behind the scenes), stored as JSON in
/// crm.bot_test_messages.content and worded for the reader when the transcript is read (BotTestMapper). Key is a
/// "bots.test.notes.*" text key; Args are plain values (a booking label, a stage, a reason).</summary>
public record BotTestNote(string Key, IReadOnlyList<string> Args)
{
    public const string BookedKey = "bots.test.notes.booked";
    public const string RescheduledKey = "bots.test.notes.rescheduled";
    public const string CanceledKey = "bots.test.notes.canceled";
    public const string ContactUpdatedKey = "bots.test.notes.contactUpdated";
    public const string StageMovedKey = "bots.test.notes.stageMoved";
    public const string HandedOffKey = "bots.test.notes.handedOff";
    public const string HandedOffNoReasonKey = "bots.test.notes.handedOffNoReason";
    public const string ReplyRefusedKey = "bots.test.notes.replyRefused";
    public const string NotReachableKey = "bots.test.notes.notReachable";

    public static BotTestNote Of(string key, params string[] args) => new(key, args);
}
