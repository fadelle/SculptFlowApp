namespace Omni.App.Common.Helpers;

/// <summary>
/// The rules for a client's contact details (QA t1-accounts-12), the same for its own Business Info page and the admin portal:
/// blank = cleared; a phone is 6 to 15 digits written with phone characters only; an email must look like one; a website is
/// http(s) only ("example.com" becomes "https://example.com", so "javascript:" can never be a link); every field fits its
/// column (address, hours and booking text get sensible caps). ArgumentException with a strings-file key otherwise.
/// </summary>
public static class ClientDetailsRules
{
    public const int PhoneMax = 50, EmailMax = 200, WebsiteMax = 300, AddressMax = 500, TextMax = 2000;

    public record Cleaned(string? Phone, string? Email, string? Website, string? Address, string? OperatingHours, string? BookingInfo);

    public static Cleaned Clean(string? phone, string? email, string? website, string? address, string? operatingHours, string? bookingInfo) =>
        new(Phone(phone), Email(email), Website(website),
            Limited(address, AddressMax, () => new ArgumentException("clients.errors.addressTooLong")),
            Limited(operatingHours, TextMax, () => new ArgumentException("clients.errors.hoursTooLong")),
            Limited(bookingInfo, TextMax, () => new ArgumentException("clients.errors.bookingInfoTooLong")));

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? Limited(string? value, int max, Func<ArgumentException> tooLong)
    {
        var clean = Trim(value);
        return clean is null || clean.Length <= max ? clean : throw tooLong().WithArgs(max);
    }

    private static string? Phone(string? phone)
    {
        var clean = Trim(phone);
        if (clean is null) return null;
        var digits = clean.Count(char.IsAsciiDigit);
        var phoneCharacters = clean.All(c => char.IsAsciiDigit(c) || c is ' ' or '+' or '-' or '(' or ')' or '.' or '/');
        return clean.Length <= PhoneMax && phoneCharacters && digits is >= 6 and <= 15
            ? clean
            : throw new ArgumentException("clients.errors.invalidPhone");
    }

    private static string? Email(string? email)
    {
        var clean = Trim(email);
        return clean is null || (clean.Length <= EmailMax && EmailAddress.IsValid(clean)) ? clean : throw new ArgumentException("clients.errors.invalidEmail");
    }

    private static string? Website(string? website)
    {
        var clean = Trim(website);
        if (clean is null) return null;
        var withScheme = clean.Contains("://", StringComparison.Ordinal) ? clean : "https://" + clean;
        var ok = withScheme.Length <= WebsiteMax
                 && Uri.TryCreate(withScheme, UriKind.Absolute, out var uri)
                 && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
                 && uri.Host.Contains('.') && !clean.Any(char.IsWhiteSpace);
        return ok ? withScheme : throw new ArgumentException("clients.errors.invalidWebsite");
    }
}
