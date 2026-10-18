using System.IO;
using System.Windows;

namespace Civ6EnglishDemo;

internal static class SelfTest
{
    internal static void Run()
    {
        var log = new List<string>();
        void Check(bool condition, string name) { if (!condition) throw new Exception(name); log.Add("PASS " + name); }
        Check(Translation.YoudaoInput("short") == "short", "Youdao short input");
        Check(Translation.YoudaoInput("0123456789abcdefghijXYZ") == "012345678923defghijXYZ", "Youdao long input");
        Check(Translation.YoudaoInput(new string('a', 20) + "😀") == "aaaaaaaaaa21aaaaaaaaa😀", "Youdao Unicode code points");
        Check(Translation.Hash("abc", true) == "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", "SHA256 signing");
        var parameters = Translation.Parameters("apple", new(1, "2015063000000001", "12345678"), "1435660288", "0");
        Check(parameters["sign"] == "f89f9594663708c1605f3d736d01d2d4", "Baidu published signature example");
        Check(Translation.Parse("{\"errorCode\":\"0\",\"translation\":[\"产出\"]}", 0, 1).Text == "产出", "Youdao response");
        Check(Translation.Parse("{\"trans_result\":[{\"dst\":\"苹果\"}]}", 1, 1).Text == "苹果", "Baidu response");
        Check(!Translation.Parse("{\"error_code\":\"54003\"}", 1, 1).Success, "API error is not success");
        Check(!Translation.Parse("{\"translation\":[]}", 0, 1).Success, "Empty response is not success");
        Check(!Translation.Parse("[]", 0, 1).Success, "Unexpected root is not success");
        var invalidApp = Translation.Parse("{\"errorCode\":\"108\"}", 0, 1);
        Check(!invalidApp.Success && invalidApp.Code == "108" && invalidApp.Text.Contains("应用 ID 无效"), "Youdao 108 explains invalid app ID");
        Check(Translation.ErrorMessage(0, "110").Contains("绑定"), "Youdao 110 explains service binding");
        Check(Translation.ErrorMessage(0, "202").Contains("同一应用"), "Youdao 202 explains credential pairing");
        Check(!Translation.ErrorMessage(1, "108").Contains("有道"), "Provider-specific error guidance");
        Check(Statistics.Percentile(Enumerable.Range(1, 20).Select(i => (double)i), .95) == 19, "P95 nearest rank");
        Check(Statistics.Percentile([], .95) == 0, "Empty statistics");
        Check(Statistics.Median([1, 3, 9, 11]) == 6, "Even sample median");
        var yieldEntry = LocalDictionary.Lookup("YIELD").Entry;
        Check(yieldEntry != null && yieldEntry.Chinese.Count >= 3 && yieldEntry.English.Count >= 4, "Multi-sense bilingual dictionary lookup");
        Check(yieldEntry!.PartsOfSpeech.Contains("名词") && yieldEntry.PartsOfSpeech.Contains("及物动词") && yieldEntry.PartsOfSpeech.Contains("不及物动词"), "Parts of speech retained across Chinese and English");
        Check(yieldEntry.Forms.Contains("yielded") && yieldEntry.Forms.Contains("yielding"), "Word forms displayed");
        Check(LocalDictionary.Lookup("denounced").Entry?.Word == "denounce", "Inflected word resolves to lemma");
        Check(LocalDictionary.Lookup("went").Entry?.Word == "go", "Irregular past resolves to lemma");
        Check(LocalDictionary.Lookup("yields").Entry?.Word == "yield", "Plural or third-person form lookup");
        var phrase = LocalDictionary.Lookup("trade route").Entry;
        Check(phrase != null && phrase.Chinese.Count > 0 && phrase.English.Count == 0, "Phrase with missing English is represented honestly");
        Check(LocalDictionary.Lookup("zzzzunknownwordabcd").Entry == null, "Uncollected word has no fabricated meaning");
        Check(LocalDictionary.Lookup("Your cities gain additional production.").Entry == null, "Sentence routes to translation");
        Check(LocalDictionary.Lookup("yield", Path.Combine(AppPaths.Data, "missing-dictionary.db")).Message.Contains("缺少"), "Missing dictionary handled without creating a file");
        Check(LocalDictionary.Senses("n. first\\nvt. second\n[医] third").Count == 3, "Literal and actual newlines in dictionary senses");
        Check(LocalDictionary.Senses("v change location").Single().Label == "v. 动词", "POS without a dot");
        var lookupWatch = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < 100; i++) LocalDictionary.Lookup(i % 2 == 0 ? "yield" : "suzerainty");
        log.Add($"  100 indexed local lookups: {lookupWatch.Elapsed.TotalMilliseconds:F0} ms; dictionary opened read-only with 2 MB page-cache limit.");
        var crop = SelectionWindow.MapCrop(new Point(90, 80), new Point(10, 20), 100, 100, 200, 300);
        Check(crop == new Int32Rect(20, 60, 160, 180), "Reverse selection and scaled crop");
        var edge = SelectionWindow.MapCrop(new Point(-10, -20), new Point(120, 130), 100, 100, 200, 300);
        Check(edge == new Int32Rect(0, 0, 200, 300), "Crop clamp");
        var directory = Path.Combine(Path.GetTempPath(), "Civ6DemoTest-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            var db = Path.Combine(directory, "test.db");
            using (var store = new LocalStore(db))
            {
                store.AddNote("yield", "A \"quoted\" sentence, with comma.\nNext line.", "=1+1");
                Check(store.Notes().Single().Word == "yield", "SQLite note round trip");
                store.ExportNotes(Path.Combine(directory, "notes.csv"));
                var csv = File.ReadAllText(Path.Combine(directory, "notes.csv"));
                Check(csv.Contains("\"\"quoted\"\"") && csv.Contains("'=1+1"), "CSV quotes, newlines and formula protection");
                store.AddTest(new(DateTimeOffset.Now.ToString("o"), "OCR", "Tesseract", "sample", 50, "OK", .9, 1, 2));
                store.ExportTests(Path.Combine(directory, "tests.csv"));
                Check(File.ReadAllLines(Path.Combine(directory, "tests.csv")).Length == 2, "CSV benchmark export");
            }
            using (var store = new LocalStore(db)) Check(store.Notes().Count == 1, "Notes persist after reopen");
            foreach (bool preprocessing in new[] { false, true })
            {
                var image = MainWindow.LoadImage(Path.Combine(AppContext.BaseDirectory, "samples", preprocessing ? "english-sample.png" : "light-sample.png"));
                var result = OcrService.Recognize(image, preprocessing);
                Check(result.Text.Contains("production", StringComparison.OrdinalIgnoreCase) && result.Words.Count >= 15, $"Real offline OCR, preprocessing={preprocessing}, text={result.Text}");
                log.Add($"  OCR {result.Milliseconds:F0} ms, {result.Words.Count} words, working set {result.WorkingSet / 1048576.0:F1} MB");
            }
            using var client = Translation.CreateClient();
            Check(SpeechService.IsEnglishLanguage("409") && SpeechService.IsEnglishLanguage("809;409"), "English speech languages detected");
            Check(!SpeechService.IsEnglishLanguage("804") && !SpeechService.IsEnglishLanguage("invalid"), "Non-English voice is not selected for pronunciation");
            long RenderSpeech(string file, bool slow)
            {
                using var done = new ManualResetEventSlim();
                SpeechState? final = null;
                using (var speech = new SpeechService(file))
                {
                    speech.StateChanged += state => { if (!state.IsSpeaking) { final = state; done.Set(); } };
                    speech.Speak("yield. an amount of a product.", slow);
                    if (!done.Wait(TimeSpan.FromSeconds(15))) throw new Exception("English speech timed out.");
                    if (final == null || final.IsError) throw new Exception(final?.Message ?? "No speech result.");
                }
                var bytes = File.ReadAllBytes(file);
                if (bytes.Length < 1000 || System.Text.Encoding.ASCII.GetString(bytes, 0, 4) != "RIFF" || System.Text.Encoding.ASCII.GetString(bytes, 8, 4) != "WAVE") throw new Exception("Speech did not produce a valid wave file.");
                return bytes.Length;
            }
            long normalAudio = RenderSpeech(Path.Combine(directory, "normal.wav"), false);
            Check(normalAudio > 1000, "Real offline English speech produces WAV audio");
            long slowAudio = RenderSpeech(Path.Combine(directory, "slow.wav"), true);
            Check(slowAudio > normalAudio, "Slow speech produces a longer pronunciation");
            using (var started = new ManualResetEventSlim())
            using (var stopped = new ManualResetEventSlim())
            using (var speech = new SpeechService(Path.Combine(directory, "stop.wav")))
            {
                speech.StateChanged += state => { if (state.IsSpeaking) started.Set(); if (state.Message.StartsWith("已停止")) stopped.Set(); };
                speech.Speak("district");
                speech.Speak(string.Join(" ", Enumerable.Repeat("Trade routes provide gold and science.", 10)), true);
                Check(started.Wait(TimeSpan.FromSeconds(15)), "Repeated speech request replaces previous playback");
                speech.Stop();
                Check(stopped.Wait(TimeSpan.FromSeconds(5)), "Stop cancels active speech");
            }
            using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
            var cancelled = Translation.QueryAsync(client, "yield", new(0, "test", "test"), cancellation.Token).GetAwaiter().GetResult();
            Check(cancelled.Cancelled && !cancelled.Success, "Cancellation without real API request");
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(directory, true);
        }
        Directory.CreateDirectory(AppPaths.Data); File.WriteAllLines(Path.Combine(AppPaths.Data, "self-test.txt"), log);
    }
}
