namespace AutoCorrect.Core.Engines;

/// <summary>
/// Thrown when an engine cannot reach its backing service. The message is shown to
/// the user verbatim, so it is written in German and contains a hint on how to fix it.
/// </summary>
public sealed class EngineUnavailableException : Exception
{
    public EngineUnavailableException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}
