using ConsoleMode.Core;
using ConsoleMode.UI;

namespace ConsoleMode;

internal static class Program
{
    private const string MutexName = @"Local\ConsoleMode.SingleInstance";
    public const string ShowEventName = @"Local\ConsoleMode.ShowWindow";

    [STAThread]
    private static void Main(string[] args)
    {
        using var mutex = new Mutex(initiallyOwned: true, MutexName, out var isFirstInstance);
        if (!isFirstInstance)
        {
            // Ya está en marcha (quizá sin icono en la bandeja): le pedimos que muestre su ventana.
            try
            {
                using var show = EventWaitHandle.OpenExisting(ShowEventName);
                show.Set();
            }
            catch
            {
                // La otra instancia aún está arrancando.
            }
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.ThreadException += (_, e) => Log.Error("Excepción no controlada (UI)", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Error("Excepción no controlada", e.ExceptionObject as Exception);

        var startInBackground = args.Any(a => string.Equals(a, StartupManager.BackgroundArg, StringComparison.OrdinalIgnoreCase));
        using var context = new TrayApplicationContext(showWindow: !startInBackground);
        Application.Run(context);
    }
}
