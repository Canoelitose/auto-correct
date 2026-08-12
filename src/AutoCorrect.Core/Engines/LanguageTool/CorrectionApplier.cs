using System.Text;

namespace AutoCorrect.Core.Engines.LanguageTool;

/// <summary>A single replacement suggested by LanguageTool, in UTF-16 code units.</summary>
public readonly record struct TextCorrection(int Offset, int Length, string Replacement);

/// <summary>
/// Applies LanguageTool matches to the original text.
///
/// Corrections are applied back to front. Applying them front to back would shift every
/// following offset by the length delta of the previous replacement.
/// </summary>
public static class CorrectionApplier
{
    public static string Apply(string input, IEnumerable<TextCorrection> corrections)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(corrections);

        // Longest match first on equal offsets, so a broad suggestion wins over a narrow one.
        var ordered = corrections
            .OrderByDescending(c => c.Offset)
            .ThenByDescending(c => c.Length)
            .ToList();

        if (ordered.Count == 0)
        {
            return input;
        }

        var builder = new StringBuilder(input);

        // Start of the region handled last. Anything reaching into it would work on
        // offsets that are no longer valid, so it is skipped.
        var handledFrom = input.Length;

        foreach (var correction in ordered)
        {
            if (correction.Offset < 0 ||
                correction.Length < 0 ||
                correction.Offset + correction.Length > input.Length)
            {
                // Offsets out of range mean the server saw different text than we sent.
                continue;
            }

            if (correction.Offset + correction.Length > handledFrom)
            {
                continue;
            }

            handledFrom = correction.Offset;

            var replacement = correction.Replacement ?? string.Empty;
            var original = input.Substring(correction.Offset, correction.Length);
            if (string.Equals(original, replacement, StringComparison.Ordinal))
            {
                continue;
            }

            builder.Remove(correction.Offset, correction.Length);
            builder.Insert(correction.Offset, replacement);
        }

        return builder.ToString();
    }
}
