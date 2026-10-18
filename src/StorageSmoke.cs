using System.IO;
using System.Windows;
using Microsoft.Data.Sqlite;

namespace Civ6EnglishDemo;

internal static class StorageSmoke
{
    internal static void Run()
    {
        string expected = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "data"));
        if (!string.Equals(Path.GetFullPath(AppPaths.Data), expected, StringComparison.OrdinalIgnoreCase))
            throw new Exception("Packaged application did not resolve project data directory.");
        string path = Path.Combine(AppPaths.Data, "learning.db");
        if (!File.Exists(path)) throw new Exception("Migrated database missing.");
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA integrity_check";
        if ((string?)command.ExecuteScalar() != "ok") throw new Exception("Database integrity check failed.");
        var window = new MainWindow();
        Application.Current.MainWindow = window;
        window.Show(); window.UpdateLayout();
        if (!LocalDictionary.Lookup("production").Entry!.Translation.Contains("生产")) throw new Exception("Local dictionary missing.");
        if (SyllableDictionary.Lookup("production") != "pro · duc · tion") throw new Exception("Syllable database missing.");
        window.Close();
        string report = Path.Combine(AppContext.BaseDirectory, "..", "verification", "storage-smoke.txt");
        File.WriteAllText(report, "PASS Project-relative personal database; SQLite integrity; actual main window initialization; local dictionary and syllables. No translation requests or personal data edits.");
    }
}
