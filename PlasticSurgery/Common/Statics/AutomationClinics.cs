namespace PlasticSurgery.Common.Statics;

/// <summary>
/// How the SculptFlowAutomation tester marks its throwaway clinics. Only a clinic whose name starts with
/// <see cref="NamePrefix"/> AND whose users all have an email at <see cref="EmailDomain"/> (a reserved .test domain no
/// real person can own) may be deleted by the automation cleanup API.
/// </summary>
public static class AutomationClinics
{
    public const string NamePrefix = "[Automation]";
    public const string EmailDomain = "@sculptflow-automation.test";
}
