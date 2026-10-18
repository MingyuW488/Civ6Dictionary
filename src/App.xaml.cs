using System.IO;
using System.Windows;

namespace Civ6EnglishDemo;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Contains("--storage-smoke"))
        {
            try { StorageSmoke.Run(); Shutdown(0); }
            catch (Exception ex)
            {
                File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "storage-smoke-failure.txt"), ex.ToString());
                Shutdown(1);
            }
            return;
        }
        string? smokeMode = e.Args.FirstOrDefault(arg => arg is "--ui-smoke" or "--notes-smoke" or "--sentence-smoke" or "--syllable-smoke" or "--vocabulary-smoke");
        if (smokeMode != null)
        {
            string report = smokeMode switch { "--vocabulary-smoke" => "vocabulary-smoke.txt", "--notes-smoke" => "notes-smoke.txt", "--sentence-smoke" => "sentence-smoke.txt", "--syllable-smoke" => "syllable-smoke.txt", _ => "ui-smoke.txt" };
            try
            {
                switch (smokeMode)
                {
                    case "--vocabulary-smoke": await VocabularySmoke.RunAsync(); break;
                    case "--notes-smoke": await WindowSmoke.RunNotesAsync(); break;
                    case "--sentence-smoke": await WindowSmoke.RunSentenceAsync(); break;
                    case "--syllable-smoke": await WindowSmoke.RunSyllableAsync(); break;
                    default: await WindowSmoke.RunAsync(); break;
                }
                Shutdown(0);
            }
            catch (Exception ex)
            {
                Directory.CreateDirectory(AppPaths.Data);
                File.WriteAllText(Path.Combine(AppPaths.Data, report), "FAIL\n" + ex);
                Shutdown(1);
            }
            return;
        }
        if (e.Args.Contains("--self-test"))
        {
            try { SelfTest.Run(); Shutdown(0); }
            catch (Exception ex)
            {
                Directory.CreateDirectory(AppPaths.Data);
                File.WriteAllText(Path.Combine(AppPaths.Data, "self-test.txt"), "FAIL\n" + ex);
                Shutdown(1);
            }
            return;
        }
        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }
}
