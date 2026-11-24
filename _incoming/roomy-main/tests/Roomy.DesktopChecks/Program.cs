using System.Windows;
using System.Windows.Threading;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Toolkit.Uwp.Notifications;
using Roomy;
using Roomy.Core;
using Roomy.Services;
using Roomy.ViewModels;
using Roomy.Views;

namespace Roomy.DesktopChecks;

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("/Roomy;component/Themes/Styles.xaml", UriKind.Relative),
        });
        var exitCode = 0;
        app.Startup += async (_, _) =>
        {
            try
            {
                var snapshotIndex = Array.IndexOf(args, "--snapshot");
                var snapshot = snapshotIndex >= 0 && snapshotIndex + 1 < args.Length ? args[snapshotIndex + 1] : null;
                await CheckLifecycle(snapshot);
                if (args.Contains("--notifications")) CheckNotifications();
                Console.WriteLine("Desktop checks passed.");
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); exitCode = 1; }
            finally { app.Shutdown(); }
        };
        app.Run();
        return exitCode;
    }

    static async Task CheckLifecycle(string? snapshot)
    {
        var vm = new MainVM(demo: true);
        var window = new MainWindow(vm)
        {
            Left = -20000, Top = -20000, ShowActivated = false, ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
        };
        var quitRequests = 0;
        using (var desktop = new DesktopController(window, vm, () => quitRequests++))
        {
            window.Show();
            await CheckSettings(desktop, window, snapshot);
            desktop.Apply(new Preferences { RunInBackground = true, LowSpaceAlerts = true, WeeklyChecks = true });
            window.Close();
            Require(!window.IsVisible && quitRequests == 0, "Closing in background mode should hide the window.");
            desktop.Open();
            Require(window.IsVisible, "Opening from the tray should restore the window.");
            desktop.Handle("snooze");
            Require(desktop.Preferences.SnoozedUntil > DateTimeOffset.UtcNow.AddHours(23), "Snooze should last a day.");
            desktop.Handle("disable");
            Require(!desktop.Preferences.LowSpaceAlerts && !desktop.Preferences.WeeklyChecks, "Turn off should disable both reminders.");
            desktop.Handle("resume");
            Require(desktop.Preferences.SnoozedUntil is null, "Resume should clear the pause.");
            desktop.Apply(new Preferences());
            window.Close();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Require(quitRequests == 1, "Closing with background mode off should exit.");
        }
        window.Close();
        Console.WriteLine("PASS: tray lifecycle, snooze, resume, reminder opt-out and normal close.");
    }

    static async Task CheckSettings(DesktopController desktop, Window owner, string? snapshot)
    {
        var settings = new SettingsWindow(desktop)
        {
            Owner = owner, Left = -20000, Top = -20000, ShowActivated = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
        };
        settings.Show();
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        T Control<T>(string name) where T : FrameworkElement => (T)settings.FindName(name);
        var save = Control<Button>("SaveButton");
        Require(save.IsEnabled, "Settings should be ready to save after loading.");
        Require(!Control<CheckBox>("StartupCheck").IsEnabled, "Demo mode must not register startup.");
        if (snapshot != null)
        {
            var bitmap = new RenderTargetBitmap((int)settings.ActualWidth * 2, (int)settings.ActualHeight * 2, 192, 192, PixelFormats.Pbgra32);
            bitmap.Render(settings);
            var png = new PngBitmapEncoder();
            png.Frames.Add(BitmapFrame.Create(bitmap));
            using var output = System.IO.File.Create(snapshot);
            png.Save(output);
        }
        var lowSpace = Control<CheckBox>("LowSpaceCheck");
        lowSpace.IsChecked = true;
        lowSpace.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Require(Control<CheckBox>("BackgroundCheck").IsChecked == true, "Enabling reminders should also enable tray mode.");
        Control<TextBox>("ThresholdBox").Text = "0";
        save.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Require(settings.IsVisible && !desktop.Preferences.LowSpaceAlerts, "Invalid thresholds must not save preferences.");
        Control<TextBox>("ThresholdBox").Text = "20";
        save.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Require(!settings.IsVisible && desktop.Preferences.LowSpaceAlerts && desktop.Preferences.LowSpaceGB == 20,
            "Valid settings should save and close the window.");
        Console.WriteLine("PASS: settings initialization, dependencies, validation and saving.");
    }

    static void CheckNotifications()
    {
        // This console app gets its own notification identity. Never unregister Roomy's identity.
        using var notifications = new NotificationService();
        try
        {
            Require(notifications.Show("Roomy notification test", "This test notification will clear itself.", "test"),
                notifications.Error ?? "Windows has disabled notifications for the test app.");
            Require(ToastNotificationManagerCompat.History.GetHistory().Any(n => n.Tag == "test"), "Notification should appear in Windows history.");
            notifications.Clear();
            Require(ToastNotificationManagerCompat.History.GetHistory().Count == 0, "Test notification should be cleared.");
            Console.WriteLine("PASS: native notification registration, delivery and removal.");
        }
        finally { ToastNotificationManagerCompat.Uninstall(); }
    }

    static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
