using System.Globalization;
using System.Windows;
using System.Windows.Interop;
using Roomy.Core;
using Roomy.Services;
using Roomy.Localization;

namespace Roomy.Views;

public partial class SettingsWindow : Window
{
    readonly DesktopController _desktop;
    StartupStatus? _startup;
    DateTimeOffset? _snoozedUntil;
    bool _pauseChanged;
    bool _saving;

    public SettingsWindow(DesktopController desktop)
    {
        InitializeComponent();
        Controls.FitToScreen.Apply(this);
        var v = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = v is null ? S.AppName : Loc.Format("AppVersion", $"{v.Major}.{v.Minor}.{v.Build}");
        _desktop = desktop;
        var p = desktop.Preferences;
        BackgroundCheck.IsChecked = p.RunInBackground;
        LowSpaceCheck.IsChecked = p.LowSpaceAlerts;
        WeeklyCheck.IsChecked = p.WeeklyChecks;
        RecycleCheck.IsChecked = p.RecycleDownloads;
        if (!desktop.IsDemo && !Hibernation.IsOn) HibernationCard.Visibility = Visibility.Visible;
        ThresholdBox.Text = p.LowSpaceGB.ToString(CultureInfo.InvariantCulture);
        IntervalBox.SelectedValue = p.CheckEveryMinutes.ToString(CultureInfo.InvariantCulture);
        _snoozedUntil = p.SnoozedUntil;
        UpdatePause();
        Options.IsEnabled = desktop.CanConfigure;
        SaveButton.IsEnabled = false;
        StatusText.Text = desktop.Status ?? "";
        SourceInitialized += (_, _) =>
        {
            int dark = 1;
            try { Native.DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, 20, ref dark, sizeof(int)); } catch { }
        };
        Loaded += async (_, _) => await ReadStartupAsync();
        Activated += async (_, _) => { if (!_saving && IsLoaded) await ReadStartupAsync(); };
        Closing += (_, e) => { if (_saving) e.Cancel = true; };
    }

    async Task ReadStartupAsync()
    {
        try
        {
            _startup = _desktop.IsDemo ? new(false, true) : await StartupRegistration.ReadAsync();
            StartupCheck.IsChecked = _startup.Enabled;
            StartupCheck.IsEnabled = !_startup.Blocked && !_desktop.IsDemo;
            StartupMessage.Text = _startup.Message ?? "";
            StartupMessage.Visibility = string.IsNullOrEmpty(_startup.Message) ? Visibility.Collapsed : Visibility.Visible;
            SaveButton.IsEnabled = _desktop.CanConfigure;
        }
        catch (Exception ex)
        {
            StartupCheck.IsEnabled = false;
            SaveButton.IsEnabled = false;
            StatusText.Text = S.CantReadStartup + ex.Message;
        }
    }

    void Background_Click(object sender, RoutedEventArgs e)
    {
        if (BackgroundCheck.IsChecked == true) return;
        if (_startup is { Enabled: true, Blocked: true })
        {
            BackgroundCheck.IsChecked = true;
            StatusText.Text = S.WindowsManagesStartup;
            return;
        }
        StartupCheck.IsChecked = false;
        LowSpaceCheck.IsChecked = false;
        WeeklyCheck.IsChecked = false;
    }

    void EnableBackground_Click(object sender, RoutedEventArgs e)
    {
        if (StartupCheck.IsChecked == true || LowSpaceCheck.IsChecked == true || WeeklyCheck.IsChecked == true)
            BackgroundCheck.IsChecked = true;
    }

    void Pause_Click(object sender, RoutedEventArgs e)
    {
        _pauseChanged = true;
        _snoozedUntil = DateTimeOffset.UtcNow.AddDays(1);
        UpdatePause();
    }

    void Resume_Click(object sender, RoutedEventArgs e)
    {
        _pauseChanged = true;
        _snoozedUntil = null;
        UpdatePause();
    }

    void UpdatePause() => PauseStatus.Text = _snoozedUntil > DateTimeOffset.UtcNow
        ? Loc.Format("RemindersPausedUntil", _snoozedUntil.Value.LocalDateTime.ToString("g"))
        : S.RemindersFollowWindows;

    async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_startup is null || _saving) return;
        if (!int.TryParse(ThresholdBox.Text, out var threshold) || threshold is < 1 or > 500)
        {
            StatusText.Text = S.ThresholdRange;
            ThresholdBox.Focus();
            return;
        }
        _saving = true;
        SaveButton.IsEnabled = false;
        Options.IsEnabled = false;
        var previousStartup = _startup.Enabled;
        var startupChanged = false;
        try
        {
            var start = StartupCheck.IsChecked == true;
            if (!_desktop.IsDemo && start != previousStartup)
            {
                var result = await StartupRegistration.SetEnabledAsync(start);
                if (result.Enabled != start) throw new InvalidOperationException(result.Message ?? S.WindowsDeniedStartup);
                startupChanged = true;
            }
            var current = _desktop.Preferences;
            _desktop.Apply(current with
            {
                RunInBackground = BackgroundCheck.IsChecked == true || start,
                LowSpaceAlerts = LowSpaceCheck.IsChecked == true,
                WeeklyChecks = WeeklyCheck.IsChecked == true,
                RecycleDownloads = RecycleCheck.IsChecked == true,
                LowSpaceGB = threshold,
                CheckEveryMinutes = IntervalBox.SelectedValue as string == "60" ? 60 : 30,
                SnoozedUntil = _pauseChanged ? _snoozedUntil : current.SnoozedUntil,
                LastWeeklyCheck = WeeklyCheck.IsChecked == true && !current.WeeklyChecks ? DateTimeOffset.UtcNow : current.LastWeeklyCheck,
            });
            _saving = false;
            Close();
        }
        catch (Exception ex)
        {
            StatusText.Text = S.CantSaveSettings + ex.Message;
            if (startupChanged)
            {
                try { await StartupRegistration.SetEnabledAsync(previousStartup); }
                catch { StatusText.Text += S.StartupCheckHint; }
            }
        }
        finally
        {
            _saving = false;
            SaveButton.IsEnabled = _desktop.CanConfigure;
            Options.IsEnabled = _desktop.CanConfigure;
        }
    }

    async void TurnOnHibernation_Click(object sender, RoutedEventArgs e)
    {
        HibernationButton.IsEnabled = false;
        var ok = await Hibernation.SetAsync(true) && Hibernation.IsOn;
        StatusText.Text = ok ? S.HibernationOnAgain : S.HibernationUnchanged;
        if (ok) HibernationCard.Visibility = Visibility.Collapsed;
        else HibernationButton.IsEnabled = true;
    }

    void StartupSettings_Click(object sender, RoutedEventArgs e) => Shell.Open("ms-settings:startupapps");
    void NotificationSettings_Click(object sender, RoutedEventArgs e) => Shell.Open("ms-settings:notifications");
    void Cancel_Click(object sender, RoutedEventArgs e) => Close();
}
