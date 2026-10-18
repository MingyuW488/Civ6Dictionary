using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;

namespace Civ6EnglishDemo;

public partial class MainWindow
{
    int vocabularyPage;
    DispatcherTimer? vocabularySearchTimer;

    static DictionaryEntry? VocabularyEntry(string word)
    {
        try { return LocalDictionary.Lookup(word).Entry; }
        catch (Exception ex) when (ex is IOException or Microsoft.Data.Sqlite.SqliteException) { return null; }
    }

    void OpenVocabulary_Click(object sender, RoutedEventArgs e) { RefreshVocabulary(); Tabs.SelectedIndex = 4; }

    async void CollectVocabulary_Click(object sender, RoutedEventArgs e)
    {
        if (vocabularyBusy || networkCancellation != null) return;
        string word = LocalDictionary.Normalize(QueryText.Text);
        if (!LocalDictionary.IsDictionaryQuery(word)) { Notify("请先输入英文单词或短语；整句请保存到本地笔记。"); return; }
        string context = ContextText.Text.Trim(), note = NoteText.Text.Trim();
        SetVocabularyBusy(true);
        try
        {
            var entry = await Task.Run(() => VocabularyEntry(word));
            bool added = store.CollectVocabulary(word, entry, context, note);
            RefreshVocabulary();
            Notify(added ? $"已收藏 {word}，可在“生词本”搜索。" : $"{word} 已在生词本中，原有内容已保留。");
        }
        catch (Exception ex) { Error(ex); }
        finally { SetVocabularyBusy(false); }
    }

    void SetVocabularyBusy(bool value)
    {
        vocabularyBusy = value;
        ImportVocabularyButton.IsEnabled = !value;
        CollectVocabularyButton.IsEnabled = !value && networkCancellation == null;
    }

    async void ImportVocabulary_Click(object sender, RoutedEventArgs e)
    {
        if (vocabularyBusy) return;
        SetVocabularyBusy(true);
        try
        {
            var notes = store.Notes(-1);
            var candidates = notes.GroupBy(n => LocalDictionary.Normalize(n.Word))
                .Where(g => LocalDictionary.IsDictionaryQuery(g.Key)).Select(g => g.First()).ToList();
            int added = 0;
            foreach (var note in candidates)
            {
                var entry = await Task.Run(() => VocabularyEntry(note.Word));
                if (store.CollectVocabulary(note.Word, entry, note.Context, note.Note)) added++;
            }
            vocabularyPage = 0; RefreshVocabulary();
            Notify($"导入完成：新增 {added} 个词 / 短语，重复词已合并；整句笔记仍保留在“本地笔记”。");
        }
        catch (Exception ex) { RefreshVocabulary(); Error(ex); }
        finally { SetVocabularyBusy(false); }
    }

    void VocabularySearch_Changed(object sender, TextChangedEventArgs e)
    {
        // XAML creates the search box before the grid and before LocalStore is opened.
        if (VocabularyGrid == null || store == null) return;
        if (vocabularySearchTimer == null)
        {
            vocabularySearchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            vocabularySearchTimer.Tick += (_, _) => { vocabularySearchTimer.Stop(); vocabularyPage = 0; RefreshVocabulary(); };
            Closed += (_, _) => vocabularySearchTimer.Stop();
        }
        vocabularySearchTimer.Stop(); vocabularySearchTimer.Start();
    }

    internal void RefreshVocabulary()
    {
        try
        {
            var result = store.SearchVocabulary(VocabularySearch.Text, vocabularyPage);
            if (result.Rows.Count == 0 && vocabularyPage > 0)
            {
                vocabularyPage = Math.Max(0, (int)((result.Total - 1) / 100));
                result = store.SearchVocabulary(VocabularySearch.Text, vocabularyPage);
            }
            VocabularyGrid.ItemsSource = result.Rows;
            VocabularyCount.Text = $"找到 {result.Total} 个词 · 第 {vocabularyPage + 1} / {Math.Max(1, (result.Total + 99) / 100)} 页";
            VocabularyPrevious.IsEnabled = vocabularyPage > 0;
            VocabularyNext.IsEnabled = (long)(vocabularyPage + 1) * 100 < result.Total;
            VocabularyDetail.Text = result.Total == 0 ? "暂无符合条件的词。可在词典详情收藏，或点击“导入已有笔记”。" : "选中一个词查看完整内容，双击重新查词。";
        }
        catch (Exception ex) { Error(ex); }
    }

    void RefreshVocabulary_Click(object sender, RoutedEventArgs e) { vocabularyPage = 0; RefreshVocabulary(); }
    void VocabularyPrevious_Click(object sender, RoutedEventArgs e) { vocabularyPage = Math.Max(0, vocabularyPage - 1); RefreshVocabulary(); }
    void VocabularyNext_Click(object sender, RoutedEventArgs e) { vocabularyPage++; RefreshVocabulary(); }

    void VocabularySelection_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (VocabularyGrid.SelectedItem is not VocabularyRow row) return;
        try
        {
            var text = new StringBuilder($"{row.Word}  {row.Pos}\n{row.Meaning}\n\n收藏时原句：\n{row.Context}\n\n收藏时笔记：\n{row.Note}");
            foreach (var note in store.VocabularyNotes(row.Word))
                text.Append($"\n\n关联笔记（{note.Created}）：\n原句：{note.Context}\n笔记：{note.Note}");
            VocabularyDetail.Text = text.ToString();
        }
        catch (Exception ex) { Error(ex); }
    }

    async void LookupVocabulary_Click(object sender, RoutedEventArgs e)
    {
        if (networkCancellation != null) { Notify("请等待当前查询结束。"); return; }
        if (VocabularyGrid.SelectedItem is not VocabularyRow row) { Notify("请先选中一个词。"); return; }
        QueryText.Text = row.Word;
        await LookupAsync();
    }

    void Vocabulary_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || e.OriginalSource is not DependencyObject source) return;
        if (ItemsControl.ContainerFromElement(VocabularyGrid, source) is not DataGridRow { Item: VocabularyRow row }) return;
        VocabularyGrid.SelectedItem = row;
        e.Handled = true; LookupVocabulary_Click(sender, e);
    }

    void RemoveVocabulary_Click(object sender, RoutedEventArgs e)
    {
        if (VocabularyGrid.SelectedItem is not VocabularyRow row) { Notify("请先选中要移出的词。"); return; }
        try { store.RemoveVocabulary(row.Id); RefreshVocabulary(); Notify($"已将 {row.Word} 移出生词本，原有本地笔记仍保留。可重新收藏或导入。"); }
        catch (Exception ex) { Error(ex); }
    }

    void ExportVocabulary_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = "CSV 文件|*.csv", FileName = $"civ6-vocabulary-{DateTime.Now:yyyyMMdd-HHmmss}.csv" };
        if (dialog.ShowDialog() != true) return;
        try { store.ExportVocabulary(dialog.FileName); Notify("已导出全部生词及收藏时的原句、笔记：" + dialog.FileName); }
        catch (Exception ex) { Error(ex); }
    }
}
