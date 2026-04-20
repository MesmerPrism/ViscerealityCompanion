using System.Text;

namespace ViscerealityCompanion.Core.Services;

internal static class PdfTextLayoutHelper
{
    private const char ZeroWidthSpace = '\u200B';
    private const int MaxContinuousTokenLength = 18;

    public static string PrepareForParagraph(string? text, bool preserveCodeShape = false)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        var builder = new StringBuilder(normalized.Length + Math.Max(8, normalized.Length / 6));
        var continuousTokenLength = 0;

        foreach (var ch in normalized)
        {
            builder.Append(ch);

            if (ch == '\n' || char.IsWhiteSpace(ch))
            {
                continuousTokenLength = 0;
                continue;
            }

            continuousTokenLength++;

            if (ShouldInsertBreakAfter(ch, preserveCodeShape))
            {
                builder.Append(ZeroWidthSpace);
                continuousTokenLength = 0;
                continue;
            }

            if (continuousTokenLength >= MaxContinuousTokenLength && char.IsLetterOrDigit(ch))
            {
                builder.Append(ZeroWidthSpace);
                continuousTokenLength = 0;
            }
        }

        return builder.ToString();
    }

    private static bool ShouldInsertBreakAfter(char ch, bool preserveCodeShape)
        => ch switch
        {
            '\\' or '/' => true,
            '.' or ':' or ';' or ',' or '|' or ')' or ']' or '}' => true,
            '_' or '-' or '=' or '+' => preserveCodeShape,
            _ => false
        };
}
