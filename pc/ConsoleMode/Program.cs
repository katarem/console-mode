using ConsoleMode.Core;
using ConsoleMode.UI;

namespace ConsoleMode;

internal static class Program
{
    private const string MutexName = @"Local\ConsoleMode.SingleInstance";
    public const string ShowEventName = @"Local\ConsoleMode.ShowWindow";
    public const string ExitEventName = @"Local\ConsoleMode.Exit";
    public const string ExitArg = "--exit";

    [STAThread]
    private static void Main(string[] args)
    {
        // --exit: cierra la instancia en marcha (lo usa el instalador antes de actualizar o desinstalar).
        var exitRequested = HasArg(args, ExitArg);

        using var mutex = new Mutex(initiallyOwned: true, MutexName, out var isFirstInstance);
        if (!isFirstInstance)
        {
            // Ya está en marcha (quizá sin icono en la bandeja): le pedimos que muestre su ventana o que se cierre.
            try
            {
                using var signal = EventWaitHandle.OpenExisting(exitRequested ? ExitEventName : ShowEventName);
                signal.Set();
            }
            catch
            {
                // La otra instancia aún está arrancando.
            }

            if (exitRequested)
            {
                WaitForOtherInstanceToExit(mutex);
            }
            return;
        }

        if (exitRequested)
        {
            return; // No había nada que cerrar.
        }

        ApplicationConfiguration.Initialize();
        Application.ThreadException += (_, e) => Log.Error("Excepción no controlada (UI)", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Error("Excepción no controlada", e.ExceptionObject as Exception);

        var startInBackground = HasArg(args, StartupManager.BackgroundArg);
        using (var context = new TrayApplicationContext(showWindow: !startInBackground))
        {
            Application.Run(context);
        }
        mutex.ReleaseMutex();
    }

    private static bool HasArg(string[] args, string name) =>
        args.Any(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));

    private static void WaitForOtherInstanceToExit(Mutex mutex)
    {
        try
        {
            if (mutex.WaitOne(TimeSpan.FromSeconds(10)))
            {
                mutex.ReleaseMutex();
            }
        }
        catch (AbandonedMutexException)
        {
            // La otra instancia terminó sin liberar el mutex: también vale.
        }
    }
}
