using System.Runtime.InteropServices;
using System.Windows.Threading;
using Roomy.Core;
using Forms = System.Windows.Forms;
using Roomy.Localization;

namespace Roomy.Services;

public sealed class BackgroundMonitor : IDisposable
{
    readonly Func<Preferences> _settings;
    readonly Func<bool> _foregroundBusy;
    readonly Func<string, string, string, bool> _notify;
    readonly Action<Preferences> _save;
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMinutes(1) };
    CancellationTokenSource? _scan;
    DateTimeOffset _nextSpaceCheck = DateTimeOffset.UtcNow.AddMinutes(2);
    DateTimeOffset _weeklyRetryAfter;
    bool _checking;

    public BackgroundMonitor(Func<Preferences> settings, Func<bool> foregroundBusy,
        Func<string, string, string, bool> notify, Action<Preferences> save)
    {
        _settings = settings;
        _foregroundBusy = foregroundBusy;
        _notify = notify;
        _save = save;
        _timer.Tick += Tick;
    }

    public void Refresh()
    {
        CancelScan();
        if (_settings().RunInBackground) _timer.Start();
        else _timer.Stop();
    }

    public void CancelScan() => _scan?.Cancel();

    async void Tick(object? sender, EventArgs e)
    {
        if (_checking) return;
        var now = DateTimeOffset.UtcNow;
        var p = _settings();
        if (!ReminderPolicy.CanNotify(p, now) || _foregroundBusy()) return;
        _checking = true;
        try
        {
            if (p.LowSpaceAlerts && now >= _nextSpaceCheck)
            {
                _nextSpaceCheck = now.AddMinutes(p.CheckEveryMinutes);
                var drive = new DriveInfo(Env.SystemRoot);
                if (drive.IsReady && ReminderPolicy.IsLowSpace(p, drive.AvailableFreeSpace))
                {
                    Notify(Loc.Format("LowSpaceTitle", drive.Name.TrimEnd('\\')),
                        Loc.Format("LowSpaceBody", Format.Bytes(drive.AvailableFreeSpace)), "low-space");
                }
            }

            p = _settings();
            if (now < _weeklyRetryAfter || !ReminderPolicy.WeeklyCheckDue(p, now) || !CanScanNow()) return;
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            _scan = cts;
            // Check idle state throughout the scan, not just when it starts.
            var idleTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            idleTimer.Tick += (_, _) => { if (_foregroundBusy() || !CanScanNow()) cts.Cancel(); };
            idleTimer.Start();
            try
            {
                var bytes = await Task.Run(() => ScanCaches(cts.Token), cts.Token);
                cts.Token.ThrowIfCancellationRequested();
                var current = _settings();
                if (!current.RunInBackground || !current.WeeklyChecks) return;
                _save(current with { LastWeeklyCheck = DateTimeOffset.UtcNow });
                if (bytes >= 1L << 30 && ReminderPolicy.CanNotify(_settings(), DateTimeOffset.UtcNow))
                    Notify(Loc.Format("WeeklyTitle", Format.Bytes(bytes)),
                        S.WeeklyBody, "weekly");
            }
            finally
            {
                idleTimer.Stop();
                _scan = null;
            }
        }
        catch (OperationCanceledException) { _weeklyRetryAfter = now.AddMinutes(30); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            // An unavailable drive or unwritable settings folder must not interrupt the desktop.
            _weeklyRetryAfter = now.AddMinutes(30);
            System.Diagnostics.Trace.WriteLine(S.BackgroundCheckFailed + ex.Message);
        }
        finally { _checking = false; }
    }

    void Notify(string title, string body, string tag)
    {
        var previous = _settings().LastNotification;
        // Persist the cooldown before delivery so restarting during delivery cannot repeat an alert.
        _save(_settings() with { LastNotification = DateTimeOffset.UtcNow });
        if (!_notify(title, body, tag)) _save(_settings() with { LastNotification = previous });
    }

    static long ScanCaches(CancellationToken token)
    {
        var context = new ScanContext { Token = token, IsAdmin = false };
        long size = 0;
        foreach (var step in Scanner.Steps.Where(s => s.Id is "temp" or "browser" or "apps" or "crash"))
        {
            token.ThrowIfCancellationRequested();
            size += step.Run(context)?.Size ?? 0;
        }
        return size;
    }

    static bool CanScanNow()
    {
        var info = new LastInputInfo { Size = (uint)Marshal.SizeOf<LastInputInfo>() };
        return GetLastInputInfo(ref info) && unchecked((uint)Environment.TickCount - info.Time) >= 5 * 60 * 1000 &&
            Forms.SystemInformation.PowerStatus.PowerLineStatus != Forms.PowerLineStatus.Offline;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct LastInputInfo { public uint Size; public uint Time; }
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GetLastInputInfo(ref LastInputInfo info);

    public void Dispose()
    {
        _timer.Stop();
        _timer.Tick -= Tick;
        CancelScan();
    }
}
