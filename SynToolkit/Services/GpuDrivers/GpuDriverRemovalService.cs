#nullable enable

using System.IO;
using System.Diagnostics;
using Microsoft.Win32;
using SynToolkit.Models.GpuDrivers;

namespace SynToolkit.Services.GpuDrivers;

/// <summary>
/// A lightweight, DDU-style GPU driver removal engine. Purges the active display
/// driver using only legitimate Windows mechanisms (vendor uninstallers, pnputil
/// driver-store deletion, service removal, and leftover file/registry cleanup).
/// Every step is isolated so a single failure never aborts the whole purge.
/// </summary>
internal static class GpuDriverRemovalService
{
    private const string DisplayClassGuid = "{4d36e968-e325-11ce-bfc1-08002be10318}";
    private const string DisplayClassKeyPath =
        @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";

    public static GpuDriverVendor DetectVendor(GpuDeviceInfo? device)
    {
        if (device is null)
            return GpuDriverVendor.Unknown;
        if (device.IsNvidia)
            return GpuDriverVendor.Nvidia;
        if (device.IsAmd)
            return GpuDriverVendor.Amd;
        if (device.VendorId.Equals("8086", StringComparison.OrdinalIgnoreCase))
            return GpuDriverVendor.Intel;

        var name = device.DisplayName;
        if (name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) || name.Contains("GeForce", StringComparison.OrdinalIgnoreCase))
            return GpuDriverVendor.Nvidia;
        if (name.Contains("AMD", StringComparison.OrdinalIgnoreCase) || name.Contains("Radeon", StringComparison.OrdinalIgnoreCase))
            return GpuDriverVendor.Amd;
        if (name.Contains("Intel", StringComparison.OrdinalIgnoreCase) || name.Contains("Arc", StringComparison.OrdinalIgnoreCase))
            return GpuDriverVendor.Intel;

        return GpuDriverVendor.Unknown;
    }

    public static async Task<GpuRemovalResult> RemoveAsync(
        GpuRemovalOptions options,
        IProgress<GpuRemovalLogEntry> log,
        CancellationToken cancellationToken = default)
    {
        var ok = 0;
        var failed = 0;

        void Info(string m) => log.Report(new GpuRemovalLogEntry { Level = GpuRemovalLogLevel.Info, Message = m });
        void Step(string m) => log.Report(new GpuRemovalLogEntry { Level = GpuRemovalLogLevel.Step, Message = m });
        void Success(string m) { ok++; log.Report(new GpuRemovalLogEntry { Level = GpuRemovalLogLevel.Success, Message = m }); }
        void Warn(string m) { failed++; log.Report(new GpuRemovalLogEntry { Level = GpuRemovalLogLevel.Warning, Message = m }); }

        if (options.Vendor == GpuDriverVendor.Unknown)
            return new GpuRemovalResult { Completed = false, Summary = "Could not determine the GPU vendor to remove." };

        Info($"Starting {options.Vendor} driver removal.");

        var profile = VendorProfile.For(options.Vendor);

        if (options.RunVendorUninstaller)
        {
            Step("Running vendor uninstaller(s)...");
            try
            {
                var ran = await RunVendorUninstallersAsync(profile, Info, cancellationToken);
                if (ran > 0) Success($"Vendor uninstaller(s) completed ({ran}).");
                else Info("No vendor uninstaller found — continuing with manual purge.");
            }
            catch (Exception ex) { Warn($"Vendor uninstaller step failed: {ex.Message}"); }
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (options.RemoveServices)
        {
            Step("Stopping and deleting vendor services...");
            foreach (var svc in profile.Services)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    await ProcessRunner.RunAsync("sc.exe", $"stop \"{svc}\"", cancellationToken);
                    var (deleted, output) = await ProcessRunner.RunAsync("sc.exe", $"delete \"{svc}\"", cancellationToken);
                    if (deleted) Success($"Removed service: {svc}");
                    else if (output.Contains("1060")) Info($"Service not present: {svc}");
                    else Info($"Service {svc}: {Trim(output)}");
                }
                catch (Exception ex) { Warn($"Service {svc} failed: {ex.Message}"); }
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (options.RemoveDriverStorePackages)
        {
            Step("Removing driver-store packages (pnputil)...");
            try
            {
                var infs = await FindDisplayDriverPackagesAsync(profile, cancellationToken);
                if (infs.Count == 0)
                {
                    Info("No matching driver-store packages found.");
                }
                else
                {
                    foreach (var inf in infs)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var (success, output) = await ProcessRunner.RunAsync(
                            "pnputil.exe", $"/delete-driver {inf} /uninstall /force", cancellationToken);
                        if (success) Success($"Deleted driver package: {inf}");
                        else Warn($"Could not delete {inf}: {Trim(output)}");
                    }
                }
            }
            catch (Exception ex) { Warn($"Driver-store removal failed: {ex.Message}"); }
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (options.RemoveLeftoverFiles)
        {
            Step("Deleting leftover files and folders...");
            foreach (var folder in profile.Folders.Select(Environment.ExpandEnvironmentVariables).Distinct())
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    if (!Directory.Exists(folder)) continue;
                    Directory.Delete(folder, recursive: true);
                    Success($"Deleted folder: {folder}");
                }
                catch (Exception ex) { Warn($"Locked/failed: {folder} ({ex.Message})"); }
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (options.RemoveRegistryEntries)
        {
            Step("Resetting registry (leftover keys + persisted tweaks)...");
            foreach (var (hive, subKey) in profile.RegistryKeys)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
                    if (baseKey.OpenSubKey(subKey) is null) continue;
                    baseKey.DeleteSubKeyTree(subKey, throwOnMissingSubKey: false);
                    Success($"Deleted registry key: {Describe(hive)}\\{subKey}");
                }
                catch (Exception ex) { Warn($"Registry {subKey} failed: {ex.Message}"); }
            }

            // Reset the driver's per-adapter class keys — this is where persisted
            // overclock/undervolt tweaks live, so clearing them undoes bad settings.
            try
            {
                var cleared = ResetDisplayAdapterClassKeys(profile, Success, Warn, cancellationToken);
                if (cleared == 0)
                    Info("No matching display-adapter class keys found.");
            }
            catch (Exception ex) { Warn($"Adapter class-key reset failed: {ex.Message}"); }

        }

        if (options.BlockAutomaticReinstall)
        {
            Step("Blocking automatic driver reinstall...");
            try
            {
                using var key = Registry.LocalMachine.CreateSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\DriverSearching");
                key?.SetValue("SearchOrderConfig", 0, RegistryValueKind.DWord);

                using var meta = Registry.LocalMachine.CreateSubKey(
                    @"SOFTWARE\Policies\Microsoft\Windows\Device Metadata");
                meta?.SetValue("PreventDeviceMetadataFromNetwork", 1, RegistryValueKind.DWord);

                Success("Automatic driver downloads disabled.");
                Info("Re-enable later in Settings if you want Windows Update drivers back.");
            }
            catch (Exception ex) { Warn($"Could not block auto reinstall: {ex.Message}"); }
        }

        var summary = failed == 0
            ? $"{options.Vendor} driver removed. {ok} steps completed."
            : $"{options.Vendor} removal finished with {failed} warning(s) and {ok} step(s) done.";
        Info(summary);
        Info("Reboot to finish cleanup. Some files may be removed on restart.");

        return new GpuRemovalResult
        {
            Completed = true,
            StepsSucceeded = ok,
            StepsFailed = failed,
            Summary = summary
        };
    }

    public static Task<(bool Success, string Output)> RebootNowAsync() =>
        ProcessRunner.RunAsync("shutdown.exe", "/r /t 4 /c \"SynToolkit: restart to finish driver removal\"");

    private static int ResetDisplayAdapterClassKeys(
        VendorProfile profile, Action<string> success, Action<string> warn, CancellationToken token)
    {
        var cleared = 0;
        using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var classKey = baseKey.OpenSubKey(DisplayClassKeyPath, writable: true);
        if (classKey is null)
            return 0;

        foreach (var name in classKey.GetSubKeyNames())
        {
            token.ThrowIfCancellationRequested();
            if (!int.TryParse(name, out _)) // adapter instances are "0000", "0001", ...
                continue;

            try
            {
                bool belongsToVendor;
                using (var instance = classKey.OpenSubKey(name))
                {
                    if (instance is null) continue;
                    var marker = string.Join(' ',
                        (instance.GetValue("ProviderName") as string) ?? string.Empty,
                        (instance.GetValue("DriverDesc") as string) ?? string.Empty,
                        (instance.GetValue("MatchingDeviceId") as string) ?? string.Empty);
                    belongsToVendor = profile.ProviderTokens.Any(t =>
                        marker.Contains(t, StringComparison.OrdinalIgnoreCase));
                }

                if (!belongsToVendor) continue;

                classKey.DeleteSubKeyTree(name, throwOnMissingSubKey: false);
                success($"Reset adapter class key: ...\\Class\\{{Display}}\\{name}");
                cleared++;
            }
            catch (Exception ex) { warn($"Adapter key {name}: {ex.Message}"); }
        }

        return cleared;
    }

    private static async Task<int> RunVendorUninstallersAsync(
        VendorProfile profile, Action<string> info, CancellationToken token)
    {
        var ran = 0;
        foreach (var (path, args) in profile.Uninstallers)
        {
            token.ThrowIfCancellationRequested();
            var resolved = Environment.ExpandEnvironmentVariables(path);
            if (!File.Exists(resolved)) continue;

            info($"Running {Path.GetFileName(resolved)}...");
            try
            {
                using var process = Process.Start(new ProcessStartInfo
                {
                    FileName = resolved,
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                if (process is null) continue;
                await process.WaitForExitAsync(token);
                ran++;
            }
            catch (Exception ex)
            {
                info($"Uninstaller {Path.GetFileName(resolved)} did not complete: {ex.Message}");
            }
        }

        return ran;
    }

    private static async Task<List<string>> FindDisplayDriverPackagesAsync(
        VendorProfile profile, CancellationToken token)
    {
        var result = new List<string>();
        var (_, output) = await ProcessRunner.RunAsync("pnputil.exe", "/enum-drivers", token);
        if (string.IsNullOrWhiteSpace(output))
            return result;

        // Parse blocks separated by blank lines. We match on the locale-independent
        // display class GUID and the vendor provider token, then take the oemNN.inf.
        var blocks = output.Replace("\r\n", "\n").Split("\n\n", StringSplitOptions.RemoveEmptyEntries);
        foreach (var block in blocks)
        {
            if (!block.Contains(DisplayClassGuid, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!profile.ProviderTokens.Any(t => block.Contains(t, StringComparison.OrdinalIgnoreCase)))
                continue;

            foreach (var line in block.Split('\n'))
            {
                var idx = line.IndexOf("oem", StringComparison.OrdinalIgnoreCase);
                if (idx < 0) continue;
                var token2 = line[idx..].Split(' ', '\t', ',').FirstOrDefault()?.Trim();
                if (!string.IsNullOrEmpty(token2) &&
                    token2.EndsWith(".inf", StringComparison.OrdinalIgnoreCase) &&
                    !result.Contains(token2, StringComparer.OrdinalIgnoreCase))
                {
                    result.Add(token2);
                }
            }
        }

        return result;
    }

    private static string Trim(string s) =>
        string.IsNullOrWhiteSpace(s) ? string.Empty : s.Replace("\r", " ").Replace("\n", " ").Trim();

    private static string Describe(RegistryHive hive) => hive switch
    {
        RegistryHive.LocalMachine => "HKLM",
        RegistryHive.CurrentUser => "HKCU",
        _ => hive.ToString()
    };

    private sealed class VendorProfile
    {
        public string[] ProviderTokens { get; init; } = [];
        public string[] Services { get; init; } = [];
        public string[] Folders { get; init; } = [];
        public (RegistryHive Hive, string SubKey)[] RegistryKeys { get; init; } = [];
        public (string Path, string Args)[] Uninstallers { get; init; } = [];

        public static VendorProfile For(GpuDriverVendor vendor) => vendor switch
        {
            GpuDriverVendor.Nvidia => Nvidia,
            GpuDriverVendor.Amd => Amd,
            GpuDriverVendor.Intel => Intel,
            _ => new VendorProfile()
        };

        private static readonly VendorProfile Nvidia = new()
        {
            ProviderTokens = ["NVIDIA"],
            Services =
            [
                "nvlddmkm", "nvagent", "NVDisplay.ContainerLocalSystem", "NvContainerLocalSystem",
                "NvTelemetryContainer", "nvsvc", "NVWMI", "nvvad_WaveExtensible", "NvBroadcast.ContainerLocalSystem"
            ],
            Folders =
            [
                @"%ProgramFiles%\NVIDIA Corporation",
                @"%ProgramFiles(x86)%\NVIDIA Corporation",
                @"%ProgramData%\NVIDIA Corporation",
                @"%ProgramData%\NVIDIA",
                @"%LocalAppData%\NVIDIA",
                @"%LocalAppData%\NVIDIA Corporation",
                @"%AppData%\NVIDIA",
                @"%SystemRoot%\System32\DriverStore\Temp",
                @"C:\NVIDIA"
            ],
            RegistryKeys =
            [
                (RegistryHive.LocalMachine, @"SOFTWARE\NVIDIA Corporation"),
                (RegistryHive.LocalMachine, @"SOFTWARE\WOW6432Node\NVIDIA Corporation"),
                (RegistryHive.CurrentUser, @"SOFTWARE\NVIDIA Corporation")
            ],
            Uninstallers =
            [
                (@"%ProgramFiles(x86)%\NVIDIA Corporation\Installer2\InstallerCore\NVI2.DLL", string.Empty)
            ]
        };

        private static readonly VendorProfile Amd = new()
        {
            ProviderTokens = ["Advanced Micro Devices", "AMD", "ATI Technologies"],
            Services =
            [
                "amdkmdag", "amdwddmg", "AMD External Events Utility", "amdfendr", "amdlog", "AMDRyzenMasterDriverV"
            ],
            Folders =
            [
                @"%ProgramFiles%\AMD",
                @"%ProgramFiles(x86)%\AMD",
                @"%ProgramFiles%\ATI Technologies",
                @"%ProgramFiles(x86)%\ATI Technologies",
                @"%ProgramData%\AMD",
                @"%LocalAppData%\AMD",
                @"C:\AMD"
            ],
            RegistryKeys =
            [
                (RegistryHive.LocalMachine, @"SOFTWARE\AMD"),
                (RegistryHive.LocalMachine, @"SOFTWARE\WOW6432Node\AMD"),
                (RegistryHive.LocalMachine, @"SOFTWARE\ATI Technologies"),
                (RegistryHive.LocalMachine, @"SOFTWARE\WOW6432Node\ATI Technologies")
            ],
            Uninstallers =
            [
                (@"%ProgramFiles%\AMD\CIM\BIN64\InstallManagerApp.exe", "-Uninstall"),
                (@"C:\Program Files\AMD\CIM\BIN64\InstallManagerApp.exe", "-Uninstall")
            ]
        };

        private static readonly VendorProfile Intel = new()
        {
            ProviderTokens = ["Intel"],
            Services = ["igfxCUIService2.0.0.0", "Intel(R) Graphics Command Center Service"],
            Folders =
            [
                @"%ProgramFiles%\Intel\Intel(R) Processor Graphics",
                @"%ProgramData%\Intel",
                @"%LocalAppData%\Intel"
            ],
            RegistryKeys =
            [
                (RegistryHive.LocalMachine, @"SOFTWARE\Intel\GFX"),
                (RegistryHive.LocalMachine, @"SOFTWARE\WOW6432Node\Intel\GFX")
            ],
            Uninstallers = []
        };
    }
}
