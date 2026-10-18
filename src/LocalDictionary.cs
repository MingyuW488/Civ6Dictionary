using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace Civ6EnglishDemo;

internal record DictionarySense(string Label, string Meaning);
internal record DictionaryEntry(string Word, string Phonetic, string Definition, string Translation, string Pos, string Exchange)
{
    internal List<DictionarySense> Chinese => LocalDictionary.Senses(Translation);
    internal List<DictionarySense> English => LocalDictionary.Senses(Definition);
    internal string PartsOfSpeech => string.Join(" · ", Chinese.Concat(English).Select(x => x.Label).Where(x => x != "释义").Distinct());
    internal string Forms => LocalDictionary.FormatForms(Exchange);
}
internal record DictionaryMatch(string Query, DictionaryEntry? Entry, string Message, double Milliseconds);

internal static class LocalDictionary
{
    static readonly Regex PosPrefix = new(@"^(?<pos>vt|vi|v|n|adj|adv|a|ad|prep|pron|conj|interj|int|aux|art|num|abbr|phr)(?:\.\s*|\s+)(?<meaning>.+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    static readonly Dictionary<string, string> PosNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["n"]="名词", ["v"]="动词", ["vt"]="及物动词", ["vi"]="不及物动词", ["a"]="形容词", ["adj"]="形容词",
        ["adv"]="副词", ["ad"]="副词", ["prep"]="介词", ["pron"]="代词", ["conj"]="连词", ["int"]="感叹词",
        ["interj"]="感叹词", ["aux"]="助动词", ["art"]="冠词", ["num"]="数词", ["abbr"]="缩略词", ["phr"]="短语"
    };
    static readonly Dictionary<string, string> FormNames = new()
    {
        ["s"]="复数", ["3"]="第三人称单数", ["p"]="过去式", ["d"]="过去分词", ["i"]="现在分词", ["r"]="比较级", ["t"]="最高级"
    };
    internal static string FilePath => Path.Combine(AppContext.BaseDirectory, "data", "dictionary.db");

    internal static bool IsDictionaryQuery(string query) => query.Length <= 120 && query.Length > 0
        && query.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 6
        && Regex.IsMatch(query, @"^[A-Za-z][A-Za-z '\-’]*$", RegexOptions.CultureInvariant);

    internal static string Normalize(string query) => Regex.Replace(query.Trim().Replace('’', '\''), @"\s+", " ").ToLowerInvariant();

    internal static DictionaryMatch Lookup(string query, string? database = null)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        string normalized = Normalize(query);
        if (!IsDictionaryQuery(normalized)) return new(query, null, "这段内容适合整句翻译，可使用已配置的在线接口。", watch.Elapsed.TotalMilliseconds);
        string path = database ?? FilePath;
        if (!File.Exists(path)) return new(query, null, "缺少本地词库，请使用包含 data 文件夹的完整版本。", watch.Elapsed.TotalMilliseconds);
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        connection.Open();
        using (var config = connection.CreateCommand()) { config.CommandText = "PRAGMA cache_size=-2048; PRAGMA mmap_size=0;"; config.ExecuteNonQuery(); }
        var entry = Find(connection, normalized);
        if (entry != null)
        {
            var root = entry.Exchange.Split('/').FirstOrDefault(x => x.StartsWith("0:", StringComparison.Ordinal))?[2..];
            if (!string.IsNullOrWhiteSpace(root)) entry = Find(connection, root) ?? entry;
        }
        else
        {
            using var alias = connection.CreateCommand();
            alias.CommandText = "SELECT word FROM forms WHERE form=$query COLLATE NOCASE ORDER BY word LIMIT 1";
            alias.Parameters.AddWithValue("$query", normalized);
            if (alias.ExecuteScalar() is string root) entry = Find(connection, root);
        }
        return new(query, entry, entry == null ? "本地词库未收录这个词或短语，可尝试在线翻译。" : "本地词典 · ECDICT", watch.Elapsed.TotalMilliseconds);
    }

    static DictionaryEntry? Find(SqliteConnection connection, string word)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT word,phonetic,definition,translation,pos,exchange FROM entries WHERE word=$word COLLATE NOCASE LIMIT 1";
        command.Parameters.AddWithValue("$word", word);
        using var reader = command.ExecuteReader();
        return reader.Read() ? new(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5)) : null;
    }

    internal static List<DictionarySense> Senses(string text)
    {
        var lines = text.Replace("\\r\\n", "\n").Replace("\\n", "\n").Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return lines.Select(line =>
        {
            var match = PosPrefix.Match(line);
            if (!match.Success) return new DictionarySense("释义", line);
            string pos = match.Groups["pos"].Value.ToLowerInvariant();
            return new DictionarySense(pos + ". " + PosNames[pos], match.Groups["meaning"].Value);
        }).ToList();
    }

    internal static string FormatForms(string exchange) => string.Join("    ·    ", exchange.Split('/')
        .Select(item => item.Split(':', 2)).Where(parts => parts.Length == 2 && FormNames.ContainsKey(parts[0]) && parts[1].Length > 0)
        .Select(parts => FormNames[parts[0]] + "  " + parts[1]));
}
