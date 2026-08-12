namespace AutoCorrect.Core.Engines;

/// <summary>
/// The single abstraction the user interface works against. Concrete engines
/// (LanguageTool, a local LLM, a LAN server) are interchangeable and must never
/// be referenced directly from UI code.
/// </summary>
public interface ITextEngine
{
    /// <summary>Short technical name, shown in the popup status line.</summary>
    string Name { get; }

    /// <summary>True when this engine can handle the given mode.</summary>
    bool SupportsMode(ProcessingMode mode);

    /// <summary>Cheap reachability probe. Must never throw and must never block for long.</summary>
    Task<bool> IsAvailableAsync(CancellationToken ct);

    /// <summary>
    /// Processes <paramref name="input"/> and yields the result incrementally.
    /// Engines that cannot stream (LanguageTool) yield exactly one element.
    /// Throws <see cref="EngineUnavailableException"/> when the backing service is not reachable.
    /// </summary>
    IAsyncEnumerable<string> ProcessAsync(
        string input,
        ProcessingMode mode,
        CancellationToken ct);
}

/// <summary>The four operations offered in the popup.</summary>
public enum ProcessingMode
{
    Correct,
    Rephrase,
    Formal,
    Shorten,
}
