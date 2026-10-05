#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SynToolkit.Utils;
using SynToolkit.ViewModels;

namespace SynToolkit.Services;

internal enum NeedsAttentionAction
{
    OpenDiskCleanup,
    CreateRestorePoint,
    SyncWindowsClock,
    OpenInstaller,
    OpenGraphicsDriverPage,
    OpenDeviceManager,
    OpenSettings,
}

internal sealed record NeedsAttentionItem(
    string Title,
    string Description,
    string ActionText,
    NeedsAttentionAction Action,
    string? ActionTarget = null,
    string? IgnoreKey = null,
    string CheckId = "")
{
    public bool CanIgnore =>
        !string.IsNullOrWhiteSpace(IgnoreKey) || !string.IsNullOrWhiteSpace(CheckId);
}

internal sealed record NeedsAttentionSnapshot(
    IReadOnlyList<NeedsAttentionItem> Items,
    DateTimeOffset CheckedAt,
    bool IncludesOnlineChecks,
    int IgnoredCheckCount = 0);

internal sealed record NeedsAttentionIgnoreRecord(
    DateTimeOffset IgnoredAtUtc,
    DateTimeOffset ExpiresAtUtc);

/// <summary>
/// A deliberately small, cached health summary for the home screen. Startup uses only local
/// checks plus SynToolkit's one release request; the slower NTP and app-catalog checks happen
/// only when the dashboard is opened or manually refreshed.
/// </summary>
internal sealed class NeedsAttentionService
{
    private const long LowDiskSpaceBytes = 10L * 1024L * 1024L * 1024L;
    private const int LowDiskSpacePercent = 10;
    private const int ClockDifferenceThresholdMilliseconds = 3_000;
    private static readonly TimeSpan LocalCacheDuration = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan OnlineCacheDuration = TimeSpan.FromMinutes(15);
    private const string CheckIgnoreKeyPrefix = "check:";
    private static readonly string IgnoreStatePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SynToolkit",
        "NeedsAttentionIgnores.json");

    private readonly WingetInstallerService _wingetInstallerService;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly SemaphoreSlim _ignoreStateGate = new(1, 1);
    private readonly object _ignoredItemsLock = new();
    private readonly Dictionary<string, NeedsAttentionIgnoreRecord> _ignoredItems = new(StringComparer.Ordinal);
    private NeedsAttentionSnapshot? _localSnapshot;
    private NeedsAttentionSnapshot? _onlineSnapshot;
    private bool _ignoreStateLoaded;

    public NeedsAttentionService(WingetInstallerService wingetInstallerService)
    {
        _wingetInstallerService = wingetInstallerService;
    }

    public Task<NeedsAttentionSnapshot> GetStartupSnapshotAsync(CancellationToken cancellationToken = default) =>
        GetSnapshotAsync(includeOnlineChecks: false, includeToolkitUpdate: true, forceRefresh: false, cancellationToken);

    public Task<NeedsAttentionSnapshot> GetDashboardSnapshotAsync(
        bool forceRefresh = false,
        CancellationToken cancellationToken = default) =>
        GetSnapshotAsync(includeOnlineChecks: true, includeToolkitUpdate: true, forceRefresh, cancellationToken);

    private async Task<NeedsAttentionSnapshot> GetSnapshotAsync(
        bool includeOnlineChecks,
        bool includeToolkitUpdate,
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        await EnsureIgnoreStateLoadedAsync(cancellationToken);

        NeedsAttentionSnapshot? cached = includeOnlineChecks ? _onlineSnapshot : _localSnapshot;
        TimeSpan cacheDuration = includeOnlineChecks ? OnlineCacheDuration : LocalCacheDuration;
        if (!forceRefresh && cached is not null && DateTimeOffset.UtcNow - cached.CheckedAt < cacheDuration)
        {
            return WithIgnoredCheckCount(cached);
        }

        await _refreshGate.WaitAsync(cancellationToken);
        try
        {
            cached = includeOnlineChecks ? _onlineSnapshot : _localSnapshot;
            if (!forceRefresh && cached is not null && DateTimeOffset.UtcNow - cached.CheckedAt < cacheDuration)
            {
                return WithIgnoredCheckCount(cached);
            }

            List<NeedsAttentionItem> items = await Task.Run(CollectLocalItems, cancellationToken);

            if (includeToolkitUpdate && !IsCheckIgnored(NeedsAttentionCheckIds.SynToolkitVersion))
            {
                await AddToolkitUpdateItemAsync(items, forceRefresh, cancellationToken);
            }

            if (includeOnlineChecks)
            {
                if (!IsCheckIgnored(NeedsAttentionCheckIds.WindowsTime))
                {
                    await AddClockItemAsync(items, cancellationToken);
                }

                if (!IsCheckIgnored(NeedsAttentionCheckIds.Apps))
                {
                    await AddInstallerUpdateItemsAsync(items, cancellationToken);
                }
            }

            NeedsAttentionSnapshot snapshot = new(
                items,
                DateTimeOffset.UtcNow,
                includeOnlineChecks,
                GetIgnoredCheckCount());
            if (includeOnlineChecks)
            {
                _onlineSnapshot = snapshot;
                _localSnapshot = snapshot;
            }
            else
            {
                _localSnapshot = snapshot;
            }

            return snapshot;
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    public async Task IgnoreItemAsync(
        NeedsAttentionItem item,
        NeedsAttentionIgnoreDuration duration = NeedsAttentionIgnoreDuration.ThreeMonths,
        CancellationToken cancellationToken = default)
    {
        if (!item.CanIgnore || string.IsNullOrWhiteSpace(item.IgnoreKey))
        {
            return;
        }

        await EnsureIgnoreStateLoadedAsync(cancellationToken);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        DateTimeOffset expiresAt = NeedsAttentionIgnorePolicy.ResolveExpiry(duration, now);
        lock (_ignoredItemsLock)
        {
            _ignoredItems[item.IgnoreKey] = new NeedsAttentionIgnoreRecord(now, expiresAt);
        }

        await PersistIgnoreStateAsync(cancellationToken);
        RemoveIgnoredItemFromCaches(item.IgnoreKey);
        App.logger.Info("[NeedsAttention] Ignored warning {IgnoreKey} until {ExpiresAt}.", item.IgnoreKey, expiresAt);
    }

    public async Task IgnoreCheckAsync(string checkId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(checkId))
        {
            return;
        }

        await EnsureIgnoreStateLoadedAsync(cancellationToken);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        DateTimeOffset expiresAt = NeedsAttentionIgnorePolicy.ResolveExpiry(NeedsAttentionIgnoreDuration.Forever, now);
        lock (_ignoredItemsLock)
        {
            _ignoredItems[ToCheckIgnoreKey(checkId)] = new NeedsAttentionIgnoreRecord(now, expiresAt);
        }

        await PersistIgnoreStateAsync(cancellationToken);
        RemoveIgnoredCheckFromCaches(checkId);
        App.logger.Info("[NeedsAttention] Ignored check category {CheckId}.", checkId);
    }

    public async Task IgnoreAllChecksAsync(CancellationToken cancellationToken = default)
    {
        await EnsureIgnoreStateLoadedAsync(cancellationToken);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        DateTimeOffset expiresAt = NeedsAttentionIgnorePolicy.ResolveExpiry(NeedsAttentionIgnoreDuration.Forever, now);
        lock (_ignoredItemsLock)
        {
            foreach (string checkId in NeedsAttentionCheckIds.All)
            {
                _ignoredItems[ToCheckIgnoreKey(checkId)] = new NeedsAttentionIgnoreRecord(now, expiresAt);
            }
        }

        await PersistIgnoreStateAsync(cancellationToken);
        RemoveAllIgnoredChecksFromCaches();
        App.logger.Info("[NeedsAttention] Ignored all Needs Attention check categories.");
    }

    public async Task RestoreIgnoredChecksAsync(CancellationToken cancellationToken = default)
    {
        await EnsureIgnoreStateLoadedAsync(cancellationToken);
        lock (_ignoredItemsLock)
        {
            List<string> checkKeys = _ignoredItems.Keys
                .Where(key => key.StartsWith(CheckIgnoreKeyPrefix, StringComparison.Ordinal))
                .ToList();
            foreach (string key in checkKeys)
            {
                _ignoredItems.Remove(key);
            }
        }

        await PersistIgnoreStateAsync(cancellationToken);
        // Drop caches so Refresh / next open re-runs the restored checks.
        _localSnapshot = null;
        _onlineSnapshot = null;
        App.logger.Info("[NeedsAttention] Restored ignored Needs Attention check categories.");
    }

    public int GetIgnoredCheckCount()
    {
        lock (_ignoredItemsLock)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            int count = 0;
            List<string>? expiredKeys = null;
            foreach ((string key, NeedsAttentionIgnoreRecord record) in _ignoredItems)
            {
                if (!key.StartsWith(CheckIgnoreKeyPrefix, StringComparison.Ordinal))
                {
                    continue;
                }

                if (record.ExpiresAtUtc > now)
                {
                    count++;
                }
                else
                {
                    expiredKeys ??= new List<string>();
                    expiredKeys.Add(key);
                }
            }

            if (expiredKeys is not null)
            {
                foreach (string key in expiredKeys)
                {
                    _ignoredItems.Remove(key);
                }
            }

            return count;
        }
    }

    public bool HasIgnoredChecks => GetIgnoredCheckCount() > 0;

    private List<NeedsAttentionItem> CollectLocalItems()
    {
        List<NeedsAttentionItem> items = new();
        if (!IsCheckIgnored(NeedsAttentionCheckIds.LowDisk))
        {
            AddLowDiskSpaceItem(items);
        }

        if (!IsCheckIgnored(NeedsAttentionCheckIds.RestorePoint))
        {
            AddRestorePointItem(items);
        }

        if (!IsCheckIgnored(NeedsAttentionCheckIds.Drivers))
        {
            AddGraphicsDriverItems(items);
            AddRequiredPlatformDeviceItems(items);
        }

        return items;
    }

    private static void AddLowDiskSpaceItem(ICollection<NeedsAttentionItem> items)
    {
        foreach (string root in GetInternalFixedDriveRoots())
        {
            try
            {
                DriveInfo drive = new(root);
                if (!drive.IsReady || drive.DriveType != DriveType.Fixed || drive.TotalSize <= 0)
                {
                    continue;
                }

                int freePercent = (int)Math.Floor(drive.AvailableFreeSpace * 100d / drive.TotalSize);
                if (drive.AvailableFreeSpace > LowDiskSpaceBytes && freePercent > LowDiskSpacePercent)
                {
                    continue;
                }

                items.Add(new NeedsAttentionItem(
                    FormatText("NeedsAttention_LowDiskTitle", drive.Name.TrimEnd('\\')),
                    FormatText("NeedsAttention_LowDiskDescription", FormatGiB(drive.AvailableFreeSpace), drive.Name),
                    Text("NeedsAttention_OpenDiskCleanup"),
                    NeedsAttentionAction.OpenDiskCleanup,
                    CheckId: NeedsAttentionCheckIds.LowDisk));
            }
            catch (Exception exception)
            {
                App.logger.Debug(exception, "[NeedsAttention] Unable to read free space for {Drive}.", root);
            }
        }
    }

    private static void AddRestorePointItem(ICollection<NeedsAttentionItem> items)
    {
        try
        {
            using ManagementObjectSearcher searcher = new(@"root\default", "SELECT CreationTime FROM SystemRestore");
            using ManagementObjectCollection restorePoints = searcher.Get();
            if (restorePoints.Cast<ManagementObject>().Any())
            {
                return;
            }

            items.Add(new NeedsAttentionItem(
                Text("NeedsAttention_NoRestoreTitle"),
                Text("NeedsAttention_NoRestoreDescription"),
                Text("NeedsAttention_CreateRestore"),
                NeedsAttentionAction.CreateRestorePoint,
                CheckId: NeedsAttentionCheckIds.RestorePoint));
        }
        catch (Exception exception)
        {
            // The WMI class is unavailable when System Restore is disabled. That is not enough
            // evidence to show a warning, so failure remains silent apart from the debug log.
            App.logger.Debug(exception, "[NeedsAttention] Restore-point status is unavailable.");
        }
    }

    private static void AddGraphicsDriverItems(ICollection<NeedsAttentionItem> items)
    {
        try
        {
            using ManagementObjectSearcher searcher = new(
                "SELECT Name, PNPDeviceID, ConfigManagerErrorCode FROM Win32_VideoController");
            using ManagementObjectCollection controllers = searcher.Get();

            foreach (ManagementObject controller in controllers.Cast<ManagementObject>())
            {
                using (controller)
                {
                    string name = controller["Name"] as string ?? Text("NeedsAttention_UnknownGraphicsAdapter");
                    string pnpDeviceId = controller["PNPDeviceID"] as string ?? string.Empty;
                    GpuVendor vendor = GpuDetectionService.GetVendor(name, pnpDeviceId);
                    string driverPage = GetGraphicsDriverUrl(vendor);
                    uint? deviceManagerErrorCode = TryReadUInt32(controller["ConfigManagerErrorCode"]);

                    // A deliberately disabled GPU (most commonly an iGPU) is an intentional
                    // configuration, not a missing-driver warning.
                    if (deviceManagerErrorCode == 22)
                    {
                        continue;
                    }

                    if (GpuVendorClassification.IsBasicDisplayAdapter(name))
                    {
                        bool vendorIsKnown = vendor != GpuVendor.Unknown;
                        items.Add(new NeedsAttentionItem(
                            vendorIsKnown
                                ? FormatText("NeedsAttention_GraphicsMissingTitle", GetGpuVendorName(vendor))
                                : Text("NeedsAttention_GraphicsMissingUnknownTitle"),
                            vendorIsKnown
                                ? FormatText("NeedsAttention_GraphicsMissingKnownDescription", name, GetGpuVendorName(vendor))
                                : FormatText("NeedsAttention_GraphicsMissingUnknownDescription", name),
                            vendorIsKnown ? Text("NeedsAttention_GetGraphicsDriver") : Text("NeedsAttention_OpenDeviceManager"),
                            vendorIsKnown ? NeedsAttentionAction.OpenGraphicsDriverPage : NeedsAttentionAction.OpenDeviceManager,
                            vendorIsKnown ? driverPage : null,
                            CheckId: NeedsAttentionCheckIds.Drivers));
                        continue;
                    }

                    if (deviceManagerErrorCode is > 0)
                    {
                        items.Add(new NeedsAttentionItem(
                            Text("NeedsAttention_GraphicsErrorTitle"),
                            FormatText("NeedsAttention_DeviceProblemDescription", name, GetDeviceManagerProblemText(deviceManagerErrorCode.Value)),
                            Text("NeedsAttention_OpenDeviceManager"),
                            NeedsAttentionAction.OpenDeviceManager,
                            CheckId: NeedsAttentionCheckIds.Drivers));
                    }
                }
            }
        }
        catch (Exception exception)
        {
            App.logger.Debug(exception, "[NeedsAttention] Graphics-driver status is unavailable.");
        }
    }

    private void AddRequiredPlatformDeviceItems(ICollection<NeedsAttentionItem> items)
    {
        try
        {
            using ManagementObjectSearcher searcher = new(
                "SELECT Name, PNPDeviceID, ConfigManagerErrorCode FROM Win32_PnPEntity WHERE ConfigManagerErrorCode <> 0");
            using ManagementObjectCollection devices = searcher.Get();
            foreach (ManagementObject device in devices.Cast<ManagementObject>())
            {
                using (device)
                {
                    string name = device["Name"] as string ?? Text("NeedsAttention_UnknownSystemComponent");
                    string pnpDeviceId = device["PNPDeviceID"] as string ?? string.Empty;
                    uint? deviceManagerErrorCode = TryReadUInt32(device["ConfigManagerErrorCode"]);
                    if (!IsRequiredPlatformDevice(name, pnpDeviceId) || deviceManagerErrorCode is not > 0)
                    {
                        continue;
                    }

                    string ignoreKey = GetPlatformIgnoreKey(pnpDeviceId, deviceManagerErrorCode.Value);
                    if (IsIgnored(ignoreKey))
                    {
                        continue;
                    }

                    string componentType = IsSerialIoDevice(name)
                        ? Text("NeedsAttention_SerialIoComponent")
                        : Text("NeedsAttention_ChipsetComponent");
                    items.Add(new NeedsAttentionItem(
                        FormatText("NeedsAttention_ComponentErrorTitle", componentType),
                        FormatText("NeedsAttention_DeviceProblemDescription", name, GetDeviceManagerProblemText(deviceManagerErrorCode.Value)),
                        Text("NeedsAttention_OpenDeviceManager"),
                        NeedsAttentionAction.OpenDeviceManager,
                        IgnoreKey: ignoreKey,
                        CheckId: NeedsAttentionCheckIds.Drivers));
                }
            }
        }
        catch (Exception exception)
        {
            App.logger.Debug(exception, "[NeedsAttention] Required platform-device status is unavailable.");
        }
    }

    private async Task AddToolkitUpdateItemAsync(
        ICollection<NeedsAttentionItem> items,
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        try
        {
            SynToolkitUpdateStatus status = await SynToolkitUpdateHelper.CheckUpdatesAsync(forceRefresh, cancellationToken);
            if (!status.IsUpdateAvailable)
            {
                return;
            }

            items.Add(new NeedsAttentionItem(
                Text("NeedsAttention_SynToolkitUpdateTitle"),
                FormatText(
                    "NeedsAttention_SynToolkitUpdateDescription",
                    status.AvailableVersion?.ToString() ?? string.Empty,
                    status.CurrentVersion?.ToString() ?? string.Empty),
                Text("NeedsAttention_OpenUpdateSettings"),
                NeedsAttentionAction.OpenSettings,
                CheckId: NeedsAttentionCheckIds.SynToolkitVersion));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            App.logger.Debug(exception, "[NeedsAttention] SynToolkit update status is unavailable.");
        }
    }

    private async Task AddClockItemAsync(ICollection<NeedsAttentionItem> items, CancellationToken cancellationToken)
    {
        try
        {
            TimeSpan? difference = await GetNetworkClockDifferenceAsync(cancellationToken);
            if (!difference.HasValue || Math.Abs(difference.Value.TotalMilliseconds) < ClockDifferenceThresholdMilliseconds)
            {
                return;
            }

            items.Add(new NeedsAttentionItem(
                Text("NeedsAttention_ClockTitle"),
                FormatText("NeedsAttention_ClockDescription", Math.Abs(difference.Value.TotalSeconds).ToString("0.0", CultureInfo.CurrentCulture)),
                Text("NeedsAttention_SyncClock"),
                NeedsAttentionAction.SyncWindowsClock,
                CheckId: NeedsAttentionCheckIds.WindowsTime));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            // A captive portal, firewall, or offline connection should never be displayed as a clock problem.
            App.logger.Debug(exception, "[NeedsAttention] Network clock status is unavailable.");
        }
    }

    private async Task AddInstallerUpdateItemsAsync(ICollection<NeedsAttentionItem> items, CancellationToken cancellationToken)
    {
        try
        {
            IReadOnlyList<FeaturedInstallerViewModel> installers = AppFetchPageViewModel.CreateFeaturedInstallers();
            IReadOnlyList<CuratedPackageStatus> statuses = await _wingetInstallerService.DetectPackageStatusesAsync(
                installers
                    .Where(installer => !installer.IsManualOnly)
                    .Select(installer => new CuratedPackageProbe(
                        installer.PackageIdentifier,
                        installer.InstalledDisplayNamePrefixes))
                    .ToList(),
                cancellationToken);

            foreach (CuratedPackageStatus status in statuses.Where(status => status.IsUpdateAvailable))
            {
                FeaturedInstallerViewModel? installer = installers
                    .FirstOrDefault(candidate => string.Equals(
                        candidate.PackageIdentifier,
                        status.PackageIdentifier,
                        StringComparison.OrdinalIgnoreCase));
                if (installer is null)
                {
                    continue;
                }

                // Only a known target version may be ignored. This guarantees that a later
                // release immediately reappears instead of inheriting an "unknown" ignore.
                string? ignoreKey = string.IsNullOrWhiteSpace(status.AvailableVersion)
                    ? null
                    : GetInstallerUpdateIgnoreKey(status.PackageIdentifier, status.AvailableVersion);
                if (ignoreKey is not null && IsIgnored(ignoreKey))
                {
                    continue;
                }

                string versionDetail = !string.IsNullOrWhiteSpace(status.InstalledVersion) &&
                    !string.IsNullOrWhiteSpace(status.AvailableVersion)
                    ? FormatText("NeedsAttention_InstallerUpdateVersionDescription", status.InstalledVersion, status.AvailableVersion)
                    : Text("NeedsAttention_InstallerUpdateDescription");
                items.Add(new NeedsAttentionItem(
                    FormatText("NeedsAttention_InstallerUpdateTitle", installer.Name),
                    versionDetail,
                    Text("NeedsAttention_OpenInstaller"),
                    NeedsAttentionAction.OpenInstaller,
                    installer.Name,
                    ignoreKey,
                    NeedsAttentionCheckIds.Apps));
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            App.logger.Debug(exception, "[NeedsAttention] Curated installer update status is unavailable.");
        }
    }

    public static async Task<string?> CreateRestorePointAsync(CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            using ManagementClass restore = new(@"root\default", "SystemRestore", null);
            using ManagementBaseObject input = restore.GetMethodParameters("CreateRestorePoint");
            input["Description"] = "SynToolkit restore point";
            input["RestorePointType"] = 0;
            input["EventType"] = 100;
            using ManagementBaseObject? result = restore.InvokeMethod("CreateRestorePoint", input, null);
            uint returnValue = result?["ReturnValue"] as uint? ?? 1;
            return returnValue == 0
                ? null
                : Text("NeedsAttention_RestoreCreateFailed");
        }, cancellationToken);
    }

    public static async Task<string?> SyncWindowsClockAsync(CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            ServiceHelper.StartService("W32Time", TimeSpan.FromSeconds(15));
            CommandResult result = CommandPromptHelper.RunProcessResult("w32tm.exe", ["/resync"], 30_000);
            return result.Succeeded ? null : Text("NeedsAttention_ClockSyncFailed");
        }, cancellationToken);
    }

    private static Task<TimeSpan?> GetNetworkClockDifferenceAsync(CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            const string host = "time.windows.com";
            const int ntpPort = 123;
            byte[] request = new byte[48];
            request[0] = 0x1B;

            using UdpClient client = new();
            client.Client.ReceiveTimeout = 2_500;
            Stopwatch stopwatch = Stopwatch.StartNew();
            client.Send(request, request.Length, host, ntpPort);
            IPEndPoint responseEndpoint = new(IPAddress.Any, 0);
            byte[] response = client.Receive(ref responseEndpoint);
            stopwatch.Stop();

            if (response.Length < 48)
            {
                return (TimeSpan?)null;
            }

            ulong seconds = ((ulong)response[40] << 24) |
                ((ulong)response[41] << 16) |
                ((ulong)response[42] << 8) |
                response[43];
            ulong fraction = ((ulong)response[44] << 24) |
                ((ulong)response[45] << 16) |
                ((ulong)response[46] << 8) |
                response[47];
            if (seconds == 0)
            {
                return (TimeSpan?)null;
            }

            DateTimeOffset networkUtc = DateTimeOffset.UnixEpoch.AddSeconds(seconds - 2_208_988_800UL)
                .AddMilliseconds(fraction * 1_000d / 0x1_0000_0000UL);
            DateTimeOffset localUtcAtResponse = DateTimeOffset.UtcNow -
                TimeSpan.FromMilliseconds(stopwatch.Elapsed.TotalMilliseconds / 2d);
            return (TimeSpan?)(localUtcAtResponse - networkUtc);
        }, cancellationToken);

    private async Task EnsureIgnoreStateLoadedAsync(CancellationToken cancellationToken)
    {
        if (_ignoreStateLoaded)
        {
            return;
        }

        await _ignoreStateGate.WaitAsync(cancellationToken);
        try
        {
            if (_ignoreStateLoaded)
            {
                return;
            }

            Dictionary<string, NeedsAttentionIgnoreRecord>? savedItems = await Task.Run(() =>
            {
                if (!File.Exists(IgnoreStatePath))
                {
                    return null;
                }

                string json = File.ReadAllText(IgnoreStatePath);
                return JsonSerializer.Deserialize<Dictionary<string, NeedsAttentionIgnoreRecord>>(json);
            }, cancellationToken);

            if (savedItems is not null)
            {
                DateTimeOffset now = DateTimeOffset.UtcNow;
                lock (_ignoredItemsLock)
                {
                    foreach ((string key, NeedsAttentionIgnoreRecord record) in savedItems)
                    {
                        if (record.ExpiresAtUtc > now)
                        {
                            _ignoredItems[key] = record;
                        }
                    }
                }
            }

            _ignoreStateLoaded = true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            // A corrupted preference file must never prevent health checks from running.
            App.logger.Warn(exception, "[NeedsAttention] Saved ignored warnings could not be loaded.");
            _ignoreStateLoaded = true;
        }
        finally
        {
            _ignoreStateGate.Release();
        }
    }

    private async Task PersistIgnoreStateAsync(CancellationToken cancellationToken)
    {
        Dictionary<string, NeedsAttentionIgnoreRecord> snapshot;
        lock (_ignoredItemsLock)
        {
            snapshot = new Dictionary<string, NeedsAttentionIgnoreRecord>(_ignoredItems, StringComparer.Ordinal);
        }

        await Task.Run(() =>
        {
            string? directory = Path.GetDirectoryName(IgnoreStatePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string temporaryPath = IgnoreStatePath + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(snapshot));
            File.Move(temporaryPath, IgnoreStatePath, overwrite: true);
        }, cancellationToken);
    }

    private bool IsIgnored(string ignoreKey)
    {
        lock (_ignoredItemsLock)
        {
            if (!_ignoredItems.TryGetValue(ignoreKey, out NeedsAttentionIgnoreRecord? record))
            {
                return false;
            }

            if (record.ExpiresAtUtc > DateTimeOffset.UtcNow)
            {
                return true;
            }

            _ignoredItems.Remove(ignoreKey);
            return false;
        }
    }

    private bool IsCheckIgnored(string checkId) =>
        !string.IsNullOrWhiteSpace(checkId) && IsIgnored(ToCheckIgnoreKey(checkId));

    private static string ToCheckIgnoreKey(string checkId) => CheckIgnoreKeyPrefix + checkId;

    private NeedsAttentionSnapshot WithIgnoredCheckCount(NeedsAttentionSnapshot snapshot) =>
        snapshot.IgnoredCheckCount == GetIgnoredCheckCount()
            ? snapshot
            : snapshot with { IgnoredCheckCount = GetIgnoredCheckCount() };

    private void RemoveIgnoredItemFromCaches(string ignoreKey)
    {
        _localSnapshot = RemoveIgnoredItem(_localSnapshot, ignoreKey);
        _onlineSnapshot = RemoveIgnoredItem(_onlineSnapshot, ignoreKey);
    }

    private void RemoveIgnoredCheckFromCaches(string checkId)
    {
        _localSnapshot = RemoveIgnoredCheck(_localSnapshot, checkId);
        _onlineSnapshot = RemoveIgnoredCheck(_onlineSnapshot, checkId);
    }

    private void RemoveAllIgnoredChecksFromCaches()
    {
        foreach (string checkId in NeedsAttentionCheckIds.All)
        {
            RemoveIgnoredCheckFromCaches(checkId);
        }
    }

    private NeedsAttentionSnapshot? RemoveIgnoredItem(NeedsAttentionSnapshot? snapshot, string ignoreKey)
    {
        if (snapshot is null)
        {
            return null;
        }

        List<NeedsAttentionItem> remainingItems = snapshot.Items
            .Where(item => !string.Equals(item.IgnoreKey, ignoreKey, StringComparison.Ordinal))
            .ToList();
        return remainingItems.Count == snapshot.Items.Count
            ? WithIgnoredCheckCount(snapshot)
            : new NeedsAttentionSnapshot(
                remainingItems,
                snapshot.CheckedAt,
                snapshot.IncludesOnlineChecks,
                GetIgnoredCheckCount());
    }

    private NeedsAttentionSnapshot? RemoveIgnoredCheck(NeedsAttentionSnapshot? snapshot, string checkId)
    {
        if (snapshot is null)
        {
            return null;
        }

        List<NeedsAttentionItem> remainingItems = snapshot.Items
            .Where(item => !string.Equals(item.CheckId, checkId, StringComparison.Ordinal))
            .ToList();
        return remainingItems.Count == snapshot.Items.Count
            ? WithIgnoredCheckCount(snapshot)
            : new NeedsAttentionSnapshot(
                remainingItems,
                snapshot.CheckedAt,
                snapshot.IncludesOnlineChecks,
                GetIgnoredCheckCount());
    }

    private static string GetInstallerUpdateIgnoreKey(string packageIdentifier, string availableVersion) =>
        $"installer-update:{packageIdentifier.ToUpperInvariant()}:{availableVersion}";

    private static string GetPlatformIgnoreKey(string pnpDeviceId, uint errorCode) =>
        $"platform-driver:{pnpDeviceId.ToUpperInvariant()}:{errorCode}";

    private static bool IsRequiredPlatformDevice(string name, string pnpDeviceId)
    {
        bool isIntelOrAmdPlatformDevice =
            pnpDeviceId.Contains("VEN_8086", StringComparison.OrdinalIgnoreCase) ||
            pnpDeviceId.Contains("VEN_1022", StringComparison.OrdinalIgnoreCase);
        if (!isIntelOrAmdPlatformDevice)
        {
            return false;
        }

        return string.IsNullOrWhiteSpace(name) ||
            name.Contains("unknown device", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("chipset", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("serial io", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("GPIO", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("SMBus", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("I2C", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("SPI", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("UART", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("LPC", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("PCI Express Root", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Management Engine", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSerialIoDevice(string name) =>
        name.Contains("serial io", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("GPIO", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("I2C", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("SPI", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("UART", StringComparison.OrdinalIgnoreCase);

    private static uint? TryReadUInt32(object? value)
    {
        try
        {
            return value is null ? null : Convert.ToUInt32(value);
        }
        catch
        {
            return null;
        }
    }

    private static string GetDeviceManagerProblemText(uint errorCode) => errorCode switch
    {
        22 => Text("NeedsAttention_DeviceDisabled"),
        28 => Text("NeedsAttention_DeviceNoDriver"),
        31 => Text("NeedsAttention_DeviceCannotLoad"),
        43 => Text("NeedsAttention_DeviceStopped"),
        _ => FormatText("NeedsAttention_DeviceErrorCode", errorCode)
    };

    /// <summary>
    /// Returns local fixed volumes that are backed by an internal disk. USB, SD/removable, and
    /// network volumes are excluded before free space is evaluated. The system volume is kept as
    /// a safe fallback if an unusual WMI provider cannot expose the disk-to-volume mapping.
    /// </summary>
    private static IReadOnlyList<string> GetInternalFixedDriveRoots()
    {
        HashSet<string> roots = new(StringComparer.OrdinalIgnoreCase);
        try
        {
            using ManagementObjectSearcher searcher = new(
                "SELECT DeviceID, InterfaceType, MediaType, PNPDeviceID FROM Win32_DiskDrive");
            using ManagementObjectCollection disks = searcher.Get();
            foreach (ManagementObject disk in disks.Cast<ManagementObject>())
            {
                using (disk)
                {
                    if (!IsInternalDisk(disk))
                    {
                        continue;
                    }

                    using ManagementObjectCollection partitions = disk.GetRelated("Win32_DiskPartition");
                    foreach (ManagementObject partition in partitions.Cast<ManagementObject>())
                    {
                        using (partition)
                        {
                            using ManagementObjectCollection logicalDisks = partition.GetRelated("Win32_LogicalDisk");
                            foreach (ManagementObject logicalDisk in logicalDisks.Cast<ManagementObject>())
                            {
                                using (logicalDisk)
                                {
                                    string? deviceId = logicalDisk["DeviceID"] as string;
                                    if (string.IsNullOrWhiteSpace(deviceId))
                                    {
                                        continue;
                                    }

                                    string root = deviceId.TrimEnd('\\') + "\\";
                                    try
                                    {
                                        if (new DriveInfo(root).DriveType == DriveType.Fixed)
                                        {
                                            roots.Add(root);
                                        }
                                    }
                                    catch
                                    {
                                        // The volume changed while WMI was enumerating it.
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
        catch (Exception exception)
        {
            App.logger.Debug(exception, "[NeedsAttention] Internal-disk mapping is unavailable.");
        }

        if (roots.Count == 0)
        {
            roots.Add(Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\");
        }

        return roots.OrderBy(root => root, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static bool IsInternalDisk(ManagementBaseObject disk)
    {
        string interfaceType = disk["InterfaceType"] as string ?? string.Empty;
        string pnpDeviceId = disk["PNPDeviceID"] as string ?? string.Empty;
        string deviceId = disk["DeviceID"] as string ?? string.Empty;
        return !interfaceType.Equals("USB", StringComparison.OrdinalIgnoreCase) &&
            !interfaceType.Equals("SD", StringComparison.OrdinalIgnoreCase) &&
            !pnpDeviceId.Contains("USB", StringComparison.OrdinalIgnoreCase) &&
            !pnpDeviceId.Contains("SDSTOR", StringComparison.OrdinalIgnoreCase) &&
            !deviceId.Contains("USB", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetGpuVendorName(GpuVendor vendor) => vendor switch
    {
        GpuVendor.Nvidia => "NVIDIA",
        GpuVendor.Amd => "AMD",
        GpuVendor.Intel => "Intel",
        _ => "Unknown"
    };

    private static string GetGraphicsDriverUrl(GpuVendor vendor) => vendor switch
    {
        GpuVendor.Nvidia => "https://www.nvidia.com/en-us/drivers/",
        GpuVendor.Amd => "https://www.amd.com/en/support/download/drivers.html",
        GpuVendor.Intel => "https://www.intel.com/content/www/us/en/support/detect.html",
        _ => "https://www.intel.com/content/www/us/en/support/detect.html",
    };

    private static string FormatGiB(long bytes) => (bytes / 1024d / 1024d / 1024d).ToString("0.#") + " GB";

    private static string Text(string key) => App.GetValueFromItemList(key);

    private static string FormatText(string key, params object[] arguments)
    {
        try
        {
            return string.Format(CultureInfo.CurrentCulture, Text(key), arguments);
        }
        catch (FormatException)
        {
            return Text(key);
        }
    }
}
