using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using AutoCorrect.Core.Diagnostics;
using AutoCorrect.Core.Engines;
using Microsoft.Data.Sqlite;

namespace AutoCorrect.Core.Caching;

/// <summary>
/// Remembers what the language model produced for a given text, mode and model, so asking for
/// the same thing twice is instant instead of costing another generation.
///
/// The database lives in %LOCALAPPDATA% rather than %APPDATA%: it is derived data, it can be
/// deleted at any time, and it must not travel with a roaming profile — the entries contain
/// the user's own text.
///
/// Everything here treats failure as "no cache". A broken or locked database must never stop a
/// correction; it only makes it slower.
/// </summary>
public sealed class ResultCache : IDisposable
{
    /// <summary>Entries kept at most; the oldest are dropped first.</summary>
    public const int MaxEntries = 5000;

    /// <summary>How many entries may pile up above the limit before a cleanup runs.</summary>
    private const int EvictionSlack = 100;

    /// <summary>Unit separator between the parts of a cache key; it never occurs in real text.</summary>
    private const char Separator = '\u001f';

    private readonly string _filePath;
    private readonly object _gate = new();

    private SqliteConnection? _connection;
    private bool _broken;
    private bool _disposed;
    private int _writesSinceEviction;

    public ResultCache(string? filePath = null)
    {
        _filePath = filePath ?? DefaultFilePath;
    }

    public static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AutoCorrect");

    public static string DefaultFilePath => Path.Combine(DefaultDirectory, "cache.db");

    public string FilePath => _filePath;

    /// <summary>Returns the stored result, or null when nothing is stored (or the cache is broken).</summary>
    public string? TryGet(string input, ProcessingMode mode, string model)
    {
        ArgumentNullException.ThrowIfNull(input);

        lock (_gate)
        {
            var connection = Open();
            if (connection is null)
            {
                return null;
            }

            try
            {
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT value FROM entries WHERE key = $key;";
                command.Parameters.AddWithValue("$key", BuildKey(input, mode, model));

                // Interpreted as text; a NULL row simply means "not cached".
                return command.ExecuteScalar() as string;
            }
            catch (SqliteException ex)
            {
                Fail("The cache could not be read.", ex);
                return null;
            }
        }
    }

    /// <summary>Stores a result. Blank results are not worth a row.</summary>
    public void Set(string input, ProcessingMode mode, string model, string result)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (string.IsNullOrWhiteSpace(result))
        {
            return;
        }

        lock (_gate)
        {
            var connection = Open();
            if (connection is null)
            {
                return;
            }

            try
            {
                using var command = connection.CreateCommand();
                command.CommandText =
                    """
                    INSERT INTO entries (key, value, created_at) VALUES ($key, $value, $now)
                    ON CONFLICT(key) DO UPDATE SET value = excluded.value, created_at = excluded.created_at;
                    """;
                command.Parameters.AddWithValue("$key", BuildKey(input, mode, model));
                command.Parameters.AddWithValue("$value", result);
                command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                command.ExecuteNonQuery();

                // Counting rows on every write would double the cost of a cheap insert.
                if (++_writesSinceEviction >= EvictionSlack)
                {
                    _writesSinceEviction = 0;
                    Evict(connection);
                }
            }
            catch (SqliteException ex)
            {
                Fail("The cache could not be written.", ex);
            }
        }
    }

    /// <summary>Drops every entry. Offered in the settings because the rows hold the user's text.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            var connection = Open();
            if (connection is null)
            {
                return;
            }

            try
            {
                using var command = connection.CreateCommand();
                command.CommandText = "DELETE FROM entries;";
                command.ExecuteNonQuery();
                _writesSinceEviction = 0;
            }
            catch (SqliteException ex)
            {
                Fail("The cache could not be cleared.", ex);
            }
        }
    }

    /// <summary>Number of stored entries, or 0 when the cache is unusable.</summary>
    public long Count()
    {
        lock (_gate)
        {
            var connection = Open();
            if (connection is null)
            {
                return 0;
            }

            try
            {
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT COUNT(*) FROM entries;";
                return command.ExecuteScalar() as long? ?? 0;
            }
            catch (SqliteException ex)
            {
                Fail("The cache could not be counted.", ex);
                return 0;
            }
        }
    }

    /// <summary>
    /// SHA-256 over text, mode and model. Hashing keeps the row key a fixed size and means the
    /// raw text is not repeated in the index, but note that the value column still holds plain
    /// text — the cache is a convenience, not a protection.
    /// </summary>
    internal static string BuildKey(string input, ProcessingMode mode, string model)
    {
        // U+001F separates the parts so no combination of text and model can collide with
        // another one by concatenation.
        var material = $"{input}{Separator}{mode}{Separator}{model}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        return Convert.ToHexString(hash);
    }

    private SqliteConnection? Open()
    {
        if (_broken || _disposed)
        {
            return null;
        }

        if (_connection is not null)
        {
            return _connection;
        }

        try
        {
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = _filePath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Private,
            }.ToString());

            connection.Open();

            using (var pragma = connection.CreateCommand())
            {
                // WAL survives a hard shutdown without corrupting the file, and NORMAL avoids an
                // fsync per insert. Losing the newest cache entry after a crash costs nothing.
                pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL;";
                pragma.ExecuteNonQuery();
            }

            using (var schema = connection.CreateCommand())
            {
                schema.CommandText =
                    """
                    CREATE TABLE IF NOT EXISTS entries (
                        key TEXT PRIMARY KEY,
                        value TEXT NOT NULL,
                        created_at INTEGER NOT NULL
                    );
                    CREATE INDEX IF NOT EXISTS idx_entries_created_at ON entries (created_at);
                    """;
                schema.ExecuteNonQuery();
            }

            _connection = connection;
            return _connection;
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException)
        {
            Fail($"The cache at '{_filePath}' could not be opened; running without it.", ex);
            return null;
        }
    }

    private void Evict(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            DELETE FROM entries WHERE key IN (
                SELECT key FROM entries ORDER BY created_at DESC, key LIMIT -1 OFFSET $keep
            );
            """;
        command.Parameters.AddWithValue("$keep", MaxEntries);

        var removed = command.ExecuteNonQuery();
        if (removed > 0)
        {
            Log.Debug($"Cache eviction removed {removed} entries.");
        }
    }

    /// <summary>
    /// One failure is enough: after that the cache stays out of the way for the rest of the
    /// session instead of logging the same error on every keystroke.
    /// </summary>
    private void Fail(string message, Exception ex)
    {
        _broken = true;
        Log.Warn(message, ex);

        try
        {
            _connection?.Dispose();
        }
        catch (SqliteException)
        {
        }

        _connection = null;
    }

    /// <summary>Forces the next call to try again, for example after the user cleared a problem.</summary>
    internal void ResetForTests()
    {
        lock (_gate)
        {
            _broken = false;
            _writesSinceEviction = 0;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            try
            {
                _connection?.Dispose();
            }
            catch (SqliteException ex)
            {
                Log.Debug($"Closing the cache failed: {ex.SqliteErrorCode.ToString(CultureInfo.InvariantCulture)}");
            }

            _connection = null;
        }
    }
}
