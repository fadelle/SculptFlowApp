using Omni.App.Business.Services.Clients;

namespace Omni.App.Tests.Helpers;

/// <summary>QA t5-ops-21: every new client started on UTC; the browser's zone is kept when the server knows it.</summary>
public class SignupTimezoneTests
{
    [Theory]
    [InlineData("Asia/Beirut", "Asia/Beirut")]
    [InlineData(" Europe/Paris ", "Europe/Paris")]
    [InlineData("UTC", null)]            // not an Area/City zone: the default stays
    [InlineData("Mars/Olympus", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Only_a_known_area_city_zone_is_kept(string? browser, string? expected) =>
        Assert.Equal(expected, ClientRegistrationService.KnownTimezone(browser));
}
