using System.Runtime.CompilerServices;
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

    /// <summary>Name of the engine that last produced a result, for the popup status line.</summary>
    public string? LastUsedName { get; private set; }

    /// <summary>
    /// Runs the first engine that supports the mode. If it reports that its service is not
    /// reachable before producing anything, the next one takes over. That is the fallback chain:
    /// LanguageTool when it runs, the built-in Windows spell checker otherwise.
    ///
    /// Once an engine has produced output the fallback is off - switching mid result would mix
    /// two different answers together.
    /// </summary>
    public async IAsyncEnumerable<string> ProcessAsync(
        string input,
        ProcessingMode mode,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var candidates = _engines.Where(e => e.SupportsMode(mode)).ToList();
        if (candidates.Count == 0)
        {
            throw new EngineUnavailableException(UiText.ModeNotSupported);
        }

        for (var index = 0; index < candidates.Count; index++)
        {
            var engine = candidates[index];
            var isLast = index == candidates.Count - 1;
            var produced = false;

            await using var enumerator = engine.ProcessAsync(input, mode, ct).GetAsyncEnumerator(ct);

            while (true)
            {
                bool moved;
                try
                {
                    moved = await enumerator.MoveNextAsync().ConfigureAwait(false);
                }
                catch (EngineUnavailableException) when (!produced && !isLast)
                {
                    // Nothing was shown to the user yet, so the next engine can take over.
                    break;
                }

                if (!moved)
                {
                    LastUsedName = engine.Name;
                    yield break;
                }

                produced = true;
                LastUsedName = engine.Name;
                yield return enumerator.Current;
            }
        }
    }
}
