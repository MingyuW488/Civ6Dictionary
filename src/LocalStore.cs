using System.Globalization;
using System.IO;
using System.Text;
using Microsoft.Data.Sqlite;

namespace Civ6EnglishDemo;

internal static class AppPaths
{
    internal static string Data
    {
        get
        {
            if (Environment.GetEnvironmentVariable("CIV6_DEMO_DATA") is { Length: > 0 } configured)
                return Path.GetFullPath(configured);
            // The same project data folder is used by the packaged app and VS Code builds.
            for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder != null; folder = folder.Parent)
                if (File.Exists(Path.Combine(folder.FullName, ".civ6-project")))
                    return Path.Combine(folder.FullName, "data");
            return Path.Combine(AppContext.BaseDirectory, "user-data");
        }
    }
}

internal record NoteRow(long Id, string Created, string Word, string Context, string Note);
internal record VocabularyRow(long Id, string Created, string Word, string Meaning, string Pos, string Context, string Note);
internal record TestRow(string Created, string Kind, string Provider, string Query, double Milliseconds, string Status, double? Confidence = null, long? WorkingSet = null, long? PrivateBytes = null);

internal sealed class LocalStore : IDisposable
{
    readonly SqliteConnection connection;
    internal LocalStore(string file)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file))!);
        connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = file }.ToString()); connection.Open();
        connection.CreateFunction<string, string>("vocab_word", LocalDictionary.Normalize, isDeterministic: true);
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS notes(id INTEGER PRIMARY KEY, created TEXT NOT NULL, word TEXT NOT NULL, context TEXT NOT NULL, note TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS tests(id INTEGER PRIMARY KEY, created TEXT NOT NULL, kind TEXT NOT NULL, provider TEXT NOT NULL, query TEXT NOT NULL, milliseconds REAL NOT NULL, status TEXT NOT NULL, confidence REAL, working_set INTEGER, private_bytes INTEGER);
            CREATE TABLE IF NOT EXISTS vocabulary(id INTEGER PRIMARY KEY, created TEXT NOT NULL, word TEXT NOT NULL UNIQUE, meaning TEXT NOT NULL, pos TEXT NOT NULL, context TEXT NOT NULL, note TEXT NOT NULL);
            """;
        command.ExecuteNonQuery();
    }

    internal void AddNote(string word, string context, string note)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO notes(created,word,context,note) VALUES($created,$word,$context,$note)";
        command.Parameters.AddWithValue("$created", DateTimeOffset.Now.ToString("o"));
        command.Parameters.AddWithValue("$word", word); command.Parameters.AddWithValue("$context", context); command.Parameters.AddWithValue("$note", note); command.ExecuteNonQuery();
    }

    internal List<NoteRow> Notes(int limit = 500)
    {
        using var command = connection.CreateCommand(); command.CommandText = "SELECT id,created,word,context,note FROM notes ORDER BY id DESC LIMIT $limit";
        command.Parameters.AddWithValue("$limit", limit);
        using var reader = command.ExecuteReader(); var rows = new List<NoteRow>();
        while (reader.Read()) rows.Add(new(reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4)));
        return rows;
    }

    // Return whether this was a new word. Duplicate collection preserves the saved context and note.
    internal bool CollectVocabulary(string word, DictionaryEntry? entry, string context, string note)
    {
        word = LocalDictionary.Normalize(word);
        if (!LocalDictionary.IsDictionaryQuery(word)) throw new ArgumentException("生词本支持英文单词或最多六个词的短语；整句请保存到本地笔记。");
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT OR IGNORE INTO vocabulary(created,word,meaning,pos,context,note) VALUES($created,$word,$meaning,$pos,$context,$note)";
        command.Parameters.AddWithValue("$created", DateTimeOffset.Now.ToString("o"));
        command.Parameters.AddWithValue("$word", word);
        command.Parameters.AddWithValue("$meaning", entry?.Translation ?? "");
        command.Parameters.AddWithValue("$pos", entry?.PartsOfSpeech ?? "");
        command.Parameters.AddWithValue("$context", context);
        command.Parameters.AddWithValue("$note", note);
        return command.ExecuteNonQuery() == 1;
    }

    internal (List<VocabularyRow> Rows, long Total) SearchVocabulary(string search, int page = 0, int size = 100)
    {
        if (page < 0 || size is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(page));
        using var command = connection.CreateCommand();
        // instr treats %, _ and quotes literally. Search is evaluated over the complete database before paging.
        const string filter = """
            WHERE $q='' OR instr(lower(v.word),$q)>0 OR instr(v.meaning,$q)>0 OR instr(v.pos,$q)>0
            OR instr(lower(v.context),$q)>0 OR instr(lower(v.note),$q)>0
            OR v.word IN(SELECT vocab_word(n.word) FROM notes n
              WHERE instr(lower(n.context),$q)>0 OR instr(lower(n.note),$q)>0)
            """;
        command.Parameters.AddWithValue("$q", search.Trim().ToLowerInvariant());
        command.CommandText = "SELECT count(*) FROM vocabulary v " + filter;
        long count = (long)command.ExecuteScalar()!;
        command.CommandText = "SELECT v.id,v.created,v.word,v.meaning,v.pos,v.context,v.note FROM vocabulary v " + filter + " ORDER BY v.id DESC LIMIT $size OFFSET $offset";
        command.Parameters.AddWithValue("$size", size);
        command.Parameters.AddWithValue("$offset", (long)page * size);
        using var reader = command.ExecuteReader();
        var rows = new List<VocabularyRow>();
        while (reader.Read()) rows.Add(new(reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetString(6)));
        return (rows, count);
    }

    internal List<NoteRow> VocabularyNotes(string word)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id,created,word,context,note FROM notes WHERE vocab_word(word)=$word ORDER BY id DESC";
        command.Parameters.AddWithValue("$word", word);
        using var reader = command.ExecuteReader();
        var rows = new List<NoteRow>();
        while (reader.Read()) rows.Add(new(reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4)));
        return rows;
    }

    internal void RemoveVocabulary(long id)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM vocabulary WHERE id=$id";
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    internal void ExportVocabulary(string path) => ExportQuery(path,
        "SELECT created,word,meaning,pos,context,note FROM vocabulary ORDER BY id", ["收藏时间", "单词 / 短语", "中文释义", "词性", "收藏时原句", "收藏时笔记"]);

    internal NoteRow UpdateNote(NoteRow original, string word, string context, string note)
    {
        if (string.IsNullOrWhiteSpace(word)) throw new ArgumentException("单词 / 短语不能为空。");
        var edited = original with { Word = word.Trim(), Context = context, Note = note };
        using var command = connection.CreateCommand();
        // Match the stable ID and previous values so another open version cannot silently
        // overwrite a newer edit. Old databases already have these columns; no migration is needed.
        command.CommandText = """
            UPDATE notes SET word=$word,context=$context,note=$note
            WHERE id=$id AND created=$created AND word=$oldWord AND context=$oldContext AND note=$oldNote
            """;
        command.Parameters.AddWithValue("$id", original.Id);
        command.Parameters.AddWithValue("$created", original.Created);
        command.Parameters.AddWithValue("$oldWord", original.Word);
        command.Parameters.AddWithValue("$oldContext", original.Context);
        command.Parameters.AddWithValue("$oldNote", original.Note);
        command.Parameters.AddWithValue("$word", edited.Word);
        command.Parameters.AddWithValue("$context", edited.Context);
        command.Parameters.AddWithValue("$note", edited.Note);
        if (command.ExecuteNonQuery() != 1) throw new InvalidOperationException("这条笔记已在其他窗口中修改或删除。请复制需要保留的内容，关闭详情并刷新列表后重试。");
        return edited;
    }

    internal void AddTest(TestRow row)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO tests(created,kind,provider,query,milliseconds,status,confidence,working_set,private_bytes) VALUES($created,$kind,$provider,$query,$ms,$status,$confidence,$ws,$private)";
        command.Parameters.AddWithValue("$created", row.Created); command.Parameters.AddWithValue("$kind", row.Kind);
        command.Parameters.AddWithValue("$provider", row.Provider); command.Parameters.AddWithValue("$query", row.Query);
        command.Parameters.AddWithValue("$ms", row.Milliseconds); command.Parameters.AddWithValue("$status", row.Status);
        command.Parameters.AddWithValue("$confidence", (object?)row.Confidence ?? DBNull.Value);
        command.Parameters.AddWithValue("$ws", (object?)row.WorkingSet ?? DBNull.Value);
        command.Parameters.AddWithValue("$private", (object?)row.PrivateBytes ?? DBNull.Value); command.ExecuteNonQuery();
    }

    internal void ExportTests(string path) => ExportQuery(path, "SELECT created,kind,provider,query,milliseconds,status,confidence,working_set,private_bytes FROM tests ORDER BY id", ["时间", "类型", "服务", "查询", "耗时_ms", "状态", "OCR置信度_非准确率", "识别结束前工作集_bytes", "识别结束前私有内存_bytes"]);
    internal void ExportNotes(string path) => ExportQuery(path, "SELECT created,word,context,note FROM notes ORDER BY id", ["时间", "单词", "原句", "个人笔记"]);

    void ExportQuery(string path, string sql, string[] headers)
    {
        using var writer = new StreamWriter(path, false, new UTF8Encoding(true));
        writer.WriteLine(string.Join(",", headers.Select(Csv)));
        using var command = connection.CreateCommand(); command.CommandText = sql;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var values = Enumerable.Range(0, reader.FieldCount).Select(i => Csv(reader.IsDBNull(i) ? "" : Convert.ToString(reader.GetValue(i), CultureInfo.InvariantCulture) ?? ""));
            writer.WriteLine(string.Join(",", values));
        }
    }

    internal static string Csv(string value)
    {
        // Keep spreadsheet applications from treating user-entered text as formulas.
        if (value.TrimStart().StartsWith('=') || value.TrimStart().StartsWith('+') || value.TrimStart().StartsWith('-') || value.TrimStart().StartsWith('@')) value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
    public void Dispose() => connection.Dispose();
}
