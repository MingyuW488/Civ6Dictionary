using System.Windows;

namespace Civ6EnglishDemo;

public partial class NoteDetailsWindow : Window
{
    readonly Action<NoteRow, NoteRow> saveChanges;
    internal NoteRow CurrentNote { get; private set; }
    internal bool HasSavedChanges { get; private set; }

    internal NoteDetailsWindow(NoteRow note, Action<NoteRow, NoteRow> saveChanges)
    {
        CurrentNote = note; this.saveChanges = saveChanges;
        InitializeComponent();
        CreatedText.Text = "保存时间：" + (DateTimeOffset.TryParse(note.Created, out var created) ? created.ToLocalTime().ToString("yyyy-MM-dd HH:mm") : note.Created);
        RestoreFields();
    }

    void RestoreFields()
    {
        WordDetail.Text = CurrentNote.Word; ContextDetail.Text = CurrentNote.Context; NoteDetail.Text = CurrentNote.Note;
    }

    internal void BeginEditing()
    {
        SetEditing(true); WordDetail.Focus();
        EditStatus.Text = "可修改单词、原句和笔记。点击“保存修改”更新原记录；取消或关闭不会保存。";
    }

    void SetEditing(bool editing)
    {
        WordDetail.IsReadOnly = ContextDetail.IsReadOnly = NoteDetail.IsReadOnly = !editing;
        EditNoteButton.Visibility = editing ? Visibility.Collapsed : Visibility.Visible;
        SaveChangesButton.Visibility = CancelChangesButton.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
        UseNoteButton.IsEnabled = !editing;
        CloseDetailButton.Content = editing ? "关闭（不保存）" : "关闭";
    }

    void EditNote_Click(object sender, RoutedEventArgs e) => BeginEditing();
    void CancelChanges_Click(object sender, RoutedEventArgs e)
    {
        RestoreFields(); SetEditing(false); EditStatus.Text = "已取消修改，恢复到上次保存的内容。";
    }
    void SaveChanges_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(WordDetail.Text)) { EditStatus.Text = "单词 / 短语不能为空，请填写后再保存。"; WordDetail.Focus(); return; }
        var edited = CurrentNote with { Word = WordDetail.Text.Trim(), Context = ContextDetail.Text, Note = NoteDetail.Text };
        try
        {
            saveChanges(CurrentNote, edited);
            CurrentNote = edited; HasSavedChanges = true;
            RestoreFields(); SetEditing(false); EditStatus.Text = "修改已保存，原记录已更新。";
        }
        catch (Exception ex) { EditStatus.Text = "保存未完成：" + ex.Message; }
    }

    void UseNote_Click(object sender, RoutedEventArgs e) => DialogResult = true;
    void CloseDetail_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
