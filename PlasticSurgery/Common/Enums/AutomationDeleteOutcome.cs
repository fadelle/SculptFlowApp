namespace PlasticSurgery.Common.Enums;

public enum AutomationDeleteOutcome
{
    Deleted,
    NotFound,
    /// <summary>The clinic exists but isn't an automation test clinic; nothing was deleted.</summary>
    NotAutomationClinic
}
