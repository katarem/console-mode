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
    private static int Main(string[] args)
    {
        // --wake-setup: proceso elevado que prepara (o deshace) el encendido a distancia. No abre ventanas.
        if (HasArg(args, WakeSetup.Arg))
        {
            return WakeSetup.RunFromCommandLine(args);
        }

        // --enable-startup: lo usa el instalador (como el usuario que instala) para activar el inicio con Windows.
        if (HasArg(args, StartupManager.EnableArg))
        {
            StartupManager.SetEnabled(true);
            return 0;
        }

        // --exit: cierra la instancia en marcha (lo usa el instalador antes de actualizar o desinstalar).
        var exitRequested = HasArg(args, ExitArg);

        using var mutex = new Mutex(initiallyOwned: true, MutexName, out var isFirstInstance);
        var afterUpdate = HasArg(args, Updater.AfterUpdateArg);
        if (!isFirstInstance && afterUpdate)
        {
            // Nos ha lanzado la versión anterior al actualizarse: esperamos a que termine de cerrarse.
            isFirstInstance = AcquireMutex(mutex, TimeSpan.FromSeconds(20));
        }

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
            return 0;
        }

        if (exitRequested)
        {
            return 0; // No había nada que cerrar.
        }

        ApplicationConfiguration.Initialize();
        Application.ThreadException += (_, e) => Log.Error("Excepción no controlada (UI)", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Error("Excepción no controlada", e.ExceptionObject as Exception);

        var startInBackground = HasArg(args, StartupManager.BackgroundArg);
        if (startInBackground)
        {
            // Al iniciar sesión compite con el resto de apps de inicio: con prioridad alta los primeros minutos está
            // lista antes para cambiar de pantalla o atender al móvil. Después vuelve a la normal.
            BoostPriorityForStartup();
        }
        using (var context = new TrayApplicationContext(showWindow: !startInBackground, afterUpdate))
        {
            Application.Run(context);
        }
        mutex.ReleaseMutex();
        return 0;
    }

    private static void BoostPriorityForStartup()
    {
        try
        {
            using var process = System.Diagnostics.Process.GetCurrentProcess();
            process.PriorityClass = System.Diagnostics.ProcessPriorityClass.AboveNormal;
            _ = Task.Delay(TimeSpan.FromMinutes(2)).ContinueWith(_ =>
            {
                using var current = System.Diagnostics.Process.GetCurrentProcess();
                current.PriorityClass = System.Diagnostics.ProcessPriorityClass.Normal;
            });
        }
        catch (Exception ex)
        {
            Log.Error("No se pudo subir la prioridad al arrancar", ex);
        }
    }

    private static bool HasArg(string[] args, string name) =>
        args.Any(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));

    private static bool AcquireMutex(Mutex mutex, TimeSpan timeout)
    {
        try
        {
            return mutex.WaitOne(timeout);
        }
        catch (AbandonedMutexException)
        {
            return true; // la otra instancia terminó sin liberarlo: ya es nuestro
        }
    }

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
