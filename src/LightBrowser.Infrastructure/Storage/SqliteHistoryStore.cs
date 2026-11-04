using SudyBrowser.Core.Interfaces;
using SudyBrowser.Core.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace SudyBrowser.Infrastructure.Storage;

/// <summary>
/// Browsing history in a small SQLite file. The connection is opened lazily on a worker thread, all work
/// runs off the UI thread, and the table is capped so memory and query time stay bounded.
/// Failures are logged, never thrown; after a failed open the store stays empty for the session.
/// </summary>
public sealed class SqliteHistoryStore : IHistoryStore, IDisposable
{
    private const int MaxRows = 5000;
    private const int PruneEveryWrites = 200;
    private const int CacheSizeKiB = 1024;
    private const char LikeEscape = '\\';

    private readonly string _connectionString;
    private readonly ILogger<SqliteHistoryStore> _logger;
    private readonly object _sync = new();
    private SqliteConnection? _connection;
    private bool _unavailable;
    private bool _disposed;
    private int _writesSincePrune;

    public SqliteHistoryStore(ILogger<SqliteHistoryStore> logger)
        : this(Path.Combine(AppDataPaths.Root, "history.db"), logger)
    {
    }

    public SqliteHistoryStore(string databasePath, ILogger<SqliteHistoryStore> logger)
    {
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Pooling = false,
        }.ToString();
        _logger = logger;
    }

    public Task RecordVisitAsync(
        string url,
        string title,
        bool countVisit,
        string faviconUrl = "",
        CancellationToken cancellationToken = default) =>
        Task.Run(() => Execute(connection => Upsert(connection, url, title, countVisit, faviconUrl)), CancellationToken.None);

    public Task<IReadOnlyList<HistoryEntry>> SearchAsync(string normalizedQuery, int limit, CancellationToken cancellationToken) =>
        Task.Run(
            () => Execute(connection => Query(connection, normalizedQuery, limit, cancellationToken))
                  ?? Array.Empty<HistoryEntry>(),
            cancellationToken);

    public Task<IReadOnlyList<HistoryEntry>> GetRecentAsync(int limit, CancellationToken cancellationToken = default) =>
        Task.Run(
            () => Execute(connection => QueryRecent(connection, limit, cancellationToken))
                  ?? Array.Empty<HistoryEntry>(),
            cancellationToken);

    public Task DeleteAsync(string url, CancellationToken cancellationToken = default) =>
        Task.Run(
            () => Execute(connection => DeleteUrl(connection, url)),
            cancellationToken);

    public Task DeleteSinceAsync(DateTime cutoffUtc, CancellationToken cancellationToken = default) =>
        Task.Run(
            () => Execute(connection => DeleteSince(connection, cutoffUtc)),
            cancellationToken);

    public void Dispose()
    {
        lock (_sync)
        {
            _disposed = true;
            _connection?.Dispose();
            _connection = null;
        }
    }

    private T? Execute<T>(Func<SqliteConnection, T> work)
    {
        lock (_sync)
        {
            if (_disposed || _unavailable)
            {
                return default;
            }

            try
            {
                _connection ??= Open();
                return work(_connection);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "History database operation failed");
                if (_connection is null)
                {
                    _unavailable = true;
                }

                return default;
            }
        }
    }

    private void Execute(Action<SqliteConnection> work) =>
        Execute<object?>(connection =>
        {
            work(connection);
            return null;
        });

    private SqliteConnection Open()
    {
        var path = new SqliteConnectionStringBuilder(_connectionString).DataSource;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);

        var connection = new SqliteConnection(_connectionString);
        try
        {
            connection.Open();
            Run(connection, "PRAGMA journal_mode=WAL;");
            Run(connection, "PRAGMA synchronous=NORMAL;");
            Run(connection, $"PRAGMA cache_size=-{CacheSizeKiB};");
            Run(connection,
                """
                CREATE TABLE IF NOT EXISTS history (
                    url TEXT PRIMARY KEY NOT NULL,
                    title TEXT NOT NULL,
                    host TEXT NOT NULL COLLATE NOCASE,
                    visit_count INTEGER NOT NULL,
                    last_visit INTEGER NOT NULL,
                    favicon_url TEXT NOT NULL DEFAULT ''
                );
                CREATE INDEX IF NOT EXISTS idx_history_host ON history(host);
                CREATE INDEX IF NOT EXISTS idx_history_last_visit ON history(last_visit);
                """);
            EnsureFaviconColumn(connection);
            Prune(connection);
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    private static void EnsureFaviconColumn(SqliteConnection connection)
    {
        using var check = connection.CreateCommand();
        check.CommandText = "SELECT COUNT(*) FROM pragma_table_info('history') WHERE name = 'favicon_url';";
        var exists = Convert.ToInt32(check.ExecuteScalar()) > 0;
        if (!exists)
        {
            Run(connection, "ALTER TABLE history ADD COLUMN favicon_url TEXT NOT NULL DEFAULT '';");
        }
    }

    private void Upsert(SqliteConnection connection, string url, string title, bool countVisit, string faviconUrl)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO history (url, title, host, visit_count, last_visit, favicon_url)
            VALUES ($url, $title, $host, 1, $now, $favicon)
            ON CONFLICT(url) DO UPDATE SET
                title = excluded.title,
                last_visit = excluded.last_visit,
                visit_count = visit_count + $increment,
                favicon_url = CASE WHEN excluded.favicon_url != '' THEN excluded.favicon_url ELSE history.favicon_url END;
            """;
        command.Parameters.AddWithValue("$url", url);
        command.Parameters.AddWithValue("$title", title);
        command.Parameters.AddWithValue("$host", HostKey(url));
        command.Parameters.AddWithValue("$now", DateTime.UtcNow.Ticks);
        command.Parameters.AddWithValue("$increment", countVisit ? 1 : 0);
        command.Parameters.AddWithValue("$favicon", faviconUrl ?? string.Empty);
        command.ExecuteNonQuery();

        if (++_writesSincePrune >= PruneEveryWrites)
        {
            Prune(connection);
        }
    }

    private static IReadOnlyList<HistoryEntry> Query(
        SqliteConnection connection, string query, int limit, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var escaped = EscapeLike(query);
        using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            SELECT url, title, visit_count, last_visit, favicon_url FROM history
            WHERE host LIKE $prefix ESCAPE '{LikeEscape}'
               OR url LIKE $contains ESCAPE '{LikeEscape}'
               OR title LIKE $contains ESCAPE '{LikeEscape}'
            ORDER BY visit_count DESC, last_visit DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$prefix", escaped + "%");
        command.Parameters.AddWithValue("$contains", "%" + escaped + "%");
        command.Parameters.AddWithValue("$limit", limit);

        var results = new List<HistoryEntry>(limit);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            results.Add(new HistoryEntry(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetInt32(2),
                new DateTime(reader.GetInt64(3), DateTimeKind.Utc),
                reader.IsDBNull(4) ? string.Empty : reader.GetString(4)));
        }

        return results;
    }

    private static IReadOnlyList<HistoryEntry> QueryRecent(
        SqliteConnection connection, int limit, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT url, title, visit_count, last_visit, favicon_url FROM history
            ORDER BY last_visit DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$limit", limit);

        var results = new List<HistoryEntry>(Math.Min(limit, 64));
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            results.Add(new HistoryEntry(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetInt32(2),
                new DateTime(reader.GetInt64(3), DateTimeKind.Utc),
                reader.IsDBNull(4) ? string.Empty : reader.GetString(4)));
        }

        return results;
    }

    private static void DeleteUrl(SqliteConnection connection, string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM history WHERE url = $url;";
        command.Parameters.AddWithValue("$url", url);
        command.ExecuteNonQuery();
    }

    private static void DeleteSince(SqliteConnection connection, DateTime cutoffUtc)
    {
        using var command = connection.CreateCommand();
        if (cutoffUtc <= DateTime.MinValue)
        {
            command.CommandText = "DELETE FROM history;";
        }
        else
        {
            var utcTicks = cutoffUtc.Kind == DateTimeKind.Utc ? cutoffUtc.Ticks : cutoffUtc.ToUniversalTime().Ticks;
            command.CommandText = "DELETE FROM history WHERE last_visit >= $cutoff;";
            command.Parameters.AddWithValue("$cutoff", utcTicks);
        }

        command.ExecuteNonQuery();
    }

    private void Prune(SqliteConnection connection)
    {
        _writesSincePrune = 0;
        using var command = connection.CreateCommand();
        command.CommandText =
            "DELETE FROM history WHERE url IN (SELECT url FROM history ORDER BY last_visit DESC LIMIT -1 OFFSET $max);";
        command.Parameters.AddWithValue("$max", MaxRows);
        command.ExecuteNonQuery();
    }

    private static void Run(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    /// <summary>Host without "www.", used by the prefix match so "you" finds "www.youtube.com".</summary>
    private static string HostKey(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
            ? (uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host[4..] : uri.Host)
            : string.Empty;

    private static string EscapeLike(string text) =>
        text.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
}
