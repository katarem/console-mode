namespace ConsoleMode.Core;

/// <summary>Log mínimo en %APPDATA%\ConsoleMode\console-mode.log (se rota al superar 1 MB).</summary>
public static class Log
{
    private static readonly object Sync = new();
    private static string FilePath => Path.Combine(AppSettings.Directory, "console-mode.log");

    public static void Info(string message) => Write("INFO", message);

    public static void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex == null ? message : $"{message}: {ex}");

    private static void Write(string level, string message)
    {
        try
        {
            lock (Sync)
            {
                Directory.CreateDirectory(AppSettings.Directory);
                var info = new FileInfo(FilePath);
                if (info.Exists && info.Length > 1_000_000)
                {
                    File.Move(FilePath, FilePath + ".old", overwrite: true);
                }
                File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // El log nunca debe tumbar la app.
        }
    }
}
