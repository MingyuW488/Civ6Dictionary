using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace Civ6EnglishDemo;

public partial class MainWindow : Window
{
    readonly LocalStore store;
    BitmapSource? currentImage;
    CancellationTokenSource? networkCancellation;
    bool ocrBusy, captureBusy, hotkeyRegistered, vocabularyBusy;
    HwndSource? hwndSource;
    long ocrGeneration;
    internal Func<System.Net.Http.HttpClient> TranslationClientFactory = Translation.CreateClient;

    public MainWindow()
    {
        InitializeComponent();
        store = new LocalStore(Path.Combine(AppPaths.Data, "learning.db"));
        RefreshNotes();
        RefreshVocabulary();
        SourceInitialized += (_, _) =>
        {
            hwndSource = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle); hwndSource.AddHook(Hook);
            hotkeyRegistered = Native.RegisterHotKey(hwndSource.Handle, 1, 0x4003, 0x44);
            if (!hotkeyRegistered) Status.Text = "Ctrl+Alt+D 被占用或注册失败。仍可使用“框选屏幕”按钮。";
        };
        Closing += (_, e) =>
        {
            // Keep the UI/database alive until in-flight work has completed or been cancelled.
            if (ocrBusy || captureBusy || vocabularyBusy || networkCancellation != null)
            {
                networkCancellation?.Cancel(); e.Cancel = true;
                Status.Text = "正在结束当前任务，请稍候再关闭窗口。";
            }
        };
        Closed += (_, _) =>
        {
            DictionaryCard.DisposeSpeech();
            if (hwndSource != null)
            {
                if (hotkeyRegistered) Native.UnregisterHotKey(hwndSource.Handle, 1);
                hwndSource.RemoveHook(Hook);
            }
            Secret.Clear(); store.Dispose();
        };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape && !captureBusy) WindowState = WindowState.Minimized; };
    }

    IntPtr Hook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == 0x0312 && wParam.ToInt32() == 1)
        {
            handled = true; _ = CaptureScreenAsync();
        }
        return IntPtr.Zero;
    }

    void Notify(string text) => Status.Text = text;
    void Error(Exception ex) => Notify("操作未完成：" + ex.Message);
    void TransferAll_Click(object sender, RoutedEventArgs e) => TransferTextToQuery(ContextText.Text);
    void TransferSelected_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ContextText.SelectedText)) { Notify("请先在识别文本中选中需要查询的句子。"); return; }
        TransferTextToQuery(ContextText.SelectedText);
    }

    void TransferTextToQuery(string text)
    {
        if (networkCancellation != null || ocrBusy) { Notify("当前任务尚未结束，请稍候再填入查询栏。"); return; }
        string query = string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (query.Length == 0) { Notify("请先识别图片，或在识别文本框中填写原句。"); return; }
        if (query.Length > 2000) { Notify("内容超过 2000 字符，请先选中需要翻译的句子，再点击“选中文字填入”。"); return; }
        QueryText.Text = query;
        QueryText.Focus(); QueryText.CaretIndex = 0; QueryText.BringIntoView();
        Notify("已填入查询栏。点击“查词 / 翻译”或按 Enter 即可翻译。");
    }
    async void Capture_Click(object sender, RoutedEventArgs e) => await CaptureScreenAsync();

    async Task CaptureScreenAsync()
    {
        if (captureBusy || ocrBusy) { Notify("正在识别，请稍候再截图。"); return; }
        captureBusy = true;
        var oldState = WindowState;
        try
        {
            Hide(); await Task.Delay(250);
            var image = Native.Capture();
            var selector = new SelectionWindow(image);
            if (selector.ShowDialog() == true && selector.SelectedImage != null)
            {
                Show(); WindowState = WindowState.Normal; Activate(); Tabs.SelectedIndex = 0;
                SetImage(selector.SelectedImage); await RunOcrAsync();
            }
            else { Show(); WindowState = oldState; }
        }
        catch (Exception ex) { Show(); WindowState = WindowState.Normal; Error(ex); }
        finally { captureBusy = false; }
    }

    async void Open_Click(object sender, RoutedEventArgs e)
    {
        if (captureBusy || ocrBusy) return;
        var dialog = new OpenFileDialog { Filter = "图片|*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff" };
        if (dialog.ShowDialog() != true) return;
        captureBusy = true;
        try
        {
            var image = LoadImage(dialog.FileName);
            var selector = new SelectionWindow(image);
            if (selector.ShowDialog() == true && selector.SelectedImage != null) { SetImage(selector.SelectedImage); await RunOcrAsync(); }
        }
        catch (Exception ex) { Error(ex); }
        finally { captureBusy = false; }
    }

    internal static BitmapSource LoadImage(string path)
    {
        using var input = File.OpenRead(path);
        var image = BitmapFrame.Create(input, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        if ((long)image.PixelWidth * image.PixelHeight > 100_000_000) throw new InvalidOperationException("图片过大，请使用单张游戏截图。");
        image.Freeze(); return image;
    }

    async void Sample_Click(object sender, RoutedEventArgs e)
    {
        if (ocrBusy || captureBusy) return;
        try { SetImage(LoadImage(Path.Combine(AppContext.BaseDirectory, "samples", "english-sample.png"))); await RunOcrAsync(); }
        catch (Exception ex) { Error(ex); }
    }

    void SetImage(BitmapSource image)
    {
        currentImage = image; Preview.Source = image;
        ContextText.Clear(); WordPanel.Children.Clear();
        OcrMetrics.Text = $"选区 {image.PixelWidth} × {image.PixelHeight} 像素";
    }

    async void Recognize_Click(object sender, RoutedEventArgs e) => await RunOcrAsync();

    async Task RunOcrAsync()
    {
        if (ocrBusy) return;
        if (currentImage == null) { Notify("请先打开图片或框选屏幕。"); return; }
        ocrBusy = true; RecognizeButton.IsEnabled = CaptureButton.IsEnabled = false;
        long generation = ++ocrGeneration;
        var image = currentImage; bool preprocess = Preprocess.IsChecked == true;
        Notify("正在本地识别英文，首次运行包含模型加载时间……");
        try
        {
            var result = await Task.Run(() => OcrService.Recognize(image, preprocess));
            if (generation != ocrGeneration) return;
            ContextText.Text = result.Text; WordPanel.Children.Clear();
            foreach (var word in result.Words)
            {
                var button = new Button { Content = word.Text, Padding = new Thickness(7, 4, 7, 4), ToolTip = $"图中位置 {word.X}, {word.Y}；点击填入查询栏" };
                button.Click += (_, _) => QueryText.Text = word.Text.Trim(' ', '.', ',', ':', ';', '!', '?', '"', '(', ')', '[', ']');
                WordPanel.Children.Add(button);
            }
            using var process = Process.GetCurrentProcess(); process.Refresh();
            OcrMetrics.Text = $"识别 {result.Milliseconds:F0} ms（含预处理和模型加载）\n{result.Words.Count} 个词 · 引擎置信度 {result.Confidence:P0}（不等于准确率）\n识别结束前工作集 {result.WorkingSet / 1048576.0:F1} MB / 私有内存 {result.PrivateBytes / 1048576.0:F1} MB\n释放引擎后当前工作集 {process.WorkingSet64 / 1048576.0:F1} MB";
            store.AddTest(new(DateTimeOffset.Now.ToString("o"), "OCR", preprocess ? "Tesseract/en/2x-auto" : "Tesseract/en/original", $"{image.PixelWidth}x{image.PixelHeight}", result.Milliseconds, result.Words.Count > 0 ? "OK" : "EMPTY", result.Confidence, result.WorkingSet, result.PrivateBytes));
            Notify(result.Words.Count == 0 ? "没有识别到文字。尝试更小的选区，或切换预处理选项。" : "识别完成。点击单词可填入查询栏，也可先修正原句。");
        }
        catch (Exception ex) { Error(ex); }
        finally { ocrBusy = false; RecognizeButton.IsEnabled = CaptureButton.IsEnabled = true; }
    }

    Credentials? ReadCredentials()
    {
        if (string.IsNullOrWhiteSpace(AppId.Text) || string.IsNullOrWhiteSpace(Secret.Password))
        {
            Tabs.SelectedIndex = 1; Notify("请填写自己已开通服务的应用 ID 和密钥。密钥不保存。 "); return null;
        }
        return new(Provider.SelectedIndex, AppId.Text.Trim(), Secret.Password.Trim());
    }

    void NetworkBusy(bool value)
    {
        Provider.IsEnabled = AppId.IsEnabled = Secret.IsEnabled = BenchmarkButton.IsEnabled = LookupButton.IsEnabled = DictionaryLookup.IsEnabled = QueryText.IsEnabled = DictionaryQuery.IsEnabled = !value;
        WordPanel.IsEnabled = NotesGrid.IsEnabled = !value;
        TransferAllButton.IsEnabled = TransferSelectedButton.IsEnabled = !value;
        CollectVocabularyButton.IsEnabled = !value && !vocabularyBusy;
        CancelButton.IsEnabled = value;
    }

    async void Lookup_Click(object sender, RoutedEventArgs e) => await LookupAsync();

    async void Query_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { e.Handled = true; await LookupAsync(); }
    }

    void BackToNotes_Click(object sender, RoutedEventArgs e) => Tabs.SelectedIndex = 0;

    internal async Task LookupAsync()
    {
        if (networkCancellation != null) return;
        string query = QueryText.Text.Trim();
        if (query.Length == 0 || query.Length > 2000) { Notify("请输入 1～2000 字符的单词、短语或句子。"); return; }
        using var cancellation = new CancellationTokenSource(); networkCancellation = cancellation; NetworkBusy(true);
        try
        {
            Tabs.SelectedIndex = 3;
            DictionaryCard.ShowMatch(new(query, null, "正在查询本地词库……", 0));
            Notify("正在查询本地中英文词典……");
            DictionaryMatch match;
            try { match = await Task.Run(() => LocalDictionary.Lookup(query)); }
            catch (Exception ex) when (ex is IOException or Microsoft.Data.Sqlite.SqliteException)
            { match = new(query, null, "本地词库读取失败，请从完整版本恢复 data 文件夹；仍可使用在线翻译。", 0); }
            if (cancellation.IsCancellationRequested) { Notify("查词已取消。"); return; }
            DictionaryCard.ShowMatch(match);
            bool sentence = !LocalDictionary.IsDictionaryQuery(LocalDictionary.Normalize(query));
            LookupResult.Text = match.Entry == null ? match.Message : $"{match.Entry.Word} · {match.Entry.PartsOfSpeech}\n完整中英文释义已显示在“词典详情”。";
            store.AddTest(new(DateTimeOffset.Now.ToString("o"), "DICTIONARY", "ECDICT", query, match.Milliseconds, match.Entry == null ? "NOT_FOUND" : "OK"));
            if (!sentence && OnlineSupplement.IsChecked != true)
            {
                DictionaryCard.OnlineText.Text = "联网补充已关闭。本次仅查询本地词典。";
                Notify(match.Entry == null ? match.Message : "本地查词完成，无需联网。"); return;
            }
            bool hasCredentials = !string.IsNullOrWhiteSpace(AppId.Text) && !string.IsNullOrWhiteSpace(Secret.Password);
            if (!hasCredentials && !sentence)
            {
                DictionaryCard.OnlineText.Text = "本地查词无需密钥。如需联网补充，可在“接口与测速”配置有道或百度。";
                Notify(match.Entry == null ? match.Message : "本地查词完成，已展示中英文释义及词性。"); return;
            }
            var credentials = ReadCredentials(); if (credentials == null) return;
            DictionaryCard.OnlineText.Text = "正在直连获取补充译文……";
            Notify(match.Entry == null ? "正在直连翻译……" : "本地释义已显示，正在获取补充译文……");
            using var client = TranslationClientFactory();
            var result = await Translation.QueryAsync(client, query, credentials, cancellation.Token);
            string online = result.Success ? $"{result.Text}\n\n{result.Milliseconds:F0} ms · {(credentials.Provider == 0 ? "有道" : "百度")}文本翻译" : $"{result.Text}（{result.Code}）";
            DictionaryCard.OnlineText.Text = online;
            if (match.Entry == null) LookupResult.Text = online;
            store.AddTest(new(DateTimeOffset.Now.ToString("o"), "LOOKUP", ProviderName(credentials), query, result.Milliseconds, result.Code));
            Notify(result.Success ? "查词与翻译完成。" : match.Entry != null ? "本地释义可正常查看；在线补充失败（" + result.Code + "）。" : "查询未成功（" + result.Code + "）：" + result.Text);
        }
        catch (Exception ex) { Error(ex); }
        finally { networkCancellation = null; NetworkBusy(false); }
    }

    static string ProviderName(Credentials credentials) => credentials.Provider == 0 ? "YoudaoTranslate" : "BaiduTranslate";

    async void Benchmark_Click(object sender, RoutedEventArgs e)
    {
        if (networkCancellation != null) return;
        var credentials = ReadCredentials(); if (credentials == null) return;
        var samples = SamplesText.Text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (samples.Length == 0 || samples.Length > 100 || samples.Any(s => s.Length > 2000)) { Notify("请输入 1～100 行样本，每行不超过 2000 字符。"); return; }
        using var cancellation = new CancellationTokenSource(); networkCancellation = cancellation; NetworkBusy(true);
        NetworkLog.Clear(); var results = new List<ApiResult>(); int cancelled = 0;
        try
        {
            using var client = Translation.CreateClient();
            for (int i = 0; i < samples.Length; i++)
            {
                if (cancellation.IsCancellationRequested) break;
                Notify($"测速 {i + 1}/{samples.Length}：{samples[i]}");
                var result = await Translation.QueryAsync(client, samples[i], credentials, cancellation.Token);
                if (result.Cancelled) cancelled++;
                else results.Add(result);
                NetworkLog.AppendText($"{i + 1:00}  {result.Milliseconds:F0} ms  {result.Code}  {samples[i]}\n"); if (!result.Success) NetworkLog.AppendText("    " + result.Text + "\n"); NetworkLog.ScrollToEnd();
                store.AddTest(new(DateTimeOffset.Now.ToString("o"), "BENCHMARK", ProviderName(credentials), samples[i], result.Milliseconds, result.Code));
                UpdateMetrics(results, cancelled);
                if (cancellation.IsCancellationRequested) break;
                if (i < samples.Length - 1) await Task.Delay(1200, cancellation.Token);
            }
            Notify(cancellation.IsCancellationRequested ? "测速已停止，已完成的记录已保留。" : "测速完成，可导出 CSV。建议换一个时段再次测试。");
        }
        catch (OperationCanceledException) { Notify("测速已停止，已完成的记录已保留。"); }
        catch (Exception ex) { Error(ex); }
        finally { networkCancellation = null; NetworkBusy(false); }
    }

    void UpdateMetrics(List<ApiResult> results, int cancelled)
    {
        var success = results.Where(r => r.Success).Select(r => r.Milliseconds).ToArray();
        string latency = success.Length == 0 ? "尚无成功样本" : $"成功耗时中位数 {Statistics.Median(success):F0} ms · P95 {Statistics.Percentile(success, .95):F0} ms";
        NetworkMetrics.Text = $"完成 {results.Count} 条 · 成功 {success.Length} · 失败 {results.Count - success.Length} · 取消 {cancelled}\n成功率 {(results.Count == 0 ? 0 : (double)success.Length / results.Count):P1}\n{latency}";
    }

    void Cancel_Click(object sender, RoutedEventArgs e) => networkCancellation?.Cancel();

    void SaveNote_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(QueryText.Text)) { Notify("先选择或输入要保存的单词。"); return; }
        try { store.AddNote(QueryText.Text.Trim(), ContextText.Text.Trim(), NoteText.Text.Trim()); RefreshNotes(); Notify("单词、原句和个人笔记已保存到本机。"); }
        catch (Exception ex) { Error(ex); }
    }

    void RefreshNotes() => NotesGrid.ItemsSource = store.Notes();
    void Refresh_Click(object sender, RoutedEventArgs e) { try { RefreshNotes(); } catch (Exception ex) { Error(ex); } }
    void Notes_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || e.OriginalSource is not DependencyObject source) return;
        if (ItemsControl.ContainerFromElement(NotesGrid, source) is not DataGridRow { Item: NoteRow row }) return;
        e.Handled = true;
        ShowNoteDetails(row);
    }

    void EditSelectedNote_Click(object sender, RoutedEventArgs e)
    {
        if (NotesGrid.SelectedItem is NoteRow row) ShowNoteDetails(row, true);
        else Notify("请先选中要修改的笔记。");
    }

    internal void ShowNoteDetails(NoteRow row, bool edit = false)
    {
        var details = new NoteDetailsWindow(row, (before, after) => { store.UpdateNote(before, after.Word, after.Context, after.Note); }) { Owner = this };
        if (edit) details.BeginEditing();
        bool useNote = details.ShowDialog() == true;
        if (details.HasSavedChanges)
        {
            RefreshNotes();
            NotesGrid.SelectedItem = NotesGrid.Items.OfType<NoteRow>().FirstOrDefault(note => note.Id == row.Id);
            Notify("笔记修改已保存。");
        }
        if (!useNote) return;
        row = details.CurrentNote;
        QueryText.Text = row.Word; ContextText.Text = row.Context; NoteText.Text = row.Note; Tabs.SelectedIndex = 0;
    }
    void Export_Click(object sender, RoutedEventArgs e) => Export(false);
    void ExportNotes_Click(object sender, RoutedEventArgs e) => Export(true);
    void Export(bool notes)
    {
        var dialog = new SaveFileDialog { Filter = "CSV 文件|*.csv", FileName = $"civ6-{(notes ? "notes" : "tests")}-{DateTime.Now:yyyyMMdd-HHmmss}.csv" };
        if (dialog.ShowDialog() != true) return;
        try { if (notes) store.ExportNotes(dialog.FileName); else store.ExportTests(dialog.FileName); Notify("已导出：" + dialog.FileName); }
        catch (Exception ex) { Error(ex); }
    }
    void DataFolder_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo(AppPaths.Data) { UseShellExecute = true }); }
        catch (Exception ex) { Error(ex); }
    }
}
