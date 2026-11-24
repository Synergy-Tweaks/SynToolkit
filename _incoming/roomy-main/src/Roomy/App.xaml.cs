using System.Windows;
using Roomy.ViewModels;
using Roomy.Services;
using Roomy.Core;
using Roomy.Localization;

namespace Roomy;

public partial class App : Application
{
    SingleInstance? _instance;
    DesktopController? _desktop;
    NotificationService? _activationRelay;

    public App() => CrashLog.Install();

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            await StartAsync(e.Args);
        }
        catch (Exception ex)
        {
            // Without this, a failed start only shows Windows' "unknown hard error".
            CrashLog.Write(ex, "startup");
            CrashLog.ShowNative(ex, "Roomy couldn't start.");
            Shutdown(1);
        }
    }

    async Task StartAsync(string[] args)
    {
#if DEBUG
        if (Environment.GetEnvironmentVariable("ROOMY_TEST_CRASH") == "startup") throw new InvalidOperationException("Test startup crash");
#endif
        if (args.Contains("--uninstall"))
        {
            try
            {
                using var instance = new SingleInstance(Admin.IsElevated, demo: false);
                if (!instance.IsPrimary) await instance.SendAsync("quit");
                await StartupRegistration.SetEnabledAsync(false);
                Microsoft.Toolkit.Uwp.Notifications.ToastNotificationManagerCompat.Uninstall();
            }
            catch { /* Uninstall must still work when Windows registration is already gone. */ }
            Shutdown();
            return;
        }
        string? Arg(string name)
        {
            var i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        DispatcherUnhandledException += (_, ex) =>
        {
            CrashLog.Write(ex.Exception, "ui");
            MessageBox.Show(ex.Exception.Message, S.SnagTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
            ex.Handled = true;
        };

        // Build-time helper: render the logo into a multi-size .ico (+ a big PNG).
        if (Arg("--export-icon") is { } iconPath)
        {
            IconExporter.Export(iconPath);
            Shutdown();
            return;
        }

        // Local-only tools can hook in here (not part of the public build).
        var handled = false;
        RunExtras(args, ref handled);
        if (handled) return;

        if (Arg("--export-store-art") is { } artDir)
        {
            IconExporter.ExportStoreArt(artDir);
            Shutdown();
            return;
        }

        // Diagnostic: run every scan step (read-only) and write what was found to a text file.
        if (Arg("--scan-report") is { } reportPath)
        {
            var ctx = new Core.ScanContext { IsAdmin = Core.Admin.IsElevated };
            var lines = new List<string> { $"Roomy scan report · admin={ctx.IsAdmin} · {DateTime.Now:u}" };
            foreach (var step in Core.Scanner.Steps)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    var cat = step.Run(ctx);
                    lines.Add($"{step.Title,-16} {(cat is null ? "-" : cat.Blocked ? "needs admin" : Core.Format.Bytes(cat.Size)),12}  ({sw.ElapsedMilliseconds} ms)");
                    if (cat != null)
                        foreach (var i in cat.Items.Take(8)) lines.Add($"    {i.Name,-40} {Core.Format.Bytes(i.Size),10}  {i.Detail}");
                }
                catch (Exception ex) { lines.Add($"{step.Title,-16} ERROR {ex.Message}"); }
            }
            File.WriteAllLines(reportPath, lines);
            Shutdown();
            return;
        }

        var shots = Arg("--shots");
        var record = Arg("--record");
        if (record != null) shots = null;
        var demo = shots != null || record != null || args.Contains("--demo");
        var automation = shots != null || record != null;
        var notificationLaunch = args.Contains("-ToastActivated");
        var background = args.Contains("--background");
        if (!automation)
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _instance = new SingleInstance(Admin.IsElevated, demo);
            if (!_instance.IsPrimary)
            {
                if (notificationLaunch)
                {
                    _activationRelay = new NotificationService();
                    _activationRelay.Activated += command => Dispatcher.BeginInvoke(async () =>
                    {
                        await _instance.SendAsync(command);
                        Shutdown();
                    });
                    if (!_activationRelay.Initialize()) { Shutdown(); return; }
                    // COM activation should arrive promptly. Do not leave a hidden relay alive forever.
                    await Task.Delay(TimeSpan.FromSeconds(10));
                    Shutdown();
                }
                else
                {
                    var sent = await _instance.SendAsync(background ? "background" : args.Contains("--scan") ? "scan" : "open");
                    if (!sent && !background) MessageBox.Show(S.AlreadyOpening, "Roomy");
                    Shutdown();
                }
                return;
            }
        }
        var vm = new MainVM(demo);
        var window = new MainWindow(vm);

        if (shots != null || record != null)
        {
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = -20000;
            window.Top = 0;
            window.Width = 1240;
            window.Height = record != null ? 780 : 800;
            window.ShowActivated = false;
            window.ShowInTaskbar = false;
        }

        MainWindow = window;
        if (!automation)
        {
            _desktop = new DesktopController(window, vm, Shutdown);
            _instance!.Listen(command => Dispatcher.BeginInvoke(() => _desktop.Handle(command)));
            if (background && !_desktop.Preferences.RunInBackground) { Shutdown(); return; }
            if (notificationLaunch) _desktop.RegisterNotificationActivation();
        }
        if (!background && !notificationLaunch) window.Show();

        if (notificationLaunch && !_desktop!.Preferences.RunInBackground)
        {
            // A snooze/disable action can cold-start Roomy without needing a window.
            _ = ExitUnusedActivationAsync();
        }

        if (shots != null) _ = window.RunScreenshotsAsync(shots);
        else if (record != null) _ = window.RecordDemoAsync(record);
        else if (args.Contains("--scan")) vm.ScanCommand.Execute(null);
    }

    async Task ExitUnusedActivationAsync()
    {
        await Task.Delay(TimeSpan.FromSeconds(10));
        if (MainWindow?.IsVisible != true && _desktop?.Preferences.RunInBackground != true) Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _desktop?.Dispose();
        _activationRelay?.Dispose();
        _instance?.Dispose();
        base.OnExit(e);
    }

    partial void RunExtras(string[] args, ref bool handled);
}
