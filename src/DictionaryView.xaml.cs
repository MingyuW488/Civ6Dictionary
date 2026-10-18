using System.Windows;
using System.Windows.Controls;

namespace Civ6EnglishDemo;

public partial class DictionaryView : UserControl
{
    SpeechService? speech;
    string pronunciation = "", rootWord = "";
    bool speechDisposed;
    internal Func<SpeechService> SpeechFactory = () => new SpeechService();
    internal string LastSpokenText => speech?.LastText ?? "";

    public DictionaryView() => InitializeComponent();

    void Pronounce_Click(object sender, RoutedEventArgs e) => Speak(pronunciation);
    void RootPronounce_Click(object sender, RoutedEventArgs e) => Speak(rootWord);
    void ReadDefinition_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string text }) Speak(text);
    }
    void StopSpeech_Click(object sender, RoutedEventArgs e) => speech?.Stop();

    void Speak(string text)
    {
        if (speechDisposed) return;
        try
        {
            if (speech == null)
            {
                speech = SpeechFactory();
                speech.StateChanged += state => Dispatcher.BeginInvoke(() =>
                {
                    if (speechDisposed) return;
                    StopSpeechButton.IsEnabled = state.IsSpeaking;
                    SpeechStatus.Text = state.Message;
                });
            }
            SpeechStatus.Text = "正在准备英文语音……";
            StopSpeechButton.IsEnabled = true;
            speech.Speak(text, SlowSpeech.IsChecked == true);
        }
        catch (Exception ex) { StopSpeechButton.IsEnabled = false; SpeechStatus.Text = "无法朗读：" + ex.Message; }
    }

    internal void DisposeSpeech()
    {
        speechDisposed = true;
        speech?.Dispose(); speech = null;
    }

    internal void ShowMatch(DictionaryMatch match)
    {
        speech?.Stop();
        StopSpeechButton.IsEnabled = false;
        var entry = match.Entry;
        pronunciation = match.Query.Trim(); rootWord = entry?.Word ?? "";
        PronounceButton.IsEnabled = pronunciation.Length is > 0 and <= 2000;
        RootPronounceButton.Visibility = rootWord.Length > 0 && LocalDictionary.Normalize(pronunciation) != rootWord.ToLowerInvariant() ? Visibility.Visible : Visibility.Collapsed;
        SpeechStatus.Text = "点击发音朗读当前查询；英文解释旁也可逐条朗读。";
        Heading.Text = entry == null || LocalDictionary.Normalize(match.Query) == entry.Word.ToLowerInvariant() ? entry?.Word ?? match.Query : $"{match.Query}  →  {entry.Word}";
        string? syllables = entry == null ? null : SyllableDictionary.Lookup(entry.Word);
        Syllables.Text = syllables ?? "";
        Syllables.Visibility = syllables == null ? Visibility.Collapsed : Visibility.Visible;
        Syllables.ToolTip = entry == null ? null : $"{entry.Word} 的书面分节；实际发音请结合音标和朗读。";
        Phonetic.Text = string.IsNullOrWhiteSpace(entry?.Phonetic) ? "" : $"/{entry.Phonetic}/";
        PartsOfSpeech.Text = entry == null ? "" : string.IsNullOrWhiteSpace(entry.PartsOfSpeech) ? "词性暂未收录" : entry.PartsOfSpeech;
        MeaningsGrid.Visibility = entry == null ? Visibility.Collapsed : Visibility.Visible;
        SourceInfo.Text = entry == null ? match.Message : $"本地词典 · ECDICT · {match.Milliseconds:F0} ms";
        Notice.Text = entry == null ? "可以调整拼写，或使用有道 / 百度补充翻译。" : "不同词条的收录程度不同；中文义项和英文义项分别展示。";
        Chinese.ItemsSource = entry?.Chinese is { Count: > 0 } chinese ? chinese : new[] { new DictionarySense("", "本词条暂未收录中文释义。") };
        English.ItemsSource = entry?.English is { Count: > 0 } english ? english : new[] { new DictionarySense("", "本词条暂未收录英文解释。") };
        Forms.Text = entry?.Forms ?? "";
        FormsBox.Visibility = string.IsNullOrEmpty(Forms.Text) ? Visibility.Collapsed : Visibility.Visible;
        OnlineText.Text = "本地释义已就绪。需要简明译文时，可勾选“联网补充翻译”后再次查询。";
        ResultsScroll.ScrollToTop();
    }
}
