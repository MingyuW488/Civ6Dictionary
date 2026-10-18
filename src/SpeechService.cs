using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace Civ6EnglishDemo;

internal record SpeechState(bool IsSpeaking, string Message, bool IsError = false);

/// <summary>Lazy, offline SAPI playback on its own STA. Speech never blocks the WPF thread.</summary>
internal sealed class SpeechService : IDisposable
{
    readonly string? testWavePath;
    readonly ManualResetEventSlim ready = new(false);
    Thread? worker;
    Dispatcher? dispatcher;
    DispatcherTimer? timer;
    object? voice;
    object? fileStream;
    string voiceName = "";
    volatile bool disposed;
    long revision, activeRevision;

    internal event Action<SpeechState>? StateChanged;
    internal string LastText { get; private set; } = "";

    // A file output is used only by the offline tests; normal playback uses the system speaker.
    internal SpeechService(string? testWavePath = null) => this.testWavePath = testWavePath;

    internal void Speak(string text, bool slow = false)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        text = text.Trim();
        if (text.Length == 0) throw new ArgumentException("请先选择要朗读的英文。");
        if (text.Length > 2000) throw new ArgumentException("单次最多朗读 2000 个字符，请选择较短的解释。");
        EnsureWorker();
        LastText = text;
        long ticket = Interlocked.Increment(ref revision);
        dispatcher!.BeginInvoke(() => Start(text, slow, ticket));
    }

    void EnsureWorker()
    {
        if (worker != null) return;
        worker = new Thread(() =>
        {
            dispatcher = Dispatcher.CurrentDispatcher;
            timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = TimeSpan.FromMilliseconds(100) };
            timer.Tick += (_, _) => Poll();
            ready.Set();
            try { Dispatcher.Run(); }
            finally { Cleanup(); }
        }) { IsBackground = true, Name = "Dictionary English speech" };
        worker.SetApartmentState(ApartmentState.STA);
        worker.Start();
        if (!ready.Wait(TimeSpan.FromSeconds(3))) throw new InvalidOperationException("语音组件启动超时，请稍后重试。");
    }

    void Start(string text, bool slow, long ticket)
    {
        if (disposed || ticket != Volatile.Read(ref revision)) return;
        try
        {
            if (voice == null)
            {
                voice = CreateCom("SAPI.SpVoice");
                voiceName = SelectEnglishVoice(voice);
                if (testWavePath != null)
                {
                    fileStream = CreateCom("SAPI.SpFileStream");
                    ((dynamic)fileStream).Open(testWavePath, 3, false); // SSFMCreateForWrite
                    ((dynamic)voice).AudioOutputStream = fileStream;
                }
            }
            if (disposed || ticket != Volatile.Read(ref revision)) return;
            dynamic speaker = voice;
            speaker.Rate = slow ? -3 : 0;
            speaker.Volume = 100;
            // Asynchronous + purge previous request + literal text (never SSML or a filename).
            speaker.Speak(text, 1 | 2 | 16);
            activeRevision = ticket;
            timer!.Start();
            StateChanged?.Invoke(new(true, $"正在朗读 · {voiceName}{(slow ? " · 慢速" : "")}"));
        }
        catch (Exception ex)
        {
            Cleanup();
            if (!disposed && ticket == Volatile.Read(ref revision)) StateChanged?.Invoke(new(false, "无法朗读：" + ex.Message, true));
        }
    }

    void Poll()
    {
        try
        {
            if (voice == null || !((dynamic)voice).WaitUntilDone(0)) return;
            timer!.Stop();
            if (!disposed && activeRevision == Volatile.Read(ref revision)) StateChanged?.Invoke(new(false, "朗读结束 · " + voiceName));
        }
        catch (Exception ex)
        {
            timer?.Stop();
            if (!disposed) StateChanged?.Invoke(new(false, "朗读未完成：" + ex.Message, true));
        }
    }

    internal void Stop()
    {
        if (disposed || dispatcher == null) return;
        long ticket = Interlocked.Increment(ref revision);
        dispatcher.BeginInvoke(() =>
        {
            if (ticket != Volatile.Read(ref revision)) return;
            timer?.Stop();
            try { if (voice != null) ((dynamic)voice).Speak("", 1 | 2 | 16); }
            catch { /* A failed stop must not prevent a new playback request. */ }
            if (!disposed) StateChanged?.Invoke(new(false, "已停止朗读。"));
        });
    }

    static object CreateCom(string progId)
    {
        var type = Type.GetTypeFromProgID(progId) ?? throw new InvalidOperationException("Windows 语音组件不可用。");
        return Activator.CreateInstance(type) ?? throw new InvalidOperationException("无法启动 Windows 语音组件。");
    }

    internal static bool IsEnglishLanguage(string language) => language.Split(';').Any(value =>
        int.TryParse(value.Trim(), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int id) && (id & 0x3ff) == 9);

    static string SelectEnglishVoice(object speaker)
    {
        object? tokens = null, best = null;
        int bestRank = int.MaxValue;
        try
        {
            tokens = ((dynamic)speaker).GetVoices("", "");
            int count = ((dynamic)tokens).Count;
            for (int i = 0; i < count; i++)
            {
                object? token = ((dynamic)tokens).Item(i);
                try
                {
                    string language = ((dynamic)token).GetAttribute("Language");
                    if (!IsEnglishLanguage(language)) continue;
                    int rank = language.Split(';').Any(x => x.Trim().Equals("409", StringComparison.OrdinalIgnoreCase)) ? 0 : 1;
                    if (rank >= bestRank) continue;
                    Release(best); best = token; token = null; bestRank = rank;
                }
                finally { Release(token); }
            }
            if (best == null) throw new InvalidOperationException("没有可用的英文桌面语音。请在 Windows 中安装英文语音后重启程序。");
            ((dynamic)speaker).Voice = best;
            return ((dynamic)best).GetDescription(0);
        }
        finally { Release(best); Release(tokens); }
    }

    static void Release(object? value)
    {
        if (value != null && Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value);
    }

    void Cleanup()
    {
        timer?.Stop();
        try { if (voice != null) ((dynamic)voice).Speak("", 1 | 2 | 16); } catch { }
        Release(voice); voice = null;
        if (fileStream != null)
        {
            try { ((dynamic)fileStream).Close(); } catch { }
            Release(fileStream); fileStream = null;
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        Interlocked.Increment(ref revision);
        var target = dispatcher;
        if (target == null) return;
        try
        {
            target.Invoke(new Action(() => { Cleanup(); target.BeginInvokeShutdown(DispatcherPriority.Background); }), DispatcherPriority.Send, CancellationToken.None, TimeSpan.FromSeconds(2));
        }
        catch (TimeoutException) { target.BeginInvoke(() => { Cleanup(); target.BeginInvokeShutdown(DispatcherPriority.Background); }); }
    }
}
