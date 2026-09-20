#nullable enable

using System;
using System.Threading;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media;
using SynToolkit.Models.GpuDrivers;

namespace SynToolkit.Services.GpuDrivers;

/// <summary>
/// Detects WinUI composition surface loss after a display-driver install and
/// triggers a guarded visual-tree rebuild of the GPU page. True DXGI device
/// recreation is owned by the framework; this coordinator recovers the page
/// when rasterized chrome/text stays blank afterward.
/// </summary>
public static class GpuUiRecoveryCoordinator
{
    private static readonly object Gate = new();
    private static Func<GpuPageUiRestoreState?>? _captureState;
    private static Action<GpuPageUiRestoreState>? _rebuild;
    private static DispatcherQueue? _dispatcherQueue;
    private static DateTime _armedUntilUtc = DateTime.MinValue;
    private static DateTime _lastRecoveryUtc = DateTime.MinValue;
    private static int _recoveriesThisArm;
    private static int _recoveryQueued;
    private static bool _eventsHooked;

    private static readonly TimeSpan ArmDuration = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan SettleDelay = TimeSpan.FromSeconds(1.5);
    private const int MaxRecoveriesPerArm = 3;

    public static void Arm(
        DispatcherQueue dispatcherQueue,
        Func<GpuPageUiRestoreState?> captureState,
        Action<GpuPageUiRestoreState> rebuild)
    {
        ArgumentNullException.ThrowIfNull(dispatcherQueue);
        ArgumentNullException.ThrowIfNull(captureState);
        ArgumentNullException.ThrowIfNull(rebuild);

        lock (Gate)
        {
            _dispatcherQueue = dispatcherQueue;
            _captureState = captureState;
            _rebuild = rebuild;
            _armedUntilUtc = DateTime.UtcNow.Add(ArmDuration);
            _recoveriesThisArm = 0;
            EnsureEventsHooked_NoLock();
        }

        App.logger.Info("[GPU] Armed UI recovery for display-driver install (expires in {0} minutes).", ArmDuration.TotalMinutes);
    }

    public static void Disarm()
    {
        lock (Gate)
        {
            _armedUntilUtc = DateTime.MinValue;
            _captureState = null;
            _rebuild = null;
        }
    }

    private static void EnsureEventsHooked_NoLock()
    {
        if (_eventsHooked)
            return;

        CompositionTarget.SurfaceContentsLost += OnSurfaceContentsLost;
        try
        {
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        }
        catch (Exception exception)
        {
            App.logger.Debug(exception, "[GPU] Could not subscribe to DisplaySettingsChanged.");
        }

        _eventsHooked = true;
    }

    private static void OnSurfaceContentsLost(object? sender, object e)
    {
        App.logger.Info("[GPU] CompositionTarget.SurfaceContentsLost observed.");
        ScheduleRecovery("SurfaceContentsLost");
    }

    private static void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        App.logger.Info("[GPU] DisplaySettingsChanged observed.");
        ScheduleRecovery("DisplaySettingsChanged");
    }

    private static void ScheduleRecovery(string reason)
    {
        DispatcherQueue? dispatcher;
        lock (Gate)
        {
            if (!IsArmed_NoLock())
                return;

            if (_recoveriesThisArm >= MaxRecoveriesPerArm)
                return;

            if (DateTime.UtcNow - _lastRecoveryUtc < Cooldown)
                return;

            dispatcher = _dispatcherQueue;
        }

        if (dispatcher is null)
            return;

        if (Interlocked.Exchange(ref _recoveryQueued, 1) == 1)
            return;

        if (!dispatcher.TryEnqueue(DispatcherQueuePriority.Normal, () =>
            {
                _ = RunRecoveryAfterSettleAsync(reason);
            }))
        {
            Interlocked.Exchange(ref _recoveryQueued, 0);
        }
    }

    private static async System.Threading.Tasks.Task RunRecoveryAfterSettleAsync(string reason)
    {
        try
        {
            await System.Threading.Tasks.Task.Delay(SettleDelay);
            TryRecover(reason);
        }
        finally
        {
            Interlocked.Exchange(ref _recoveryQueued, 0);
        }
    }

    private static void TryRecover(string reason)
    {
        Func<GpuPageUiRestoreState?>? capture;
        Action<GpuPageUiRestoreState>? rebuild;
        int recoveryNumber;

        lock (Gate)
        {
            if (!IsArmed_NoLock())
                return;

            if (_recoveriesThisArm >= MaxRecoveriesPerArm)
                return;

            if (DateTime.UtcNow - _lastRecoveryUtc < Cooldown)
                return;

            capture = _captureState;
            rebuild = _rebuild;
            if (capture is null || rebuild is null)
                return;

            _lastRecoveryUtc = DateTime.UtcNow;
            _recoveriesThisArm++;
            recoveryNumber = _recoveriesThisArm;
        }

        try
        {
            GpuPageUiRestoreState? state = capture();
            if (state is null)
            {
                App.logger.Warn("[GPU] UI recovery skipped ({0}): no page state available.", reason);
                return;
            }

            App.logger.Info(
                "[GPU] Rebuilding GPU page visual tree after {0} (recovery {1}/{2}).",
                reason,
                recoveryNumber,
                MaxRecoveriesPerArm);
            rebuild(state);
        }
        catch (Exception exception)
        {
            App.logger.Error(exception, "[GPU] UI recovery after {0} failed.", reason);
        }
    }

    private static bool IsArmed_NoLock() => DateTime.UtcNow < _armedUntilUtc;
}
