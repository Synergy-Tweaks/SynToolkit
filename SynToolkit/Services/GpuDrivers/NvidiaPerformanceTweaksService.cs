#nullable enable

using System.Collections.Generic;
using Microsoft.Win32;
using SynToolkit.Models.GpuDrivers;
using System.Globalization;

namespace SynToolkit.Services.GpuDrivers;

internal enum NvidiaTweakGroup
{
    NvidiaSettings,
    RmPowerFeature,
    Telemetry,
    Ecc,
    Hdcp
}

internal enum NvidiaTweakValueState
{
    NotSet,
    Different,
    Partial,
    Applied
}

internal sealed record NvidiaRegistryTarget(
    RegistryHive Hive,
    string Path,
    string LocationLabel,
    string ValueName,
    RegistryValueKind Kind,
    object ExpectedValue);

internal sealed record NvidiaTweakDefinition(
    string Id,
    string Name,
    NvidiaTweakGroup Group,
    IReadOnlyList<NvidiaRegistryTarget> Targets)
{
    public string LocationText =>
        string.Join(" + ", Targets.Select(static target => target.LocationLabel).Distinct(StringComparer.Ordinal));

    public string ExpectedValueText
    {
        get
        {
            var target = Targets[0];
            return target.Kind switch
            {
                RegistryValueKind.DWord => $"DWORD {Convert.ToUInt32(target.ExpectedValue):X}",
                RegistryValueKind.Binary when target.ExpectedValue is byte[] bytes => $"BINARY · {bytes.Length} bytes",
                _ => target.ExpectedValue.ToString() ?? string.Empty
            };
        }
    }

    public string RecommendedValueText
    {
        get
        {
            var target = Targets[0];
            return target.Kind switch
            {
                RegistryValueKind.DWord => $"{Convert.ToUInt32(target.ExpectedValue):X}",
                RegistryValueKind.Binary when target.ExpectedValue is byte[] bytes => $"{bytes.Length} bytes",
                _ => target.ExpectedValue.ToString() ?? string.Empty
            };
        }
    }

    public bool SupportsCustomValue =>
        Targets.All(static target => target.Kind == RegistryValueKind.DWord);
}

internal sealed record NvidiaTweakValueSnapshot(
    NvidiaTweakValueState State,
    string CurrentValueText);

internal sealed record NvidiaTweakCheckResult(
    bool Success,
    string Message,
    IReadOnlyDictionary<string, NvidiaTweakValueSnapshot> Values);

internal static class NvidiaPerformanceTweaksService
{
    private const string DriverClassToken = "$NVIDIA_DRIVER_CLASS$";
    private const string DisplayClassRoot =
        @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";
    private const string ServiceTweakPath = @"SYSTEM\CurrentControlSet\Services\nvlddmkm\Global\NVTweak";
    private const string GlobalTweakPath = @"SOFTWARE\NVIDIA Corporation\Global\NVTweak";
    private const string DriverServicePath = @"SYSTEM\CurrentControlSet\Services\nvlddmkm";
    private const string DriverParametersPath = @"SYSTEM\CurrentControlSet\Services\nvlddmkm\Parameters";
    private const string DriverStartupPath = @"SYSTEM\CurrentControlSet\Services\nvlddmkm\Global\Startup";

    private sealed class RegistryContext : IDisposable
    {
        private readonly Dictionary<RegistryHive, RegistryKey> _baseKeys = [];

        public RegistryKey GetBaseKey(RegistryHive hive)
        {
            if (!_baseKeys.TryGetValue(hive, out var key))
            {
                key = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
                _baseKeys.Add(hive, key);
            }

            return key;
        }

        public void Dispose()
        {
            foreach (var key in _baseKeys.Values)
                key.Dispose();
        }
    }

    public static IReadOnlyList<NvidiaTweakDefinition> Definitions { get; } = CreateDefinitions();

    public static IReadOnlyList<NvidiaTweakDefinition> GetDefinitions(NvidiaTweakGroup group) =>
        Definitions.Where(definition => definition.Group == group).ToList();

    public static Task<NvidiaTweakCheckResult> CheckAsync(GpuDeviceInfo device) =>
        Task.Run(() => Check(device));

    public static Task<(bool Success, string Message, NvidiaTweakValueSnapshot? Value)> CheckItemAsync(
        GpuDeviceInfo device,
        string id) =>
        Task.Run(() => CheckItem(device, id));

    public static Task<(bool Success, string Message)> ApplyItemAsync(GpuDeviceInfo device, string id) =>
        ApplyAsync(device, Definitions.Where(definition => definition.Id == id));

    public static Task<(bool Success, string Message)> RemoveItemAsync(GpuDeviceInfo device, string id) =>
        Task.Run(() => Remove(device, id));

    public static Task<(bool Success, string Message)> SetItemValueAsync(
        GpuDeviceInfo device,
        string id,
        string valueText) =>
        Task.Run(() => SetItemValue(device, id, valueText));

    public static Task<(bool Success, string Message)> ApplyGroupAsync(
        GpuDeviceInfo device,
        NvidiaTweakGroup group) =>
        ApplyAsync(device, Definitions.Where(definition => definition.Group == group));

    public static Task<(bool Success, string Message)> RemoveGroupAsync(
        GpuDeviceInfo device,
        NvidiaTweakGroup group) =>
        Task.Run(() => RemoveGroup(device, group));

    private static NvidiaTweakCheckResult Check(GpuDeviceInfo device)
    {
        try
        {
            using var context = new RegistryContext();
            var classPath = FindNvidiaDriverClassPath(context.GetBaseKey(RegistryHive.LocalMachine), device);
            if (classPath is null)
            {
                return new NvidiaTweakCheckResult(
                    false,
                    "No installed NVIDIA display-driver registry key was found.",
                    new Dictionary<string, NvidiaTweakValueSnapshot>());
            }

            var values = Definitions.ToDictionary(
                definition => definition.Id,
                definition => ReadSnapshot(context, classPath, definition),
                StringComparer.Ordinal);
            return new NvidiaTweakCheckResult(true, string.Empty, values);
        }
        catch (Exception ex)
        {
            return new NvidiaTweakCheckResult(
                false,
                ex.Message,
                new Dictionary<string, NvidiaTweakValueSnapshot>());
        }
    }

    private static (bool Success, string Message, NvidiaTweakValueSnapshot? Value) CheckItem(
        GpuDeviceInfo device,
        string id)
    {
        try
        {
            var definition = Definitions.FirstOrDefault(item => item.Id == id);
            if (definition is null)
                return (false, "The selected NVIDIA registry value was not found.", null);

            using var context = new RegistryContext();
            var classPath = FindNvidiaDriverClassPath(context.GetBaseKey(RegistryHive.LocalMachine), device);
            if (classPath is null)
                return (false, "No installed NVIDIA display-driver registry key was found.", null);

            return (true, string.Empty, ReadSnapshot(context, classPath, definition));
        }
        catch (Exception ex)
        {
            return (false, ex.Message, null);
        }
    }

    private static Task<(bool Success, string Message)> ApplyAsync(
        GpuDeviceInfo device,
        IEnumerable<NvidiaTweakDefinition> selectedDefinitions) =>
        Task.Run(() =>
        {
            try
            {
                var definitions = selectedDefinitions.ToList();
                if (definitions.Count == 0)
                    return (false, "No NVIDIA registry values were selected.");

                using var context = new RegistryContext();
                var classPath = FindNvidiaDriverClassPath(context.GetBaseKey(RegistryHive.LocalMachine), device);
                if (classPath is null)
                    return (false, "No installed NVIDIA display-driver registry key was found.");

                var changed = 0;
                var skipped = 0;
                foreach (var definition in definitions)
                {
                    if (ReadSnapshot(context, classPath, definition).State == NvidiaTweakValueState.Applied)
                    {
                        skipped++;
                        continue;
                    }

                    foreach (var target in definition.Targets)
                    {
                        var path = ResolvePath(target.Path, classPath);
                        using var key = context.GetBaseKey(target.Hive).CreateSubKey(path, writable: true)
                            ?? throw new InvalidOperationException(
                                $"Windows could not open {GetHiveLabel(target.Hive)}\\{path}.");
                        key.SetValue(target.ValueName, target.ExpectedValue, target.Kind);
                    }

                    if (ReadSnapshot(context, classPath, definition).State != NvidiaTweakValueState.Applied)
                        throw new InvalidOperationException($"{definition.Name} could not be verified after writing.");

                    changed++;
                }

                if (changed == 0)
                    return (true, "The selected NVIDIA registry values are already applied.");

                var skippedText = skipped > 0 ? $" {skipped} already-correct value(s) were skipped." : string.Empty;
                return (true, $"Applied {changed} NVIDIA registry value(s).{skippedText} Restart Windows to load them.");
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        });

    private static (bool Success, string Message) Remove(GpuDeviceInfo device, string id)
    {
        try
        {
            var definition = Definitions.FirstOrDefault(item => item.Id == id);
            if (definition is null)
                return (false, "The selected NVIDIA registry value was not found.");

            using var context = new RegistryContext();
            var classPath = FindNvidiaDriverClassPath(context.GetBaseKey(RegistryHive.LocalMachine), device);
            if (classPath is null)
                return (false, "No installed NVIDIA display-driver registry key was found.");

            var current = ReadSnapshot(context, classPath, definition);
            if (current.State == NvidiaTweakValueState.NotSet)
                return (true, $"{definition.Name} is already reverted.");
            foreach (var target in definition.Targets)
            {
                var path = ResolvePath(target.Path, classPath);
                using var key = context.GetBaseKey(target.Hive).OpenSubKey(path, writable: true);
                key?.DeleteValue(target.ValueName, throwOnMissingValue: false);
            }

            if (ReadSnapshot(context, classPath, definition).State != NvidiaTweakValueState.NotSet)
                return (false, $"{definition.Name} could not be verified after removal.");

            return (true, $"Reverted {definition.Name}. Restart Windows to load the change.");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    private static (bool Success, string Message) RemoveGroup(
        GpuDeviceInfo device,
        NvidiaTweakGroup group)
    {
        try
        {
            var definitions = GetDefinitions(group);
            if (definitions.Count == 0)
                return (false, "No NVIDIA registry values were found in this group.");

            using var context = new RegistryContext();
            var classPath = FindNvidiaDriverClassPath(context.GetBaseKey(RegistryHive.LocalMachine), device);
            if (classPath is null)
                return (false, "No installed NVIDIA display-driver registry key was found.");

            var removed = 0;
            foreach (var definition in definitions)
            {
                if (ReadSnapshot(context, classPath, definition).State == NvidiaTweakValueState.NotSet)
                    continue;

                foreach (var target in definition.Targets)
                {
                    var path = ResolvePath(target.Path, classPath);
                    using var key = context.GetBaseKey(target.Hive).OpenSubKey(path, writable: true);
                    key?.DeleteValue(target.ValueName, throwOnMissingValue: false);
                }

                if (ReadSnapshot(context, classPath, definition).State != NvidiaTweakValueState.NotSet)
                    return (false, $"{definition.Name} could not be verified after removal.");

                removed++;
            }

            return removed == 0
                ? (true, "This NVIDIA registry group is already reverted.")
                : (true, $"Reverted {removed} NVIDIA registry setting(s). Restart Windows to load them.");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    private static (bool Success, string Message) SetItemValue(
        GpuDeviceInfo device,
        string id,
        string valueText)
    {
        try
        {
            var definition = Definitions.FirstOrDefault(item => item.Id == id);
            if (definition is null)
                return (false, "The selected NVIDIA registry value was not found.");
            if (!definition.SupportsCustomValue)
                return (false, $"{definition.Name} does not support a custom text value.");

            var normalizedValue = valueText.Trim();
            if (normalizedValue.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                normalizedValue = normalizedValue[2..];
            if (!uint.TryParse(
                    normalizedValue,
                    NumberStyles.AllowHexSpecifier,
                    CultureInfo.InvariantCulture,
                    out var value))
            {
                return (false, "Enter a hexadecimal DWORD value, for example 2A.");
            }

            using var context = new RegistryContext();
            var classPath = FindNvidiaDriverClassPath(context.GetBaseKey(RegistryHive.LocalMachine), device);
            if (classPath is null)
                return (false, "No installed NVIDIA display-driver registry key was found.");

            foreach (var target in definition.Targets)
            {
                var path = ResolvePath(target.Path, classPath);
                using var key = context.GetBaseKey(target.Hive).CreateSubKey(path, writable: true)
                    ?? throw new InvalidOperationException(
                        $"Windows could not open {GetHiveLabel(target.Hive)}\\{path}.");
                key.SetValue(target.ValueName, unchecked((int)value), RegistryValueKind.DWord);
            }

            foreach (var target in definition.Targets)
            {
                var path = ResolvePath(target.Path, classPath);
                using var key = context.GetBaseKey(target.Hive).OpenSubKey(path, writable: false);
                var actual = key?.GetValue(
                    target.ValueName,
                    null,
                    RegistryValueOptions.DoNotExpandEnvironmentNames);
                if (actual is null || ConvertRegistryDword(actual) != value)
                    return (false, $"{definition.Name} could not be verified after writing.");
            }

            return (true, $"Set {definition.Name} to {value:X}. Restart Windows to load the change.");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    private static NvidiaTweakValueSnapshot ReadSnapshot(
        RegistryContext context,
        string classPath,
        NvidiaTweakDefinition definition)
    {
        var existingCount = 0;
        var matchingCount = 0;
        var currentValues = new List<string>();

        foreach (var target in definition.Targets)
        {
            var path = ResolvePath(target.Path, classPath);
            using var key = context.GetBaseKey(target.Hive).OpenSubKey(path, writable: false);
            var actual = key?.GetValue(target.ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            if (actual is null)
            {
                currentValues.Add("Not set");
                continue;
            }

            existingCount++;
            currentValues.Add(FormatRegistryValue(actual, target.Kind));
            if (ValuesMatch(actual, target.ExpectedValue, target.Kind))
                matchingCount++;
        }

        var state = NvidiaTweakValueState.Different;
        if (matchingCount == definition.Targets.Count)
            state = NvidiaTweakValueState.Applied;
        else if (existingCount == 0)
            state = NvidiaTweakValueState.NotSet;
        else if (existingCount < definition.Targets.Count || matchingCount > 0)
            state = NvidiaTweakValueState.Partial;

        var distinctValues = currentValues.Distinct(StringComparer.Ordinal).ToList();
        var currentText = distinctValues.Count == 1
            ? distinctValues[0]
            : string.Join(" / ", distinctValues);
        return new NvidiaTweakValueSnapshot(state, currentText);
    }

    private static string FormatRegistryValue(object value, RegistryValueKind kind) => kind switch
    {
        RegistryValueKind.DWord => $"{ConvertRegistryDword(value):X}",
        RegistryValueKind.Binary when value is byte[] bytes => $"{bytes.Length} bytes",
        _ => value.ToString() ?? string.Empty
    };

    private static bool ValuesMatch(object actual, object expected, RegistryValueKind kind)
    {
        if (kind == RegistryValueKind.Binary)
        {
            return actual is byte[] actualBytes &&
                   expected is byte[] expectedBytes &&
                   actualBytes.AsSpan().SequenceEqual(expectedBytes);
        }

        if (kind == RegistryValueKind.DWord)
            return ConvertRegistryDword(actual) == ConvertRegistryDword(expected);

        return Equals(actual, expected);
    }

    private static uint ConvertRegistryDword(object value) => value switch
    {
        int signed => unchecked((uint)signed),
        uint unsigned => unsigned,
        long signed => unchecked((uint)signed),
        _ => Convert.ToUInt32(value, CultureInfo.InvariantCulture)
    };

    private static string? FindNvidiaDriverClassPath(RegistryKey baseKey, GpuDeviceInfo device)
    {
        using var root = baseKey.OpenSubKey(DisplayClassRoot, writable: false);
        if (root is null)
            return null;

        string? firstNvidiaPath = null;
        foreach (var subKeyName in root.GetSubKeyNames()
                     .Where(static name => name.Length == 4 && name.All(char.IsDigit))
                     .OrderBy(static name => name, StringComparer.Ordinal))
        {
            using var key = root.OpenSubKey(subKeyName, writable: false);
            var provider = key?.GetValue("ProviderName")?.ToString() ?? string.Empty;
            var description = key?.GetValue("DriverDesc")?.ToString() ?? string.Empty;
            if (!provider.StartsWith("NVIDIA", StringComparison.OrdinalIgnoreCase) &&
                !description.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) &&
                !description.Contains("GeForce", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var path = $@"{DisplayClassRoot}\{subKeyName}";
            firstNvidiaPath ??= path;

            var infPath = key?.GetValue("InfPath")?.ToString() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(device.DriverInfName) &&
                infPath.Equals(device.DriverInfName, StringComparison.OrdinalIgnoreCase))
            {
                return path;
            }
        }

        return firstNvidiaPath;
    }

    private static string ResolvePath(string path, string classPath) =>
        path == DriverClassToken ? classPath : path;

    private static string GetHiveLabel(RegistryHive hive) => hive switch
    {
        RegistryHive.ClassesRoot => "HKCR",
        RegistryHive.CurrentUser => "HKCU",
        RegistryHive.LocalMachine => "HKLM",
        _ => hive.ToString()
    };

    private static NvidiaRegistryTarget ClassTarget(string name, int value) =>
        new(
            RegistryHive.LocalMachine,
            DriverClassToken,
            "Driver class",
            name,
            RegistryValueKind.DWord,
            value);

    private static NvidiaRegistryTarget DwordTarget(string path, string location, string name, int value) =>
        DwordTarget(RegistryHive.LocalMachine, path, location, name, value);

    private static NvidiaRegistryTarget DwordTarget(
        RegistryHive hive,
        string path,
        string location,
        string name,
        int value) =>
        new(hive, path, location, name, RegistryValueKind.DWord, value);

    private static NvidiaRegistryTarget StringTarget(
        RegistryHive hive,
        string path,
        string location,
        string name,
        string value) =>
        new(hive, path, location, name, RegistryValueKind.String, value);

    private static NvidiaRegistryTarget BinaryTarget(
        string path,
        string location,
        string name,
        string hexValue) =>
        new(
            RegistryHive.LocalMachine,
            path,
            location,
            name,
            RegistryValueKind.Binary,
            Convert.FromHexString(hexValue));

    private static NvidiaTweakDefinition ClassDword(
        NvidiaTweakGroup group,
        string name,
        int value) =>
        new(name, name, group, [ClassTarget(name, value)]);

    private static IReadOnlyList<NvidiaTweakDefinition> CreateDefinitions()
    {
        var definitions = new List<NvidiaTweakDefinition>
        {
            new(
                "ShowDlssIndicator",
                "ShowDlssIndicator",
                NvidiaTweakGroup.NvidiaSettings,
                [
                    DwordTarget(
                        @"SOFTWARE\NVIDIA Corporation\Global\NGXCore",
                        "NGXCore",
                        "ShowDlssIndicator",
                        0)
                ]),
            new(
                "NvidiaContainerContextMenu",
                "NVIDIA Container context menu",
                NvidiaTweakGroup.NvidiaSettings,
                [
                    StringTarget(
                        RegistryHive.ClassesRoot,
                        @"DesktopBackground\Shell\NvidiaContainer",
                        "Desktop context menu",
                        "Icon",
                        "nvcpl.dll"),
                    StringTarget(
                        RegistryHive.ClassesRoot,
                        @"DesktopBackground\Shell\NvidiaContainer",
                        "Desktop context menu",
                        "MUIVerb",
                        "Nvidia Container"),
                    StringTarget(
                        RegistryHive.ClassesRoot,
                        @"DesktopBackground\Shell\NvidiaContainer",
                        "Desktop context menu",
                        "Position",
                        "Bottom"),
                    StringTarget(
                        RegistryHive.ClassesRoot,
                        @"DesktopBackground\Shell\NvidiaContainer",
                        "Desktop context menu",
                        "SubCommands",
                        string.Empty),
                    StringTarget(
                        RegistryHive.ClassesRoot,
                        @"DesktopBackground\Shell\NvidiaContainer\Shell\EnableNvContainer",
                        "Desktop context menu",
                        "MUIVerb",
                        "Enable Container"),
                    StringTarget(
                        RegistryHive.ClassesRoot,
                        @"DesktopBackground\Shell\NvidiaContainer\Shell\EnableNvContainer\command",
                        "Desktop context menu",
                        string.Empty,
                        @"cmd.exe /c C:\Windows\Misc\NvContainerON.bat"),
                    StringTarget(
                        RegistryHive.ClassesRoot,
                        @"DesktopBackground\Shell\NvidiaContainer\Shell\DisableNvContainer",
                        "Desktop context menu",
                        "MUIVerb",
                        "Disable Container"),
                    StringTarget(
                        RegistryHive.ClassesRoot,
                        @"DesktopBackground\Shell\NvidiaContainer\Shell\DisableNvContainer\command",
                        "Desktop context menu",
                        string.Empty,
                        @"cmd.exe /c C:\Windows\Misc\NvContainerOFF.bat")
                ]),
            new(
                "NotifyNewDisplayUpdates",
                "NotifyNewDisplayUpdates",
                NvidiaTweakGroup.NvidiaSettings,
                [
                    DwordTarget(
                        RegistryHive.CurrentUser,
                        @"SOFTWARE\NVIDIA Corporation\Global\GFExperience",
                        "GFExperience",
                        "NotifyNewDisplayUpdates",
                        0)
                ]),
            new(
                "NvDevToolsVisible",
                "NvDevToolsVisible",
                NvidiaTweakGroup.NvidiaSettings,
                [DwordTarget(ServiceTweakPath, "Service NVTweak", "NvDevToolsVisible", 1)]),
            new(
                "StartOnLogin",
                "StartOnLogin",
                NvidiaTweakGroup.NvidiaSettings,
                [
                    DwordTarget(
                        @"SOFTWARE\NVIDIA Corporation\NvTray",
                        "NVIDIA NvTray",
                        "StartOnLogin",
                        0)
                ]),
            new(
                "HideXGpuTrayIcon",
                "HideXGpuTrayIcon",
                NvidiaTweakGroup.NvidiaSettings,
                [DwordTarget(ServiceTweakPath, "Service NVTweak", "HideXGpuTrayIcon", 1)]),
            new(
                "ShowTrayIcon",
                "ShowTrayIcon",
                NvidiaTweakGroup.NvidiaSettings,
                [
                    DwordTarget(
                        @"SOFTWARE\NVIDIA Corporation\Global\CoProcManager",
                        "CoProcManager",
                        "ShowTrayIcon",
                        0)
                ]),
            new(
                "DisplayPowerSaving",
                "DisplayPowerSaving",
                NvidiaTweakGroup.NvidiaSettings,
                [
                    DwordTarget(ServiceTweakPath, "Service NVTweak", "DisplayPowerSaving", 0),
                    DwordTarget(GlobalTweakPath, "Global NVTweak", "DisplayPowerSaving", 0)
                ]),
            ClassDword(NvidiaTweakGroup.NvidiaSettings, "EnableRuntimePowerManagement", 0x00000000),
            new(
                "RmProfilingAdminOnly",
                "RmProfilingAdminOnly",
                NvidiaTweakGroup.NvidiaSettings,
                [
                    ClassTarget("RmProfilingAdminOnly", 0),
                    DwordTarget(ServiceTweakPath, "Service NVTweak", "RmProfilingAdminOnly", 0)
                ]),
            new(
                "EnableHDAudioD3Cold",
                "EnableHDAudioD3Cold",
                NvidiaTweakGroup.NvidiaSettings,
                [DwordTarget(DriverServicePath, "nvlddmkm", "EnableHDAudioD3Cold", 0)]),
            ClassDword(NvidiaTweakGroup.NvidiaSettings, "RmDisableHwFaultBuffer", 0x00000001),
            ClassDword(NvidiaTweakGroup.NvidiaSettings, "RMDisablePerIntrDPCQueueing", 0x00000001),
            ClassDword(NvidiaTweakGroup.NvidiaSettings, "RMElcg", 0x55555555),
            ClassDword(NvidiaTweakGroup.NvidiaSettings, "RMBlcg", 0x11111111),
            ClassDword(NvidiaTweakGroup.NvidiaSettings, "RMElpg", 0x00000FFF),
            ClassDword(NvidiaTweakGroup.NvidiaSettings, "RMSlcg", 0x0003FFF3),
            ClassDword(NvidiaTweakGroup.NvidiaSettings, "RMFspg", 0x0000000F),
            ClassDword(NvidiaTweakGroup.NvidiaSettings, "RMGC6Feature", 0x000AAAAA),
            ClassDword(NvidiaTweakGroup.NvidiaSettings, "RMGC6Parameters", 0x00000055),
            ClassDword(NvidiaTweakGroup.NvidiaSettings, "RMDidleFeatureGC5", 0x02AA8AAA),
            ClassDword(NvidiaTweakGroup.NvidiaSettings, "RMHotPlugSupportDisable", 0x00000001),
            ClassDword(NvidiaTweakGroup.NvidiaSettings, "RmFbsrPagedDMA", 0x00000001),
            ClassDword(NvidiaTweakGroup.NvidiaSettings, "RMDisablePostL2Compression", 0x00000001),
            ClassDword(NvidiaTweakGroup.NvidiaSettings, "RmRcWatchdog", 0x00000000),
            ClassDword(NvidiaTweakGroup.NvidiaSettings, "RmLogonRC", 0x00000000),
            ClassDword(NvidiaTweakGroup.NvidiaSettings, "RMIntrDetailedLogs", 0x00000000),
            ClassDword(NvidiaTweakGroup.NvidiaSettings, "RMCtxswLog", 0x00000000),
            ClassDword(NvidiaTweakGroup.NvidiaSettings, "RMNvLog", 0x00000000),
            ClassDword(NvidiaTweakGroup.NvidiaSettings, "RMSuppressGPIOIntrErrLog", 0x00000000),
            new(
                "LogDisableMasks",
                "LogDisableMasks",
                NvidiaTweakGroup.NvidiaSettings,
                [
                    BinaryTarget(
                        DriverParametersPath,
                        "nvlddmkm Parameters",
                        "LogDisableMasks",
                        "00ffff0f01ffff0f02ffff0f03ffff0f04ffff0f05ffff0f06ffff0f07ffff0f08ffff0f09ffff0f0affff0f0bffff0f0cffff0f0dffff0f0effff0f0fffff0f10ffff0f11ffff0f12ffff0f13ffff0f14ffff0f15ffff0f16ffff0f00ffff1f01ffff1f02ffff1f03ffff1f04ffff1f05ffff1f06ffff1f07ffff1f08ffff1f09ffff1f0affff1f0bffff1f0cffff1f0dffff1f0effff1f0fffff1f00ffff2f01ffff2f02ffff2f03ffff2f04ffff2f05ffff2f06ffff2f07ffff2f08ffff2f09ffff2f0affff2f0bffff2f0cffff2f0dffff2f0effff2f0fffff2f00ffff3f01ffff3f02ffff3f03ffff3f04ffff3f05ffff3f06ffff3f07ffff3f")
                ]),
            new(
                "LogWarningEntries",
                "LogWarningEntries",
                NvidiaTweakGroup.NvidiaSettings,
                [DwordTarget(DriverParametersPath, "nvlddmkm Parameters", "LogWarningEntries", 0)]),
            new(
                "LogPagingEntries",
                "LogPagingEntries",
                NvidiaTweakGroup.NvidiaSettings,
                [DwordTarget(DriverParametersPath, "nvlddmkm Parameters", "LogPagingEntries", 0)]),
            new(
                "LogEventEntries",
                "LogEventEntries",
                NvidiaTweakGroup.NvidiaSettings,
                [DwordTarget(DriverParametersPath, "nvlddmkm Parameters", "LogEventEntries", 0)]),
            new(
                "LogErrorEntries",
                "LogErrorEntries",
                NvidiaTweakGroup.NvidiaSettings,
                [DwordTarget(DriverParametersPath, "nvlddmkm Parameters", "LogErrorEntries", 0)]),
            ClassDword(NvidiaTweakGroup.NvidiaSettings, "RMUsbcDebugMode", 0x00000000),
            ClassDword(NvidiaTweakGroup.NvidiaSettings, "RMDisableFeatureDisablement", 0x00000000),
            ClassDword(NvidiaTweakGroup.NvidiaSettings, "RmBreakonRC", 0x00000000),
            ClassDword(NvidiaTweakGroup.NvidiaSettings, "RMDebugSetSMCMode", 0x00000000),
            ClassDword(NvidiaTweakGroup.NvidiaSettings, "RMDisableLRCCoalescing", 0x00000001),
            ClassDword(NvidiaTweakGroup.NvidiaSettings, "RmDisableRegistryCaching", 0x00000001),
            ClassDword(NvidiaTweakGroup.NvidiaSettings, "D3PCLatency", 0x00000001),
            ClassDword(NvidiaTweakGroup.NvidiaSettings, "EnableMsHybrid", 0x00000000),
            ClassDword(NvidiaTweakGroup.NvidiaSettings, "RMDisableIntrIllegalCompstatAccess", 0x00000001),
            ClassDword(NvidiaTweakGroup.NvidiaSettings, "SetPanelRefreshRate", 0x00000000),
            ClassDword(NvidiaTweakGroup.NvidiaSettings, "RMDisableNoncontigAlloc", 0x00000001),

            ClassDword(NvidiaTweakGroup.RmPowerFeature, "RmPowerFeature", 0x55455555),
            ClassDword(NvidiaTweakGroup.RmPowerFeature, "RmPowerFeature2", 0x05555555),

            new(
                "SendTelemetryData",
                "SendTelemetryData",
                NvidiaTweakGroup.Telemetry,
                [DwordTarget(DriverStartupPath, "nvlddmkm Startup", "SendTelemetryData", 0)]),
            new(
                "EnableRID44231",
                "EnableRID44231",
                NvidiaTweakGroup.Telemetry,
                [
                    DwordTarget(
                        @"SOFTWARE\NVIDIA Corporation\Global\FTS",
                        "NVIDIA FTS",
                        "EnableRID44231",
                        0)
                ]),
            new(
                "EnableRID64640",
                "EnableRID64640",
                NvidiaTweakGroup.Telemetry,
                [
                    DwordTarget(
                        @"SOFTWARE\NVIDIA Corporation\Global\FTS",
                        "NVIDIA FTS",
                        "EnableRID64640",
                        0)
                ]),
            new(
                "EnableRID66610",
                "EnableRID66610",
                NvidiaTweakGroup.Telemetry,
                [
                    DwordTarget(
                        @"SOFTWARE\NVIDIA Corporation\Global\FTS",
                        "NVIDIA FTS",
                        "EnableRID66610",
                        0)
                ]),
            new(
                "OptInOrOutPreference",
                "OptInOrOutPreference",
                NvidiaTweakGroup.Telemetry,
                [
                    DwordTarget(
                        @"SOFTWARE\NVIDIA Corporation\NvControlPanel2\Client",
                        "NVIDIA Control Panel",
                        "OptInOrOutPreference",
                        0)
                ]),

            ClassDword(NvidiaTweakGroup.Ecc, "RMEnableL1ECC", 0x00000000),
            ClassDword(NvidiaTweakGroup.Ecc, "RMEnableSMECC", 0x00000000),
            ClassDword(NvidiaTweakGroup.Ecc, "RMEnableSHMECC", 0x00000000),
            ClassDword(NvidiaTweakGroup.Ecc, "RMAssertOnEccErrors", 0x00000000),
            ClassDword(NvidiaTweakGroup.Ecc, "RM1441072", 0x00000000),

            ClassDword(NvidiaTweakGroup.Hdcp, "RMHdcpKeyglobZero", 0x00000001),
            ClassDword(NvidiaTweakGroup.Hdcp, "RmDisableHdcp22", 0x00000001)
        };

        return definitions;
    }
}
