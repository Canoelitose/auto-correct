using System.Text.Json.Serialization;

namespace AutoCorrect.Core.Engines.Llm;

/// <summary>
/// Picks a usable model out of what is actually installed.
///
/// Requiring the exact tag from the documentation was the wrong bargain: anyone who already had
/// a model, or who pulled a slightly different one, got "model not loaded" and a command to run
/// even though a perfectly good model was sitting right there. The configured name still wins
/// whenever it exists; this only decides what to do when it does not.
/// </summary>
internal static class ModelCatalogue
{
    /// <summary>
    /// Model families in the order they are preferred, best first. The list is about German
    /// quality at a small size, which is what this application needs.
    /// </summary>
    private static readonly string[] PreferredFamilies =
    [
        "qwen2.5", "qwen3", "qwen2", "llama3.2", "llama3.1", "llama3", "mistral", "gemma2", "gemma3", "phi",
    ];

    /// <summary>
    /// Models that cannot do the job, whatever else is installed. Embedding models have no chat
    /// endpoint at all, and picking one would produce a confusing error instead of an answer.
    /// </summary>
    private static readonly string[] Unusable =
    [
        "embed", "embedding", "bge-", "nomic-", "minilm", "rerank", "whisper", "clip", "vision", "moondream",
    ];

    /// <summary>
    /// Chooses the model to use. Returns null when nothing installed can do the job.
    /// </summary>
    /// <param name="configured">The name from the settings. Wins whenever it is installed.</param>
    /// <param name="installed">What the server reports under /v1/models.</param>
    public static string? Choose(string configured, IReadOnlyList<string> installed)
    {
        ArgumentNullException.ThrowIfNull(installed);

        if (installed.Count == 0)
        {
            return null;
        }

        // An exact match, or the same model under a tag such as "qwen2.5:3b" vs "qwen2.5:3b-instruct".
        var exact = installed.FirstOrDefault(m => Matches(m, configured));
        if (exact is not null)
        {
            return exact;
        }

        var usable = installed.Where(IsUsable).ToList();
        if (usable.Count == 0)
        {
            return null;
        }

        // A known family beats an unknown one; within a family the smaller model is preferred
        // because it answers sooner, which matters more here than the last bit of quality.
        return usable
            .OrderBy(FamilyRank)
            .ThenBy(SizeInBillions)
            .ThenBy(m => m, StringComparer.OrdinalIgnoreCase)
            .First();
    }

    /// <summary>True when the installed name is the configured one, ignoring tag decoration.</summary>
    private static bool Matches(string installed, string configured)
    {
        if (string.IsNullOrWhiteSpace(configured))
        {
            return false;
        }

        if (string.Equals(installed, configured, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Ollama reports "qwen2.5:3b" for what a user may have written as "qwen2.5:3b-instruct-q4_K_M".
        // Comparing up to the first tag separator matches those without matching everything.
        return string.Equals(BaseTag(installed), BaseTag(configured), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>"qwen2.5:3b-instruct-q4_K_M" becomes "qwen2.5:3b".</summary>
    private static string BaseTag(string name)
    {
        var colon = name.IndexOf(':', StringComparison.Ordinal);
        if (colon < 0)
        {
            return name;
        }

        var tag = name[(colon + 1)..];
        var dash = tag.IndexOf('-', StringComparison.Ordinal);
        return dash < 0 ? name : string.Concat(name.AsSpan(0, colon + 1), tag.AsSpan(0, dash));
    }

    private static bool IsUsable(string name)
    {
        foreach (var marker in Unusable)
        {
            if (name.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Position in <see cref="PreferredFamilies"/>, or one past the end when unknown.
    ///
    /// Hosted catalogues prefix the family with the publisher - "meta/llama-3.1-8b-instruct",
    /// "qwen/qwen2.5-7b-instruct" - so the family is looked for after a slash as well. Matching
    /// only the start of the name would rank every hosted model as unknown.
    /// </summary>
    internal static int FamilyRank(string name)
    {
        var afterSlash = name.LastIndexOf('/');
        var bare = afterSlash >= 0 ? name[(afterSlash + 1)..] : name;

        for (var index = 0; index < PreferredFamilies.Length; index++)
        {
            if (name.StartsWith(PreferredFamilies[index], StringComparison.OrdinalIgnoreCase) ||
                bare.StartsWith(PreferredFamilies[index], StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return PreferredFamilies.Length;
    }

    /// <summary>
    /// Parameter count read out of the tag ("qwen2.5:3b" gives 3). Unknown sizes sort last, so a
    /// model whose size cannot be read is never preferred over one that is known to be small.
    /// </summary>
    internal static double SizeInBillions(string name)
    {
        for (var index = 0; index < name.Length; index++)
        {
            if (name[index] is not ('b' or 'B'))
            {
                continue;
            }

            // Walk back over the digits and an optional decimal point in front of the "b".
            var end = index;
            var start = index;
            while (start > 0 && (char.IsDigit(name[start - 1]) || name[start - 1] == '.'))
            {
                start--;
            }

            if (start == end)
            {
                continue;
            }

            // A letter directly in front means this is part of a word, not a size.
            if (start > 0 && char.IsLetter(name[start - 1]))
            {
                continue;
            }

            if (double.TryParse(
                    name.AsSpan(start, end - start),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var size) &&
                size is > 0 and < 1000)
            {
                return size;
            }
        }

        return double.MaxValue;
    }
}
