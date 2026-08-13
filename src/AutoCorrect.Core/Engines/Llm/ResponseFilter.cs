using System.Text;

namespace AutoCorrect.Core.Engines.Llm;

/// <summary>
/// Removes the wrapping that small instruct models add even when the prompt forbids it:
/// a leading "Hier ist die umformulierte Version:", quotation marks around the whole answer,
/// or the --- fences from the prompt echoed back.
///
/// Whatever slips through here lands in the user's document, so the filter is deliberately
/// narrow: it only removes what is recognisably an artefact, and when in doubt it removes
/// nothing.
///
/// It works on a stream and gives up as little of that as possible. The opening is undecided
/// only while the text could still turn into an artefact - usually a handful of characters -
/// and then everything flows straight through. An answer that starts with a quotation mark is
/// the exception: whether that mark is wrapping or part of the text is only known once the end
/// has arrived, so such an answer is held back completely.
/// </summary>
internal sealed class ResponseFilter
{
    /// <summary>A preamble line ends with a colon and is short.</summary>
    private const int MaxPreambleLength = 90;

    /// <summary>Characters kept back so a trailing fence can still be removed.</summary>
    private const int TailReserve = 8;

    /// <summary>
    /// Openings of an announcement line. Matched against the start of the first line, not
    /// anywhere inside it: a legitimate "Wichtig:" or "Traktanden:" has to survive.
    /// </summary>
    private static readonly string[] PreambleMarkers =
    [
        "hier ist", "hier sind", "hier die", "hier der", "hier das", "hier eine", "hier folgt",
        "here is", "here's", "here are", "sure", "certainly", "of course", "gerne", "klar",
        "umformuliert", "überarbeitet", "ueberarbeitet", "gekürzt", "gekuerzt", "förmlich",
        "foermlich", "neue version", "kürzere version", "kuerzere version", "formelle version",
        "rephrased", "rewritten", "revised", "shortened", "formal version", "korrigiert",
    ];

    private static readonly char[] OpeningQuotes = ['"', '“', '„', '«', '‘'];
    private static readonly char[] ClosingQuotes = ['"', '”', '“', '»', '’'];

    private readonly StringBuilder _buffer = new();
    private readonly bool _inputStartsQuoted;

    private State _state = State.Deciding;

    /// <param name="input">
    /// The original text. When it is itself quoted, the answer may legitimately start with a
    /// quotation mark and nothing is stripped.
    /// </param>
    public ResponseFilter(string input)
    {
        var trimmed = input.AsSpan().TrimStart();
        _inputStartsQuoted = trimmed.Length > 0 && Array.IndexOf(OpeningQuotes, trimmed[0]) >= 0;
    }

    private enum State
    {
        /// <summary>The opening could still be an artefact; nothing is handed out yet.</summary>
        Deciding,

        /// <summary>Plain text: everything but a short tail goes straight through.</summary>
        Streaming,

        /// <summary>Quoted answer: the whole thing is held back until the closing mark is known.</summary>
        Buffering,
    }

    /// <summary>Feeds one streamed token in and returns what is safe to show already.</summary>
    public string Push(string token)
    {
        _buffer.Append(token);

        if (_state == State.Deciding)
        {
            Decide(endOfStream: false);
        }

        return _state == State.Streaming ? Release() : string.Empty;
    }

    /// <summary>Returns the rest once the stream has finished.</summary>
    public string Finish()
    {
        if (_state == State.Deciding)
        {
            Decide(endOfStream: true);
        }

        TrimEnd();
        DropTrailingFence();
        TrimEnd();

        if (_state == State.Buffering)
        {
            DropQuotePair();
        }

        var rest = _buffer.ToString();
        _buffer.Clear();
        return rest;
    }

    /// <summary>Hands out everything except the reserved tail.</summary>
    private string Release()
    {
        if (_buffer.Length <= TailReserve)
        {
            return string.Empty;
        }

        var count = _buffer.Length - TailReserve;
        var text = _buffer.ToString(0, count);
        _buffer.Remove(0, count);
        return text;
    }

    /// <summary>
    /// Decides what the beginning is. Leaves the state at <see cref="State.Deciding"/> while
    /// more characters are needed, unless the stream has ended and a decision has to be made.
    /// </summary>
    private void Decide(bool endOfStream)
    {
        while (true)
        {
            TrimStart();

            if (_buffer.Length == 0)
            {
                // Only whitespace so far. At the end of the stream that is the whole answer.
                if (endOfStream)
                {
                    _state = State.Streaming;
                }

                return;
            }

            // A fence has to be a line of its own, so it is only recognisable once the line
            // break arrives.
            var fence = MeasureLeadingFence(endOfStream);
            if (fence == Undecided)
            {
                return;
            }

            if (fence > 0)
            {
                _buffer.Remove(0, fence);
                continue;
            }

            if (!_inputStartsQuoted && Array.IndexOf(OpeningQuotes, _buffer[0]) >= 0)
            {
                // Keep the mark: only the end of the answer shows whether it was wrapping.
                _state = State.Buffering;
                return;
            }

            var preamble = MeasureLeadingPreamble(endOfStream);
            if (preamble == Undecided)
            {
                return;
            }

            if (preamble > 0)
            {
                _buffer.Remove(0, preamble);
                continue;
            }

            _state = State.Streaming;
            return;
        }
    }

    private const int Undecided = -1;

    /// <summary>
    /// Length of a leading "---" line including its line break, 0 when there is none, and
    /// <see cref="Undecided"/> while the line has not finished arriving.
    /// </summary>
    private int MeasureLeadingFence(bool endOfStream)
    {
        var dashes = 0;
        while (dashes < _buffer.Length && _buffer[dashes] == '-')
        {
            dashes++;
        }

        if (dashes == 0)
        {
            return 0;
        }

        if (dashes < 3)
        {
            // Could still grow into a fence, or be a dash that belongs to the text.
            return dashes == _buffer.Length && !endOfStream ? Undecided : 0;
        }

        var index = dashes;
        while (index < _buffer.Length && (_buffer[index] == ' ' || _buffer[index] == '\r'))
        {
            index++;
        }

        if (index >= _buffer.Length)
        {
            // Nothing after the dashes yet; at the end of the stream an answer of only dashes
            // is not a fence to strip, it is all there is.
            return endOfStream ? 0 : Undecided;
        }

        return _buffer[index] == '\n' ? index + 1 : 0;
    }

    /// <summary>
    /// Length of a leading announcement line including its line break, 0 when there is none,
    /// and <see cref="Undecided"/> while it is still unclear.
    /// </summary>
    private int MeasureLeadingPreamble(bool endOfStream)
    {
        var newline = IndexOf(_buffer, '\n');

        if (newline < 0)
        {
            if (_buffer.Length > MaxPreambleLength || endOfStream)
            {
                // Too long to be a preamble, or there is no second line to announce.
                return 0;
            }

            // Wait only while the text could still become an announcement.
            return StartsWithMarker() || CouldStillBecomeMarker() ? Undecided : 0;
        }

        if (newline > MaxPreambleLength)
        {
            return 0;
        }

        var line = _buffer.ToString(0, newline).TrimEnd();
        return line.EndsWith(':') && StartsWithMarker() ? newline + 1 : 0;
    }

    private bool StartsWithMarker()
    {
        foreach (var marker in PreambleMarkers)
        {
            if (Matches(marker, marker.Length))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>True while the buffer is still shorter than a marker it could grow into.</summary>
    private bool CouldStillBecomeMarker()
    {
        foreach (var marker in PreambleMarkers)
        {
            if (_buffer.Length < marker.Length && Matches(marker, _buffer.Length))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Compares the first <paramref name="length"/> characters against the marker.</summary>
    private bool Matches(string marker, int length)
    {
        if (_buffer.Length < length)
        {
            return false;
        }

        for (var index = 0; index < length; index++)
        {
            if (char.ToLowerInvariant(_buffer[index]) != char.ToLowerInvariant(marker[index]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Removes the wrapping quotation marks, but only if both of them are there.</summary>
    private void DropQuotePair()
    {
        if (_buffer.Length < 2 || Array.IndexOf(ClosingQuotes, _buffer[^1]) < 0)
        {
            // An opening mark without a closing one belongs to the text.
            return;
        }

        _buffer.Remove(_buffer.Length - 1, 1);
        _buffer.Remove(0, 1);
        TrimEnd();
    }

    private void DropTrailingFence()
    {
        var end = _buffer.Length;
        var index = end;

        while (index > 0 && _buffer[index - 1] == '-')
        {
            index--;
        }

        if (end - index < 3)
        {
            return;
        }

        // Only a fence on its own line; a dashed word must stay intact.
        if (index > 0 && _buffer[index - 1] is not ('\n' or '\r'))
        {
            return;
        }

        _buffer.Remove(index, end - index);
    }

    private void TrimStart()
    {
        var index = 0;
        while (index < _buffer.Length && char.IsWhiteSpace(_buffer[index]))
        {
            index++;
        }

        if (index > 0)
        {
            _buffer.Remove(0, index);
        }
    }

    private void TrimEnd()
    {
        var end = _buffer.Length;
        while (end > 0 && char.IsWhiteSpace(_buffer[end - 1]))
        {
            end--;
        }

        if (end < _buffer.Length)
        {
            _buffer.Remove(end, _buffer.Length - end);
        }
    }

    private static int IndexOf(StringBuilder builder, char value)
    {
        for (var index = 0; index < builder.Length; index++)
        {
            if (builder[index] == value)
            {
                return index;
            }
        }

        return -1;
    }
}
