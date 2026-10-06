namespace PlasticSurgery.Common.Helpers;

/// <summary>Infobip wants international numbers as digits only ("+44 7860 099299" -> "447860099299").</summary>
public static class InfobipNumbers
{
    public static string Normalize(string? number) =>
        new string((number ?? string.Empty).Where(char.IsDigit).ToArray());
}
