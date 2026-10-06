namespace PlasticSurgery.Common.Enums;

public static class KnowledgeSimilarityMethod
{
    public const string Cosine = "cosine";

    public static string Label(string? value) =>
        string.Equals(value, Cosine, StringComparison.OrdinalIgnoreCase) ? "Cosine" : (value ?? "—");
}
