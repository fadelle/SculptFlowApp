using System.Text.Json.Nodes;

namespace Omni.App.Common.Helpers;

/// <summary>
/// Masks secrets in a JSON copy before it leaves the app (the platform-admin cache view): every string property whose name
/// says it is a secret (access/refresh/verify tokens, token hashes, secrets, passwords, API and private keys, credentials,
/// a PIN) becomes <see cref="Mask"/>, at any depth. Null and empty values stay as they are, so "is one set?" is still
/// visible; numbers (token counts) are never touched.
/// </summary>
public static class SecretRedactor
{
    /// <summary>What a set secret shows as (the same as Configuration's secret settings).</summary>
    public const string Mask = "(set)";

    private static readonly string[] SecretWords = ["token", "secret", "password", "apikey", "privatekey", "credential"];

    /// <summary>True when a property with this name holds a secret (any casing, with or without _ or -).</summary>
    public static bool IsSecretName(string name)
    {
        var plain = name.Replace("_", "").Replace("-", "").ToLowerInvariant();
        return plain == "pin" || SecretWords.Any(plain.Contains);
    }

    /// <summary>Masks the secrets in <paramref name="node"/> in place and returns it.</summary>
    public static JsonNode? Redact(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var (name, value) in obj.ToList())
                {
                    if (IsSecretName(name) && value is JsonValue v && v.TryGetValue<string>(out var text) && !string.IsNullOrEmpty(text))
                    {
                        obj[name] = Mask;
                    }
                    else
                    {
                        Redact(value);
                    }
                }
                break;
            case JsonArray array:
                foreach (var item in array) Redact(item);
                break;
        }
        return node;
    }

    /// <summary>The JSON text with its secrets masked.</summary>
    public static string Redact(string json) => Redact(JsonNode.Parse(json))?.ToJsonString() ?? "null";
}
