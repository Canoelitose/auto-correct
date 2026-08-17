using System.Text;

namespace AutoCorrect.Core.Privacy;

/// <summary>
/// Replaces personal details with stand-ins before the text leaves the device, and puts the
/// originals back into the answer.
///
/// **What this is for.** A model on your own machine sees your text either way. A hosted
/// endpoint does not have to: names, addresses and account numbers can be swapped for
/// stand-ins that carry the same grammar, so the service gets a sentence it can work on
/// without getting the people in it.
///
/// **Why stand-in names and not [NAME1].** A bracket token is a foreign body: models comment on
/// it, drop it, translate it or decline it wrongly, and any of that leaves a visible placeholder
/// in the user's document. "Herr Muster" is just a sentence, so grammar, case and agreement
/// survive the round trip and the swap back is a plain string replacement.
///
/// **How a name is found.** Four ways, and the last one does most of the work:
/// a known first name, a salutation in front of the word, a word the user listed, or - when a
/// dictionary is available - a capitalised word that no dictionary knows. In German every noun
/// is capitalised, so capitalisation alone says nothing; "not a word in any dictionary" is what
/// separates "Brunnenwieser" from "Rechnung".
///
/// **What it does not do.** This is still pattern matching, not understanding. A surname that
/// happens to be an ordinary word passes - Herr Koch, Frau Berg, Mr Baker - unless a salutation
/// or a first name introduces it. Addresses and dates of birth are not recognised at all, and
/// the rest of the sentence goes out as written. It reduces what a service gets to see; it is
/// not a guarantee, and it is not anonymisation in the legal sense. Text that must not leave
/// the device belongs on a local model.
/// </summary>
public sealed class PrivacyMask
{
    /// <summary>Salutations after which the following capitalised words are a name.</summary>
    private static readonly string[] Salutations =
    [
        "Herr", "Herrn", "Frau", "Hr", "Fr", "Dr", "Prof", "Mr", "Mrs", "Ms", "Miss", "Dear",
    ];

    /// <summary>
    /// Stand-ins, used in order of first appearance. Ordinary names on purpose: the sentence has
    /// to stay grammatical for the model to work on it properly.
    /// </summary>
    private static readonly string[] GivenNameSubstitutes =
    [
        "Alex", "Robin", "Kim", "Toni", "Nico", "Sascha", "Luca", "Mika", "Jamie", "Charlie",
    ];

    private static readonly string[] SurnameSubstitutes =
    [
        "Muster", "Beispiel", "Vogel", "Berger", "Keller", "Winter", "Sommer", "Frei", "Roth", "Steiner",
    ];

    private readonly Dictionary<string, string> _backwards;

    private PrivacyMask(string masked, Dictionary<string, string> backwards)
    {
        Masked = masked;
        _backwards = backwards;
    }

    /// <summary>The text as it is sent, with the details replaced.</summary>
    public string Masked { get; }

    /// <summary>How many distinct details were replaced. Zero means nothing was found.</summary>
    public int ReplacementCount => _backwards.Count;

    /// <summary>Stand-in to original, longest stand-in first so replacing cannot overlap.</summary>
    internal IReadOnlyList<KeyValuePair<string, string>> Backwards =>
        _backwards.OrderByDescending(p => p.Key.Length).ToList();

    /// <summary>
    /// Builds the mask for a text. The assignment of stand-ins depends only on the input, so the
    /// same text always produces the same masked version - which keeps the result cache correct.
    /// </summary>
    /// <param name="input">The user's text.</param>
    /// <param name="protectedTerms">Words the user always wants replaced, whatever they are.</param>
    /// <param name="knowledge">
    /// Optional dictionary. With it, a capitalised word that no dictionary knows is treated as
    /// a name - which is what catches surnames nobody could put on a list. Without it, only the
    /// first-name list, salutations and the user's own words are used.
    /// </param>
    public static PrivacyMask Create(
        string input,
        IReadOnlyList<string>? protectedTerms = null,
        IWordKnowledge? knowledge = null)
    {
        ArgumentNullException.ThrowIfNull(input);

        var spans = new List<Span>();

        CollectProtectedTerms(input, protectedTerms, spans);
        CollectContacts(input, spans);
        CollectPeople(input, spans);
        CollectUnknownCapitalised(input, knowledge, spans);

        if (spans.Count == 0)
        {
            return new PrivacyMask(input, new Dictionary<string, string>(StringComparer.Ordinal));
        }

        return Build(input, spans);
    }

    // ---------------------------------------------------------------- finding

    private readonly record struct Span(int Start, int Length, SpanKind Kind)
    {
        public int End => Start + Length;
    }

    private enum SpanKind
    {
        GivenName,
        Surname,
        Email,
        Phone,
        Iban,
    }

    /// <summary>Words the user listed. Whole words only, so "Ana" does not hit "Banane".</summary>
    private static void CollectProtectedTerms(string input, IReadOnlyList<string>? terms, List<Span> spans)
    {
        if (terms is null)
        {
            return;
        }

        foreach (var raw in terms)
        {
            var term = raw?.Trim();
            if (string.IsNullOrEmpty(term))
            {
                continue;
            }

            var from = 0;
            while (from < input.Length)
            {
                var index = input.IndexOf(term, from, StringComparison.OrdinalIgnoreCase);
                if (index < 0)
                {
                    break;
                }

                if (IsWholeWord(input, index, term.Length))
                {
                    spans.Add(new Span(index, term.Length, SpanKind.Surname));
                }

                from = index + term.Length;
            }
        }
    }

    /// <summary>Addresses and numbers. These are unambiguous, so they are matched directly.</summary>
    private static void CollectContacts(string input, List<Span> spans)
    {
        for (var index = 0; index < input.Length; index++)
        {
            if (input[index] == '@')
            {
                var span = ReadEmail(input, index);
                if (span is not null)
                {
                    spans.Add(span.Value);
                }

                continue;
            }

            if (char.IsAsciiDigit(input[index]) || input[index] == '+')
            {
                var phone = ReadPhone(input, index);
                if (phone is not null)
                {
                    spans.Add(phone.Value);
                    index = phone.Value.End - 1;
                }

                continue;
            }

            if (char.IsAsciiLetterUpper(input[index]))
            {
                var iban = ReadIban(input, index);
                if (iban is not null)
                {
                    spans.Add(iban.Value);
                    index = iban.Value.End - 1;
                }
            }
        }
    }

    private static Span? ReadEmail(string input, int at)
    {
        var start = at;
        while (start > 0 && IsEmailChar(input[start - 1]))
        {
            start--;
        }

        var end = at + 1;
        while (end < input.Length && IsEmailChar(input[end]))
        {
            end++;
        }

        // Trim a trailing dot that belongs to the sentence, not the address.
        while (end > at && input[end - 1] == '.')
        {
            end--;
        }

        var local = at - start;
        var domain = input.AsSpan(at + 1, end - at - 1);

        return local > 0 && domain.Contains('.') && domain.Length >= 3
            ? new Span(start, end - start, SpanKind.Email)
            : null;

        static bool IsEmailChar(char c) =>
            char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-' or '+';
    }

    private static Span? ReadPhone(string input, int start)
    {
        if (start > 0 && (char.IsAsciiDigit(input[start - 1]) || char.IsAsciiLetter(input[start - 1])))
        {
            return null;
        }

        var end = start;
        var digits = 0;

        while (end < input.Length)
        {
            var c = input[end];
            if (char.IsAsciiDigit(c))
            {
                digits++;
                end++;
                continue;
            }

            if (c is ' ' or '/' or '-' or '(' or ')' or '+' or '.')
            {
                // A separator only continues the number if another digit follows it.
                var next = end + 1;
                while (next < input.Length && input[next] is ' ' or '(' or ')')
                {
                    next++;
                }

                if (next < input.Length && char.IsAsciiDigit(input[next]))
                {
                    end = next;
                    continue;
                }
            }

            break;
        }

        // Seven digits is the shortest thing worth treating as a phone number; below that it is
        // a year, a price or a house number.
        if (digits < 7)
        {
            return null;
        }

        while (end > start && input[end - 1] is ' ' or '.' or '-' or '/')
        {
            end--;
        }

        return new Span(start, end - start, SpanKind.Phone);
    }

    private static Span? ReadIban(string input, int start)
    {
        if (start + 4 >= input.Length || !IsWholeWordStart(input, start))
        {
            return null;
        }

        if (!char.IsAsciiLetterUpper(input[start + 1]) ||
            !char.IsAsciiDigit(input[start + 2]) ||
            !char.IsAsciiDigit(input[start + 3]))
        {
            return null;
        }

        var end = start + 4;
        var characters = 4;

        while (end < input.Length)
        {
            var c = input[end];
            if (char.IsAsciiLetterOrDigit(c))
            {
                characters++;
                end++;
                continue;
            }

            if (c == ' ' && end + 1 < input.Length && char.IsAsciiLetterOrDigit(input[end + 1]))
            {
                end++;
                continue;
            }

            break;
        }

        while (end > start && input[end - 1] == ' ')
        {
            end--;
        }

        // An IBAN is 15 to 34 characters; anything shorter is a word in capitals.
        return characters is >= 15 and <= 34 ? new Span(start, end - start, SpanKind.Iban) : null;
    }

    /// <summary>People, by salutation or by a known first name.</summary>
    private static void CollectPeople(string input, List<Span> spans)
    {
        var index = 0;

        while (index < input.Length)
        {
            if (!IsWordStart(input, index))
            {
                index++;
                continue;
            }

            var end = WordEnd(input, index);
            var word = input[index..end];

            if (IsSalutation(input, index, end, word))
            {
                // Everything capitalised after the salutation belongs to the name:
                // "Herr Dr. Müller", "Frau Anna Meier".
                var cursor = SkipNameSeparator(input, end);

                while (cursor < input.Length && IsWordStart(input, cursor) && char.IsUpper(input[cursor]))
                {
                    var nameEnd = WordEnd(input, cursor);
                    var candidate = input[cursor..nameEnd];

                    if (IsSalutation(input, cursor, nameEnd, candidate))
                    {
                        cursor = SkipNameSeparator(input, nameEnd);
                        continue;
                    }

                    spans.Add(new Span(cursor, nameEnd - cursor, SpanKind.Surname));
                    cursor = SkipNameSeparator(input, nameEnd);

                    // One given name plus one surname is the realistic maximum; going further
                    // would swallow the rest of a capitalised sentence.
                    if (spans.Count > 0 && cursor >= input.Length)
                    {
                        break;
                    }

                    if (!(cursor < input.Length && IsWordStart(input, cursor) && char.IsUpper(input[cursor])))
                    {
                        break;
                    }

                    var secondEnd = WordEnd(input, cursor);
                    spans.Add(new Span(cursor, secondEnd - cursor, SpanKind.Surname));
                    cursor = secondEnd;
                    break;
                }

                index = Math.Max(end, cursor);
                continue;
            }

            if (FirstNames.IsKnown(word))
            {
                spans.Add(new Span(index, end - index, SpanKind.GivenName));

                // The capitalised word right after a first name is the surname. Not if it opens
                // a new sentence - "Hallo Anna. Der Termin ..." must not lose "Der".
                var after = SkipSpaces(input, end);
                if (after > end &&
                    after < input.Length &&
                    char.IsUpper(input[after]) &&
                    IsWordStart(input, after) &&
                    !EndsSentence(input, end, after))
                {
                    var surnameEnd = WordEnd(input, after);
                    var surname = input[after..surnameEnd];

                    if (!FirstNames.IsKnown(surname) && !IsSalutation(input, after, surnameEnd, surname))
                    {
                        spans.Add(new Span(after, surnameEnd - after, SpanKind.Surname));
                        index = surnameEnd;
                        continue;
                    }
                }
            }

            index = end;
        }
    }

    /// <summary>
    /// Capitalised words that no dictionary knows. This is the rule that catches a surname
    /// nobody listed and no first name introduced - "Ich habe mit Brunnenwieser gesprochen."
    ///
    /// Everything that is capitalised for another reason has to survive: abbreviations, product
    /// names in capitals, anything with a digit in it, and above all a merely misspelled noun,
    /// which the dictionary recognises as close to a real word.
    /// </summary>
    private static void CollectUnknownCapitalised(string input, IWordKnowledge? knowledge, List<Span> spans)
    {
        if (knowledge is null)
        {
            return;
        }

        var index = 0;

        while (index < input.Length)
        {
            if (!IsWordStart(input, index) || !char.IsUpper(input[index]))
            {
                index++;
                continue;
            }

            var end = WordEnd(input, index);
            var word = input[index..end];

            if (CanBeAName(word) && !knowledge.IsDictionaryWord(word))
            {
                spans.Add(new Span(index, end - index, SpanKind.Surname));
            }

            index = Math.Max(end, index + 1);
        }
    }

    /// <summary>Shape test before the dictionary is asked at all.</summary>
    private static bool CanBeAName(string word)
    {
        // Two letters is an abbreviation or an initial, not something worth masking.
        if (word.Length < 3)
        {
            return false;
        }

        var lowerCount = 0;

        foreach (var c in word)
        {
            if (char.IsDigit(c))
            {
                // "Version2", "A4" - not a person.
                return false;
            }

            if (char.IsLower(c))
            {
                lowerCount++;
            }
        }

        // All capitals is an abbreviation: GMBH, PDF, AG. A name has lower case letters in it.
        return lowerCount > 0;
    }

    // ---------------------------------------------------------------- building

    private static PrivacyMask Build(string input, List<Span> spans)
    {
        // Overlaps happen: a user term inside a salutation, a phone number inside an IBAN.
        // The earlier and longer span wins.
        var ordered = spans
            .OrderBy(s => s.Start)
            .ThenByDescending(s => s.Length)
            .ToList();

        var chosen = new List<Span>();
        var reachedTo = 0;

        foreach (var span in ordered)
        {
            if (span.Start < reachedTo)
            {
                continue;
            }

            chosen.Add(span);
            reachedTo = span.End;
        }

        var forwards = new Dictionary<string, string>(StringComparer.Ordinal);
        var backwards = new Dictionary<string, string>(StringComparer.Ordinal);
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var builder = new StringBuilder(input.Length);
        var copiedTo = 0;

        foreach (var span in chosen)
        {
            var original = input.Substring(span.Start, span.Length);

            if (!forwards.TryGetValue(original, out var substitute))
            {
                substitute = NextSubstitute(span.Kind, input, used, forwards.Count);
                forwards[original] = substitute;
                backwards[substitute] = original;
                used.Add(substitute);
            }

            builder.Append(input, copiedTo, span.Start - copiedTo);
            builder.Append(substitute);
            copiedTo = span.End;
        }

        builder.Append(input, copiedTo, input.Length - copiedTo);
        return new PrivacyMask(builder.ToString(), backwards);
    }

    /// <summary>A stand-in that does not already occur in the text, so the swap back is unique.</summary>
    private static string NextSubstitute(SpanKind kind, string input, HashSet<string> used, int ordinal)
    {
        switch (kind)
        {
            case SpanKind.Email:
                return Unique($"kontakt{{0}}@example.com", input, used, ordinal);
            case SpanKind.Phone:
                return Unique("+41 00 000 00 {0:00}", input, used, ordinal);
            case SpanKind.Iban:
                return Unique("CH00 0000 0000 0000 {0:0000} 0", input, used, ordinal);
            case SpanKind.GivenName:
                return FromPool(GivenNameSubstitutes, input, used, ordinal);
            default:
                return FromPool(SurnameSubstitutes, input, used, ordinal);
        }
    }

    private static string FromPool(string[] pool, string input, HashSet<string> used, int ordinal)
    {
        for (var offset = 0; offset < pool.Length; offset++)
        {
            var candidate = pool[(ordinal + offset) % pool.Length];
            if (!used.Contains(candidate) &&
                input.IndexOf(candidate, StringComparison.OrdinalIgnoreCase) < 0)
            {
                return candidate;
            }
        }

        // Every stand-in is taken; number them instead of risking an ambiguous swap back.
        return Unique(pool[0] + "{0}", input, used, ordinal);
    }

    private static string Unique(string format, string input, HashSet<string> used, int ordinal)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var candidate = string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                format,
                ordinal + attempt + 1);

            if (!used.Contains(candidate) &&
                input.IndexOf(candidate, StringComparison.OrdinalIgnoreCase) < 0)
            {
                return candidate;
            }
        }

        return string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            format,
            Environment.TickCount & 0xffff);
    }

    // ---------------------------------------------------------------- word helpers

    private static bool IsSalutation(string input, int start, int end, string word)
    {
        var bare = word.TrimEnd('.');
        if (Array.IndexOf(Salutations, bare) < 0)
        {
            return false;
        }

        // "Frau" is also an ordinary noun. Treat it as a salutation only when a capitalised
        // word follows it, which is what a name looks like.
        var after = SkipNameSeparator(input, end);
        return after < input.Length && char.IsUpper(input[after]) && IsWordStart(input, after);
    }

    /// <summary>Skips what stands between the parts of a name: spaces and an abbreviation dot.</summary>
    private static int SkipNameSeparator(string input, int index)
    {
        while (index < input.Length && (input[index] is ' ' or '\t' or '\u00a0' or '.'))
        {
            index++;
        }

        return index;
    }

    private static int SkipSpaces(string input, int index)
    {
        while (index < input.Length && (input[index] is ' ' or '\t' or '\u00a0'))
        {
            index++;
        }

        return index;
    }

    /// <summary>True when a sentence ends between the two positions.</summary>
    private static bool EndsSentence(string input, int from, int to)
    {
        for (var index = from; index < to && index < input.Length; index++)
        {
            if (input[index] is '.' or '!' or '?' or '\n')
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsWordStart(string input, int index) =>
        index < input.Length && char.IsLetter(input[index]) && IsWholeWordStart(input, index);

    private static bool IsWholeWordStart(string input, int index) =>
        index == 0 || !char.IsLetterOrDigit(input[index - 1]);

    private static int WordEnd(string input, int index)
    {
        while (index < input.Length && (char.IsLetter(input[index]) || input[index] == '-'))
        {
            index++;
        }

        while (index > 0 && input[index - 1] == '-')
        {
            index--;
        }

        return index;
    }

    private static bool IsWholeWord(string input, int start, int length)
    {
        var before = start == 0 || !char.IsLetterOrDigit(input[start - 1]);
        var after = start + length >= input.Length || !char.IsLetterOrDigit(input[start + length]);
        return before && after;
    }
}
