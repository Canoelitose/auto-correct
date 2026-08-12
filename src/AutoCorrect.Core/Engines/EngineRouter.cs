using AutoCorrect.Core.Localization;

namespace AutoCorrect.Core.Engines;

/// <summary>
/// The only <see cref="ITextEngine"/> the UI ever holds. It forwards each mode to the first
/// registered engine that supports it, which is where the phase 3 fallback chain
/// (LAN server, local model, correction only) will be plugged in.
/// </summary>
public sealed class EngineRouter : ITextEngine
{
    private readonly IReadOnlyList<ITextEngine> _engines;

    public EngineRouter(params ITextEngine[] engines)
        : this((IReadOnlyList<ITextEngine>)engines)
    {
    }

    public EngineRouter(IReadOnlyList<ITextEngine> engines)
    {
        _engines = engines ?? throw new ArgumentNullException(nameof(engines));
    }

    public string Name => "Auto";

    public IReadOnlyList<ITextEngine> Engines => _engines;

    public bool SupportsMode(ProcessingMode mode) => Resolve(mode) is not null;

    /// <summary>The engine that would handle the mode, or null when none is registered.</summary>
    public ITextEngine? Resolve(ProcessingMode mode)
    {
        foreach (var engine in _engines)
        {
            if (engine.SupportsMode(mode))
            {
                return engine;
            }
        }

        return null;
    }

    public async Task<bool> IsAvailableAsync(CancellationToken ct)
    {
        foreach (var engine in _engines)
        {
            if (await engine.IsAvailableAsync(ct).ConfigureAwait(false))
            {
                return true;
            }
        }

        return false;
    }

    public IAsyncEnumerable<string> ProcessAsync(string input, ProcessingMode mode, CancellationToken ct)
    {
        var engine = Resolve(mode)
            ?? throw new EngineUnavailableException(UiText.ModeNotSupported);

        return engine.ProcessAsync(input, mode, ct);
    }
}
