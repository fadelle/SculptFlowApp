namespace Omni.App.Tests.Support;

/// <summary>Entitlement services for tests: <see cref="All"/> allows everything (billing off), <see cref="Plan"/> is an
/// active subscription whose plan sets exactly the given values (a key it doesn't list is off / 0).</summary>
internal static class EntitlementKit
{
    public static Mock<IEntitlementService> All() => With(ClientEntitlements.Unrestricted);

    public static Mock<IEntitlementService> Plan(params (string Key, string Value)[] values) =>
        With(new ClientEntitlements(true, true, SubscriptionStatus.Active, "plan", "Plan", null,
            values.ToDictionary(v => v.Key, v => v.Value)));

    /// <summary>The service answers <paramref name="entitlements"/> for every client, and EnsureFeatureAsync refuses
    /// (EntitlementDeniedException, like EntitlementService) a feature they don't enable.</summary>
    public static Mock<IEntitlementService> With(ClientEntitlements entitlements)
    {
        var mock = new Mock<IEntitlementService>();
        mock.Setup(e => e.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(entitlements);
        mock.Setup(e => e.EnsureFeatureAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<Guid, string, CancellationToken>((_, key, _) => entitlements.IsEnabled(key)
                ? Task.CompletedTask
                : Task.FromException(new EntitlementDeniedException(key, EntitlementDeniedException.NotIncludedKey)));
        return mock;
    }
}
