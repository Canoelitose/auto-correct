using System.Diagnostics;

namespace AutoCorrect.Core.Tests;

/// <summary>Tiny test harness so the test project needs no NuGet packages.</summary>
public sealed class TestRunner
{
    private readonly List<(string Name, Func<Task> Body)> _tests = new();

    public void Add(string name, Action body) => _tests.Add((name, () =>
    {
        body();
        return Task.CompletedTask;
    }));

    public void Add(string name, Func<Task> body) => _tests.Add((name, body));

    public async Task<int> RunAsync(string? filter = null)
    {
        var passed = 0;
        var failed = 0;
        var stopwatch = Stopwatch.StartNew();

        foreach (var (name, body) in _tests)
        {
            if (!string.IsNullOrEmpty(filter) && !name.Contains(filter, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                await body();
                passed++;
                Console.WriteLine($"  PASS  {name}");
            }
            catch (Exception ex)
            {
                failed++;
                Console.WriteLine($"  FAIL  {name}");
                Console.WriteLine($"        {ex.GetType().Name}: {ex.Message}");
                if (ex is not AssertionException && ex.StackTrace is { } stack)
                {
                    Console.WriteLine(stack);
                }
            }
        }

        stopwatch.Stop();
        Console.WriteLine();
        Console.WriteLine($"{passed} passed, {failed} failed in {stopwatch.ElapsedMilliseconds} ms");
        return failed == 0 ? 0 : 1;
    }
}

public sealed class AssertionException : Exception
{
    public AssertionException(string message)
        : base(message)
    {
    }
}

public static class Assert
{
    public static void Equal<T>(T expected, T actual, string? because = null)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new AssertionException(
                $"expected <{Format(expected)}>, got <{Format(actual)}>{(because is null ? "" : $" ({because})")}");
        }
    }

    public static void True(bool condition, string message = "expected true")
    {
        if (!condition)
        {
            throw new AssertionException(message);
        }
    }

    public static void False(bool condition, string message = "expected false") => True(!condition, message);

    public static void NotNull(object? value, string message = "expected not null")
    {
        if (value is null)
        {
            throw new AssertionException(message);
        }
    }

    public static void Contains(string expectedFragment, string actual)
    {
        if (actual is null || !actual.Contains(expectedFragment, StringComparison.Ordinal))
        {
            throw new AssertionException($"expected to find <{expectedFragment}> in <{Format(actual)}>");
        }
    }

    public static async Task<TException> ThrowsAsync<TException>(Func<Task> action)
        where TException : Exception
    {
        try
        {
            await action();
        }
        catch (TException ex)
        {
            return ex;
        }
        catch (Exception ex)
        {
            throw new AssertionException($"expected {typeof(TException).Name}, got {ex.GetType().Name}: {ex.Message}");
        }

        throw new AssertionException($"expected {typeof(TException).Name}, but no exception was thrown");
    }

    public static TException Throws<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException ex)
        {
            return ex;
        }
        catch (Exception ex)
        {
            throw new AssertionException($"expected {typeof(TException).Name}, got {ex.GetType().Name}: {ex.Message}");
        }

        throw new AssertionException($"expected {typeof(TException).Name}, but no exception was thrown");
    }

    private static string Format<T>(T value) => value switch
    {
        null => "null",
        string s => s.Replace("\n", "\\n", StringComparison.Ordinal),
        _ => value.ToString() ?? "",
    };
}
