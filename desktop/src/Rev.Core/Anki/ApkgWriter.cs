using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Rev.Core.Media;
using Rev.Core.Models;
using Rev.Core.Scheduling;
using Rev.Core.Text;

namespace Rev.Core.Anki;

/// <summary>What an export wrote, for the message afterwards.</summary>
public sealed record ExportResult(int Decks, int Cards, int Images);

/// <summary>
/// Builds an Anki .apkg file from Rev decks, so you can open your cards in Anki.
///
/// It writes the older, widely supported format ("collection.anki2", schema 11), which every current
/// Anki (computer, AnkiMobile, AnkiDroid) can import. Cards use a simple "Rev Basic" note type with
/// Front and Back fields. Progress is included: graduated cards keep their interval, due date and ease
/// (and FSRS memory); suspended cards stay suspended; leeches get Anki's "leech" tag.
/// The same file as Rev for iPhone's ApkgWriter makes.
/// </summary>
public static class ApkgWriter
{
    /// <summary>Every deck, with its cards in the order they were made (for "Export all decks").</summary>
    public static List<(Deck Deck, List<Card> Cards)> AllDecks(Data.RevDatabase db)
    {
        var cards = db.GetCards().ToLookup(c => c.DeckId);
        return db.GetDecks().Select(d => (d, cards[d.Id].OrderBy(c => c.CreatedAt).ToList())).ToList();
    }

    /// <summary>A fixed id for the "Rev Basic" note type, so importing again reuses the same note type.</summary>
    private const long NoteTypeId = 1_700_000_000_777;

    public static ExportResult Write(string path, IReadOnlyList<(Deck Deck, List<Card> Cards)> decks, string mediaFolder,
                                     DateTime? now = null)
    {
        var at = now ?? DateTime.Now;
        string work = Path.Combine(Path.GetTempPath(), "rev-export-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            WriteCollection(Path.Combine(work, "collection.anki2"), decks, at);

            // Images: copied in as "0", "1", … and listed in the "media" file as {"0": "heart.jpg", …}.
            var media = new Dictionary<string, string>();
            var names = decks.SelectMany(d => d.Cards)
                .SelectMany(c => CardText.ImageNames(c.Front).Concat(CardText.ImageNames(c.Back)))
                .Distinct().Order();
            foreach (var name in names)
            {
                if (MediaStore.SafeName(name) is not { } safe) continue;
                string source = Path.Combine(mediaFolder, safe);
                if (!File.Exists(source)) continue;
                string entry = media.Count.ToString(CultureInfo.InvariantCulture);
                File.Copy(source, Path.Combine(work, entry));
                media[entry] = safe;
            }
            File.WriteAllText(Path.Combine(work, "media"), JsonSerializer.Serialize(media));

            // The package is a zip of the database, the media list and the media files.
            string temp = path + ".part";
            File.Delete(temp);
            using (var zip = ZipFile.Open(temp, ZipArchiveMode.Create))
            {
                foreach (var file in new[] { "collection.anki2", "media" }.Concat(media.Keys))
                    zip.CreateEntryFromFile(Path.Combine(work, file), file, CompressionLevel.Optimal);
            }
            File.Move(temp, path, overwrite: true);   // only replace the old file once the new one is complete
            return new ExportResult(decks.Count, decks.Sum(d => d.Cards.Count), media.Count);
        }
        finally
        {
            Directory.Delete(work, recursive: true);
        }
    }

    // ---- The database inside the package ----

    private static void WriteCollection(string file, IReadOnlyList<(Deck Deck, List<Card> Cards)> decks, DateTime now)
    {
        using var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = file, Pooling = false }.ToString());
        db.Open();
        Execute(db, Schema);

        long nowSeconds = new DateTimeOffset(now).ToUnixTimeSeconds();
        long nowMillis = nowSeconds * 1000;
        var today = now.StartOfStudyDay();
        long collectionCreated = new DateTimeOffset(today).ToUnixTimeSeconds();

        // Give each deck an Anki id. (Anki matches decks by name when importing.)
        var deckJson = new JsonObject { ["1"] = DeckEntry(1, "Default", nowSeconds) };
        var deckIds = new List<long>();
        for (int i = 0; i < decks.Count; i++)
        {
            long id = nowMillis + i + 1;
            deckIds.Add(id);
            // Rev shows sub-decks as "Parent › Child"; Anki writes them as "Parent::Child".
            deckJson[id.ToString(CultureInfo.InvariantCulture)] = DeckEntry(id, decks[i].Deck.Name.Replace(" › ", "::"), nowSeconds);
        }

        Execute(db, "INSERT INTO col VALUES (1, $crt, $mod, $scm, 11, 0, 0, 0, $conf, $models, $decks, $dconf, '{}')",
            ("$crt", collectionCreated), ("$mod", nowMillis), ("$scm", nowMillis),
            ("$conf", CollectionConfig.ToJsonString()),
            ("$models", new JsonObject { [NoteTypeId.ToString(CultureInfo.InvariantCulture)] = NoteType(nowSeconds) }.ToJsonString()),
            ("$decks", deckJson.ToJsonString()), ("$dconf", DeckOptions.ToJsonString()));

        using var transaction = db.BeginTransaction();
        long nextId = nowMillis;
        long newPosition = 0;
        for (int i = 0; i < decks.Count; i++)
        {
            foreach (var card in decks[i].Cards)
            {
                nextId += 1;
                string fields = AnkiText.HtmlFromPlain(card.Front) + "\u001f" + AnkiText.HtmlFromPlain(card.Back);
                // Anki stores tags with a space on each side. Leeches also get Anki's "leech" tag.
                var tagList = Tags.Parse(card.TagsText);
                if (card.IsLeech) tagList.Add("leech");
                string tags = tagList.Count == 0 ? "" : " " + Tags.Format(tagList) + " ";
                Execute(db, "INSERT INTO notes VALUES ($id, $guid, $mid, $mod, -1, $tags, $flds, $sfld, $csum, 0, '')",
                    ("$id", nextId), ("$guid", NoteGuid(card)), ("$mid", NoteTypeId), ("$mod", nowSeconds), ("$tags", tags),
                    ("$flds", fields), ("$sfld", card.Front), ("$csum", Checksum(card.Front)));

                var progress = Progress(card, today, ref newPosition);
                // Anki marks a suspended card with queue = -1.
                long queue = card.IsSuspended ? -1 : progress.Type;
                Execute(db, "INSERT INTO cards VALUES ($id, $nid, $did, 0, $mod, -1, $type, $queue, $due, $ivl, $factor, " +
                            "$reps, $lapses, 0, 0, 0, 0, $data)",
                    ("$id", nextId), ("$nid", nextId), ("$did", deckIds[i]), ("$mod", nowSeconds),
                    ("$type", progress.Type), ("$queue", queue), ("$due", progress.Due), ("$ivl", progress.Interval),
                    ("$factor", progress.Factor), ("$reps", (long)card.Reps), ("$lapses", (long)card.Lapses),
                    ("$data", Memory(card)));
            }
        }
        transaction.Commit();
    }

    /// <summary>
    /// Rev's schedule in Anki's numbers. Graduated (and relearning) cards become Anki review cards,
    /// due a whole number of days from today; everything else becomes a new card, in order.
    /// </summary>
    private static (long Type, long Due, long Interval, long Factor) Progress(Card card, DateTime today, ref long newPosition)
    {
        if (card.Phase is CardPhase.Review or CardPhase.Relearning)
        {
            long days = today.StudyDaysTo(card.Due);
            return (2, days, (long)Math.Max(1, IntervalHelpers.Round(card.IntervalDays)), (long)IntervalHelpers.Round(card.Ease * 1000));
        }
        newPosition += 1;
        return (0, newPosition, 0, 0);
    }

    /// <summary>FSRS memory for Anki's FSRS, as {"s": stability, "d": difficulty}; empty if the card has none.</summary>
    private static string Memory(Card card) =>
        card.Stability is double s && card.Difficulty is double d && card.Phase != CardPhase.New
            ? JsonSerializer.Serialize(new { s = Math.Round(s, 4), d = Math.Round(d, 4) })
            : "";

    /// <summary>A stable id for each note, so exporting the same card twice updates it in Anki instead of duplicating.</summary>
    private static string NoteGuid(Card card) => card.Id.ToString("N").ToUpperInvariant();

    /// <summary>Anki's duplicate-check number: the first 8 hex digits of the SHA-1 of the first field.</summary>
    private static long Checksum(string text)
    {
        byte[] hash = SHA1.HashData(Encoding.UTF8.GetBytes(text));
        return ((long)hash[0] << 24) | ((long)hash[1] << 16) | ((long)hash[2] << 8) | hash[3];
    }

    private static void Execute(SqliteConnection db, string sql, params (string Name, object Value)[] values)
    {
        using var command = db.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in values) command.Parameters.AddWithValue(name, value);
        command.ExecuteNonQuery();
    }

    // ---- Fixed pieces Anki expects ----

    private const string Schema = """
        CREATE TABLE col (id integer primary key, crt integer not null, mod integer not null, scm integer not null,
            ver integer not null, dty integer not null, usn integer not null, ls integer not null, conf text not null,
            models text not null, decks text not null, dconf text not null, tags text not null);
        CREATE TABLE notes (id integer primary key, guid text not null, mid integer not null, mod integer not null,
            usn integer not null, tags text not null, flds text not null, sfld integer not null, csum integer not null,
            flags integer not null, data text not null);
        CREATE TABLE cards (id integer primary key, nid integer not null, did integer not null, ord integer not null,
            mod integer not null, usn integer not null, type integer not null, queue integer not null, due integer not null,
            ivl integer not null, factor integer not null, reps integer not null, lapses integer not null,
            left integer not null, odue integer not null, odid integer not null, flags integer not null, data text not null);
        CREATE TABLE revlog (id integer primary key, cid integer not null, usn integer not null, ease integer not null,
            ivl integer not null, lastIvl integer not null, factor integer not null, time integer not null, type integer not null);
        CREATE TABLE graves (usn integer not null, oid integer not null, type integer not null);
        CREATE INDEX ix_notes_usn ON notes (usn);
        CREATE INDEX ix_cards_usn ON cards (usn);
        CREATE INDEX ix_revlog_usn ON revlog (usn);
        CREATE INDEX ix_cards_nid ON cards (nid);
        CREATE INDEX ix_cards_sched ON cards (did, queue, due);
        CREATE INDEX ix_revlog_cid ON revlog (cid);
        CREATE INDEX ix_notes_csum ON notes (csum);
        """;

    private static JsonObject CollectionConfig => new()
    {
        ["activeDecks"] = new JsonArray(1), ["curDeck"] = 1, ["newSpread"] = 0, ["collapseTime"] = 1200, ["timeLim"] = 0,
        ["estTimes"] = true, ["dueCounts"] = true, ["curModel"] = null, ["nextPos"] = 1, ["sortType"] = "noteFld",
        ["sortBackwards"] = false, ["addToCur"] = true,
    };

    private static JsonObject DeckOptions => new()
    {
        ["1"] = new JsonObject
        {
            ["id"] = 1, ["name"] = "Default", ["mod"] = 0, ["usn"] = 0, ["maxTaken"] = 60, ["autoplay"] = true, ["timer"] = 0,
            ["replayq"] = true, ["dyn"] = false,
            ["new"] = new JsonObject
            {
                ["delays"] = new JsonArray(1, 10), ["ints"] = new JsonArray(1, 4, 7), ["initialFactor"] = 2500,
                ["separate"] = true, ["order"] = 1, ["perDay"] = 20, ["bury"] = false,
            },
            ["lapse"] = new JsonObject
            {
                ["delays"] = new JsonArray(10), ["mult"] = 0, ["minInt"] = 1, ["leechFails"] = 8, ["leechAction"] = 0,
            },
            ["rev"] = new JsonObject
            {
                ["perDay"] = 200, ["ease4"] = 1.3, ["fuzz"] = 0.05, ["minSpace"] = 1, ["ivlFct"] = 1, ["maxIvl"] = 36500,
                ["bury"] = false, ["hardFactor"] = 1.2,
            },
        },
    };

    private static JsonObject DeckEntry(long id, string name, long modified) => new()
    {
        ["id"] = id, ["name"] = name, ["mod"] = modified, ["usn"] = -1, ["desc"] = "", ["dyn"] = 0, ["conf"] = 1,
        ["collapsed"] = false, ["browserCollapsed"] = false, ["extendNew"] = 0, ["extendRev"] = 0,
        ["newToday"] = new JsonArray(0, 0), ["revToday"] = new JsonArray(0, 0), ["lrnToday"] = new JsonArray(0, 0),
        ["timeToday"] = new JsonArray(0, 0),
    };

    private static JsonObject NoteType(long modified)
    {
        JsonObject Field(string name, int ord) => new()
        {
            ["name"] = name, ["ord"] = ord, ["sticky"] = false, ["rtl"] = false, ["font"] = "Arial", ["size"] = 20,
            ["media"] = new JsonArray(),
        };
        return new JsonObject
        {
            ["id"] = NoteTypeId, ["name"] = "Rev Basic", ["type"] = 0, ["mod"] = modified, ["usn"] = -1,
            ["sortf"] = 0, ["did"] = 1, ["tags"] = new JsonArray(), ["vers"] = new JsonArray(),
            ["flds"] = new JsonArray(Field("Front", 0), Field("Back", 1)),
            ["tmpls"] = new JsonArray(new JsonObject
            {
                ["name"] = "Card 1", ["ord"] = 0, ["qfmt"] = "{{Front}}",
                ["afmt"] = "{{FrontSide}}\n\n<hr id=answer>\n\n{{Back}}", ["did"] = null, ["bqfmt"] = "", ["bafmt"] = "",
            }),
            ["req"] = new JsonArray(new JsonArray(0, "all", new JsonArray(0))),
            ["css"] = ".card {\n font-family: arial;\n font-size: 20px;\n text-align: center;\n color: black;\n background-color: white;\n}\n",
            ["latexPre"] = "\\documentclass[12pt]{article}\n\\special{papersize=3in,5in}\n\\usepackage[utf8]{inputenc}\n\\usepackage{amssymb,amsmath}\n\\pagestyle{empty}\n\\setlength{\\parindent}{0in}\n\\begin{document}\n",
            ["latexPost"] = "\\end{document}",
        };
    }
}
