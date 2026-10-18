using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace Civ6EnglishDemo;

internal static class SyllableDictionary
{
    // Written word divisions are supplied by Moby Hyphenator II. Never invent
    // boundaries for missing words or modify the word used by search or speech.
    internal static string? Lookup(string word, string? database = null)
    {
        if (word.Length > 100 || !Regex.IsMatch(word, @"^[A-Za-z]+(?:['-][A-Za-z]+)*$", RegexOptions.CultureInvariant)) return null;
        string path = database ?? Path.Combine(AppContext.BaseDirectory, "data", "syllables.db");
        if (!File.Exists(path)) return null;
        try
        {
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT segmented FROM syllables WHERE word=$word COLLATE NOCASE LIMIT 1";
            command.Parameters.AddWithValue("$word", word);
            if (command.ExecuteScalar() is not string segmented || !segmented.Contains('·')) return null;
            var parts = segmented.Split('·');
            // Do not show corrupt or mismatched segmentation data.
            if (parts.Any(part => part.Length == 0) || !string.Concat(parts).Equals(word, StringComparison.OrdinalIgnoreCase)) return null;
            return string.Join(" · ", parts);
        }
        catch (Exception ex) when (ex is IOException or SqliteException) { return null; }
    }
}
