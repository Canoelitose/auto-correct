using System.Text;

namespace AutoCorrect.Core.Privacy;

/// <summary>
/// Puts the real details back into the streamed answer.
///
/// It has to work on the stream, not on the finished text: the popup fills up while the model
/// writes, and showing "Herr Muster" for a second before it flips to the real name would be
/// worse than not masking at all. A stand-in can arrive split across two tokens, so a short
/// tail is held back - just long enough that no stand-in can straddle the boundary unseen.
/// </summary>
public sealed class MaskRestorer
{
    private readonly IReadOnlyList<KeyValuePair<string, string>> _backwards;
    private readonly int _hold;
    private readonly StringBuilder _buffer = new();

    public MaskRestorer(PrivacyMask mask)
    {
        ArgumentNullException.ThrowIfNull(mask);

        _backwards = mask.Backwards;

        // One character short of the longest stand-in is all that has to be kept: a stand-in
        // that is fully inside the released part is found, and one that is cut is still in the
        // buffer when the rest arrives.
        _hold = _backwards.Count == 0 ? 0 : _backwards.Max(p => p.Key.Length) - 1;
    }

    /// <summary>True when there is nothing to put back, so the text can pass through untouched.</summary>
    public bool IsEmpty => _backwards.Count == 0;

    public string Push(string chunk)
    {
        if (IsEmpty)
        {
            return chunk;
        }

        _buffer.Append(chunk);

        if (_buffer.Length <= _hold)
        {
            return string.Empty;
        }

        var text = Replace(_buffer.ToString());
        _buffer.Clear();

        if (text.Length <= _hold)
        {
            _buffer.Append(text);
            return string.Empty;
        }

        var release = text.Length - _hold;
        _buffer.Append(text, release, _hold);
        return text[..release];
    }

    public string Finish()
    {
        if (IsEmpty || _buffer.Length == 0)
        {
            return string.Empty;
        }

        var rest = Replace(_buffer.ToString());
        _buffer.Clear();
        return rest;
    }

    /// <summary>
    /// Longest stand-in first, so a short one can never eat part of a longer one.
    /// </summary>
    private string Replace(string text)
    {
        foreach (var (substitute, original) in _backwards)
        {
            if (text.Contains(substitute, StringComparison.Ordinal))
            {
                text = text.Replace(substitute, original, StringComparison.Ordinal);
            }
        }

        return text;
    }
}
