using Roomy.Localization;
using System.ComponentModel;
using System.Windows;
using Roomy.Core;
using Roomy.ViewModels;
using Roomy.Views;
using Forms = System.Windows.Forms;

namespace Roomy.Services;

public sealed class DesktopController : IDisposable
{
    readonly MainWindow _window;
    readonly MainVM _vm;
    readonly PreferencesStore _store = new(PreferencesStore.DefaultPath);
    readonly NotificationService _notifications = new();
    readonly BackgroundMonitor _monitor;
    readonly Action _quit;
    Forms.NotifyIcon? _tray;
    SettingsWindow? _settingsWindow;
    bool _quitting;
    bool _disposed;
    public Preferences Preferences { get; private set; }
    public bool IsDemo => _vm.IsDemo;
    public bool CanConfigure => IsDemo || !Admin.IsElevated;
    public string? Status => IsDemo ? S.DemoSettingsNote :
        Admin.IsElevated ? S.OpenNormallyToChange : _store.LoadError ?? _notifications.Error;

    public DesktopController(MainWindow window, MainVM vm, Action quit)
    {
        _window = window;
        _vm = vm;
        _quit = quit;
        Preferences = vm.IsDemo || Admin.IsElevated ? new() : _store.Load();
        _monitor = new(() => Preferences,
            () => _disposed || _vm.State is AppState.Scanning or AppState.Cleaning || _vm.SpaceMap.IsScanning,
            (title, body, tag) => !IsDemo && _notifications.Show(title, body, tag), Save);
        _notifications.Activated += command => _window.Dispatcher.BeginInvoke(() => Handle(command));
        _window.Closing += OnClosing;
        _vm.PropertyChanged += OnActivity;
        _vm.SpaceMap.PropertyChanged += OnActivity;
        _vm.SettingsRequested += (_, _) => OpenSettings();
        Refresh();
    }

    public void RegisterNotificationActivation() => _notifications.Initialize();

    public void Save(Preferences preferences)
    {
        if (_disposed) return;
        if (!IsDemo) _store.Save(preferences);
        Preferences = preferences.Normalize();
    }

    public void Apply(Preferences preferences)
    {
        Save(preferences);
        Refresh();
    }

    void Refresh()
    {
        if (Preferences.RunInBackground)
        {
            EnsureTray();
            if (!IsDemo && (Preferences.LowSpaceAlerts || Preferences.WeeklyChecks)) _notifications.Initialize();
        }
        else
        {
            DisposeTray();
        }
        if (!Preferences.RunInBackground || (!Preferences.LowSpaceAlerts && !Preferences.WeeklyChecks)) _notifications.Clear();
        if (!IsDemo) _monitor.Refresh();
    }

    void EnsureTray()
    {
        if (_tray != null) return;
        using var stream = Application.GetResourceStream(new Uri("pack://application:,,,/Roomy;component/Assets/roomy.ico"))!.Stream;
        using var icon = new System.Drawing.Icon(stream);
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(S.TrayOpen, null, (_, _) => Open());
        menu.Items.Add(S.TrayScanNow, null, (_, _) => Handle("scan"));
        menu.Items.Add(new Forms.ToolStripSeparator());
        var pause = menu.Items.Add(S.TrayPauseReminders, null, (_, _) => Handle("snooze"));
        menu.Items.Add(S.TrayResumeReminders, null, (_, _) => Handle("resume"));
        menu.Items.Add(S.TraySettings, null, (_, _) => OpenSettings());
        menu.Items.Add(new Forms.ToolStripSeparator());
        var quit = menu.Items.Add(S.TrayQuit, null, (_, _) => Quit());
        menu.Opening += (_, _) =>
        {
            pause.Enabled = Preferences.LowSpaceAlerts || Preferences.WeeklyChecks;
            menu.Items[4].Enabled = Preferences.SnoozedUntil > DateTimeOffset.UtcNow;
            quit.Enabled = _vm.State != AppState.Cleaning;
        };
        _tray = new Forms.NotifyIcon
        {
            Text = "Roomy", Icon = (System.Drawing.Icon)icon.Clone(), ContextMenuStrip = menu, Visible = true,
        };
        _tray.DoubleClick += (_, _) => Open();
    }

    public void Open()
    {
        if (_disposed) return;
        _window.Show();
        if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    public void Handle(string command)
    {
        if (_disposed) return;
        try
        {
            switch (command)
            {
                case "background": return;
                case "snooze":
                    Save(Preferences with { SnoozedUntil = DateTimeOffset.UtcNow.AddDays(1) });
                    _monitor.CancelScan();
                    _notifications.Clear();
                    return;
                case "resume": Save(Preferences with { SnoozedUntil = null }); return;
                case "disable":
                    Apply(Preferences with { LowSpaceAlerts = false, WeeklyChecks = false });
                    return;
                case "quit": Quit(); return;
                case "scan":
                    _monitor.CancelScan();
                    Open();
                    if (_vm.State is not (AppState.Scanning or AppState.Cleaning) && !_vm.ConfirmVisible)
                        _vm.ScanCommand.Execute(null);
                    return;
                default: Open(); return;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Open();
            _vm.ShowToast(S.CantSaveReminders + ex.Message);
        }
    }

    public void OpenSettings()
    {
        Open();
        if (_settingsWindow != null) { _settingsWindow.Activate(); return; }
        _settingsWindow = new SettingsWindow(this) { Owner = _window };
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
    }

    void OnActivity(object? sender, PropertyChangedEventArgs e)
    {
        if (_vm.State is AppState.Scanning or AppState.Cleaning || _vm.SpaceMap.IsScanning) _monitor.CancelScan();
    }

    void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_quitting) return;
        if (Preferences.RunInBackground)
        {
            e.Cancel = true;
            _settingsWindow?.Close();
            _window.Hide();
        }
        else if (_vm.State == AppState.Cleaning)
        {
            e.Cancel = true;
            _vm.ShowToast(S.FinishBeforeClosing);
        }
        else
        {
            e.Cancel = true;
            _quitting = true;
            _window.Dispatcher.BeginInvoke(_quit);
        }
    }

    public void Quit()
    {
        if (_vm.State == AppState.Cleaning)
        {
            Open();
            _vm.ShowToast(S.FinishBeforeQuitting);
            return;
        }
        _quitting = true;
        _quit();
    }

    public void Dispose()
    {
        _disposed = true;
        _quitting = true;
        _monitor.Dispose();
        _notifications.Dispose();
        DisposeTray();
        _window.Closing -= OnClosing;
        _vm.PropertyChanged -= OnActivity;
        _vm.SpaceMap.PropertyChanged -= OnActivity;
    }

    void DisposeTray()
    {
        if (_tray is null) return;
        _tray.Visible = false;
        _tray.Icon?.Dispose();
        _tray.ContextMenuStrip?.Dispose();
        _tray.Dispose();
        _tray = null;
    }
}
