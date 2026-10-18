using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Data.Sqlite;

namespace Civ6EnglishDemo;

internal static class VocabularySmoke
{
    internal static async Task RunAsync()
    {
        Directory.CreateDirectory(AppPaths.Data);
        string path = Path.Combine(AppPaths.Data, "learning.db");
        if (File.Exists(path)) throw new InvalidOperationException("Use a fresh isolated CIV6_DEMO_DATA directory.");
        int checks = 0;
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); checks++; }
        // A database from the old version, before the vocabulary table existed.
        using (var old = new SqliteConnection($"Data Source={path}"))
        {
            old.Open(); using var command = old.CreateCommand();
            command.CommandText = "CREATE TABLE notes(id INTEGER PRIMARY KEY,created TEXT NOT NULL,word TEXT NOT NULL,context TEXT NOT NULL,note TEXT NOT NULL); INSERT INTO notes VALUES(1,'legacy','Production','legacy original','古老笔记独有关键字');";
            command.ExecuteNonQuery();
        }
        using (var store = new LocalStore(path))
        {
            for (int i = 0; i < 510; i++) store.AddNote("Culture", "game original", "newer note");
            store.AddNote("Your cities gain additional production.", "sentence", "keep as note");
            Check(store.SearchVocabulary("").Total == 0, "old database upgraded without changing notes");
            Check(store.Notes(-1).Count == 512, "all old notes preserved");
        }
        var window = new MainWindow();
        Application.Current.MainWindow = window;
        window.OnlineSupplement.IsChecked = false;
        window.Show(); window.Tabs.SelectedIndex = 4;
        async Task WaitIdle()
        {
            for (int i = 0; i < 400 && !window.ImportVocabularyButton.IsEnabled; i++) await Task.Delay(25);
            Check(window.ImportVocabularyButton.IsEnabled, "background action completed");
        }
        try
        {
            window.ImportVocabularyButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitIdle();
            using var store = new LocalStore(path);
            Check(store.SearchVocabulary("").Total == 2, "import includes notes older than 500, deduplicates and skips sentences");
            Check(store.SearchVocabulary("PRODUCTION").Total == 1, "case insensitive English search");
            Check(store.SearchVocabulary("古老笔记").Total == 1, "Chinese note search");
            Check(store.SearchVocabulary("legacy original").Total == 1, "context search");
            var production = store.SearchVocabulary("production").Rows.Single();
            Check(production.Meaning.Contains("生产"), "local Chinese definition stored");
            Check(store.SearchVocabulary("生产").Total >= 1, "Chinese definition search");
            Check(store.SearchVocabulary("%").Total == 0 && store.SearchVocabulary("_").Total == 0 && store.SearchVocabulary("' OR 1=1 --").Total == 0, "literal and parameterized search");
            window.ImportVocabularyButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await WaitIdle();
            Check(store.SearchVocabulary("").Total == 2, "repeat import idempotent");

            window.QueryText.Text = "YIELD"; window.ContextText.Text = new string('x', 6000); window.NoteText.Text = "我的收益笔记";
            window.CollectVocabularyButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await WaitIdle();
            Check(store.SearchVocabulary("yield").Total == 1, "dictionary collection");
            window.NoteText.Text = "do not overwrite";
            window.CollectVocabularyButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await WaitIdle();
            Check(store.SearchVocabulary("yield").Rows.Single().Note == "我的收益笔记", "duplicate preserves previous content");
            store.AddNote("  YIELD  ", "updated associated context", "associated-search-token");
            Check(store.SearchVocabulary("associated-search-token").Total == 1, "linked notes searchable");
            var note = store.VocabularyNotes("yield").Single();
            store.UpdateNote(note, "Yield", note.Context, "edited-associated-note");
            Check(store.SearchVocabulary("edited-associated-note").Total == 1 && store.SearchVocabulary("associated-search-token").Total == 0, "linked search follows note edits");

            window.Tabs.SelectedIndex = 4; window.VocabularySearch.Text = "收益";
            await Task.Delay(400);
            Check(window.VocabularyGrid.Items.Count == 1, "live search debounce updates grid");
            window.VocabularyGrid.SelectedIndex = 0;
            Check(window.VocabularyDetail.Text.Contains(new string('x', 6000)) && window.VocabularyDetail.Text.Contains("edited-associated-note"), "full long context and current associated notes shown");

            for (int i = 0; i < 125; i++) store.CollectVocabulary("word" + (char)('a' + i / 26) + (char)('a' + i % 26), null, "", "");
            window.VocabularySearch.Clear(); await Task.Delay(400);
            Check(window.VocabularyGrid.Items.Count == 100 && window.VocabularyNext.IsEnabled, "paged grid bounded to 100 rows");
            window.VocabularyNext.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(window.VocabularyGrid.Items.Count == 28 && window.VocabularyPrevious.IsEnabled, "second page");
            window.VocabularySearch.Text = "yield"; await Task.Delay(400);
            Check(window.VocabularyGrid.Items.Count == 1 && !window.VocabularyPrevious.IsEnabled, "new search resets page and searches all rows");
            window.VocabularyGrid.SelectedIndex = 0;
            window.UpdateLayout();
            var root = (FrameworkElement)window.Content;
            var image = new RenderTargetBitmap((int)root.ActualWidth, (int)root.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            var drawing = new DrawingVisual();
            using (var dc = drawing.RenderOpen()) { dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, root.ActualWidth, root.ActualHeight)); dc.DrawRectangle(new VisualBrush(root), null, new Rect(0, 0, root.ActualWidth, root.ActualHeight)); }
            image.Render(drawing); var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(image));
            using (var file = File.Create(Path.Combine(AppPaths.Data, "vocabulary-preview.png"))) png.Save(file);

            string csv = Path.Combine(AppPaths.Data, "vocabulary.csv"); store.ExportVocabulary(csv);
            using (var parser = new Microsoft.VisualBasic.FileIO.TextFieldParser(csv))
            {
                parser.SetDelimiters(","); parser.HasFieldsEnclosedInQuotes = true;
                int records = 0; bool fullContext = false;
                while (!parser.EndOfData)
                {
                    var fields = parser.ReadFields()!; records++;
                    if (fields[1] == "yield") fullContext = fields[4] == new string('x', 6000) && fields[5] == "我的收益笔记";
                }
                Check(records == 129 && fullContext, "export includes all pages and multiline fields");
            }
            window.VocabularyLookupButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            for (int i = 0; i < 200 && !window.LookupButton.IsEnabled; i++) await Task.Delay(25);
            Check(window.Tabs.SelectedIndex == 3 && window.QueryText.Text == "yield" && window.LookupButton.IsEnabled, "selected word opens real dictionary offline");
            int noteCount = store.Notes(-1).Count;
            window.Tabs.SelectedIndex = 4;
            window.RemoveVocabularyButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(store.SearchVocabulary("yield").Total == 0 && store.Notes(-1).Count == noteCount, "remove preserves notes");
            using var reopened = new LocalStore(path);
            Check(reopened.SearchVocabulary("").Total == 127, "data persists on reopen");
            File.WriteAllText(Path.Combine(AppPaths.Data, "vocabulary-smoke.txt"), $"PASS {checks} checks: legacy migration, collection, import, deduplication, literal search, Chinese, context, linked notes/edit, pagination, full detail, export, removal, persistence. No real API requests; isolated test data.");
        }
        finally { window.Close(); }
    }
}
