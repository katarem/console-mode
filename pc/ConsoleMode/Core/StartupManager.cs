using System.Diagnostics;
using System.Security;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32;

namespace ConsoleMode.Core;

/// <summary>
/// Arranque con Windows. Se usa una tarea programada «al iniciar sesión» y no HKCU\...\Run: Windows abre las entradas
/// de Run una a una y con retraso, detrás del resto de apps de inicio, y Console Mode tardaba en estar listo para
/// cambiar de pantalla o atender al móvil. La tarea arranca nada más iniciar sesión y con prioridad normal (las tareas
/// van por defecto con prioridad baja). No necesita permisos de administrador. Si no se puede crear, se usa Run.
/// </summary>
public static class StartupManager
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "ConsoleMode";
    public const string BackgroundArg = "--background";
    /// <summary>Activa el inicio con Windows y termina (lo usa el instalador).</summary>
    public const string EnableArg = "--enable-startup";

    private static string ExePath => Environment.ProcessPath ?? "";

    private static string Command => $"\"{ExePath}\" {BackgroundArg}";

    /// <summary>Una tarea por usuario: «Console Mode (usuario)». El desinstalador borra todas las «Console Mode (*».</summary>
    private static string TaskName => $"Console Mode ({Environment.UserName})";

    public static bool IsEnabled => TaskExists() || RunValueExists();

    public static void SetEnabled(bool enabled)
    {
        if (enabled)
        {
            if (TryCreateTask())
            {
                DeleteRunValue();
            }
            else
            {
                using var key = Registry.CurrentUser.CreateSubKey(RunKey);
                key.SetValue(ValueName, Command);
            }
        }
        else
        {
            DeleteTask();
            DeleteRunValue();
        }
    }

    /// <summary>
    /// Al arrancar: si el exe se ha movido, actualiza la ruta, y pasa a la tarea programada a quien aún tenga la entrada
    /// de Run de versiones anteriores.
    /// </summary>
    public static void RefreshPathIfEnabled()
    {
        try
        {
            if (IsEnabled)
            {
                SetEnabled(true);
            }
        }
        catch (Exception ex)
        {
            Log.Error("No se pudo actualizar el inicio con Windows", ex);
        }
    }

    // ---------- Tarea programada ----------

    private static bool TryCreateTask()
    {
        var file = Path.Combine(Path.GetTempPath(), $"ConsoleMode-task-{Guid.NewGuid():N}.xml");
        try
        {
            File.WriteAllText(file, TaskXml(), Encoding.Unicode);
            var (code, output) = RunSchtasks($"/create /tn \"{TaskName}\" /xml \"{file}\" /f");
            if (code != 0)
            {
                Log.Error($"No se pudo crear la tarea de inicio ({code}): {output.Trim()}. Se usa HKCU\\...\\Run.");
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("No se pudo crear la tarea de inicio; se usa HKCU\\...\\Run", ex);
            return false;
        }
        finally
        {
            try
            {
                File.Delete(file);
            }
            catch
            {
                // Archivo temporal.
            }
        }
    }

    private static bool TaskExists()
    {
        try
        {
            return RunSchtasks($"/query /tn \"{TaskName}\"").Code == 0;
        }
        catch
        {
            return false;
        }
    }

    private static void DeleteTask()
    {
        if (TaskExists())
        {
            RunSchtasks($"/delete /tn \"{TaskName}\" /f");
        }
    }

    /// <summary>
    /// Al iniciar sesión este usuario, sin retraso, con prioridad normal (4), sin límite de tiempo y también con batería.
    /// </summary>
    private static string TaskXml()
    {
        var user = SecurityElement.Escape(WindowsIdentity.GetCurrent().Name);
        return $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo>
                <Description>Abre Console Mode en segundo plano al iniciar sesión.</Description>
              </RegistrationInfo>
              <Triggers>
                <LogonTrigger>
                  <Enabled>true</Enabled>
                  <UserId>{user}</UserId>
                </LogonTrigger>
              </Triggers>
              <Principals>
                <Principal id="Author">
                  <UserId>{user}</UserId>
                  <LogonType>InteractiveToken</LogonType>
                  <RunLevel>LeastPrivilege</RunLevel>
                </Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <StartWhenAvailable>false</StartWhenAvailable>
                <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
                <IdleSettings>
                  <StopOnIdleEnd>false</StopOnIdleEnd>
                  <RestartOnIdle>false</RestartOnIdle>
                </IdleSettings>
                <AllowStartOnDemand>true</AllowStartOnDemand>
                <Enabled>true</Enabled>
                <Hidden>false</Hidden>
                <RunOnlyIfIdle>false</RunOnlyIfIdle>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <Priority>4</Priority>
              </Settings>
              <Actions Context="Author">
                <Exec>
                  <Command>{SecurityElement.Escape(ExePath)}</Command>
                  <Arguments>{BackgroundArg}</Arguments>
                </Exec>
              </Actions>
            </Task>
            """;
    }

    private static (int Code, string Output) RunSchtasks(string args)
    {
        using var process = Process.Start(new ProcessStartInfo("schtasks.exe", args)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        }) ?? throw new InvalidOperationException("No se pudo ejecutar schtasks.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(15_000))
        {
            process.Kill();
            return (-1, "schtasks no ha terminado a tiempo");
        }
        return (process.ExitCode, stderr.Result + stdout.Result);
    }

    // ---------- HKCU\...\Run (versiones anteriores o si no se puede crear la tarea) ----------

    private static bool RunValueExists()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) is string value && !string.IsNullOrWhiteSpace(value);
    }

    private static void DeleteRunValue()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        key?.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
