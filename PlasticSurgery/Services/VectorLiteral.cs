using System.Globalization;
using System.Text;

namespace PlasticSurgery.Services;

/// <summary>Formats a float[] as a pgvector text literal ("[0.1,0.2,...]"), passed as a normal string
/// parameter and cast with ::vector in the SQL — how the Knowledge Base talks to pgvector without an
/// EF vector-type package.</summary>
internal static class VectorLiteral
{
    public static string From(float[] values)
    {
        var sb = new StringBuilder(values.Length * 9 + 2);
        sb.Append('[');
        for (var i = 0; i < values.Length; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(values[i].ToString("R", CultureInfo.InvariantCulture));
        }
        return sb.Append(']').ToString();
    }
}
