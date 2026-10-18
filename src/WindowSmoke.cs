using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Net;
using System.Net.Http;
using System.Windows.Input;

namespace Civ6EnglishDemo;

internal static class WindowSmoke
{
    internal static async Task RunSyllableAsync()
    {
        Directory.CreateDirectory(AppPaths.Data);
        if (SyllableDictionary.Lookup("PRODUCTION") != "pro · duc · tion") throw new Exception("Case-insensitive word division failed.");
        if (SyllableDictionary.Lookup("civilization") != "civ · i · li · za · tion") throw new Exception("Long word division failed.");
        if (SyllableDictionary.Lookup("yield") != null || SyllableDictionary.Lookup("zzzzunknownabcd") != null) throw new Exception("Undivided or uncollected words should not be guessed.");
        if (SyllableDictionary.Lookup("trade route") != null || SyllableDictionary.Lookup("Your cities gain production.") != null) throw new Exception("Phrases or sentences should not be split as one word.");
        if (SyllableDictionary.Lookup("production", Path.Combine(AppPaths.Data, "missing-syllables.db")) != null) throw new Exception("Missing syllable data should gracefully return no division.");
        var window = new MainWindow(); Application.Current.MainWindow = window; window.Show();
        window.OnlineSupplement.IsChecked = false;
        window.QueryText.Text = "production";
        await window.LookupAsync();
        if (window.DictionaryCard.Syllables.Text != "pro · duc · tion" || window.DictionaryCard.Syllables.Visibility != Visibility.Visible || window.DictionaryCard.Heading.Text != "production" || window.QueryText.Text != "production" || window.DictionaryCard.Phonetic.Text.Length == 0) throw new Exception("Top-of-card syllable display changed lookup text or phonetic information.");
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        SaveImage(window, "syllable-preview.png");
        window.QueryText.Text = "yield"; await window.LookupAsync();
        if (window.DictionaryCard.Syllables.Visibility != Visibility.Collapsed || window.DictionaryCard.Syllables.Text.Length != 0) throw new Exception("Previous word's division was not cleared.");
        window.QueryText.Text = "Your cities gain production."; await window.LookupAsync();
        if (window.DictionaryCard.Syllables.Visibility != Visibility.Collapsed) throw new Exception("A sentence displayed stale word divisions.");
        File.WriteAllText(Path.Combine(AppPaths.Data, "syllable-smoke.txt"), "PASS Indexed offline written divisions; case-insensitive lookup; long words; unknown and undivided words not guessed; phrases/sentences ignored; missing data tolerated; dictionary card shows separated spelling above phonetics; original word and query preserved; stale display cleared.\nNo online translation requests or speech playback were used.");
        window.Close();
    }

    internal static async Task RunSentenceAsync()
    {
        Directory.CreateDirectory(AppPaths.Data);
        var window = new MainWindow(); Application.Current.MainWindow = window; window.Show();
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        Descendants(window).OfType<Button>().First(button => button.Content as string == "打开示例").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        for (int i = 0; i < 100 && !window.ContextText.Text.Contains("production"); i++) await Task.Delay(100);
        if (!window.ContextText.Text.Contains("production")) throw new Exception("OCR sample did not complete.");
        string original = window.ContextText.Text;
        window.TransferAllButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (!window.QueryText.Text.StartsWith("Your cities gain additional production.") || !window.QueryText.Text.EndsWith("Culture helps your civilization grow.") || window.QueryText.Text.Contains('\n') || window.ContextText.Text != original) throw new Exception("Transfer-all dropped text, punctuation or changed the original.");
        string sentence = "Trade routes provide gold and science.";
        window.ContextText.Select(original.IndexOf(sentence, StringComparison.Ordinal), sentence.Length);
        window.TransferSelectedButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (window.QueryText.Text != sentence || window.ContextText.Text != original) throw new Exception("Selected sentence was not transferred exactly.");
        window.WordPanel.Children.OfType<Button>().First(button => button.Content as string == "production.").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (window.QueryText.Text != "production") throw new Exception("Word-chip lookup regressed.");
        window.ContextText.Clear(); window.TransferAllButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (window.QueryText.Text != "production") throw new Exception("Empty OCR text erased an existing query.");
        window.TransferSelectedButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (!window.Status.Text.Contains("选中") || window.QueryText.Text != "production") throw new Exception("No-selection guidance failed.");
        window.ContextText.Text = "  A city's district\r\nprovides  production.  ";
        window.TransferAllButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (window.QueryText.Text != "A city's district provides production.") throw new Exception("OCR line-wrap joining failed.");
        window.ContextText.Text = new string('a', 2001); window.TransferAllButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (window.QueryText.Text != "A city's district provides production." || !window.Status.Text.Contains("2000")) throw new Exception("Oversized source was silently truncated or replaced the query.");
        window.ContextText.Text = original; window.TransferAllButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        SaveImage(window, "sentence-transfer-preview.png");
        int requests = 0; string postedBody = "";
        window.AppId.Text = "test-only-id"; window.Secret.Password = "test-only-secret";
        window.TranslationClientFactory = () => new HttpClient(new FakeTranslationHandler("{\"errorCode\":\"0\",\"translation\":[\"整段翻译测试成功\"]}", 100, body => { requests++; postedBody = body; }));
        string query = window.QueryText.Text;
        var lookup = window.LookupAsync();
        if (!lookup.IsCompleted && (window.TransferAllButton.IsEnabled || window.TransferSelectedButton.IsEnabled)) throw new Exception("Transfer controls should be disabled during a lookup.");
        await lookup;
        string postedQuery = WebUtility.UrlDecode(postedBody.Split('&').Single(part => part.StartsWith("q="))[2..]);
        if (requests != 1 || postedQuery != query || !window.DictionaryCard.OnlineText.Text.Contains("整段翻译测试成功") || !window.TransferAllButton.IsEnabled) throw new Exception("Transferred paragraph was not translated in full.");
        File.WriteAllText(Path.Combine(AppPaths.Data, "sentence-smoke.txt"), "PASS Real OCR followed by whole-paragraph transfer; selected sentence transfer; punctuation and source preservation; wrapped lines joined; word-chip regression; empty source and selection guidance; oversized source rejected without truncation; transfer disabled during lookup; complete query submitted once to a simulated translation endpoint.\nNo real keys or external API calls were used.");
        window.Close();
    }

    internal static async Task RunNotesAsync()
    {
        Directory.CreateDirectory(AppPaths.Data);
        CheckNoteUpdates();
        var window = new MainWindow(); Application.Current.MainWindow = window; window.Show();
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        window.QueryText.Text = "yield";
        window.ContextText.Text = string.Join("\n", Enumerable.Range(1, 100).Select(i => $"Paragraph {i}: Your cities gain additional production from trade routes and industrial districts.")) + "\nCONTEXT_END";
        window.NoteText.Text = string.Join("\n", Enumerable.Range(1, 100).Select(i => $"第 {i} 段：yield 可作名词和动词；这条笔记记录生产量、收益、出产、让出及其他语境。")) + "\nNOTE_END";
        Descendants(window).OfType<Button>().First(b => b.Content as string == "保存原句与笔记").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var saved = (NoteRow)window.NotesGrid.Items[0];
        int savedCount = window.NotesGrid.Items.Count;
        window.ContextText.Text = "Unsaved editor context"; window.NoteText.Text = "未保存的编辑内容";
        window.Tabs.SelectedIndex = 2;
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        window.NotesGrid.SelectedItem = saved; window.NotesGrid.ScrollIntoView(saved); window.NotesGrid.UpdateLayout();
        var row = window.NotesGrid.ItemContainerGenerator.ContainerFromItem(saved) as DataGridRow ?? throw new Exception("No note row container.");
        Exception? failure = null;
        bool opened = false;
        _ = window.Dispatcher.BeginInvoke(new Action(() =>
        {
            NoteDetailsWindow? detail = null;
            try
            {
                detail = window.OwnedWindows.OfType<NoteDetailsWindow>().Single(); opened = true;
                detail.UpdateLayout();
                if (detail.ContextDetail.Text != saved.Context || detail.NoteDetail.Text != saved.Note || detail.WordDetail.Text != saved.Word) throw new Exception("Long text was truncated in details.");
                if (!detail.ContextDetail.IsReadOnly || !detail.NoteDetail.IsReadOnly) throw new Exception("Details must not silently edit saved notes.");
                detail.ContextDetail.ScrollToEnd(); detail.NoteDetail.ScrollToEnd(); detail.UpdateLayout();
                if (detail.ContextDetail.VerticalOffset <= 0 || detail.NoteDetail.VerticalOffset <= 0) throw new Exception("Long note text is not scrollable.");
                detail.NoteDetail.Select(detail.NoteDetail.Text.Length - "NOTE_END".Length, "NOTE_END".Length);
                if (detail.NoteDetail.SelectedText != "NOTE_END") throw new Exception("Full note text is not selectable.");
                SaveImage(detail, "note-details-preview.png");
                detail.Width = 600; detail.Height = 510; detail.UpdateLayout();
                if (detail.ContextDetail.ActualHeight < 25 || detail.NoteDetail.ActualHeight < 25 || detail.CloseDetailButton.ActualHeight < 20) throw new Exception("Resized detail window hides content or controls.");
                detail.CloseDetailButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
            catch (Exception ex) { failure = ex; }
            finally { if (detail?.IsVisible == true) detail.Close(); }
        }), DispatcherPriority.Background);
        window.NotesGrid.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = Control.MouseDoubleClickEvent, Source = row });
        if (failure != null) throw failure;
        if (!opened) throw new Exception("Double-click did not open note details.");
        if (window.ContextText.Text != "Unsaved editor context" || window.NoteText.Text != "未保存的编辑内容" || window.NotesGrid.Items.Count != savedCount) throw new Exception("Viewing details changed editor content or created a note.");
        bool unexpectedDialog = false;
        var emptyAreaCheck = window.Dispatcher.InvokeAsync(() =>
        {
            foreach (var detail in window.OwnedWindows.OfType<NoteDetailsWindow>().ToArray()) { unexpectedDialog = true; detail.Close(); }
        }, DispatcherPriority.Background);
        window.NotesGrid.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = Control.MouseDoubleClickEvent });
        await emptyAreaCheck;
        if (unexpectedDialog) throw new Exception("Double-clicking non-row area opened the previous note.");
        _ = window.Dispatcher.BeginInvoke(new Action(() => window.OwnedWindows.OfType<NoteDetailsWindow>().Single().UseNoteButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent))), DispatcherPriority.Background);
        window.ShowNoteDetails(saved);
        if (window.Tabs.SelectedIndex != 0 || window.ContextText.Text != saved.Context || window.NoteText.Text != saved.Note) throw new Exception("Return-to-lookup action failed.");
        void OpenAndExercise(NoteRow note, Action<NoteDetailsWindow> exercise, bool edit = false)
        {
            Exception? modalError = null;
            _ = window.Dispatcher.BeginInvoke(new Action(() =>
            {
                NoteDetailsWindow? detail = null;
                try { detail = window.OwnedWindows.OfType<NoteDetailsWindow>().Single(); exercise(detail); }
                catch (Exception ex) { modalError = ex; }
                finally { if (detail?.IsVisible == true) detail.Close(); }
            }), DispatcherPriority.Background);
            window.ShowNoteDetails(note, edit);
            if (modalError != null) throw modalError;
        }
        NoteRow ReadPersisted()
        {
            using var check = new LocalStore(Path.Combine(AppPaths.Data, "learning.db"));
            return check.Notes().Single(note => note.Id == saved.Id);
        }
        OpenAndExercise(saved, detail =>
        {
            detail.EditNoteButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (detail.NoteDetail.IsReadOnly || detail.ContextDetail.IsReadOnly || detail.WordDetail.IsReadOnly) throw new Exception("Edit mode did not enable fields.");
            detail.NoteDetail.Text = "Cancelled draft";
            detail.CancelChangesButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (detail.NoteDetail.Text != saved.Note || !detail.NoteDetail.IsReadOnly || detail.HasSavedChanges) throw new Exception("Cancel did not restore the saved note.");
        });
        if (ReadPersisted() != saved) throw new Exception("Cancel changed the database.");
        OpenAndExercise(saved, detail =>
        {
            detail.NoteDetail.Text = "Discard this draft on close";
            detail.CloseDetailButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }, true);
        if (ReadPersisted() != saved) throw new Exception("Closing an edit saved it implicitly.");
        OpenAndExercise(saved, detail =>
        {
            detail.WordDetail.Text = " ";
            detail.SaveChangesButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (detail.HasSavedChanges || !detail.EditStatus.Text.Contains("不能为空")) throw new Exception("Empty word validation failed.");
        }, true);
        window.ContextText.Text = "Protected unsaved context"; window.NoteText.Text = "保护未保存的笔记";
        string editedContext = saved.Context + "\nUpdated context with 'quotes'.";
        string editedNote = saved.Note + "\n更新后的中文笔记。";
        OpenAndExercise(saved, detail =>
        {
            detail.WordDetail.Text = " yields "; detail.ContextDetail.Text = editedContext; detail.NoteDetail.Text = editedNote;
            detail.SaveChangesButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (!detail.HasSavedChanges || !detail.NoteDetail.IsReadOnly || detail.CurrentNote.Id != saved.Id || detail.CurrentNote.Word != "yields") throw new Exception("Save did not update the existing note.");
            detail.UpdateLayout(); SaveImage(detail, "note-edit-preview.png");
        }, true);
        var edited = ReadPersisted();
        if (edited.Id != saved.Id || edited.Created != saved.Created || edited.Word != "yields" || edited.Context != editedContext || edited.Note != editedNote || window.NotesGrid.Items.Count != savedCount) throw new Exception("Edit changed identity, timestamp, content or row count.");
        if (window.ContextText.Text != "Protected unsaved context" || window.NoteText.Text != "保护未保存的笔记") throw new Exception("Editing a saved note overwrote the lookup draft.");
        if (window.NotesGrid.SelectedItem is not NoteRow selected || selected != edited) throw new Exception("Edited list row did not refresh and remain selected.");
        OpenAndExercise(edited, detail =>
        {
            detail.NoteDetail.Text += "\nSecond saved revision.";
            detail.SaveChangesButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            detail.EditNoteButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            detail.NoteDetail.Text += "\nThird saved revision.";
            detail.SaveChangesButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (!detail.NoteDetail.IsReadOnly || !detail.CurrentNote.Note.EndsWith("Third saved revision.")) throw new Exception("Repeated edits in one dialog failed.");
            detail.UseNoteButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }, true);
        if (window.NoteText.Text != ReadPersisted().Note || !window.NoteText.Text.EndsWith("Third saved revision.") || window.NotesGrid.Items.Count != savedCount) throw new Exception("Returning the edited note to lookup failed.");
        File.WriteAllText(Path.Combine(AppPaths.Data, "notes-smoke.txt"), "PASS Actual row double-click; full long text; scrolling and selection; resized layout; close preserves unsaved editor; non-row ignored; editing word/context/note; cancel restores original; close discards draft; empty word rejected; save updates same ID and creation time without duplicates; list refresh and selection; repeated saves; return updated note to lookup.\nPASS Database updates persist after reopening; identical notes distinguished by ID; stale and missing records cannot be overwritten; parameterized quotes/newlines preserved.\nAll data belongs to the isolated test directory; no real user notes were accessed.");
        window.Close();
    }

    static void CheckNoteUpdates()
    {
        string file = Path.Combine(AppPaths.Data, "note-update-test-" + Guid.NewGuid().ToString("N") + ".db");
        NoteRow updated;
        using (var store = new LocalStore(file))
        {
            store.AddNote("yield", "identical context", "identical note");
            store.AddNote("yield", "identical context", "identical note");
            var original = store.Notes().Last(); var untouched = store.Notes().First();
            updated = store.UpdateNote(original, " yields ", "Quoted 'context'\n第二行", "Updated \"note\"\n第二段");
            if (store.Notes().Count != 2 || store.Notes().Single(note => note.Id == untouched.Id) != untouched || store.Notes().Single(note => note.Id == original.Id) != updated) throw new Exception("Update affected a duplicate or inserted a new row.");
            bool conflict = false;
            try { store.UpdateNote(original, "bad", "bad", "bad"); } catch (InvalidOperationException) { conflict = true; }
            if (!conflict || store.Notes().Single(note => note.Id == updated.Id) != updated) throw new Exception("Stale edit overwrote a newer revision.");
            bool missing = false;
            try { store.UpdateNote(updated with { Id = long.MaxValue }, "bad", "bad", "bad"); } catch (InvalidOperationException) { missing = true; }
            if (!missing || store.Notes().Count != 2) throw new Exception("Missing record update was not rejected.");
            bool empty = false;
            try { store.UpdateNote(updated, "  ", "", ""); } catch (ArgumentException) { empty = true; }
            if (!empty) throw new Exception("Store accepted an empty word.");
        }
        using var reopened = new LocalStore(file);
        if (reopened.Notes().Single(note => note.Id == updated.Id) != updated) throw new Exception("Edit did not persist after reopening.");
    }

    internal static async Task RunAsync()
    {
        Directory.CreateDirectory(AppPaths.Data);
        var window = new MainWindow(); Application.Current.MainWindow = window; window.Show();
        window.DictionaryCard.SpeechFactory = () => new SpeechService(Path.Combine(AppPaths.Data, "ui-speech.wav"));
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        Button Find(string content) => Descendants(window).OfType<Button>().First(b => b.Content as string == content);
        Find("打开示例").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        for (int i = 0; i < 100 && !window.ContextText.Text.Contains("production"); i++) await Task.Delay(100);
        if (!window.ContextText.Text.Contains("production")) throw new Exception("Sample UI did not recognize English.");
        var word = window.WordPanel.Children.OfType<Button>().First(b => b.Content as string == "production.");
        word.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (window.QueryText.Text != "production") throw new Exception("Word click did not fill query.");
        window.NoteText.Text = "测试笔记：城市的生产力；保留原句。";
        Find("保存原句与笔记").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (window.NotesGrid.Items.Count == 0) throw new Exception("Note was not saved.");
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        SaveImage(window, "demo-preview.png");
        await window.LookupAsync();
        if (window.Tabs.SelectedIndex != 3 || window.DictionaryCard.Heading.Text != "production" || !window.DictionaryCard.SourceInfo.Text.Contains("ECDICT")) throw new Exception("Offline lookup without credentials failed.");
        window.QueryText.Text = "yield";
        window.OnlineSupplement.IsChecked = false;
        await window.LookupAsync();
        if (window.DictionaryCard.Chinese.Items.Count < 3 || window.DictionaryCard.English.Items.Count < 4 || !window.DictionaryCard.PartsOfSpeech.Text.Contains("及物动词")) throw new Exception("Rich dictionary display failed.");
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        SaveImage(window, "dictionary-preview.png");
        window.DictionaryCard.PronounceButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (window.DictionaryCard.LastSpokenText != "yield") throw new Exception("Pronunciation button used the wrong word.");
        for (int i = 0; i < 150 && !window.DictionaryCard.SpeechStatus.Text.StartsWith("朗读结束"); i++) await Task.Delay(100);
        if (!window.DictionaryCard.SpeechStatus.Text.StartsWith("朗读结束")) throw new Exception("Word pronunciation did not complete: " + window.DictionaryCard.SpeechStatus.Text);
        var readDefinition = VisualDescendants(window.DictionaryCard).OfType<Button>().First(b => b.Tag as string == "an amount of a product");
        window.DictionaryCard.SlowSpeech.IsChecked = true;
        readDefinition.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (window.DictionaryCard.LastSpokenText != "an amount of a product") throw new Exception("English definition read button failed.");
        for (int i = 0; i < 150 && !window.DictionaryCard.SpeechStatus.Text.StartsWith("朗读结束"); i++) await Task.Delay(100);
        if (!window.DictionaryCard.SpeechStatus.Text.StartsWith("朗读结束")) throw new Exception("English definition speech did not finish.");
        window.QueryText.Text = "went";
        await window.LookupAsync();
        if (window.DictionaryCard.RootPronounceButton.Visibility != Visibility.Visible) throw new Exception("Lemma pronunciation button missing.");
        window.DictionaryCard.PronounceButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (window.DictionaryCard.LastSpokenText != "went") throw new Exception("Inflected pronunciation should speak the queried form.");
        window.DictionaryCard.RootPronounceButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (window.DictionaryCard.LastSpokenText != "go") throw new Exception("Lemma pronunciation should speak the root.");
        window.DictionaryCard.StopSpeechButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        window.DictionaryQuery.Text = "bank";
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.DataBind);
        if (window.QueryText.Text != "bank") throw new Exception("Dictionary search box binding failed.");
        await window.LookupAsync();
        if (window.DictionaryCard.Heading.Text != "bank" || window.DictionaryCard.English.Items.Count < 4) throw new Exception("Searching from the dictionary tab failed.");
        window.OnlineSupplement.IsChecked = true;
        window.AppId.Text = "test-only-id"; window.Secret.Password = "test-only-secret";
        window.TranslationClientFactory = () => new HttpClient(new FakeTranslationHandler("{\"errorCode\":\"108\"}"));
        await window.LookupAsync();
        if (!window.DictionaryCard.OnlineText.Text.Contains("108") || window.DictionaryCard.Chinese.Items.Count < 1) throw new Exception("Online failure must preserve offline meanings.");
        window.TranslationClientFactory = () => new HttpClient(new FakeTranslationHandler("{\"errorCode\":\"0\",\"translation\":[\"你的城市获得额外生产力。\"]}"));
        window.QueryText.Text = "Your cities gain additional production.";
        await window.LookupAsync();
        if (!window.DictionaryCard.OnlineText.Text.Contains("额外生产力") || window.DictionaryCard.Heading.Text != window.QueryText.Text || window.DictionaryCard.MeaningsGrid.Visibility != Visibility.Collapsed) throw new Exception("Sentence translation or stale-result reset failed.");
        window.AppId.Clear(); window.Secret.Clear();
        await window.LookupAsync();
        if (window.Tabs.SelectedIndex != 1) throw new Exception("Missing credentials for sentence should show settings.");
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        SaveImage(window, "api-preview.png");
        File.WriteAllText(Path.Combine(AppPaths.Data, "ui-smoke.txt"), "PASS Window load, sample OCR, word click, note save, offline bilingual/POS display, word pronunciation, English definition speech, queried-form and lemma speech, stop, dictionary search, online error preserving local results, mocked sentence translation, stale-result reset, missing credentials navigation, three rendered screenshots.\nSpeech was synthesized with a real installed English voice into a WAV file rather than playing through the speaker. HTTP responses are simulated; no user keys or real API calls were used.\nPhysical speaker output and screen capture in an actual game require user validation.");
        window.Close();
    }

    sealed class FakeTranslationHandler(string json, int delayMs = 0, Action<string>? inspectBody = null) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (inspectBody != null) inspectBody(await request.Content!.ReadAsStringAsync(token));
            if (delayMs > 0) await Task.Delay(delayMs, token);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) };
        }
    }

    static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>())
        {
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }

    static IEnumerable<DependencyObject> VisualDescendants(DependencyObject parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i); yield return child;
            foreach (var nested in VisualDescendants(child)) yield return nested;
        }
    }

    static void SaveImage(Window window, string name)
    {
        var visual = (FrameworkElement)window.Content;
        var bitmap = new RenderTargetBitmap((int)visual.ActualWidth, (int)visual.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        var drawing = new DrawingVisual();
        using (var context = drawing.RenderOpen()) context.DrawRectangle(new VisualBrush(visual), null, new Rect(0, 0, visual.ActualWidth, visual.ActualHeight));
        bitmap.Render(drawing); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(AppPaths.Data, name)); encoder.Save(file);
    }
}
