using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Media;
using Switchit.Core;

namespace Switchit;

public sealed class App : Application
{
    private Mutex? _singleInstance;

    [STAThread]
    public static void Main(string[] args)
    {
        var app = new App { ShutdownMode = ShutdownMode.OnMainWindowClose };
        var dataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Switchit");
        var customDataDirectory = false;
        for (var i = 0; i < args.Length - 1; i++)
            if (args[i] == "--data-dir") { dataDirectory = Path.GetFullPath(args[i + 1]); customDataDirectory = true; }
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(dataDirectory.ToUpperInvariant())))[..20];
        app._singleInstance = new Mutex(true, "Local\\Switchit-" + key, out var isFirst);
        if (!isFirst)
        {
            MessageBox.Show("Switchit!이 이미 실행 중입니다. 작업 표시줄에서 열어 주세요.", "Switchit!");
            return;
        }
        app.DispatcherUnhandledException += (_, e) =>
        {
            try { Directory.CreateDirectory(dataDirectory); File.AppendAllText(Path.Combine(dataDirectory, "error.log"), $"{DateTimeOffset.Now:O} {e.Exception}\n"); } catch { }
            MessageBox.Show("작업 중 오류가 발생했습니다. 저장된 기록은 다음 실행에서 복구됩니다.\n" + e.Exception.Message, "Switchit!");
            e.Handled = true;
            app.Shutdown(1);
        };
        try
        {
            if (!customDataDirectory)
            {
                // The old name appears only here for migration from pre-Switchit releases.
                var legacyDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WessClock");
                var legacyKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(legacyDirectory.ToUpperInvariant())))[..20];
                if (Mutex.TryOpenExisting("Local\\WessClock-" + legacyKey, out var legacyMutex))
                {
                    using (legacyMutex)
                    {
                        bool acquired;
                        try { acquired = legacyMutex.WaitOne(0); }
                        catch (AbandonedMutexException) { acquired = true; }
                        if (!acquired)
                        {
                            MessageBox.Show("이전 버전이 실행 중입니다. 기록을 저장하고 닫은 후 Switchit!을 실행해 주세요.", "Switchit!");
                            return;
                        }
                        legacyMutex.ReleaseMutex();
                    }
                }
                DataDirectoryMigration.CopyIfNeeded(legacyDirectory, dataDirectory);
            }
            app.Run(new MainWindow(dataDirectory, args.Contains("--resume-session")));
        }
        catch (Exception ex)
        {
            try { Directory.CreateDirectory(dataDirectory); File.AppendAllText(Path.Combine(dataDirectory, "error.log"), $"{DateTimeOffset.Now:O} {ex}\n"); } catch { }
            MessageBox.Show("Switchit!을 시작할 수 없습니다.\n" + ex.Message, "Switchit!");
        }
        finally { app._singleInstance.ReleaseMutex(); app._singleInstance.Dispose(); }
    }
}
