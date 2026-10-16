namespace Omni.App.Common.Statics;

/// <summary>
/// How the Omni.Automation tester marks its throwaway clients. Only a client whose name starts with
/// <see cref="NamePrefix"/> AND whose users all have an email at <see cref="EmailDomain"/> (a reserved .test domain no
/// real person can own) may be deleted by the automation cleanup API.
/// </summary>
public static class AutomationClients
{
    public const string NamePrefix = "[Automation]";
    public const string EmailDomain = "@omni-automation.test";
}
