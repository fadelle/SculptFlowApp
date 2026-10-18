namespace Omni.App.Entities.Dtos.Ui;

/// <summary>
/// An argument of a UI string that is itself text to put into the reader's language: another strings-file key
/// (<see cref="Of"/>, e.g. "notifications.anyone.contact" when there is no name to show) or a stored value shown as its
/// label (<see cref="Value"/>, e.g. a campaign status, read like <c>T.ValueLabel</c>). The localizer resolves it when it
/// fills the placeholders, so a server message can carry it as an argument: <c>.WithArgs(TextArg.Value(campaign.Status))</c>.
/// Notifications store it as a <c>[[key:…]]</c> / <c>[[value:…]]</c> marker (<see cref="Encode"/> / <see cref="Decode"/>).
/// </summary>
public sealed record TextArg
{
    private const string KeyMarker = "[[key:";
    private const string ValueMarker = "[[value:";
    private const string MarkerEnd = "]]";

    private TextArg(string? key, string? value)
    {
        Key = key;
        StoredValue = value;
    }

    /// <summary>The strings-file key to show, or null for a stored value.</summary>
    public string? Key { get; }

    /// <summary>The stored value to show as its label (when <see cref="Key"/> is null).</summary>
    public string? StoredValue { get; }

    public static TextArg Of(string key) => new(key, null);

    public static TextArg Value(string? value) => new(null, value);

    /// <summary>An argument as stored text: a TextArg becomes its marker, anything else its invariant string.</summary>
    public static string Encode(object? arg) => arg switch
    {
        TextArg { Key: { } key } => KeyMarker + key + MarkerEnd,
        TextArg t => ValueMarker + t.StoredValue + MarkerEnd,
        null => string.Empty,
        _ => Convert.ToString(arg, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty
    };

    /// <summary>Stored text back to an argument: a marker becomes its TextArg, anything else stays as written.</summary>
    public static object Decode(string stored)
    {
        if (stored.EndsWith(MarkerEnd, StringComparison.Ordinal))
        {
            if (stored.StartsWith(KeyMarker, StringComparison.Ordinal))
                return Of(stored[KeyMarker.Length..^MarkerEnd.Length]);
            if (stored.StartsWith(ValueMarker, StringComparison.Ordinal))
                return Value(stored[ValueMarker.Length..^MarkerEnd.Length]);
        }
        return stored;
    }
}
