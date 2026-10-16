namespace Omni.App.Common.Enums;

public enum AutomationDeleteOutcome
{
    Deleted,
    NotFound,
    /// <summary>The client exists but isn't an automation test client; nothing was deleted.</summary>
    NotAutomationClient
}
