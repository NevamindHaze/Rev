using Microsoft.Data.Sqlite;

namespace Rev.Core.Data;

/// <summary>
/// Rev's database: one SQLite file (rev.db) holding your decks, cards, review history and settings.
///
/// This file opens the database and creates its tables. The other RevDatabase.*.cs files
/// hold the reading and writing for each kind of thing (decks, cards, reviews, settings).
/// Times are stored as milliseconds since 1970 (UTC), so nothing depends on the time zone.
/// </summary>
public sealed partial class RevDatabase : IDisposable
{
    private readonly SqliteConnection connection;

    /// <summary>Where the database lives: %LOCALAPPDATA%\Rev, or the folder in REV_DATA_DIR (for testing).</summary>
    public static string DefaultFolder =>
        Environment.GetEnvironmentVariable("REV_DATA_DIR") is { Length: > 0 } custom
            ? custom
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Rev");

    public string FilePath { get; }

    /// <summary>Opens (or creates) the database file.</summary>
    public RevDatabase(string filePath)
    {
        FilePath = filePath;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(filePath))!);
        connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = filePath,
            Pooling = false,   // so the file is really closed when Rev closes it
        }.ToString());
        connection.Open();
        Execute("PRAGMA foreign_keys = ON; PRAGMA journal_mode = WAL;");
        CreateTables();
    }

    public static RevDatabase OpenDefault() => new(Path.Combine(DefaultFolder, "rev.db"));

    public void Dispose() => connection.Dispose();

    // ---- Tables ----

    /// <summary>Bumped whenever the tables change, so older files can be upgraded.</summary>
    private const int SchemaVersion = 1;

    private void CreateTables()
    {
        if (Scalar<long>("PRAGMA user_version") >= SchemaVersion) return;

        Execute("""
            CREATE TABLE IF NOT EXISTS decks (
                id          TEXT PRIMARY KEY,
                name        TEXT NOT NULL,
                created_at  INTEGER NOT NULL
            );

            CREATE TABLE IF NOT EXISTS cards (
                id               TEXT PRIMARY KEY,
                deck_id          TEXT NOT NULL REFERENCES decks(id) ON DELETE CASCADE,
                front            TEXT NOT NULL,
                back             TEXT NOT NULL,
                created_at       INTEGER NOT NULL,
                kind             TEXT NOT NULL,
                tags             TEXT NOT NULL,
                suspended        INTEGER NOT NULL,
                leech            INTEGER NOT NULL,
                cloze_source     TEXT,
                cloze_number     INTEGER,
                cloze_extra      TEXT,
                note_id          TEXT NOT NULL,
                note_ord         INTEGER NOT NULL,
                edited_at        INTEGER NOT NULL,
                sched_changed_at INTEGER NOT NULL,
                fields_read_only INTEGER NOT NULL,
                phase            TEXT NOT NULL,
                due              INTEGER NOT NULL,
                interval_days    REAL NOT NULL,
                ease             REAL NOT NULL,
                reps             INTEGER NOT NULL,
                lapses           INTEGER NOT NULL,
                step             INTEGER NOT NULL,
                last_reviewed    INTEGER,
                stability        REAL,
                difficulty       REAL
            );
            -- A card's identity "<note id>:<ord>" is unique, the same as in Anki.
            CREATE UNIQUE INDEX IF NOT EXISTS cards_identity ON cards (note_id, note_ord);
            CREATE INDEX IF NOT EXISTS cards_deck ON cards (deck_id);

            CREATE TABLE IF NOT EXISTS review_logs (
                id            TEXT PRIMARY KEY,
                card_id       TEXT NOT NULL,
                reviewed_at   INTEGER NOT NULL,
                rating        INTEGER NOT NULL,
                was_new       INTEGER NOT NULL,
                interval_days REAL NOT NULL
            );
            CREATE INDEX IF NOT EXISTS review_logs_time ON review_logs (reviewed_at);

            CREATE TABLE IF NOT EXISTS settings (
                key   TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );
            """);
        Execute($"PRAGMA user_version = {SchemaVersion}");
    }

    // ---- Small helpers used by the other files ----

    /// <summary>Runs a command that returns nothing. Values are passed as ("$name", value) pairs.</summary>
    private int Execute(string sql, params (string Name, object? Value)[] values)
    {
        using var command = Command(sql, values);
        return command.ExecuteNonQuery();
    }

    /// <summary>Runs a query that returns a single value, e.g. a count.</summary>
    private T Scalar<T>(string sql, params (string Name, object? Value)[] values)
    {
        using var command = Command(sql, values);
        var result = command.ExecuteScalar();
        return result is null or DBNull ? default! : (T)Convert.ChangeType(result, typeof(T));
    }

    /// <summary>Runs a query and turns each row into something with <paramref name="read"/>.</summary>
    private List<T> Query<T>(string sql, Func<SqliteDataReader, T> read, params (string Name, object? Value)[] values)
    {
        using var command = Command(sql, values);
        using var reader = command.ExecuteReader();
        var rows = new List<T>();
        while (reader.Read()) rows.Add(read(reader));
        return rows;
    }

    private SqliteCommand Command(string sql, (string Name, object? Value)[] values)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in values) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return command;
    }

    /// <summary>Runs several changes as one: either all of them happen, or (if something fails) none do.</summary>
    public void InTransaction(Action work)
    {
        using var transaction = connection.BeginTransaction();
        work();
        transaction.Commit();
    }

    // ---- Converting times ----

    /// <summary>A local time → milliseconds since 1970.</summary>
    internal static long ToMs(DateTime time) => new DateTimeOffset(time).ToUnixTimeMilliseconds();

    /// <summary>Milliseconds since 1970 → a local time.</summary>
    internal static DateTime FromMs(long ms) => DateTimeOffset.FromUnixTimeMilliseconds(ms).LocalDateTime;

    private static long? ToMs(DateTime? time) => time is { } t ? ToMs(t) : null;

    private static DateTime? FromNullableMs(SqliteDataReader reader, int column) =>
        reader.IsDBNull(column) ? null : FromMs(reader.GetInt64(column));
}
