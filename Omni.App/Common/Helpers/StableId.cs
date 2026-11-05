using System.Security.Cryptography;
using System.Text;

namespace Omni.App.Common.Helpers;

/// <summary>A Guid that is always the same for the same parts (SHA-256 of them, shaped as a version-5-style Guid). Used where a
/// retried request must land on the record the first attempt made, e.g. an AI reply's message id (QA t2-billing-02).</summary>
public static class StableId
{
    public static Guid From(params string[] parts)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\u001f', parts)));
        var bytes = hash[..16];
        bytes[7] = (byte)((bytes[7] & 0x0F) | 0x50); // version 5 (name-based)
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80); // RFC 4122 variant
        return new Guid(bytes);
    }

    /// <summary>The text's SHA-256 as hex, for a key that must change when the text does.</summary>
    public static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
