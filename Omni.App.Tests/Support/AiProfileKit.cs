namespace Omni.App.Tests.Support;

/// <summary>AI profiles for AI trigger tests: a profile with its own words, a non-English default language and a
/// timezone that isn't UTC, so a test can see every field travel to n8n.</summary>
internal static class AiProfileKit
{
    public static AiClientProfile Sample { get; } = new(
        "Cedar Dental", "dental", AppLanguage.Arabic, "Asia/Beirut", "You are the assistant of a dental practice.",
        new Dictionary<string, TermForms>
        {
            [TermName.Contact] = new("guest", "guests"),
            [TermName.Booking] = new("visit", "visits")
        });

    /// <summary>Answers <see cref="Sample"/> for any client.</summary>
    public static Mock<IAiProfileService> Profiles(AiClientProfile? profile = null)
    {
        var profiles = new Mock<IAiProfileService>();
        profiles.Setup(p => p.GetForClientAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(profile ?? Sample);
        return profiles;
    }
}
