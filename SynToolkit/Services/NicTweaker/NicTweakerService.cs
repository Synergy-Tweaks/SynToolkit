#nullable enable

using Microsoft.Win32;
using SynToolkit.Services;
using System.Diagnostics;
using System.Management;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.IO;

namespace SynToolkit.Services.NicTweaker;

public enum NicTweakSection
{
    AdapterAdvanced,
    PowerSaving,
    GlobalTcp,
    Rss,
    Interface,
    Offload
}

public enum NicTweakBackend
{
    Registry,
    NetAdapterRss,
    NetIpInterface,
    NetOffloadGlobal
}

public sealed record NicTweakOption(string Display, string Value);

public sealed class NicTweakDefinition
{
    public required string Id { get; init; }
    public required NicTweakSection Section { get; init; }
    public required string Label { get; init; }
    public required NicTweakBackend Backend { get; init; }
    public string? RegistryPathTemplate { get; init; }
    public string? RegistryValueName { get; init; }
    public string? RssProperty { get; init; }
    public string? InterfaceProperty { get; init; }
    public string? OffloadProperty { get; init; }
    public bool IsDword { get; init; }
    public bool SupportsDelete { get; init; }
    public bool AllowFreeText { get; init; }
    public IReadOnlyList<NicTweakOption> Options { get; init; } = Array.Empty<NicTweakOption>();
}

public sealed class NicAdapterInfo
{
    public required string InterfaceDescription { get; init; }
    public required string InterfaceAlias { get; init; }
    public required Guid InterfaceGuid { get; init; }
    public required string NetCfgInstanceId { get; init; }
    public string? PnpDeviceId { get; init; }
    public string? ClassKeyPath { get; set; }
    public string? Vendor { get; set; }
    public override string ToString() => InterfaceDescription;
}

public sealed class NicTweakReadState
{
    public required string TweakId { get; init; }
    public string CurrentRaw { get; init; } = string.Empty;
    public string CurrentDisplay { get; init; } = string.Empty;
    public bool IsUnsupported { get; init; }
}

public sealed class NicApplyItemResult
{
    public required string TweakId { get; init; }
    public required string Label { get; init; }
    public required string Status { get; init; } // Success, Skipped, Failed
    public required string Message { get; init; }
}

public sealed class NicApplyBatchResult
{
    public List<NicApplyItemResult> Items { get; } = [];
}

public sealed class NicSnapshotEntry
{
    public required string TweakId { get; init; }
    public required string Path { get; init; }
    public required string ValueName { get; init; }
    public required bool DidNotExist { get; init; }
    public string? PreviousValue { get; init; }
    public bool IsDword { get; init; }
}

public sealed class NicSnapshot
{
    public required string Id { get; init; }
    public required DateTimeOffset CreatedUtc { get; init; }
    public required string Label { get; init; }
    public string? AdapterGuid { get; init; }
    public List<NicSnapshotEntry> Entries { get; init; } = [];
}

public sealed class NicPreset
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public Dictionary<string, string?> Values { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class NicExportConfig
{
    public string Format { get; set; } = "networktweaker-config";
    public int SchemaVersion { get; set; } = 1;
    public string AppVersion { get; set; } = "1.8.0";
    public DateTimeOffset ExportedUtc { get; set; } = DateTimeOffset.UtcNow;
    public string WindowsBuild { get; set; } = Environment.OSVersion.Version.ToString();
    public string? Label { get; set; }
    public NicExportAdapter? Adapter { get; set; }
    public List<NicExportTweak> Tweaks { get; set; } = [];
}

public sealed class NicExportAdapter
{
    public string? Description { get; set; }
    public string? InterfaceGuid { get; set; }
    public string? NetCfgInstanceId { get; set; }
    public string? Vendor { get; set; }
}

public sealed class NicExportTweak
{
    public required string Id { get; set; }
    public string Section { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string State { get; set; } = "Set";
    public string? Value { get; set; }
}

public sealed class NicImportDiffRow
{
    public required string Id { get; init; }
    public required string Label { get; init; }
    public required string CurrentDisplay { get; init; }
    public required string ImportedDisplay { get; init; }
    public required string? ImportedDesired { get; init; }
    public bool Changed { get; init; }
}

public sealed class NicImportPreview
{
    public required NicExportConfig Config { get; init; }
    public List<NicImportDiffRow> Rows { get; init; } = [];
    public List<string> Skipped { get; init; } = [];
    public string? AdapterWarning { get; init; }
}

public interface INicTweakerService
{
    IReadOnlyList<NicTweakDefinition> Catalog { get; }
    string DataRoot { get; }
    Task EnsureInitializedAsync();
    Task<IReadOnlyList<NicAdapterInfo>> GetAdaptersAsync(CancellationToken cancellationToken = default);
    Task<IDictionary<string, NicTweakReadState>> ReadCurrentAsync(NicAdapterInfo adapter, string addressFamily, CancellationToken cancellationToken = default);
    Task<NicApplyBatchResult> ApplyAsync(NicAdapterInfo adapter, string addressFamily, IReadOnlyDictionary<string, string?> desired, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<NicSnapshot>> ListSnapshotsAsync(CancellationToken cancellationToken = default);
    Task BackupNowAsync(NicAdapterInfo adapter, string label, CancellationToken cancellationToken = default);
    Task RestoreSnapshotAsync(string id, CancellationToken cancellationToken = default);
    Task RevertLastAsync(CancellationToken cancellationToken = default);
    Task ExportConfigAsync(NicAdapterInfo adapter, string addressFamily, string path, CancellationToken cancellationToken = default);
    Task<NicImportPreview> BuildImportPreviewAsync(string path, NicAdapterInfo adapter, string addressFamily, CancellationToken cancellationToken = default);
    Task<NicApplyBatchResult> ApplyImportAsync(NicAdapterInfo adapter, string addressFamily, IEnumerable<NicImportDiffRow> rows, CancellationToken cancellationToken = default);
    Task<RestorePointCreateResult> CreateRestorePointAsync(string description, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> ListPresetNamesAsync(CancellationToken cancellationToken = default);
    Task<NicPreset?> LoadPresetAsync(string presetName, CancellationToken cancellationToken = default);
}

public sealed class NicTweakerService : INicTweakerService
{
    private const long MaxImportBytes = 2 * 1024 * 1024;
    private const string DataFolder = "SynToolkit\\NicTweaker";
    private static readonly Regex PlaceholderTokenRegex = new(@"\{[A-Za-z][A-Za-z0-9_]*\}", RegexOptions.Compiled);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };
    private readonly IRestorePointService _restorePoints;

    public NicTweakerService(IRestorePointService restorePoints)
    {
        _restorePoints = restorePoints;
        Catalog = BuildCatalog();
    }

    public IReadOnlyList<NicTweakDefinition> Catalog { get; }

    public string DataRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        DataFolder);

    private string BackupsDir => Path.Combine(DataRoot, "Backups");
    private string PresetsDir => Path.Combine(DataRoot, "Presets");
    private string ExportsDir => Path.Combine(DataRoot, "Exports");
    private string LogsDir => Path.Combine(DataRoot, "Logs");
    private string LogPath => Path.Combine(LogsDir, "app.log");

    public async Task EnsureInitializedAsync()
    {
        Directory.CreateDirectory(DataRoot);
        Directory.CreateDirectory(BackupsDir);
        Directory.CreateDirectory(PresetsDir);
        Directory.CreateDirectory(ExportsDir);
        Directory.CreateDirectory(LogsDir);
        await RefreshBundledPresetsAsync();
    }

    public async Task<IReadOnlyList<string>> ListPresetNamesAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync();
        return await Task.Run(() =>
        {
            return Directory.EnumerateFiles(PresetsDir, "*.json")
                .Select(static file => Path.GetFileNameWithoutExtension(file))
                .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }, cancellationToken);
    }

    public async Task<IReadOnlyList<NicAdapterInfo>> GetAdaptersAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync();
        return await Task.Run(() =>
        {
            List<NicAdapterInfo> list = [];
            using ManagementObjectSearcher searcher = new(
                "SELECT Name, NetConnectionID, GUID, PNPDeviceID FROM Win32_NetworkAdapter WHERE PhysicalAdapter = True AND NetConnectionStatus IS NOT NULL");

            foreach (ManagementObject item in searcher.Get())
            {
                cancellationToken.ThrowIfCancellationRequested();
                string? guidText = item["GUID"]?.ToString();
                if (!Guid.TryParse(guidText, out Guid guid))
                {
                    continue;
                }

                string name = item["Name"]?.ToString() ?? guidText ?? "Adapter";
                string alias = item["NetConnectionID"]?.ToString() ?? name;
                string netCfg = guidText ?? guid.ToString();
                string? pnp = item["PNPDeviceID"]?.ToString();

                NicAdapterInfo adapter = new()
                {
                    InterfaceDescription = name,
                    InterfaceAlias = alias,
                    InterfaceGuid = guid,
                    NetCfgInstanceId = netCfg,
                    PnpDeviceId = pnp,
                    Vendor = DetectVendor(name, pnp),
                };

                adapter.ClassKeyPath = FindAdapterClassKeyPath(adapter.NetCfgInstanceId);
                list.Add(adapter);
            }

            return (IReadOnlyList<NicAdapterInfo>)list
                .OrderBy(static item => item.InterfaceDescription, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }, cancellationToken);
    }

    public async Task<IDictionary<string, NicTweakReadState>> ReadCurrentAsync(
        NicAdapterInfo adapter,
        string addressFamily,
        CancellationToken cancellationToken = default)
    {
        Dictionary<string, NicTweakReadState> result = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string?> offload = [];
        Dictionary<string, string?> iface = [];
        Dictionary<string, string?> rss = [];
        bool offloadUnavailable = false;
        bool interfaceUnavailable = false;
        bool rssUnavailable = false;

        try
        {
            offload = await ReadOffloadAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            offloadUnavailable = true;
            await AppendLogAsync($"Failed reading net offload settings: {ex.Message}");
        }

        try
        {
            iface = await ReadNetIpInterfaceAsync(adapter, addressFamily, cancellationToken);
        }
        catch (Exception ex)
        {
            interfaceUnavailable = true;
            await AppendLogAsync($"Failed reading interface settings: {ex.Message}");
        }

        try
        {
            rss = await ReadRssAsync(adapter, cancellationToken);
        }
        catch (Exception ex)
        {
            rssUnavailable = true;
            await AppendLogAsync($"Failed reading RSS settings: {ex.Message}");
        }

        foreach (NicTweakDefinition definition in Catalog)
        {
            cancellationToken.ThrowIfCancellationRequested();
            NicTweakReadState state = definition.Backend switch
            {
                NicTweakBackend.Registry => ReadRegistry(definition, adapter),
                NicTweakBackend.NetOffloadGlobal when offloadUnavailable => BuildUnsupportedState(definition.Id),
                NicTweakBackend.NetIpInterface when interfaceUnavailable => BuildUnsupportedState(definition.Id),
                NicTweakBackend.NetAdapterRss when rssUnavailable => BuildUnsupportedState(definition.Id),
                NicTweakBackend.NetOffloadGlobal => ReadOffload(definition, offload),
                NicTweakBackend.NetIpInterface => ReadInterface(definition, iface),
                NicTweakBackend.NetAdapterRss => ReadRss(definition, rss),
                _ => BuildUnsupportedState(definition.Id)
            };

            result[definition.Id] = state;
        }

        return result;
    }

    public async Task<NicApplyBatchResult> ApplyAsync(
        NicAdapterInfo adapter,
        string addressFamily,
        IReadOnlyDictionary<string, string?> desired,
        CancellationToken cancellationToken = default)
    {
        NicApplyBatchResult result = new();
        List<NicSnapshotEntry> snapshotEntries = [];

        foreach (NicTweakDefinition definition in Catalog.Where(static item => item.Backend == NicTweakBackend.Registry))
        {
            if (!desired.ContainsKey(definition.Id))
            {
                continue;
            }

            string path;
            try
            {
                path = ResolveRegistryPath(definition, adapter);
            }
            catch (NotSupportedException)
            {
                continue;
            }
            bool exists = RegistryValueExists(path, definition.RegistryValueName!);
            string? previous = exists ? GetRegistryValueAsString(path, definition.RegistryValueName!) : null;
            snapshotEntries.Add(new NicSnapshotEntry
            {
                TweakId = definition.Id,
                Path = path,
                ValueName = definition.RegistryValueName!,
                DidNotExist = !exists,
                PreviousValue = previous,
                IsDword = definition.IsDword
            });
        }

        if (snapshotEntries.Count > 0)
        {
            await SaveSnapshotAsync(new NicSnapshot
            {
                Id = $"chg-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}"[..36],
                CreatedUtc = DateTimeOffset.UtcNow,
                Label = "NIC Tweaker apply",
                AdapterGuid = adapter.InterfaceGuid.ToString(),
                Entries = snapshotEntries
            }, cancellationToken);
        }

        foreach (NicTweakDefinition definition in Catalog)
        {
            if (!desired.TryGetValue(definition.Id, out string? desiredDisplay))
            {
                continue;
            }

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                await ApplyOneAsync(definition, adapter, addressFamily, desiredDisplay, cancellationToken);
                result.Items.Add(new NicApplyItemResult
                {
                    TweakId = definition.Id,
                    Label = definition.Label,
                    Status = "Success",
                    Message = "Applied"
                });
            }
            catch (NotSupportedException notSupported)
            {
                result.Items.Add(new NicApplyItemResult
                {
                    TweakId = definition.Id,
                    Label = definition.Label,
                    Status = "Skipped",
                    Message = notSupported.Message
                });
            }
            catch (Exception ex)
            {
                result.Items.Add(new NicApplyItemResult
                {
                    TweakId = definition.Id,
                    Label = definition.Label,
                    Status = "Failed",
                    Message = ex.Message
                });
            }
        }

        await AppendLogAsync("Apply finished.");
        return result;
    }

    public async Task<IReadOnlyList<NicSnapshot>> ListSnapshotsAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync();
        List<NicSnapshot> snapshots = [];
        foreach (string file in Directory.EnumerateFiles(BackupsDir, "*.json"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                string json = await File.ReadAllTextAsync(file, cancellationToken);
                NicSnapshot? snapshot = JsonSerializer.Deserialize<NicSnapshot>(json, JsonOptions);
                if (snapshot is not null)
                {
                    snapshots.Add(snapshot);
                }
            }
            catch
            {
                // Skip malformed snapshot files.
            }
        }

        return snapshots.OrderByDescending(static item => item.CreatedUtc).ToList();
    }

    public async Task BackupNowAsync(NicAdapterInfo adapter, string label, CancellationToken cancellationToken = default)
    {
        List<NicSnapshotEntry> entries = [];
        foreach (NicTweakDefinition definition in Catalog.Where(static item => item.Backend == NicTweakBackend.Registry))
        {
            string path;
            try
            {
                path = ResolveRegistryPath(definition, adapter);
            }
            catch (NotSupportedException)
            {
                continue;
            }
            bool exists = RegistryValueExists(path, definition.RegistryValueName!);
            entries.Add(new NicSnapshotEntry
            {
                TweakId = definition.Id,
                Path = path,
                ValueName = definition.RegistryValueName!,
                DidNotExist = !exists,
                PreviousValue = exists ? GetRegistryValueAsString(path, definition.RegistryValueName!) : null,
                IsDword = definition.IsDword
            });
        }

        await SaveSnapshotAsync(new NicSnapshot
        {
            Id = $"manual-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}"[..40],
            CreatedUtc = DateTimeOffset.UtcNow,
            Label = string.IsNullOrWhiteSpace(label) ? "Manual backup" : label.Trim(),
            AdapterGuid = adapter.InterfaceGuid.ToString(),
            Entries = entries
        }, cancellationToken);
    }

    public async Task RestoreSnapshotAsync(string id, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<NicSnapshot> snapshots = await ListSnapshotsAsync(cancellationToken);
        NicSnapshot? snapshot = snapshots.FirstOrDefault(item => item.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (snapshot is null)
        {
            throw new InvalidOperationException("Snapshot not found.");
        }

        foreach (NicSnapshotEntry entry in snapshot.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entry.DidNotExist)
            {
                DeleteRegistryValue(entry.Path, entry.ValueName);
                continue;
            }

            if (entry.IsDword)
            {
                int value = int.TryParse(entry.PreviousValue, out int parsed) ? parsed : 0;
                SetRegistryDword(entry.Path, entry.ValueName, value);
            }
            else
            {
                SetRegistryString(entry.Path, entry.ValueName, entry.PreviousValue ?? string.Empty);
            }
        }
    }

    public async Task RevertLastAsync(CancellationToken cancellationToken = default)
    {
        NicSnapshot? snapshot = (await ListSnapshotsAsync(cancellationToken)).FirstOrDefault();
        if (snapshot is null)
        {
            throw new InvalidOperationException("No snapshot available.");
        }

        await RestoreSnapshotAsync(snapshot.Id, cancellationToken);
    }

    public async Task ExportConfigAsync(
        NicAdapterInfo adapter,
        string addressFamily,
        string path,
        CancellationToken cancellationToken = default)
    {
        IDictionary<string, NicTweakReadState> current = await ReadCurrentAsync(adapter, addressFamily, cancellationToken);
        NicExportConfig config = new()
        {
            Adapter = new NicExportAdapter
            {
                Description = adapter.InterfaceDescription,
                InterfaceGuid = adapter.InterfaceGuid.ToString(),
                NetCfgInstanceId = adapter.NetCfgInstanceId,
                Vendor = adapter.Vendor
            }
        };

        foreach (NicTweakDefinition definition in Catalog)
        {
            current.TryGetValue(definition.Id, out NicTweakReadState? state);
            string? currentRaw = state?.CurrentRaw;
            bool notSet = state is null
                          || state.IsUnsupported
                          || string.IsNullOrWhiteSpace(currentRaw);

            config.Tweaks.Add(new NicExportTweak
            {
                Id = definition.Id,
                Section = definition.Section.ToString(),
                Label = definition.Label,
                State = notSet ? "NotSet" : "Set",
                Value = notSet ? null : currentRaw
            });
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string json = JsonSerializer.Serialize(config, JsonOptions);
        await File.WriteAllTextAsync(path, json, new UTF8Encoding(false), cancellationToken);
    }

    public async Task<NicImportPreview> BuildImportPreviewAsync(
        string path,
        NicAdapterInfo adapter,
        string addressFamily,
        CancellationToken cancellationToken = default)
    {
        NicExportConfig config = await LoadConfigAsync(path, cancellationToken);
        IDictionary<string, NicTweakReadState> current = await ReadCurrentAsync(adapter, addressFamily, cancellationToken);
        Dictionary<string, NicTweakDefinition> known = Catalog.ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);
        List<NicImportDiffRow> rows = [];
        List<string> skipped = [];

        foreach (NicExportTweak tweak in config.Tweaks)
        {
            if (!known.TryGetValue(tweak.Id, out NicTweakDefinition? definition))
            {
                skipped.Add($"{tweak.Id}: unknown tweak id");
                continue;
            }

            if (!current.TryGetValue(definition.Id, out NicTweakReadState? state))
            {
                skipped.Add($"{definition.Label}: unavailable");
                continue;
            }

            if (state.IsUnsupported)
            {
                skipped.Add($"{definition.Label}: unsupported on selected adapter");
                continue;
            }

            string currentDisplay = state.CurrentDisplay;
            string importedDisplay = tweak.State.Equals("NotSet", StringComparison.OrdinalIgnoreCase)
                ? "(not set)"
                : (tweak.Value ?? "(not set)");
            string? desired = tweak.State.Equals("NotSet", StringComparison.OrdinalIgnoreCase)
                ? null
                : tweak.Value;

            bool changed = !string.Equals(state.CurrentRaw ?? state.CurrentDisplay, tweak.Value ?? string.Empty, StringComparison.OrdinalIgnoreCase)
                || tweak.State.Equals("NotSet", StringComparison.OrdinalIgnoreCase);

            rows.Add(new NicImportDiffRow
            {
                Id = definition.Id,
                Label = definition.Label,
                CurrentDisplay = currentDisplay,
                ImportedDisplay = importedDisplay,
                ImportedDesired = desired,
                Changed = changed
            });
        }

        string? warning = null;
        if (config.Adapter is not null
            && !string.Equals(config.Adapter.InterfaceGuid, adapter.InterfaceGuid.ToString(), StringComparison.OrdinalIgnoreCase)
            && !string.Equals(config.Adapter.NetCfgInstanceId, adapter.NetCfgInstanceId, StringComparison.OrdinalIgnoreCase))
        {
            warning = $"Exported from '{config.Adapter.Description}'. Applying to '{adapter.InterfaceDescription}'. Unsupported tweaks will be skipped.";
        }

        return new NicImportPreview
        {
            Config = config,
            Rows = rows,
            Skipped = skipped,
            AdapterWarning = warning
        };
    }

    public async Task<NicApplyBatchResult> ApplyImportAsync(
        NicAdapterInfo adapter,
        string addressFamily,
        IEnumerable<NicImportDiffRow> rows,
        CancellationToken cancellationToken = default)
    {
        Dictionary<string, string?> desired = rows.ToDictionary(item => item.Id, item => item.ImportedDesired, StringComparer.OrdinalIgnoreCase);
        return await ApplyAsync(adapter, addressFamily, desired, cancellationToken);
    }

    public Task<RestorePointCreateResult> CreateRestorePointAsync(string description, CancellationToken cancellationToken = default)
    {
        return _restorePoints.CreateAsync(description, cancellationToken);
    }

    public async Task<NicPreset?> LoadPresetAsync(string presetName, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync();
        string path = Path.Combine(PresetsDir, $"{presetName.ToLowerInvariant()}.json");
        if (!File.Exists(path))
        {
            return null;
        }

        string json = await File.ReadAllTextAsync(path, cancellationToken);
        return ParsePresetJson(json);
    }

    public static NicPreset ParsePresetJson(string json)
    {
        JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;
        NicPreset preset = new()
        {
            Name = root.TryGetProperty("name", out JsonElement name) ? name.GetString() ?? string.Empty : string.Empty,
            Description = root.TryGetProperty("description", out JsonElement description) ? description.GetString() ?? string.Empty : string.Empty
        };

        if (root.TryGetProperty("values", out JsonElement values) && values.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in values.EnumerateObject())
            {
                preset.Values[property.Name] = property.Value.ValueKind == JsonValueKind.Null
                    ? null
                    : property.Value.ToString();
            }
        }

        return preset;
    }

    public static NicExportConfig ParseConfigJson(string json)
    {
        NicExportConfig? config = JsonSerializer.Deserialize<NicExportConfig>(json, JsonOptions);
        if (config is null)
        {
            throw new InvalidOperationException("Config file is empty.");
        }

        if (!string.Equals(config.Format, "networktweaker-config", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Unsupported config format '{config.Format}'.");
        }

        if (config.SchemaVersion > 1 || config.SchemaVersion < 1)
        {
            throw new InvalidOperationException($"Unsupported schema version {config.SchemaVersion}.");
        }

        return config;
    }

    private async Task<NicExportConfig> LoadConfigAsync(string path, CancellationToken cancellationToken)
    {
        FileInfo info = new(path);
        if (!info.Exists)
        {
            throw new FileNotFoundException("Config file not found.", path);
        }

        if (info.Length > MaxImportBytes)
        {
            throw new InvalidOperationException("Config file is too large.");
        }

        string json = await File.ReadAllTextAsync(path, cancellationToken);
        return ParseConfigJson(json);
    }

    private async Task SaveSnapshotAsync(NicSnapshot snapshot, CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync();
        string path = Path.Combine(BackupsDir, $"{snapshot.Id}.json");
        string json = JsonSerializer.Serialize(snapshot, JsonOptions);
        await File.WriteAllTextAsync(path, json, cancellationToken);
    }

    private async Task RefreshBundledPresetsAsync()
    {
        string baseDir = AppContext.BaseDirectory;
        string source = Path.Combine(baseDir, "Assets", "NicTweaker", "Presets");
        if (!Directory.Exists(source))
        {
            return;
        }

        foreach (string file in Directory.EnumerateFiles(source, "*.json"))
        {
            string name = Path.GetFileName(file);
            string destination = Path.Combine(PresetsDir, name);
            bool overwrite = name.Equals("intel.json", StringComparison.OrdinalIgnoreCase)
                             || name.Equals("realtek.json", StringComparison.OrdinalIgnoreCase);
            if (File.Exists(destination) && !overwrite)
            {
                continue;
            }

            File.Copy(file, destination, overwrite);
        }

        await Task.CompletedTask;
    }

    private async Task ApplyOneAsync(
        NicTweakDefinition definition,
        NicAdapterInfo adapter,
        string addressFamily,
        string? desiredDisplay,
        CancellationToken cancellationToken)
    {
        string? desired = ResolveDesiredValue(definition, desiredDisplay);
        switch (definition.Backend)
        {
            case NicTweakBackend.Registry:
                ApplyRegistry(definition, adapter, desired);
                break;
            case NicTweakBackend.NetOffloadGlobal:
                if (string.IsNullOrWhiteSpace(definition.OffloadProperty))
                {
                    throw new InvalidOperationException("Missing offload property.");
                }
                await RunPowerShellAsync(
                    $"$ErrorActionPreference='Stop'; Set-NetOffloadGlobalSetting -{definition.OffloadProperty} {SanitizePowerShellWord(desired ?? "Default")}",
                    cancellationToken);
                break;
            case NicTweakBackend.NetIpInterface:
                if (string.IsNullOrWhiteSpace(definition.InterfaceProperty))
                {
                    throw new InvalidOperationException("Missing interface property.");
                }
                string alias = EscapeSingleQuoted(adapter.InterfaceAlias);
                string safeValue = EscapeSingleQuoted(desired ?? string.Empty);
                await RunPowerShellAsync(
                    $"$ErrorActionPreference='Stop'; Set-NetIPInterface -InterfaceAlias '{alias}' -AddressFamily {SanitizePowerShellWord(addressFamily)} -{definition.InterfaceProperty} '{safeValue}'",
                    cancellationToken);
                break;
            case NicTweakBackend.NetAdapterRss:
                string aliasRss = EscapeSingleQuoted(adapter.InterfaceAlias);
                await ApplyRssAsync(definition, aliasRss, desired, cancellationToken);
                break;
            default:
                throw new NotSupportedException("Unsupported backend.");
        }
    }

    private async Task ApplyRssAsync(NicTweakDefinition definition, string alias, string? desired, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(definition.RssProperty))
        {
            throw new InvalidOperationException("Missing RSS property.");
        }

        if (definition.RssProperty.Equals("Enabled", StringComparison.OrdinalIgnoreCase))
        {
            string flag = string.Equals(desired, "true", StringComparison.OrdinalIgnoreCase) ? "$true" : "$false";
            await RunPowerShellAsync(
                $"$ErrorActionPreference='Stop'; Set-NetAdapterRss -Name '{alias}' -Enabled:{flag}",
                cancellationToken);
            return;
        }

        if (!int.TryParse(desired, out int number))
        {
            throw new InvalidOperationException("RSS value must be numeric.");
        }

        await RunPowerShellAsync(
            $"$ErrorActionPreference='Stop'; Set-NetAdapterRss -Name '{alias}' -{definition.RssProperty} {number}",
            cancellationToken);
    }

    private void ApplyRegistry(NicTweakDefinition definition, NicAdapterInfo adapter, string? desired)
    {
        string path = ResolveRegistryPath(definition, adapter);
        string valueName = definition.RegistryValueName ?? throw new InvalidOperationException("Missing value name.");
        if (string.IsNullOrWhiteSpace(desired) && definition.SupportsDelete)
        {
            DeleteRegistryValue(path, valueName);
            return;
        }

        if (definition.IsDword)
        {
            if (!int.TryParse(desired, out int value))
            {
                throw new InvalidOperationException($"{definition.Label} requires a numeric value.");
            }
            SetRegistryDword(path, valueName, value);
        }
        else
        {
            SetRegistryString(path, valueName, desired ?? string.Empty);
        }
    }

    private NicTweakReadState ReadRegistry(NicTweakDefinition definition, NicAdapterInfo adapter)
    {
        string path;
        try
        {
            path = ResolveRegistryPath(definition, adapter);
        }
        catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException)
        {
            return BuildUnsupportedState(definition.Id);
        }

        string valueName = definition.RegistryValueName ?? string.Empty;
        if (!RegistryValueExists(path, valueName))
        {
            return new NicTweakReadState
            {
                TweakId = definition.Id,
                CurrentRaw = string.Empty,
                CurrentDisplay = "(not set)"
            };
        }

        string raw = GetRegistryValueAsString(path, valueName) ?? string.Empty;
        NicTweakOption? mapped = definition.Options.FirstOrDefault(static option => false);
        mapped = definition.Options.FirstOrDefault(option =>
            string.Equals(option.Value, raw, StringComparison.OrdinalIgnoreCase));

        return new NicTweakReadState
        {
            TweakId = definition.Id,
            CurrentRaw = raw,
            CurrentDisplay = mapped?.Display ?? raw
        };
    }

    private static NicTweakReadState BuildUnsupportedState(string tweakId) => new()
    {
        TweakId = tweakId,
        IsUnsupported = true,
        CurrentDisplay = "Unsupported"
    };

    private static NicTweakReadState ReadOffload(NicTweakDefinition definition, Dictionary<string, string?> offload)
    {
        string raw = string.Empty;
        if (!string.IsNullOrWhiteSpace(definition.OffloadProperty))
        {
            offload.TryGetValue(definition.OffloadProperty, out string? found);
            raw = found ?? string.Empty;
        }

        string display = raw;
        NicTweakOption? option = definition.Options.FirstOrDefault(item =>
            item.Value.Equals(raw, StringComparison.OrdinalIgnoreCase));
        if (option is not null)
        {
            display = option.Display;
        }

        return new NicTweakReadState
        {
            TweakId = definition.Id,
            CurrentRaw = raw,
            CurrentDisplay = string.IsNullOrWhiteSpace(display) ? "(not set)" : display
        };
    }

    private static NicTweakReadState ReadInterface(NicTweakDefinition definition, Dictionary<string, string?> iface)
    {
        string raw = string.Empty;
        if (!string.IsNullOrWhiteSpace(definition.InterfaceProperty))
        {
            iface.TryGetValue(definition.InterfaceProperty, out string? found);
            raw = found ?? string.Empty;
        }

        return new NicTweakReadState
        {
            TweakId = definition.Id,
            CurrentRaw = raw,
            CurrentDisplay = string.IsNullOrWhiteSpace(raw) ? "(not set)" : raw
        };
    }

    private static NicTweakReadState ReadRss(NicTweakDefinition definition, Dictionary<string, string?> rss)
    {
        string raw = string.Empty;
        if (!string.IsNullOrWhiteSpace(definition.RssProperty))
        {
            rss.TryGetValue(definition.RssProperty, out string? found);
            raw = found ?? string.Empty;
        }

        string display = raw;
        NicTweakOption? option = definition.Options.FirstOrDefault(item =>
            item.Value.Equals(raw, StringComparison.OrdinalIgnoreCase));
        if (option is not null)
        {
            display = option.Display;
        }

        return new NicTweakReadState
        {
            TweakId = definition.Id,
            CurrentRaw = raw,
            CurrentDisplay = string.IsNullOrWhiteSpace(display) ? "(not set)" : display
        };
    }

    private async Task<Dictionary<string, string?>> ReadOffloadAsync(CancellationToken cancellationToken)
    {
        string json = await RunPowerShellAsync(@"
$ErrorActionPreference='Continue'
$g = Get-NetOffloadGlobalSetting
[ordered]@{
  ReceiveSideScaling = ('' + $g.ReceiveSideScaling)
  ReceiveSegmentCoalescing = ('' + $g.ReceiveSegmentCoalescing)
  Chimney = ('' + $g.Chimney)
  TaskOffload = ('' + $g.TaskOffload)
  PacketCoalescingFilter = ('' + $g.PacketCoalescingFilter)
} | ConvertTo-Json -Compress
", cancellationToken);

        Dictionary<string, string?> result = new(StringComparer.OrdinalIgnoreCase);
        using JsonDocument doc = JsonDocument.Parse(ExtractJsonObject(json) ?? "{}");
        foreach (JsonProperty property in doc.RootElement.EnumerateObject())
        {
            result[property.Name] = property.Value.ToString();
        }
        return result;
    }

    private async Task<Dictionary<string, string?>> ReadNetIpInterfaceAsync(
        NicAdapterInfo adapter,
        string addressFamily,
        CancellationToken cancellationToken)
    {
        string alias = EscapeSingleQuoted(adapter.InterfaceAlias);
        string json = await RunPowerShellAsync($@"
$ErrorActionPreference='Continue'
$i = Get-NetIPInterface -InterfaceAlias '{alias}' -AddressFamily {SanitizePowerShellWord(addressFamily)} -ErrorAction SilentlyContinue | Select-Object -First 1
if ($null -eq $i) {{ '{{}}' }} else {{
  [ordered]@{{
    AutomaticMetric = ('' + $i.AutomaticMetric)
    WeakHostSend = ('' + $i.WeakHostSend)
    WeakHostReceive = ('' + $i.WeakHostReceive)
    EcnMarking = ('' + $i.EcnMarking)
  }} | ConvertTo-Json -Compress
}}
", cancellationToken);

        Dictionary<string, string?> result = new(StringComparer.OrdinalIgnoreCase);
        using JsonDocument doc = JsonDocument.Parse(ExtractJsonObject(json) ?? "{}");
        foreach (JsonProperty property in doc.RootElement.EnumerateObject())
        {
            result[property.Name] = property.Value.ToString();
        }
        return result;
    }

    private async Task<Dictionary<string, string?>> ReadRssAsync(NicAdapterInfo adapter, CancellationToken cancellationToken)
    {
        string alias = EscapeSingleQuoted(adapter.InterfaceAlias);
        string json = await RunPowerShellAsync($@"
$ErrorActionPreference='Continue'
$r = Get-NetAdapterRss -Name '{alias}' -ErrorAction SilentlyContinue | Select-Object -First 1
if ($null -eq $r) {{ '{{}}' }} else {{
  [ordered]@{{
    Enabled = (if($r.Enabled) {{ 'true' }} else {{ 'false' }})
    NumberOfReceiveQueues = ('' + $r.NumberOfReceiveQueues)
    BaseProcessorNumber = ('' + $r.BaseProcessorNumber)
  }} | ConvertTo-Json -Compress
}}
", cancellationToken);

        Dictionary<string, string?> result = new(StringComparer.OrdinalIgnoreCase);
        using JsonDocument doc = JsonDocument.Parse(ExtractJsonObject(json) ?? "{}");
        foreach (JsonProperty property in doc.RootElement.EnumerateObject())
        {
            result[property.Name] = property.Value.ToString();
        }
        return result;
    }

    private async Task<string> RunPowerShellAsync(string script, CancellationToken cancellationToken)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = "powershell.exe",
            Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command -",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        using Process process = new() { StartInfo = startInfo };
        StringBuilder stdout = new();
        StringBuilder stderr = new();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };

        if (!process.Start())
        {
            throw new InvalidOperationException("Failed to start PowerShell.");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.StandardInput.WriteAsync(script.AsMemory(), cancellationToken);
        await process.StandardInput.FlushAsync(cancellationToken);
        process.StandardInput.Close();
        await process.WaitForExitAsync(cancellationToken);

        string err = stderr.ToString().Trim();
        if (process.ExitCode != 0)
        {
            string output = stdout.ToString().Trim();
            string message = !string.IsNullOrWhiteSpace(err)
                ? err
                : !string.IsNullOrWhiteSpace(output)
                    ? output
                    : $"PowerShell exited with code {process.ExitCode}.";
            throw new InvalidOperationException(message);
        }

        if (!string.IsNullOrWhiteSpace(err))
        {
            await AppendLogAsync(err);
        }

        return stdout.ToString().Trim();
    }

    private async Task AppendLogAsync(string message)
    {
        string line = $"{DateTime.Now:HH:mm:ss} {message}{Environment.NewLine}";
        await File.AppendAllTextAsync(LogPath, line);
        App.logger.Info("[NIC Tweaker] " + message);
    }

    private static string DetectVendor(string name, string? pnp)
    {
        string text = $"{name} {pnp}".ToLowerInvariant();
        if (text.Contains("intel"))
        {
            return "Intel";
        }
        if (text.Contains("realtek") || text.Contains("rtl"))
        {
            return "Realtek";
        }
        return "Other";
    }

    private static string? ExtractJsonObject(string text)
    {
        int start = text.IndexOf('{');
        int end = text.LastIndexOf('}');
        return start >= 0 && end > start ? text[start..(end + 1)] : null;
    }

    private static string EscapeSingleQuoted(string text) => text.Replace("'", "''", StringComparison.Ordinal);

    private static string SanitizePowerShellWord(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        if (!text.All(static c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.'))
        {
            throw new InvalidOperationException("Unsafe PowerShell argument.");
        }

        return text;
    }

    private string ResolveRegistryPath(NicTweakDefinition definition, NicAdapterInfo adapter)
    {
        string template = definition.RegistryPathTemplate ?? string.Empty;
        if (template.Contains("{AdapterClassKey}", StringComparison.Ordinal)
            && string.IsNullOrWhiteSpace(adapter.ClassKeyPath))
        {
            throw new NotSupportedException($"Adapter registry key not available for {definition.Id}.");
        }

        string resolved = template
            .Replace("{AdapterClassKey}", adapter.ClassKeyPath ?? string.Empty, StringComparison.Ordinal)
            .Replace("{AdapterGuid}", adapter.InterfaceGuid.ToString(), StringComparison.Ordinal);

        if (PlaceholderTokenRegex.IsMatch(resolved))
        {
            throw new InvalidOperationException($"Unresolved registry path for {definition.Id}.");
        }

        return resolved;
    }

    private static bool RegistryValueExists(string fullPath, string valueName)
    {
        using RegistryKey? key = OpenRegistryKey(fullPath, writable: false);
        return key is not null && key.GetValueNames().Contains(valueName, StringComparer.OrdinalIgnoreCase);
    }

    private static string? GetRegistryValueAsString(string fullPath, string valueName)
    {
        using RegistryKey? key = OpenRegistryKey(fullPath, writable: false);
        object? value = key?.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        return value switch
        {
            null => null,
            string text => text,
            int number => number.ToString(),
            _ => value.ToString()
        };
    }

    private static void SetRegistryDword(string fullPath, string valueName, int value)
    {
        using RegistryKey key = OpenOrCreateRegistryKey(fullPath);
        key.SetValue(valueName, value, RegistryValueKind.DWord);
    }

    private static void SetRegistryString(string fullPath, string valueName, string value)
    {
        using RegistryKey key = OpenOrCreateRegistryKey(fullPath);
        key.SetValue(valueName, value, RegistryValueKind.String);
    }

    private static void DeleteRegistryValue(string fullPath, string valueName)
    {
        using RegistryKey? key = OpenRegistryKey(fullPath, writable: true);
        key?.DeleteValue(valueName, false);
    }

    private static RegistryKey OpenOrCreateRegistryKey(string fullPath)
    {
        (RegistryKey root, string relative) = SplitRegistryPath(fullPath);
        return root.CreateSubKey(relative, true)
            ?? throw new InvalidOperationException($"Unable to create registry path {fullPath}.");
    }

    private static RegistryKey? OpenRegistryKey(string fullPath, bool writable)
    {
        (RegistryKey root, string relative) = SplitRegistryPath(fullPath);
        return root.OpenSubKey(relative, writable);
    }

    private static (RegistryKey Root, string Relative) SplitRegistryPath(string fullPath)
    {
        string normalized = fullPath.Trim();
        if (normalized.StartsWith("HKLM\\", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("HKEY_LOCAL_MACHINE\\", StringComparison.OrdinalIgnoreCase))
        {
            int idx = normalized.IndexOf('\\');
            return (Registry.LocalMachine, normalized[(idx + 1)..]);
        }

        if (normalized.StartsWith("HKCU\\", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("HKEY_CURRENT_USER\\", StringComparison.OrdinalIgnoreCase))
        {
            int idx = normalized.IndexOf('\\');
            return (Registry.CurrentUser, normalized[(idx + 1)..]);
        }

        return (Registry.LocalMachine, normalized);
    }

    private static string? ResolveStoredOption(NicTweakDefinition definition, string? displayOrValue)
    {
        if (displayOrValue is null)
        {
            return null;
        }

        NicTweakOption? option = definition.Options.FirstOrDefault(item =>
            item.Display.Equals(displayOrValue, StringComparison.OrdinalIgnoreCase)
            || item.Value.Equals(displayOrValue, StringComparison.OrdinalIgnoreCase));
        return option?.Value ?? displayOrValue;
    }

    private static string? ResolveDesiredValue(NicTweakDefinition definition, string? displayOrValue)
    {
        if (string.IsNullOrWhiteSpace(displayOrValue))
        {
            return definition.SupportsDelete ? null : displayOrValue;
        }

        string? resolved = ResolveStoredOption(definition, displayOrValue);
        if (resolved is null)
        {
            return null;
        }

        if (resolved.Equals("delete", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (resolved.StartsWith("0x", StringComparison.OrdinalIgnoreCase) && int.TryParse(resolved[2..], System.Globalization.NumberStyles.HexNumber, null, out int asHex))
        {
            return asHex.ToString();
        }

        return resolved;
    }

    private string? FindAdapterClassKeyPath(string netCfgInstanceId)
    {
        const string root = @"SYSTEM\CurrentControlSet\Control\Class\{4D36E972-E325-11CE-BFC1-08002bE10318}";
        using RegistryKey? classRoot = Registry.LocalMachine.OpenSubKey(root, false);
        if (classRoot is null)
        {
            return null;
        }

        foreach (string sub in classRoot.GetSubKeyNames())
        {
            if (!sub.All(char.IsDigit))
            {
                continue;
            }

            using RegistryKey? key = classRoot.OpenSubKey(sub, false);
            string? instance = key?.GetValue("NetCfgInstanceId") as string;
            if (string.Equals(instance, netCfgInstanceId, StringComparison.OrdinalIgnoreCase))
            {
                return $@"HKLM\{root}\{sub}";
            }
        }

        return null;
    }

    public static bool IsElevated()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static IReadOnlyList<NicTweakDefinition> BuildCatalog()
    {
        static NicTweakOption[] OffOn() =>
        [
            new("Disabled", "0"),
            new("Enabled", "1")
        ];

        static NicTweakOption[] OffOnTxRx() =>
        [
            new("0 - Disabled", "0"),
            new("1 - Tx Enabled", "1"),
            new("2 - Rx Enabled", "2"),
            new("3 - Rx and Tx Enabled", "3")
        ];

        List<NicTweakDefinition> list = [];
        void Reg(string id, NicTweakSection section, string label, string valueName, bool dword, IReadOnlyList<NicTweakOption> options, bool supportsDelete = true, bool allowFreeText = false)
        {
            list.Add(new NicTweakDefinition
            {
                Id = id,
                Section = section,
                Label = label,
                Backend = NicTweakBackend.Registry,
                RegistryPathTemplate = "{AdapterClassKey}",
                RegistryValueName = valueName,
                IsDword = dword,
                SupportsDelete = supportsDelete,
                AllowFreeText = allowFreeText,
                Options = options
            });
        }

        Reg("adv.flow", NicTweakSection.AdapterAdvanced, "FlowControl", "*FlowControl", false, OffOnTxRx(), true);
        Reg("adv.intmod", NicTweakSection.AdapterAdvanced, "InterruptModeration", "*InterruptModeration", false, OffOn(), true);
        Reg("pow.eee", NicTweakSection.PowerSaving, "EEE", "*EEE", false, OffOn(), true);
        Reg("pow.green", NicTweakSection.PowerSaving, "Green Ethernet", "EnableGreenEthernet", true, OffOn(), true);
        Reg("pow.advancedeee", NicTweakSection.PowerSaving, "AdvancedEEE", "AdvancedEEE", false, OffOn(), true);
        Reg("pow.powersaving", NicTweakSection.PowerSaving, "PowerSavingMode", "PowerSavingMode", false, OffOn(), true);
        Reg("pow.gigalite", NicTweakSection.PowerSaving, "GigaLite", "GigaLite", false, OffOn(), true);
        Reg("pow.autodisablegigabit", NicTweakSection.PowerSaving, "AutoDisableGigabit", "AutoDisableGigabit", false, OffOn(), true);
        Reg("pow.s5wol", NicTweakSection.PowerSaving, "S5WakeOnLan", "S5WakeOnLan", false, OffOn(), true);
        Reg("pow.pme", NicTweakSection.PowerSaving, "EnablePME", "EnablePME", true, OffOn(), true);

        list.Add(new NicTweakDefinition
        {
            Id = "global.tcpack",
            Section = NicTweakSection.GlobalTcp,
            Label = "TcpAckFrequency",
            Backend = NicTweakBackend.Registry,
            RegistryPathTemplate = @"HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters",
            RegistryValueName = "TcpAckFrequency",
            IsDword = true,
            SupportsDelete = true,
            AllowFreeText = true
        });

        list.Add(new NicTweakDefinition
        {
            Id = "global.rss",
            Section = NicTweakSection.Offload,
            Label = "ReceiveSideScaling",
            Backend = NicTweakBackend.NetOffloadGlobal,
            OffloadProperty = "ReceiveSideScaling",
            Options = [new NicTweakOption("Disabled", "Disabled"), new NicTweakOption("Enabled", "Enabled")]
        });

        list.Add(new NicTweakDefinition
        {
            Id = "global.rsc",
            Section = NicTweakSection.Offload,
            Label = "ReceiveSegmentCoalescing",
            Backend = NicTweakBackend.NetOffloadGlobal,
            OffloadProperty = "ReceiveSegmentCoalescing",
            Options = [new NicTweakOption("Disabled", "Disabled"), new NicTweakOption("Enabled", "Enabled")]
        });

        list.Add(new NicTweakDefinition
        {
            Id = "rss.status",
            Section = NicTweakSection.Rss,
            Label = "RSS",
            Backend = NicTweakBackend.NetAdapterRss,
            RssProperty = "Enabled",
            Options = [new NicTweakOption("Enable", "true"), new NicTweakOption("Disable", "false")]
        });

        list.Add(new NicTweakDefinition
        {
            Id = "rss.queues",
            Section = NicTweakSection.Rss,
            Label = "NumberOfReceiveQueues",
            Backend = NicTweakBackend.NetAdapterRss,
            RssProperty = "NumberOfReceiveQueues",
            AllowFreeText = true
        });

        list.Add(new NicTweakDefinition
        {
            Id = "iface.automaticmetric",
            Section = NicTweakSection.Interface,
            Label = "AutomaticMetric",
            Backend = NicTweakBackend.NetIpInterface,
            InterfaceProperty = "AutomaticMetric",
            Options = [new NicTweakOption("Enabled", "Enabled"), new NicTweakOption("Disabled", "Disabled")]
        });

        list.Add(new NicTweakDefinition
        {
            Id = "iface.weakhostsend",
            Section = NicTweakSection.Interface,
            Label = "WeakHostSend",
            Backend = NicTweakBackend.NetIpInterface,
            InterfaceProperty = "WeakHostSend",
            Options = [new NicTweakOption("Enabled", "Enabled"), new NicTweakOption("Disabled", "Disabled")]
        });

        list.Add(new NicTweakDefinition
        {
            Id = "iface.weakhostreceive",
            Section = NicTweakSection.Interface,
            Label = "WeakHostReceive",
            Backend = NicTweakBackend.NetIpInterface,
            InterfaceProperty = "WeakHostReceive",
            Options = [new NicTweakOption("Enabled", "Enabled"), new NicTweakOption("Disabled", "Disabled")]
        });

        return list;
    }
}
