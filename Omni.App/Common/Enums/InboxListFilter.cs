namespace Omni.App.Common.Enums;

/// <summary>The Inbox list's tabs (GET /api/conversations?filter=). Closed = closed or archived; needs = open and with the team
/// (human or awaiting approval); ai = open and with the AI.</summary>
public static class InboxListFilter
{
    public const string All = "all";
    public const string Needs = "needs";
    public const string Ai = "ai";
    public const string Closed = "closed";

    public static readonly IReadOnlySet<string> Values = new HashSet<string> { All, Needs, Ai, Closed };
}
