#nullable enable

using Microsoft.Win32;
using SynToolkit.Models.GpuDrivers;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Principal;
using System.Text;

namespace SynToolkit.Services.GpuDrivers;

public enum NvidiaTweakGroup
{
    NvidiaSettings,
    RmPowerFeature,
    Telemetry,
    Ecc,
    Hdcp
}

public enum NvidiaTweakRiskLevel
{
    Safe,
    Caution,
    Advanced
}

public enum NvidiaTweakValueState
{
    NotSet,
    Different,
    Partial,
    Applied
}

public sealed record NvidiaTweakMetadata(
    string DisplayName,
    string Description,
    NvidiaTweakRiskLevel RiskLevel);

public sealed record NvidiaRegistryTarget(
    RegistryHive Hive,
    string Path,
    string LocationLabel,
    string ValueName,
    RegistryValueKind Kind,
    object ExpectedValue);

public sealed record NvidiaTweakDefinition(
    string Id,
    NvidiaTweakGroup Group,
    IReadOnlyList<NvidiaRegistryTarget> Targets)
{
    public string LocationText =>
        string.Join(" + ", Targets.Select(static target => target.LocationLabel).Distinct(StringComparer.Ordinal));

    public string RecommendedValueText
    {
        get
        {
            NvidiaRegistryTarget target = Targets[0];
            return target.Kind switch
            {
                RegistryValueKind.DWord => $"0x{ConvertRegistryDword(target.ExpectedValue):X8}",
                RegistryValueKind.Binary when target.ExpectedValue is byte[] bytes => $"{bytes.Length} bytes",
                _ => target.ExpectedValue.ToString() ?? string.Empty
            };
        }
    }

    public bool SupportsCustomValue =>
        Targets.All(static target => target.Kind == RegistryValueKind.DWord);

    private static uint ConvertRegistryDword(object value) => value switch
    {
        int signed => unchecked((uint)signed),
        uint unsigned => unsigned,
        long signed => unchecked((uint)signed),
        _ => Convert.ToUInt32(value, CultureInfo.InvariantCulture)
    };
}

public sealed record NvidiaTweakCatalogItem(
    NvidiaTweakDefinition Definition,
    NvidiaTweakMetadata Metadata);

public sealed record NvidiaTweakValueSnapshot(
    NvidiaTweakValueState State,
    string CurrentValueText,
    bool RestartPending);

public sealed record NvidiaTweakCheckResult(
    bool Success,
    string Message,
    bool IsElevated,
    bool HasNvidiaGpu,
    string ActiveGpuName,
    string DriverClassPath,
    bool MatchedByInf,
    IReadOnlyDictionary<string, NvidiaTweakValueSnapshot> Values);

public sealed record NvidiaActionResult(
    bool Success,
    string Message);

public sealed record NvidiaRegExportResult(
    bool Success,
    string Message,
    string? FilePath);

public sealed record NvidiaResolvedTarget(
    RegistryHive Hive,
    string HiveLabel,
    string Path,
    string ValueName,
    RegistryValueKind Kind);

public interface INvidiaPerformanceTweaksService
{
    IReadOnlyList<NvidiaTweakCatalogItem> Catalog { get; }
    IReadOnlyList<NvidiaTweakDefinition> Definitions { get; }
    IReadOnlyList<NvidiaTweakDefinition> GetDefinitions(NvidiaTweakGroup group);
    Task<NvidiaTweakCheckResult> CheckAllAsync(GpuDeviceInfo? preferredGpu, CancellationToken cancellationToken = default);
    Task<(bool Success, string Message, NvidiaTweakValueSnapshot? Value)> CheckItemAsync(
        GpuDeviceInfo? preferredGpu, string id, CancellationToken cancellationToken = default);
    Task<NvidiaActionResult> ApplyItemAsync(GpuDeviceInfo? preferredGpu, string id, CancellationToken cancellationToken = default);
    Task<NvidiaActionResult> RevertItemAsync(GpuDeviceInfo? preferredGpu, string id, CancellationToken cancellationToken = default);
    Task<NvidiaActionResult> SetItemValueAsync(
        GpuDeviceInfo? preferredGpu, string id, string valueText, CancellationToken cancellationToken = default);
    Task<NvidiaActionResult> ApplyGroupAsync(GpuDeviceInfo? preferredGpu, NvidiaTweakGroup group, CancellationToken cancellationToken = default);
    Task<NvidiaActionResult> RevertGroupAsync(GpuDeviceInfo? preferredGpu, NvidiaTweakGroup group, CancellationToken cancellationToken = default);
    Task<NvidiaActionResult> RestoreAllOriginalsAsync(GpuDeviceInfo? preferredGpu, CancellationToken cancellationToken = default);
    Task<NvidiaRegExportResult> ExportAppliedTweaksAsRegAsync(GpuDeviceInfo? preferredGpu, CancellationToken cancellationToken = default);
    Task<(bool Success, string Message, IReadOnlyList<NvidiaResolvedTarget> Targets)> ResolveTargetsAsync(
        GpuDeviceInfo? preferredGpu, string id, CancellationToken cancellationToken = default);
}

public sealed class NvidiaPerformanceTweaksService : INvidiaPerformanceTweaksService
{
    private const string DriverClassToken = "$NVIDIA_DRIVER_CLASS$";
    private const string DisplayClassRoot =
        @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";
    private const string ServiceTweakPath = @"SYSTEM\CurrentControlSet\Services\nvlddmkm\Global\NVTweak";
    private const string GlobalTweakPath = @"SOFTWARE\NVIDIA Corporation\Global\NVTweak";
    private const string DriverServicePath = @"SYSTEM\CurrentControlSet\Services\nvlddmkm";
    private const string DriverParametersPath = @"SYSTEM\CurrentControlSet\Services\nvlddmkm\Parameters";
    private const string DriverStartupPath = @"SYSTEM\CurrentControlSet\Services\nvlddmkm\Global\Startup";

    private readonly IGpuDriverCatalogService _gpuDriverCatalogService;
    private readonly NvidiaTweaksBackupStore _backupStore = new();

    private sealed class RegistryContext : IDisposable
    {
        private readonly Dictionary<RegistryHive, RegistryKey> _baseKeys = [];

        public RegistryKey GetBaseKey(RegistryHive hive)
        {
            if (!_baseKeys.TryGetValue(hive, out RegistryKey? key))
            {
                key = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
                _baseKeys.Add(hive, key);
            }

            return key;
        }

        public void Dispose()
        {
            foreach (RegistryKey key in _baseKeys.Values)
            {
                key.Dispose();
            }
        }
    }

    private sealed record DriverClassEntry(string Path, string InfPath, string Description);
    private sealed record ResolvedDriverContext(
        bool Success,
        string Message,
        bool HasNvidiaGpu,
        string ClassPath,
        string GpuName,
        bool MatchedByInf);

    public NvidiaPerformanceTweaksService(IGpuDriverCatalogService gpuDriverCatalogService)
    {
        _gpuDriverCatalogService = gpuDriverCatalogService;
        Definitions = CreateDefinitions();
        Catalog = Definitions
            .Select(definition => new NvidiaTweakCatalogItem(definition, BuildMetadata(definition)))
            .ToList();
    }

    public IReadOnlyList<NvidiaTweakCatalogItem> Catalog { get; }
    public IReadOnlyList<NvidiaTweakDefinition> Definitions { get; }

    public IReadOnlyList<NvidiaTweakDefinition> GetDefinitions(NvidiaTweakGroup group) =>
        Definitions.Where(definition => definition.Group == group).ToList();

    public async Task<NvidiaTweakCheckResult> CheckAllAsync(
        GpuDeviceInfo? preferredGpu,
        CancellationToken cancellationToken = default)
    {
        try
        {
            ResolvedDriverContext context = await ResolveDriverContextAsync(preferredGpu, cancellationToken);
            if (!context.Success)
            {
                return new NvidiaTweakCheckResult(
                    false,
                    context.Message,
                    IsCurrentProcessElevated(),
                    context.HasNvidiaGpu,
                    context.GpuName,
                    context.ClassPath,
                    context.MatchedByInf,
                    new Dictionary<string, NvidiaTweakValueSnapshot>());
            }

            Dictionary<string, NvidiaTweakValueSnapshot> values = await Task.Run(() =>
            {
                using RegistryContext registryContext = new();
                DateTimeOffset bootUtc = GetSystemBootUtc();
                return Definitions.ToDictionary(
                    definition => definition.Id,
                    definition =>
                    {
                        NvidiaTweakValueSnapshot snapshot = ReadSnapshot(
                            registryContext,
                            context.ClassPath,
                            definition,
                            bootUtc);
                        return snapshot;
                    },
                    StringComparer.Ordinal);
            }, cancellationToken);

            return new NvidiaTweakCheckResult(
                true,
                string.Empty,
                IsCurrentProcessElevated(),
                true,
                context.GpuName,
                context.ClassPath,
                context.MatchedByInf,
                values);
        }
        catch (Exception exception)
        {
            App.logger.Error(exception, "[GPU] NVIDIA tweak verification failed.");
            return new NvidiaTweakCheckResult(
                false,
                exception.Message,
                IsCurrentProcessElevated(),
                false,
                string.Empty,
                string.Empty,
                false,
                new Dictionary<string, NvidiaTweakValueSnapshot>());
        }
    }

    public async Task<(bool Success, string Message, NvidiaTweakValueSnapshot? Value)> CheckItemAsync(
        GpuDeviceInfo? preferredGpu,
        string id,
        CancellationToken cancellationToken = default)
    {
        NvidiaTweakDefinition? definition = Definitions.FirstOrDefault(item => item.Id == id);
        if (definition is null)
        {
            return (false, "The selected NVIDIA tweak was not found.", null);
        }

        ResolvedDriverContext context = await ResolveDriverContextAsync(preferredGpu, cancellationToken);
        if (!context.Success)
        {
            return (false, context.Message, null);
        }

        try
        {
            NvidiaTweakValueSnapshot snapshot = await Task.Run(() =>
            {
                using RegistryContext registryContext = new();
                return ReadSnapshot(registryContext, context.ClassPath, definition, GetSystemBootUtc());
            }, cancellationToken);
            return (true, string.Empty, snapshot);
        }
        catch (Exception exception)
        {
            return (false, exception.Message, null);
        }
    }

    public Task<NvidiaActionResult> ApplyItemAsync(
        GpuDeviceInfo? preferredGpu,
        string id,
        CancellationToken cancellationToken = default)
        => ApplyAsync(preferredGpu, Definitions.Where(definition => definition.Id == id), cancellationToken);

    public Task<NvidiaActionResult> ApplyGroupAsync(
        GpuDeviceInfo? preferredGpu,
        NvidiaTweakGroup group,
        CancellationToken cancellationToken = default)
        => ApplyAsync(preferredGpu, Definitions.Where(definition => definition.Group == group), cancellationToken);

    public async Task<NvidiaActionResult> RevertItemAsync(
        GpuDeviceInfo? preferredGpu,
        string id,
        CancellationToken cancellationToken = default)
    {
        NvidiaTweakDefinition? definition = Definitions.FirstOrDefault(item => item.Id == id);
        if (definition is null)
        {
            return new NvidiaActionResult(false, "The selected NVIDIA tweak was not found.");
        }

        return await RevertAsync(preferredGpu, [definition], cancellationToken);
    }

    public Task<NvidiaActionResult> RevertGroupAsync(
        GpuDeviceInfo? preferredGpu,
        NvidiaTweakGroup group,
        CancellationToken cancellationToken = default)
        => RevertAsync(preferredGpu, Definitions.Where(definition => definition.Group == group), cancellationToken);

    public async Task<NvidiaActionResult> SetItemValueAsync(
        GpuDeviceInfo? preferredGpu,
        string id,
        string valueText,
        CancellationToken cancellationToken = default)
    {
        if (!IsCurrentProcessElevated())
        {
            return new NvidiaActionResult(
                false,
                "Administrator access is required to change NVIDIA registry values.");
        }

        NvidiaTweakDefinition? definition = Definitions.FirstOrDefault(item => item.Id == id);
        if (definition is null)
        {
            return new NvidiaActionResult(false, "The selected NVIDIA tweak was not found.");
        }

        if (!definition.SupportsCustomValue)
        {
            return new NvidiaActionResult(false, $"{definition.Id} does not support a custom value.");
        }

        string normalized = valueText.Trim();
        if (normalized.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[2..];
        }

        if (!uint.TryParse(
                normalized,
                NumberStyles.AllowHexSpecifier,
                CultureInfo.InvariantCulture,
                out uint customValue))
        {
            return new NvidiaActionResult(false, "Enter a hexadecimal DWORD value, for example 0x2A.");
        }

        ResolvedDriverContext context = await ResolveDriverContextAsync(preferredGpu, cancellationToken);
        if (!context.Success)
        {
            return new NvidiaActionResult(false, context.Message);
        }

        try
        {
            return await Task.Run(() =>
            {
                using RegistryContext registryContext = new();
                foreach (NvidiaRegistryTarget target in definition.Targets)
                {
                    string resolvedPath = ResolvePath(target.Path, context.ClassPath);
                    string targetKey = CreateTargetKey(target.Hive, resolvedPath, target.ValueName);
                    CaptureOriginalValueIfNeeded(registryContext, target, resolvedPath, targetKey);
                    using RegistryKey key = registryContext.GetBaseKey(target.Hive).CreateSubKey(resolvedPath, writable: true)
                        ?? throw new InvalidOperationException($"Windows could not open {GetHiveLabel(target.Hive)}\\{resolvedPath}.");
                    key.SetValue(target.ValueName, unchecked((int)customValue), RegistryValueKind.DWord);
                }

                foreach (NvidiaRegistryTarget target in definition.Targets)
                {
                    string resolvedPath = ResolvePath(target.Path, context.ClassPath);
                    using RegistryKey? key = registryContext.GetBaseKey(target.Hive).OpenSubKey(resolvedPath, writable: false);
                    object? actual = key?.GetValue(target.ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                    if (actual is null || ConvertRegistryDword(actual) != customValue)
                    {
                        throw new InvalidOperationException($"{definition.Id} could not be verified after writing.");
                    }
                }

                _backupStore.SetLastAppliedUtc(definition.Id, DateTimeOffset.UtcNow);
                return new NvidiaActionResult(true, $"Set {definition.Id} to 0x{customValue:X8}. Restart Windows to make it active.");
            }, cancellationToken);
        }
        catch (Exception exception)
        {
            return new NvidiaActionResult(false, exception.Message);
        }
    }

    public async Task<NvidiaActionResult> RestoreAllOriginalsAsync(
        GpuDeviceInfo? preferredGpu,
        CancellationToken cancellationToken = default)
    {
        if (!IsCurrentProcessElevated())
        {
            return new NvidiaActionResult(
                false,
                "Administrator access is required to restore NVIDIA registry values.");
        }

        ResolvedDriverContext context = await ResolveDriverContextAsync(preferredGpu, cancellationToken);
        if (!context.Success)
        {
            return new NvidiaActionResult(false, context.Message);
        }

        try
        {
            return await Task.Run(() =>
            {
                using RegistryContext registryContext = new();
                int restored = 0;
                foreach (NvidiaTweakDefinition definition in Definitions)
                {
                    foreach (NvidiaRegistryTarget target in definition.Targets)
                    {
                        string resolvedPath = ResolvePath(target.Path, context.ClassPath);
                        string targetKey = CreateTargetKey(target.Hive, resolvedPath, target.ValueName);
                        if (!_backupStore.TryGetOriginal(targetKey, out NvidiaTweaksBackupStore.BackupRegistryValue backup))
                        {
                            continue;
                        }

                        RestoreValueFromBackup(registryContext, target, resolvedPath, backup);
                        restored++;
                    }

                    _backupStore.ClearLastAppliedUtc(definition.Id);
                }

                return restored == 0
                    ? new NvidiaActionResult(true, "No original NVIDIA tweak values were recorded yet.")
                    : new NvidiaActionResult(true, $"Restored {restored} original NVIDIA registry value(s).");
            }, cancellationToken);
        }
        catch (Exception exception)
        {
            return new NvidiaActionResult(false, exception.Message);
        }
    }

    public async Task<NvidiaRegExportResult> ExportAppliedTweaksAsRegAsync(
        GpuDeviceInfo? preferredGpu,
        CancellationToken cancellationToken = default)
    {
        ResolvedDriverContext context = await ResolveDriverContextAsync(preferredGpu, cancellationToken);
        if (!context.Success)
        {
            return new NvidiaRegExportResult(false, context.Message, null);
        }

        try
        {
            return await Task.Run(() =>
            {
                using RegistryContext registryContext = new();
                StringBuilder builder = new();
                builder.AppendLine("Windows Registry Editor Version 5.00");
                builder.AppendLine();

                int exportedTweaks = 0;
                foreach (NvidiaTweakDefinition definition in Definitions)
                {
                    NvidiaTweakValueSnapshot snapshot = ReadSnapshot(
                        registryContext,
                        context.ClassPath,
                        definition,
                        GetSystemBootUtc());
                    if (snapshot.State != NvidiaTweakValueState.Applied)
                    {
                        continue;
                    }

                    exportedTweaks++;
                    foreach (NvidiaRegistryTarget target in definition.Targets)
                    {
                        string resolvedPath = ResolvePath(target.Path, context.ClassPath);
                        using RegistryKey? key = registryContext.GetBaseKey(target.Hive).OpenSubKey(resolvedPath, writable: false);
                        object? value = key?.GetValue(target.ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                        if (value is null)
                        {
                            continue;
                        }

                        builder.AppendLine($"[{ToRegPath(target.Hive, resolvedPath)}]");
                        string valueName = string.IsNullOrEmpty(target.ValueName)
                            ? "@"
                            : $"\"{EscapeRegString(target.ValueName)}\"";
                        builder.AppendLine($"{valueName}={FormatRegValue(value, target.Kind)}");
                        builder.AppendLine();
                    }
                }

                if (exportedTweaks == 0)
                {
                    return new NvidiaRegExportResult(false, "No fully applied tweaks were available to export.", null);
                }

                string directory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "SynToolkit");
                Directory.CreateDirectory(directory);
                string filePath = Path.Combine(directory, $"nvidia-applied-tweaks-{DateTime.Now:yyyyMMdd-HHmmss}.reg");
                File.WriteAllText(filePath, builder.ToString(), Encoding.Unicode);
                return new NvidiaRegExportResult(true, $"Exported applied tweaks to {filePath}", filePath);
            }, cancellationToken);
        }
        catch (Exception exception)
        {
            return new NvidiaRegExportResult(false, exception.Message, null);
        }
    }

    public async Task<(bool Success, string Message, IReadOnlyList<NvidiaResolvedTarget> Targets)> ResolveTargetsAsync(
        GpuDeviceInfo? preferredGpu,
        string id,
        CancellationToken cancellationToken = default)
    {
        NvidiaTweakDefinition? definition = Definitions.FirstOrDefault(item => item.Id == id);
        if (definition is null)
        {
            return (false, "The selected NVIDIA tweak was not found.", []);
        }

        ResolvedDriverContext context = await ResolveDriverContextAsync(preferredGpu, cancellationToken);
        if (!context.Success)
        {
            return (false, context.Message, []);
        }

        IReadOnlyList<NvidiaResolvedTarget> targets = definition.Targets
            .Select(target =>
            {
                string path = ResolvePath(target.Path, context.ClassPath);
                return new NvidiaResolvedTarget(
                    target.Hive,
                    GetHiveLabel(target.Hive),
                    path,
                    target.ValueName,
                    target.Kind);
            })
            .ToList();
        return (true, string.Empty, targets);
    }

    private async Task<NvidiaActionResult> ApplyAsync(
        GpuDeviceInfo? preferredGpu,
        IEnumerable<NvidiaTweakDefinition> selectedDefinitions,
        CancellationToken cancellationToken)
    {
        if (!IsCurrentProcessElevated())
        {
            return new NvidiaActionResult(
                false,
                "Administrator access is required to change NVIDIA registry values.");
        }

        List<NvidiaTweakDefinition> definitions = selectedDefinitions.ToList();
        if (definitions.Count == 0)
        {
            return new NvidiaActionResult(false, "No NVIDIA tweaks were selected.");
        }

        ResolvedDriverContext context = await ResolveDriverContextAsync(preferredGpu, cancellationToken);
        if (!context.Success)
        {
            return new NvidiaActionResult(false, context.Message);
        }

        try
        {
            return await Task.Run(() =>
            {
                using RegistryContext registryContext = new();
                int changed = 0;
                int skipped = 0;
                DateTimeOffset now = DateTimeOffset.UtcNow;
                foreach (NvidiaTweakDefinition definition in definitions)
                {
                    if (ReadSnapshot(registryContext, context.ClassPath, definition, GetSystemBootUtc()).State ==
                        NvidiaTweakValueState.Applied)
                    {
                        skipped++;
                        continue;
                    }

                    foreach (NvidiaRegistryTarget target in definition.Targets)
                    {
                        string resolvedPath = ResolvePath(target.Path, context.ClassPath);
                        string targetKey = CreateTargetKey(target.Hive, resolvedPath, target.ValueName);
                        CaptureOriginalValueIfNeeded(registryContext, target, resolvedPath, targetKey);
                        using RegistryKey key = registryContext.GetBaseKey(target.Hive).CreateSubKey(resolvedPath, writable: true)
                            ?? throw new InvalidOperationException($"Windows could not open {GetHiveLabel(target.Hive)}\\{resolvedPath}.");
                        key.SetValue(target.ValueName, target.ExpectedValue, target.Kind);
                    }

                    if (ReadSnapshot(registryContext, context.ClassPath, definition, GetSystemBootUtc()).State !=
                        NvidiaTweakValueState.Applied)
                    {
                        throw new InvalidOperationException($"{definition.Id} could not be verified after writing.");
                    }

                    _backupStore.SetLastAppliedUtc(definition.Id, now);
                    changed++;
                }

                if (changed == 0)
                {
                    return new NvidiaActionResult(true, "The selected NVIDIA tweaks are already applied.");
                }

                string skippedText = skipped > 0 ? $" {skipped} already-correct tweak(s) were skipped." : string.Empty;
                return new NvidiaActionResult(
                    true,
                    $"Applied {changed} NVIDIA tweak(s).{skippedText} Restart Windows to make them active.");
            }, cancellationToken);
        }
        catch (Exception exception)
        {
            return new NvidiaActionResult(false, exception.Message);
        }
    }

    private async Task<NvidiaActionResult> RevertAsync(
        GpuDeviceInfo? preferredGpu,
        IEnumerable<NvidiaTweakDefinition> selectedDefinitions,
        CancellationToken cancellationToken)
    {
        if (!IsCurrentProcessElevated())
        {
            return new NvidiaActionResult(
                false,
                "Administrator access is required to change NVIDIA registry values.");
        }

        List<NvidiaTweakDefinition> definitions = selectedDefinitions.ToList();
        if (definitions.Count == 0)
        {
            return new NvidiaActionResult(false, "No NVIDIA tweaks were selected.");
        }

        ResolvedDriverContext context = await ResolveDriverContextAsync(preferredGpu, cancellationToken);
        if (!context.Success)
        {
            return new NvidiaActionResult(false, context.Message);
        }

        try
        {
            return await Task.Run(() =>
            {
                using RegistryContext registryContext = new();
                int reverted = 0;
                int fallbackDeletes = 0;
                foreach (NvidiaTweakDefinition definition in definitions)
                {
                    foreach (NvidiaRegistryTarget target in definition.Targets)
                    {
                        string resolvedPath = ResolvePath(target.Path, context.ClassPath);
                        string targetKey = CreateTargetKey(target.Hive, resolvedPath, target.ValueName);
                        if (_backupStore.TryGetOriginal(targetKey, out NvidiaTweaksBackupStore.BackupRegistryValue backup))
                        {
                            RestoreValueFromBackup(registryContext, target, resolvedPath, backup);
                        }
                        else
                        {
                            using RegistryKey? key = registryContext.GetBaseKey(target.Hive).OpenSubKey(resolvedPath, writable: true);
                            key?.DeleteValue(target.ValueName, throwOnMissingValue: false);
                            fallbackDeletes++;
                        }
                    }

                    _backupStore.ClearLastAppliedUtc(definition.Id);
                    reverted++;
                }

                string fallbackText = fallbackDeletes > 0
                    ? $" {fallbackDeletes} value(s) had no original backup and were deleted as fallback."
                    : string.Empty;
                return reverted == 0
                    ? new NvidiaActionResult(true, "The selected NVIDIA tweaks are already reverted.")
                    : new NvidiaActionResult(true, $"Reverted {reverted} NVIDIA tweak(s).{fallbackText}");
            }, cancellationToken);
        }
        catch (Exception exception)
        {
            return new NvidiaActionResult(false, exception.Message);
        }
    }

    private static void RestoreValueFromBackup(
        RegistryContext context,
        NvidiaRegistryTarget target,
        string resolvedPath,
        NvidiaTweaksBackupStore.BackupRegistryValue backup)
    {
        if (!backup.Existed || backup.Kind is null)
        {
            using RegistryKey? key = context.GetBaseKey(target.Hive).OpenSubKey(resolvedPath, writable: true);
            key?.DeleteValue(target.ValueName, throwOnMissingValue: false);
            return;
        }

        object? restoredValue = NvidiaTweaksBackupStore.DeserializeRegistryValue(backup);
        object safeValue = restoredValue ?? string.Empty;
        using RegistryKey writableKey = context.GetBaseKey(target.Hive).CreateSubKey(resolvedPath, writable: true)
            ?? throw new InvalidOperationException($"Windows could not open {GetHiveLabel(target.Hive)}\\{resolvedPath}.");
        writableKey.SetValue(target.ValueName, safeValue, backup.Kind.Value);
    }

    private void CaptureOriginalValueIfNeeded(
        RegistryContext context,
        NvidiaRegistryTarget target,
        string resolvedPath,
        string targetKey)
    {
        if (_backupStore.TryGetOriginal(targetKey, out _))
        {
            return;
        }

        using RegistryKey? key = context.GetBaseKey(target.Hive).OpenSubKey(resolvedPath, writable: false);
        object? value = key?.GetValue(target.ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        RegistryValueKind? kind = value is null ? null : key?.GetValueKind(target.ValueName);
        _backupStore.RememberOriginalIfMissing(
            targetKey,
            value is not null,
            kind,
            value);
    }

    private static string CreateTargetKey(RegistryHive hive, string path, string valueName) =>
        $"{GetHiveLabel(hive)}\\{path}\\{valueName}";

    private static DateTimeOffset GetSystemBootUtc()
    {
        long uptimeMs = Math.Max(0, Environment.TickCount64);
        return DateTimeOffset.UtcNow - TimeSpan.FromMilliseconds(uptimeMs);
    }

    private static bool IsCurrentProcessElevated()
    {
        try
        {
            using WindowsIdentity identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    private async Task<ResolvedDriverContext> ResolveDriverContextAsync(
        GpuDeviceInfo? preferredGpu,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<GpuDeviceInfo> devices = await _gpuDriverCatalogService.GetGpuDevicesAsync();
        List<GpuDeviceInfo> nvidiaDevices = devices
            .Where(device => device.IsNvidia)
            .ToList();
        if (nvidiaDevices.Count == 0)
        {
            return new ResolvedDriverContext(
                false,
                "No NVIDIA GPU was detected on this machine.",
                false,
                string.Empty,
                string.Empty,
                false);
        }

        return await Task.Run(() =>
        {
            using RegistryContext registryContext = new();
            using RegistryKey? root = registryContext
                .GetBaseKey(RegistryHive.LocalMachine)
                .OpenSubKey(DisplayClassRoot, writable: false);
            if (root is null)
            {
                return new ResolvedDriverContext(
                    false,
                    "Windows does not expose the NVIDIA display-driver class registry key.",
                    true,
                    string.Empty,
                    nvidiaDevices[0].DisplayName,
                    false);
            }

            List<DriverClassEntry> classEntries = [];
            foreach (string subKeyName in root.GetSubKeyNames()
                         .Where(static name => name.Length == 4 && name.All(char.IsDigit))
                         .OrderBy(static name => name, StringComparer.Ordinal))
            {
                using RegistryKey? key = root.OpenSubKey(subKeyName, writable: false);
                string provider = key?.GetValue("ProviderName")?.ToString() ?? string.Empty;
                string description = key?.GetValue("DriverDesc")?.ToString() ?? string.Empty;
                if (!provider.StartsWith("NVIDIA", StringComparison.OrdinalIgnoreCase) &&
                    !description.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) &&
                    !description.Contains("GeForce", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                classEntries.Add(new DriverClassEntry(
                    $@"{DisplayClassRoot}\{subKeyName}",
                    key?.GetValue("InfPath")?.ToString() ?? string.Empty,
                    description));
            }

            if (classEntries.Count == 0)
            {
                return new ResolvedDriverContext(
                    false,
                    "An NVIDIA GPU was detected, but no NVIDIA display-driver class registry key was found.",
                    true,
                    string.Empty,
                    nvidiaDevices[0].DisplayName,
                    false);
            }

            GpuDeviceInfo? requested = preferredGpu?.IsNvidia == true
                ? preferredGpu
                : nvidiaDevices.FirstOrDefault();
            DriverClassEntry? matched = null;
            bool matchedByInf = false;
            if (requested is not null && !string.IsNullOrWhiteSpace(requested.DriverInfName))
            {
                matched = classEntries.FirstOrDefault(entry =>
                    entry.InfPath.Equals(requested.DriverInfName, StringComparison.OrdinalIgnoreCase));
                matchedByInf = matched is not null;
            }

            matched ??= classEntries[0];
            GpuDeviceInfo chosenDevice = requested ?? nvidiaDevices[0];
            return new ResolvedDriverContext(
                true,
                string.Empty,
                true,
                matched.Path,
                chosenDevice.DisplayName,
                matchedByInf);
        }, cancellationToken);
    }

    private NvidiaTweakValueSnapshot ReadSnapshot(
        RegistryContext context,
        string classPath,
        NvidiaTweakDefinition definition,
        DateTimeOffset bootUtc)
    {
        int existingCount = 0;
        int matchingCount = 0;
        List<string> currentValues = [];

        foreach (NvidiaRegistryTarget target in definition.Targets)
        {
            string path = ResolvePath(target.Path, classPath);
            using RegistryKey? key = context.GetBaseKey(target.Hive).OpenSubKey(path, writable: false);
            object? actual = key?.GetValue(target.ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            if (actual is null)
            {
                currentValues.Add("Not set");
                continue;
            }

            existingCount++;
            currentValues.Add(FormatRegistryValue(actual, target.Kind));
            if (ValuesMatch(actual, target.ExpectedValue, target.Kind))
            {
                matchingCount++;
            }
        }

        NvidiaTweakValueState state = NvidiaTweakValueState.Different;
        if (matchingCount == definition.Targets.Count)
        {
            state = NvidiaTweakValueState.Applied;
        }
        else if (existingCount == 0)
        {
            state = NvidiaTweakValueState.NotSet;
        }
        else if (existingCount < definition.Targets.Count || matchingCount > 0)
        {
            state = NvidiaTweakValueState.Partial;
        }

        List<string> distinctValues = currentValues.Distinct(StringComparer.Ordinal).ToList();
        string currentText = distinctValues.Count == 1
            ? distinctValues[0]
            : string.Join(" / ", distinctValues);

        DateTimeOffset? appliedUtc = _backupStore.GetLastAppliedUtc(definition.Id);
        bool pendingRestart = state == NvidiaTweakValueState.Applied &&
            appliedUtc.HasValue &&
            appliedUtc.Value > bootUtc;
        return new NvidiaTweakValueSnapshot(state, currentText, pendingRestart);
    }

    private static string FormatRegistryValue(object value, RegistryValueKind kind) => kind switch
    {
        RegistryValueKind.DWord => $"0x{ConvertRegistryDword(value):X8}",
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
        {
            return ConvertRegistryDword(actual) == ConvertRegistryDword(expected);
        }

        return Equals(actual, expected);
    }

    private static uint ConvertRegistryDword(object value) => value switch
    {
        int signed => unchecked((uint)signed),
        uint unsigned => unsigned,
        long signed => unchecked((uint)signed),
        _ => Convert.ToUInt32(value, CultureInfo.InvariantCulture)
    };

    private static string ResolvePath(string path, string classPath) =>
        path == DriverClassToken ? classPath : path;

    private static string GetHiveLabel(RegistryHive hive) => hive switch
    {
        RegistryHive.ClassesRoot => "HKCR",
        RegistryHive.CurrentUser => "HKCU",
        RegistryHive.LocalMachine => "HKLM",
        _ => hive.ToString()
    };

    private static string ToRegPath(RegistryHive hive, string path) => hive switch
    {
        RegistryHive.LocalMachine => $"HKEY_LOCAL_MACHINE\\{path}",
        RegistryHive.CurrentUser => $"HKEY_CURRENT_USER\\{path}",
        RegistryHive.ClassesRoot => $"HKEY_CLASSES_ROOT\\{path}",
        _ => $"{hive}\\{path}"
    };

    private static string EscapeRegString(string text) =>
        text.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);

    private static string FormatRegValue(object value, RegistryValueKind kind)
    {
        return kind switch
        {
            RegistryValueKind.DWord => $"dword:{ConvertRegistryDword(value):x8}",
            RegistryValueKind.Binary when value is byte[] bytes
                => "hex:" + string.Join(",", bytes.Select(static b => b.ToString("x2", CultureInfo.InvariantCulture))),
            _ => $"\"{EscapeRegString(value.ToString() ?? string.Empty)}\""
        };
    }

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
        string id,
        int value) =>
        new(id, group, [ClassTarget(id, value)]);

    private static NvidiaTweakMetadata BuildMetadata(NvidiaTweakDefinition definition)
    {
        return definition.Id switch
        {
            "RMHotPlugSupportDisable" or "RmDisableHdcp22" or "RMHdcpKeyglobZero" =>
                new(definition.Id, "Can affect HDMI/DP hot-plug or protected content playback.", NvidiaTweakRiskLevel.Caution),
            "RMEnableL1ECC" or "RMEnableSMECC" or "RMEnableSHMECC" or "RMAssertOnEccErrors" or "RM1441072" =>
                new(definition.Id, "ECC control on consumer GPUs can reduce performance or trigger driver issues.", NvidiaTweakRiskLevel.Advanced),
            "RMElcg" or "RMBlcg" or "RMElpg" or "RMSlcg" or "RMFspg" or "RMGC6Feature" or "RMDidleFeatureGC5" =>
                new(definition.Id, "Power-gating masks may increase idle power or impact stability on some drivers.", NvidiaTweakRiskLevel.Advanced),
            "RmDisableRegistryCaching" or "LogDisableMasks" =>
                new(definition.Id, "Driver diagnostics and caching behavior tweak; use only when troubleshooting.", NvidiaTweakRiskLevel.Caution),
            _ => new(definition.Id, "NVIDIA registry performance tweak.", NvidiaTweakRiskLevel.Safe)
        };
    }

    private static IReadOnlyList<NvidiaTweakDefinition> CreateDefinitions()
    {
        // Intentionally left out:
        // - NvidiaContainerContextMenu: depended on C:\Windows\Misc\NvContainerON/OFF.bat.
        //   Keeping this UI focused on verifiable registry tweaks only.
        return
        [
            new("ShowDlssIndicator", NvidiaTweakGroup.NvidiaSettings,
            [
                DwordTarget(@"SOFTWARE\NVIDIA Corporation\Global\NGXCore", "NGXCore", "ShowDlssIndicator", 0)
            ]),
            new("NotifyNewDisplayUpdates", NvidiaTweakGroup.NvidiaSettings,
            [
                DwordTarget(RegistryHive.CurrentUser, @"SOFTWARE\NVIDIA Corporation\Global\GFExperience", "GFExperience", "NotifyNewDisplayUpdates", 0)
            ]),
            new("NvDevToolsVisible", NvidiaTweakGroup.NvidiaSettings,
            [
                DwordTarget(ServiceTweakPath, "Service NVTweak", "NvDevToolsVisible", 1)
            ]),
            new("StartOnLogin", NvidiaTweakGroup.NvidiaSettings,
            [
                DwordTarget(@"SOFTWARE\NVIDIA Corporation\NvTray", "NVIDIA NvTray", "StartOnLogin", 0)
            ]),
            new("HideXGpuTrayIcon", NvidiaTweakGroup.NvidiaSettings,
            [
                DwordTarget(ServiceTweakPath, "Service NVTweak", "HideXGpuTrayIcon", 1)
            ]),
            new("ShowTrayIcon", NvidiaTweakGroup.NvidiaSettings,
            [
                DwordTarget(@"SOFTWARE\NVIDIA Corporation\Global\CoProcManager", "CoProcManager", "ShowTrayIcon", 0)
            ]),
            new("DisplayPowerSaving", NvidiaTweakGroup.NvidiaSettings,
            [
                DwordTarget(ServiceTweakPath, "Service NVTweak", "DisplayPowerSaving", 0),
                DwordTarget(GlobalTweakPath, "Global NVTweak", "DisplayPowerSaving", 0)
            ]),
            ClassDword(NvidiaTweakGroup.NvidiaSettings, "EnableRuntimePowerManagement", 0x00000000),
            new("RmProfilingAdminOnly", NvidiaTweakGroup.NvidiaSettings,
            [
                ClassTarget("RmProfilingAdminOnly", 0),
                DwordTarget(ServiceTweakPath, "Service NVTweak", "RmProfilingAdminOnly", 0)
            ]),
            new("EnableHDAudioD3Cold", NvidiaTweakGroup.NvidiaSettings,
            [
                DwordTarget(DriverServicePath, "nvlddmkm", "EnableHDAudioD3Cold", 0)
            ]),
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
            new("LogDisableMasks", NvidiaTweakGroup.NvidiaSettings,
            [
                BinaryTarget(
                    DriverParametersPath,
                    "nvlddmkm Parameters",
                    "LogDisableMasks",
                    "00ffff0f01ffff0f02ffff0f03ffff0f04ffff0f05ffff0f06ffff0f07ffff0f08ffff0f09ffff0f0affff0f0bffff0f0cffff0f0dffff0f0effff0f0fffff0f10ffff0f11ffff0f12ffff0f13ffff0f14ffff0f15ffff0f16ffff0f00ffff1f01ffff1f02ffff1f03ffff1f04ffff1f05ffff1f06ffff1f07ffff1f08ffff1f09ffff1f0affff1f0bffff1f0cffff1f0dffff1f0effff1f0fffff1f00ffff2f01ffff2f02ffff2f03ffff2f04ffff2f05ffff2f06ffff2f07ffff2f08ffff2f09ffff2f0affff2f0bffff2f0cffff2f0dffff2f0effff2f0fffff2f00ffff3f01ffff3f02ffff3f03ffff3f04ffff3f05ffff3f06ffff3f07ffff3f")
            ]),
            new("LogWarningEntries", NvidiaTweakGroup.NvidiaSettings,
            [
                DwordTarget(DriverParametersPath, "nvlddmkm Parameters", "LogWarningEntries", 0)
            ]),
            new("LogPagingEntries", NvidiaTweakGroup.NvidiaSettings,
            [
                DwordTarget(DriverParametersPath, "nvlddmkm Parameters", "LogPagingEntries", 0)
            ]),
            new("LogEventEntries", NvidiaTweakGroup.NvidiaSettings,
            [
                DwordTarget(DriverParametersPath, "nvlddmkm Parameters", "LogEventEntries", 0)
            ]),
            new("LogErrorEntries", NvidiaTweakGroup.NvidiaSettings,
            [
                DwordTarget(DriverParametersPath, "nvlddmkm Parameters", "LogErrorEntries", 0)
            ]),
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
            new("SendTelemetryData", NvidiaTweakGroup.Telemetry,
            [
                DwordTarget(DriverStartupPath, "nvlddmkm Startup", "SendTelemetryData", 0)
            ]),
            new("EnableRID44231", NvidiaTweakGroup.Telemetry,
            [
                DwordTarget(@"SOFTWARE\NVIDIA Corporation\Global\FTS", "NVIDIA FTS", "EnableRID44231", 0)
            ]),
            new("EnableRID64640", NvidiaTweakGroup.Telemetry,
            [
                DwordTarget(@"SOFTWARE\NVIDIA Corporation\Global\FTS", "NVIDIA FTS", "EnableRID64640", 0)
            ]),
            new("EnableRID66610", NvidiaTweakGroup.Telemetry,
            [
                DwordTarget(@"SOFTWARE\NVIDIA Corporation\Global\FTS", "NVIDIA FTS", "EnableRID66610", 0)
            ]),
            new("OptInOrOutPreference", NvidiaTweakGroup.Telemetry,
            [
                DwordTarget(@"SOFTWARE\NVIDIA Corporation\NvControlPanel2\Client", "NVIDIA Control Panel", "OptInOrOutPreference", 0)
            ]),
            ClassDword(NvidiaTweakGroup.Ecc, "RMEnableL1ECC", 0x00000000),
            ClassDword(NvidiaTweakGroup.Ecc, "RMEnableSMECC", 0x00000000),
            ClassDword(NvidiaTweakGroup.Ecc, "RMEnableSHMECC", 0x00000000),
            ClassDword(NvidiaTweakGroup.Ecc, "RMAssertOnEccErrors", 0x00000000),
            ClassDword(NvidiaTweakGroup.Ecc, "RM1441072", 0x00000000),
            ClassDword(NvidiaTweakGroup.Hdcp, "RMHdcpKeyglobZero", 0x00000001),
            ClassDword(NvidiaTweakGroup.Hdcp, "RmDisableHdcp22", 0x00000001)
        ];
    }
}
