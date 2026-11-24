using System.Runtime.InteropServices;
using System.Text;

namespace Roomy.Core;

/// <summary>
/// Writes errors to %LOCALAPPDATA%\Roomy\crash.log so a failed start can be diagnosed.
/// The file stays on this PC; nothing is sent anywhere. Writing never throws.
/// </summary>
public static class CrashLog
{
    const long MaxBytes = 256 * 1024;

    public static string FilePath { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Roomy", "crash.log");

    /// <summary>Logs anything that would otherwise end the process without a trace.</summary>
    public static void Install()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            Write(e.ExceptionObject as Exception, e.IsTerminating ? "fatal" : "unhandled");
            if (e.IsTerminating) ShowNative(e.ExceptionObject as Exception);
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Write(e.Exception, "background task");
            e.SetObserved();
        };
    }

    public static void Write(Exception? ex, string where)
    {
        try
        {
            var dir = Path.GetDirectoryName(FilePath)!;
            Directory.CreateDirectory(dir);
            if (File.Exists(FilePath) && new FileInfo(FilePath).Length > MaxBytes)
            {
                // Keep the newest half so the file never grows without limit.
                var old = File.ReadAllText(FilePath);
                File.WriteAllText(FilePath, old[(old.Length / 2)..]);
            }
            var v = typeof(CrashLog).Assembly.GetName().Version;
            var sb = new StringBuilder()
                .AppendLine($"===== {DateTime.Now:yyyy-MM-dd HH:mm:ss zzz} · {where}")
                .AppendLine($"Roomy {v?.ToString(3) ?? "?"} · {RuntimeInformation.OSDescription} · {RuntimeInformation.OSArchitecture}")
                .AppendLine($".NET {Environment.Version} · language {System.Globalization.CultureInfo.CurrentUICulture.Name} · admin {SafeIsElevated()}")
                .AppendLine($"Args: {string.Join(' ', Environment.GetCommandLineArgs().Skip(1))}")
                .AppendLine(ex?.ToString() ?? "(no exception details)")
                .AppendLine();
            File.AppendAllText(FilePath, sb.ToString());
        }
        catch { /* never make a crash worse */ }
    }

    static string SafeIsElevated()
    {
        try { return Admin.IsElevated ? "yes" : "no"; }
        catch { return "?"; }
    }

    /// <summary>A short, user-facing description of what failed, with where the details are.</summary>
    public static string Describe(Exception? ex, string intro)
    {
        var e = ex;
        // Wrapper exceptions hide the real cause; show the one underneath.
        while (e?.InnerException != null && (e is TypeInitializationException or System.Reflection.TargetInvocationException ||
               e.GetType().FullName == "System.Windows.Markup.XamlParseException"))
            e = e.InnerException;
        var what = e is null ? "" : $"{e.GetType().Name}: {e.Message}";
        return $"{intro}\n\n{what}\n\nDetails were saved to:\n{FilePath}\n\n" +
               "Please open an issue at github.com/krezio/roomy/issues and attach that file. It stays on your PC unless you share it.";
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int MessageBoxW(IntPtr hwnd, string text, string caption, uint type);

    /// <summary>A plain Windows message box: works even when WPF itself is what failed.</summary>
    public static void ShowNative(Exception? ex, string intro = "Roomy ran into a problem and has to close.")
    {
        try { MessageBoxW(IntPtr.Zero, Describe(ex, intro), "Roomy", 0x10 /* MB_ICONERROR */); }
        catch { }
    }
}
