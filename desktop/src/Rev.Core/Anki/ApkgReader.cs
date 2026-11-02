using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Rev.Core.Media;
using Rev.Core.Models;
using Rev.Core.Scheduling;
using Rev.Core.Text;

namespace Rev.Core.Anki;

/// <summary>One deck from an Anki file, with its cards (not yet in Rev's database).</summary>
public sealed record ImportedDeck(string Name, List<Card> Cards);

/// <summary>What came out of an Anki file.</summary>
public sealed record ApkgContents(List<ImportedDeck> Decks, int ImagesSaved);

/// <summary>
/// Reads an Anki .apkg (or .colpkg) file. It only reads: the file itself is never changed.
///
/// The file is a zip. Inside is a SQLite database with the notes and cards, plus the media files.
/// There are two formats:
/// - Older ("collection.anki21" / "collection.anki2"): deck names and note types are JSON text in the
///   `col` table, and media files are stored as they are.
/// - Newest ("collection.anki21b"): everything is compressed with zstd, and note types live in their
///   own tables in protobuf format.
/// We fill each card's template with its note's fields, then turn the HTML into Rev's text.
/// Study progress (with FSRS memory), images, tags, suspended cards and the leech tag come across;
/// sounds are skipped.
/// </summary>
public static class ApkgReader
{
    /// <param name="mediaFolder">Where to save the images (Rev's media folder).</param>
    public static ApkgContents Read(string path, string mediaFolder, DateTime? now = null)
    {
        var at = now ?? DateTime.Now;
        ZipArchive archive;
        try
        {
            archive = ZipFile.OpenRead(path);
        }
        catch (InvalidDataException)
        {
            throw new InvalidDataException("This doesn't look like an Anki .apkg or .colpkg file.");
        }

        using (archive)
        {
            // Newest format first: these files also hold an old-format "please update Anki" placeholder.
            var entry = archive.GetEntry("collection.anki21b") ?? archive.GetEntry("collection.anki21")
                        ?? archive.GetEntry("collection.anki2")
                        ?? throw new InvalidDataException("This doesn't look like an Anki .apkg or .colpkg file.");
            bool newFormat = entry.Name == "collection.anki21b";

            // 1. The database, copied to a temporary file (SQLite needs a real file).
            string temp = Path.Combine(Path.GetTempPath(), "rev-import-" + Guid.NewGuid().ToString("N") + ".sqlite");
            try
            {
                byte[] data = ReadEntry(entry);
                File.WriteAllBytes(temp, newFormat ? Zstd.Decompress(data) : data);
                var decks = ReadCollection(temp, at);
                if (decks.Count == 0 || decks.All(d => d.Cards.Count == 0))
                    throw new InvalidDataException("No cards were found in this file.");

                // 2. Images (a problem here shouldn't stop the cards from importing).
                int images = 0;
                try { images = SaveImages(archive, newFormat, mediaFolder); }
                catch (Exception error) when (error is InvalidDataException or IOException) { }
                return new ApkgContents(decks, images);
            }
            finally
            {
                File.Delete(temp);
            }
        }
    }

    private static byte[] ReadEntry(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    // ---- Media ----

    /// <summary>The "media" file lists which zip entry ("0", "1", …) is which file name.</summary>
    private static int SaveImages(ZipArchive archive, bool newFormat, string mediaFolder)
    {
        if (archive.GetEntry("media") is not { } list) return 0;
        byte[] listData = ReadEntry(list);
        var namesByEntry = new Dictionary<string, string>();
        if (newFormat)
        {
            // Protobuf: a list of entries, each with the file name in field 1.
            // The zip entry is its position in the list (or field 255, if set).
            var entries = new ProtobufMessage(Zstd.Decompress(listData)).Messages(1);
            for (int i = 0; i < entries.Count; i++)
                if (entries[i].String(1) is { } name)
                    namesByEntry[entries[i].Number(255)?.ToString() ?? i.ToString()] = name;
        }
        else if (listData.Length > 0)
        {
            namesByEntry = JsonSerializer.Deserialize<Dictionary<string, string>>(listData) ?? [];
        }

        Directory.CreateDirectory(mediaFolder);
        int saved = 0;
        foreach (var (entryName, fileName) in namesByEntry)
        {
            if (!MediaStore.IsImage(fileName) || MediaStore.SafeName(fileName) is not { } safe) continue;
            if (archive.GetEntry(entryName) is not { } file) continue;
            byte[] data = ReadEntry(file);
            File.WriteAllBytes(Path.Combine(mediaFolder, safe), newFormat ? Zstd.Decompress(data) : data);
            saved += 1;
        }
        return saved;
    }

    // ---- Reading the database ----

    private static List<ImportedDeck> ReadCollection(string databasePath, DateTime now)
    {
        using var db = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath, Mode = SqliteOpenMode.ReadOnly, Pooling = false,
        }.ToString());
        db.Open();

        long collectionCreated = (long)(Scalar(db, "SELECT crt FROM col LIMIT 1") ?? 0L);
        bool newTables = Scalar(db, "SELECT name FROM sqlite_master WHERE type = 'table' AND name = 'notetypes'") != null;
        var deckNames = newTables ? DeckNamesFromTables(db) : DeckNamesFromJson(db);
        var noteTypes = newTables ? NoteTypesFromTables(db) : NoteTypesFromJson(db);

        var notes = new Dictionary<long, (long NoteType, string[] Fields, string Guid, string Tags, long Modified)>();
        Query(db, "SELECT id, mid, flds, guid, tags, mod FROM notes", r =>
            notes[r.GetInt64(0)] = (r.GetInt64(1), r.GetString(2).Split('\u001f'), r.GetString(3), r.GetString(4), r.GetInt64(5)));

        // When each card was last reviewed (review ids are times in milliseconds).
        var lastReview = new Dictionary<long, long>();
        if (Scalar(db, "SELECT name FROM sqlite_master WHERE type = 'table' AND name = 'revlog'") != null)
            Query(db, "SELECT cid, MAX(id) FROM revlog GROUP BY cid", r => lastReview[r.GetInt64(0)] = r.GetInt64(1));

        var cardsByDeck = new Dictionary<long, List<Card>>();
        int order = 0;
        Query(db, "SELECT id, nid, did, odid, ord, type, due, odue, ivl, factor, reps, lapses, data, queue, mod " +
                  "FROM cards ORDER BY type, due, id", r =>
        {
            if (!notes.TryGetValue(r.GetInt64(1), out var note) || !noteTypes.TryGetValue(note.NoteType, out var type)) return;
            int ord = r.GetInt32(4);
            var card = type.MakeCard(note.Fields, ord);
            if (card is null) return;

            // Tiny time steps keep Anki's order for new cards.
            order += 1;
            card.CreatedAt = card.Due = now.AddMilliseconds(order);
            // The card was last changed in Anki, not in Rev: it keeps Anki's own times (the note's for the
            // text, the card's for the schedule). These are the same times the Rev Sync add-on reports, so a
            // later sync sees nothing new on either side and never pushes Rev's simpler copy of the text
            // back over Anki's. Editing or studying the card in Rev then makes Rev's copy the newer one.
            card.EditedAt = AnkiTime(note.Modified);
            card.SchedChangedAt = AnkiTime(r.GetInt64(14));
            long deckId = r.GetInt64(3) != 0 ? r.GetInt64(3) : r.GetInt64(2);   // a card in a filtered deck belongs to its original deck
            long due = r.GetInt64(3) != 0 && r.GetInt64(7) != 0 ? r.GetInt64(7) : r.GetInt64(6);
            ApplyProgress(card, type: r.GetInt64(5), due, interval: r.GetInt64(8), factor: r.GetInt64(9),
                          reps: r.GetInt32(10), lapses: r.GetInt32(11), collectionCreated, now);
            if (lastReview.TryGetValue(r.GetInt64(0), out long reviewedMs) && card.Phase != CardPhase.New)
                card.LastReviewed = DateTimeOffset.FromUnixTimeMilliseconds(reviewedMs).LocalDateTime;

            card.NoteId = note.Guid.Length > 0 ? note.Guid : r.GetInt64(1).ToString();
            card.NoteOrd = ord;
            card.IsSuspended = r.GetInt64(13) == -1;
            var tags = Tags.Parse(note.Tags);
            card.IsLeech = tags.Contains("leech", StringComparer.OrdinalIgnoreCase);
            card.TagsText = Tags.Format(tags.Where(t => !t.Equals("leech", StringComparison.OrdinalIgnoreCase)));

            // If Anki used FSRS, it stored the card's memory as {"s": stability, "d": difficulty, …}.
            ReadMemory(card, r.IsDBNull(12) ? "" : r.GetString(12));

            if (!cardsByDeck.TryGetValue(deckId, out var list)) cardsByDeck[deckId] = list = [];
            list.Add(card);
        });

        return cardsByDeck
            .Select(pair => new ImportedDeck(deckNames.GetValueOrDefault(pair.Key, "Imported"), pair.Value))
            .OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// An Anki change time (seconds since 1970) as a local time. A missing time (0) means "long ago",
    /// like Rev on iPhone treats imported cards, so anything changed in Anki or Rev later counts as newer.
    /// </summary>
    public static DateTime AnkiTime(long seconds) =>
        DateTimeOffset.FromUnixTimeSeconds(Math.Max(0, seconds)).LocalDateTime;

    /// <summary>
    /// Copies Anki's study progress onto the card. Anki card types: 0 = new, 1 = learning, 2 = review, 3 = relearning.
    /// </summary>
    private static void ApplyProgress(Card card, long type, long due, long interval, long factor, int reps, int lapses,
                                      long collectionCreated, DateTime now)
    {
        card.Reps = reps;
        card.Lapses = lapses;
        if (factor > 0) card.Ease = factor / 1000.0;
        switch (type)
        {
            case 2:
                // For review cards, "due" counts days since the collection was created.
                card.Phase = CardPhase.Review;
                card.IntervalDays = Math.Max(1, interval);
                card.Due = DateTimeOffset.FromUnixTimeSeconds(collectionCreated + due * 86_400).LocalDateTime;
                break;
            case 1 or 3:
                // Learning steps are minutes away at most: show them again now.
                card.Phase = type == 3 ? CardPhase.Relearning : CardPhase.Learning;
                card.IntervalDays = Math.Max(1, interval);
                card.Due = now;
                break;
        }
    }

    private static void ReadMemory(Card card, string json)
    {
        if (json.Length < 2) return;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("s", out var s) && s.TryGetDouble(out double stability)
                && doc.RootElement.TryGetProperty("d", out var d) && d.TryGetDouble(out double difficulty))
            {
                card.Stability = stability;
                card.Difficulty = difficulty;
            }
        }
        catch (JsonException) { /* no FSRS memory; FSRS will estimate it */ }
    }

    // ---- Older format: JSON in the `col` table ----

    private static Dictionary<long, string> DeckNamesFromJson(SqliteConnection db)
    {
        var names = new Dictionary<long, string>();
        using var doc = JsonDocument.Parse(Scalar(db, "SELECT decks FROM col LIMIT 1") as string ?? "{}");
        foreach (var deck in doc.RootElement.EnumerateObject())
            if (long.TryParse(deck.Name, out long id) && deck.Value.TryGetProperty("name", out var name))
                names[id] = (name.GetString() ?? "").Replace("::", " › ");
        return names;
    }

    private static Dictionary<long, AnkiNoteType> NoteTypesFromJson(SqliteConnection db)
    {
        var result = new Dictionary<long, AnkiNoteType>();
        using var doc = JsonDocument.Parse(Scalar(db, "SELECT models FROM col LIMIT 1") as string ?? "{}");
        foreach (var model in doc.RootElement.EnumerateObject())
        {
            if (!long.TryParse(model.Name, out long id)) continue;
            var m = model.Value;
            var fields = m.TryGetProperty("flds", out var flds)
                ? flds.EnumerateArray().OrderBy(f => f.GetProperty("ord").GetInt32()).Select(f => f.GetProperty("name").GetString() ?? "").ToList()
                : [];
            var templates = new Dictionary<int, (string Front, string Back)>();
            if (m.TryGetProperty("tmpls", out var tmpls))
                foreach (var t in tmpls.EnumerateArray())
                    templates[t.GetProperty("ord").GetInt32()] = (t.GetProperty("qfmt").GetString() ?? "", t.GetProperty("afmt").GetString() ?? "");
            bool cloze = m.TryGetProperty("type", out var type) && type.GetInt32() == 1;
            result[id] = new AnkiNoteType(fields, templates, cloze);
        }
        return result;
    }

    // ---- Newest format: separate tables with protobuf settings ----

    private static Dictionary<long, string> DeckNamesFromTables(SqliteConnection db)
    {
        var names = new Dictionary<long, string>();
        // Sub-decks are separated by an invisible character (0x1F).
        Query(db, "SELECT id, name FROM decks", r => names[r.GetInt64(0)] = r.GetString(1).Replace("\u001f", " › "));
        return names;
    }

    private static Dictionary<long, AnkiNoteType> NoteTypesFromTables(SqliteConnection db)
    {
        var isCloze = new Dictionary<long, bool>();
        // Field 1 of the note type's settings is its kind: 0 = normal, 1 = cloze.
        Query(db, "SELECT id, config FROM notetypes", r => isCloze[r.GetInt64(0)] = new ProtobufMessage(Blob(r, 1)).Number(1) == 1);

        var fields = new Dictionary<long, List<(int Ord, string Name)>>();
        Query(db, "SELECT ntid, ord, name FROM fields", r =>
        {
            if (!fields.TryGetValue(r.GetInt64(0), out var list)) fields[r.GetInt64(0)] = list = [];
            list.Add((r.GetInt32(1), r.GetString(2)));
        });

        var templates = new Dictionary<long, Dictionary<int, (string, string)>>();
        Query(db, "SELECT ntid, ord, config FROM templates", r =>
        {
            // Field 1 = question template, field 2 = answer template.
            var config = new ProtobufMessage(Blob(r, 2));
            if (!templates.TryGetValue(r.GetInt64(0), out var map)) templates[r.GetInt64(0)] = map = [];
            map[r.GetInt32(1)] = (config.String(1) ?? "", config.String(2) ?? "");
        });

        return isCloze.ToDictionary(pair => pair.Key, pair => new AnkiNoteType(
            fields.GetValueOrDefault(pair.Key, []).OrderBy(f => f.Ord).Select(f => f.Name).ToList(),
            templates.GetValueOrDefault(pair.Key, []), pair.Value));
    }

    // ---- Small SQLite helpers ----

    private static object? Scalar(SqliteConnection db, string sql)
    {
        using var command = db.CreateCommand();
        command.CommandText = sql;
        var value = command.ExecuteScalar();
        return value is DBNull ? null : value;
    }

    private static void Query(SqliteConnection db, string sql, Action<SqliteDataReader> eachRow)
    {
        using var command = db.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        while (reader.Read()) eachRow(reader);
    }

    private static byte[] Blob(SqliteDataReader r, int column) => r.IsDBNull(column) ? [] : (byte[])r.GetValue(column);
}

/// <summary>An Anki "note type": its fields and the card templates that use them.</summary>
public sealed partial class AnkiNoteType(List<string> fieldNames, Dictionary<int, (string Front, string Back)> templates, bool isCloze)
{
    [GeneratedRegex(@"\{\{\s*cloze:\s*([^}]+?)\s*\}\}", RegexOptions.IgnoreCase)]
    private static partial Regex ClozeFieldPattern();

    [GeneratedRegex(@"\{\{\s*type:", RegexOptions.IgnoreCase)]
    private static partial Regex TypePattern();

    /// <summary>One card of a note as a Rev card (text and kind only), or null if it would be empty.</summary>
    public Card? MakeCard(string[] fields, int ord)
    {
        var values = new Dictionary<string, string>();
        for (int i = 0; i < fieldNames.Count && i < fields.Length; i++) values[fieldNames[i]] = fields[i];

        // Cloze note types have one template; `ord` says which cloze number (c1, c2…) this card is.
        (string Front, string Back) template;
        if (isCloze)
        {
            if (templates.Count == 0) return null;
            template = templates.TryGetValue(0, out var first) ? first : templates.Values.First();
        }
        else if (!templates.TryGetValue(ord, out template)) return null;
        int? clozeNumber = isCloze ? ord + 1 : null;

        string front = AnkiText.PlainText(AnkiText.Render(template.Front, values, Cloze.Side.Front, clozeNumber));
        string back = AnkiText.PlainText(AnkiText.Render(template.Back, values, Cloze.Side.Back, clozeNumber));
        if (front.Length == 0) return null;

        var card = new Card { Front = front, Back = back, Kind = CardKind.Basic };
        if (isCloze && ClozeFieldPattern().Match(template.Front) is { Success: true } clozeField)
        {
            // Rev can edit cloze cards: it keeps the full text and the "extra" field separately.
            card.Kind = CardKind.Cloze;
            card.ClozeNumber = clozeNumber;
            card.ClozeSource = AnkiText.PlainText(values.GetValueOrDefault(clozeField.Groups[1].Value, ""));
            card.ClozeExtra = AnkiText.PlainText(values.GetValueOrDefault("Back Extra", values.GetValueOrDefault("Extra", "")));
        }
        else if (TypePattern().IsMatch(template.Front)) card.Kind = CardKind.TypeIn;
        else if (!isCloze && templates.Count == 2) card.Kind = CardKind.Reversed;

        // Note types other than Anki's standard ones keep their text as Anki shows it; editing it in Rev
        // changes only Rev's copy (this matters later for sync).
        card.FieldsReadOnly = !(isCloze ? fieldNames.Contains("Text") : fieldNames.SequenceEqual(["Front", "Back"]));
        return card;
    }
}
