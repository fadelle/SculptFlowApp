using System.Text.Json;
using PlasticSurgery.Common.Exceptions;
using PlasticSurgery.Entities.Dtos.KnowledgeBenchmark;

namespace PlasticSurgery.Business.Engines.KnowledgeBenchmark;

/// <summary>Reads the generator's questions from either an immediate reply or the callback body. Tolerant of the shapes n8n
/// produces: <c>{ generationId, questions: [...] }</c>, the same wrapped in a one-element array (the "Respond to Webhook"
/// habit), or a bare array of question objects (no generationId then — fine for the callback, whose URL carries the id).</summary>
public static class GeneratorResponseParser
{
    /// <exception cref="BenchmarkGenerationException">Not JSON, or no questions list.</exception>
    public static GeneratorResponse Parse(string body)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(body);
        }
        catch (JsonException ex)
        {
            throw new BenchmarkGenerationException("The question generator returned something that isn't JSON.", ex);
        }

        using (doc)
        {
            var root = doc.RootElement;
            JsonElement? list = null;
            string? generationId = null;

            if (root.ValueKind == JsonValueKind.Object && TryGetProperty(root, "questions", out var q) && q.ValueKind == JsonValueKind.Array)
            {
                list = q;
                generationId = ReadString(root, "generationId");
            }
            else if (root.ValueKind == JsonValueKind.Array)
            {
                if (root.GetArrayLength() > 0 && root[0].ValueKind == JsonValueKind.Object
                    && TryGetProperty(root[0], "questions", out var inner) && inner.ValueKind == JsonValueKind.Array)
                {
                    list = inner;
                    generationId = ReadString(root[0], "generationId");
                }
                else
                {
                    list = root;
                }
            }

            if (list is null)
            {
                throw new BenchmarkGenerationException("The question generator's response has no \"questions\" list.");
            }

            var result = new List<RawGeneratedQuestion>();
            foreach (var item in list.Value.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                result.Add(new RawGeneratedQuestion(
                    ReadString(item, "documentId"), ReadString(item, "chunkId"), ReadString(item, "question")));
            }
            return new GeneratorResponse(generationId, result);
        }
    }

    public static bool TryParse(string? body, out GeneratorResponse response)
    {
        response = null!;
        if (string.IsNullOrWhiteSpace(body)) return false;
        try
        {
            response = Parse(body);
            return true;
        }
        catch (BenchmarkGenerationException)
        {
            return false;
        }
    }

    private static bool TryGetProperty(JsonElement obj, string name, out JsonElement value)
    {
        foreach (var p in obj.EnumerateObject())
        {
            if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = p.Value;
                return true;
            }
        }
        value = default;
        return false;
    }

    private static string? ReadString(JsonElement obj, string name) =>
        TryGetProperty(obj, name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
