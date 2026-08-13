using AutoCorrect.Core.Caching;
using AutoCorrect.Core.Engines;

namespace AutoCorrect.Core.Tests.Cases;

public static class ResultCacheTests
{
    public static void Register(TestRunner runner)
    {
        runner.Add("Cache: what goes in comes back out", () =>
        {
            using var temp = new TempCacheFile();
            using var cache = new ResultCache(temp.Path);

            Assert.Equal(null, cache.TryGet("Ein Satz.", ProcessingMode.Rephrase, "m"));

            cache.Set("Ein Satz.", ProcessingMode.Rephrase, "m", "Ein anderer Satz.");
            Assert.Equal("Ein anderer Satz.", cache.TryGet("Ein Satz.", ProcessingMode.Rephrase, "m"));
            Assert.Equal(1, cache.Count());
        });

        runner.Add("Cache: text, mode and model each change the key", () =>
        {
            using var temp = new TempCacheFile();
            using var cache = new ResultCache(temp.Path);

            cache.Set("Text", ProcessingMode.Rephrase, "m", "A");

            Assert.Equal(null, cache.TryGet("Text ", ProcessingMode.Rephrase, "m"));
            Assert.Equal(null, cache.TryGet("Text", ProcessingMode.Formal, "m"));
            Assert.Equal(null, cache.TryGet("Text", ProcessingMode.Rephrase, "other"));
            Assert.Equal("A", cache.TryGet("Text", ProcessingMode.Rephrase, "m"));
        });

        runner.Add("Cache: the parts of a key cannot run into each other", () =>
        {
            // Without a separator "ab" + "c" and "a" + "bc" would hash to the same key.
            Assert.False(
                ResultCache.BuildKey("ab", ProcessingMode.Rephrase, "c") ==
                ResultCache.BuildKey("a", ProcessingMode.Rephrase, "bc"),
                "different inputs produced the same cache key");
        });

        runner.Add("Cache: writing the same key again replaces the value", () =>
        {
            using var temp = new TempCacheFile();
            using var cache = new ResultCache(temp.Path);

            cache.Set("Text", ProcessingMode.Shorten, "m", "alt");
            cache.Set("Text", ProcessingMode.Shorten, "m", "neu");

            Assert.Equal("neu", cache.TryGet("Text", ProcessingMode.Shorten, "m"));
            Assert.Equal(1, cache.Count());
        });

        runner.Add("Cache: a blank result is not stored", () =>
        {
            using var temp = new TempCacheFile();
            using var cache = new ResultCache(temp.Path);

            cache.Set("Text", ProcessingMode.Rephrase, "m", "   ");
            Assert.Equal(0, cache.Count());
        });

        runner.Add("Cache: clearing removes everything", () =>
        {
            using var temp = new TempCacheFile();
            using var cache = new ResultCache(temp.Path);

            cache.Set("Eins", ProcessingMode.Rephrase, "m", "A");
            cache.Set("Zwei", ProcessingMode.Rephrase, "m", "B");
            Assert.Equal(2, cache.Count());

            cache.Clear();
            Assert.Equal(0, cache.Count());
            Assert.Equal(null, cache.TryGet("Eins", ProcessingMode.Rephrase, "m"));
        });

        runner.Add("Cache: entries survive a restart", () =>
        {
            using var temp = new TempCacheFile();

            using (var first = new ResultCache(temp.Path))
            {
                first.Set("Text", ProcessingMode.Formal, "m", "Sehr geehrte Damen und Herren");
            }

            using var second = new ResultCache(temp.Path);
            Assert.Equal("Sehr geehrte Damen und Herren", second.TryGet("Text", ProcessingMode.Formal, "m"));
        });

        runner.Add("Cache: the oldest entries are dropped above the limit", () =>
        {
            using var temp = new TempCacheFile();
            using var cache = new ResultCache(temp.Path);

            // Eviction runs in batches, so this needs to go clearly past the limit to trigger.
            const int total = ResultCache.MaxEntries + 200;
            for (var i = 0; i < total; i++)
            {
                cache.Set($"Satz {i}", ProcessingMode.Rephrase, "m", $"Antwort {i}");
            }

            Assert.True(
                cache.Count() <= ResultCache.MaxEntries,
                $"the cache grew to {cache.Count()} entries, above the limit of {ResultCache.MaxEntries}");

            // The newest entry must still be there; the very first ones are the ones to go.
            Assert.Equal($"Antwort {total - 1}", cache.TryGet($"Satz {total - 1}", ProcessingMode.Rephrase, "m"));
            Assert.Equal(null, cache.TryGet("Satz 0", ProcessingMode.Rephrase, "m"));
        });

        runner.Add("Cache: an unusable path does not throw", () =>
        {
            // A directory where a file belongs: the cache must degrade to "no cache", not crash
            // the correction that asked for it.
            var directory = Path.Combine(Path.GetTempPath(), $"autocorrect-cache-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);

            try
            {
                using var cache = new ResultCache(directory);

                cache.Set("Text", ProcessingMode.Rephrase, "m", "A");
                Assert.Equal(null, cache.TryGet("Text", ProcessingMode.Rephrase, "m"));
                Assert.Equal(0, cache.Count());
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        });

        runner.Add("Cache: it lives under LOCALAPPDATA, not in the roaming profile", () =>
        {
            // The rows contain the user's own text; that must not travel with a roaming profile.
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            Assert.Contains(local, ResultCache.DefaultFilePath);
            Assert.Contains("AutoCorrect", ResultCache.DefaultFilePath);
        });
    }
}

/// <summary>A cache file in the temp directory that removes itself, WAL side files included.</summary>
internal sealed class TempCacheFile : IDisposable
{
    public TempCacheFile()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"autocorrect-cache-{Guid.NewGuid():N}.db");
    }

    public string Path { get; }

    public void Dispose()
    {
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            try
            {
                File.Delete(Path + suffix);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
