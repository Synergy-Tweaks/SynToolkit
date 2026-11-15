#nullable enable

using Microsoft.Win32;
using System.IO;
using System.Text.Json;

namespace SynToolkit.Services.GpuDrivers;

internal sealed class NvidiaTweaksBackupStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _backupPath;
    private readonly object _sync = new();
    private BackupDocument _document;

    public NvidiaTweaksBackupStore()
    {
        string root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SynToolkit");
        Directory.CreateDirectory(root);
        _backupPath = Path.Combine(root, "nvidia-tweaks-backup.json");
        _document = LoadDocument();
    }

    public DateTimeOffset? GetLastAppliedUtc(string tweakId)
    {
        lock (_sync)
        {
            return _document.Tweaks.TryGetValue(tweakId, out BackupTweakState? state)
                ? state.LastAppliedUtc
                : null;
        }
    }

    public void SetLastAppliedUtc(string tweakId, DateTimeOffset utcNow)
    {
        lock (_sync)
        {
            if (!_document.Tweaks.TryGetValue(tweakId, out BackupTweakState? state))
            {
                state = new BackupTweakState();
                _document.Tweaks[tweakId] = state;
            }

            state.LastAppliedUtc = utcNow;
            SaveUnsafe();
        }
    }

    public void ClearLastAppliedUtc(string tweakId)
    {
        lock (_sync)
        {
            if (_document.Tweaks.TryGetValue(tweakId, out BackupTweakState? state))
            {
                state.LastAppliedUtc = null;
                SaveUnsafe();
            }
        }
    }

    public bool TryGetOriginal(string targetKey, out BackupRegistryValue value)
    {
        lock (_sync)
        {
            return _document.OriginalValues.TryGetValue(targetKey, out value!);
        }
    }

    public void RememberOriginalIfMissing(
        string targetKey,
        bool existed,
        RegistryValueKind? kind,
        object? value)
    {
        lock (_sync)
        {
            if (_document.OriginalValues.ContainsKey(targetKey))
            {
                return;
            }

            _document.OriginalValues[targetKey] = SerializeRegistryValue(existed, kind, value);
            SaveUnsafe();
        }
    }

    private BackupDocument LoadDocument()
    {
        try
        {
            if (!File.Exists(_backupPath))
            {
                return new BackupDocument();
            }

            string json = File.ReadAllText(_backupPath);
            BackupDocument? document = JsonSerializer.Deserialize<BackupDocument>(json, JsonOptions);
            return document ?? new BackupDocument();
        }
        catch (Exception exception)
        {
            App.logger.Warn(exception, "[GPU] Failed to load NVIDIA tweaks backup store. Starting fresh.");
            return new BackupDocument();
        }
    }

    private void SaveUnsafe()
    {
        string json = JsonSerializer.Serialize(_document, JsonOptions);
        File.WriteAllText(_backupPath, json);
    }

    private static BackupRegistryValue SerializeRegistryValue(
        bool existed,
        RegistryValueKind? kind,
        object? value)
    {
        BackupRegistryValue backup = new()
        {
            Existed = existed
        };

        if (!existed || kind is null)
        {
            return backup;
        }

        backup.Kind = kind.Value;
        switch (kind.Value)
        {
            case RegistryValueKind.DWord:
                backup.Dword = value switch
                {
                    int signed => unchecked((uint)signed),
                    uint unsigned => unsigned,
                    long longValue => unchecked((uint)longValue),
                    _ => Convert.ToUInt32(value)
                };
                break;
            case RegistryValueKind.QWord:
                backup.Qword = value is long qword ? qword : Convert.ToInt64(value);
                break;
            case RegistryValueKind.String:
            case RegistryValueKind.ExpandString:
                backup.String = value?.ToString();
                break;
            case RegistryValueKind.MultiString:
                backup.MultiString = value as string[] ?? [];
                break;
            case RegistryValueKind.Binary:
                if (value is byte[] bytes)
                {
                    backup.BinaryBase64 = Convert.ToBase64String(bytes);
                }
                break;
            default:
                backup.String = value?.ToString();
                break;
        }

        return backup;
    }

    public static object? DeserializeRegistryValue(BackupRegistryValue backupValue)
    {
        if (!backupValue.Existed || backupValue.Kind is null)
        {
            return null;
        }

        return backupValue.Kind.Value switch
        {
            RegistryValueKind.DWord => unchecked((int)(backupValue.Dword ?? 0)),
            RegistryValueKind.QWord => backupValue.Qword ?? 0L,
            RegistryValueKind.String => backupValue.String ?? string.Empty,
            RegistryValueKind.ExpandString => backupValue.String ?? string.Empty,
            RegistryValueKind.MultiString => backupValue.MultiString ?? [],
            RegistryValueKind.Binary => string.IsNullOrWhiteSpace(backupValue.BinaryBase64)
                ? Array.Empty<byte>()
                : Convert.FromBase64String(backupValue.BinaryBase64),
            _ => backupValue.String ?? string.Empty
        };
    }

    internal sealed class BackupDocument
    {
        public int Version { get; set; } = 1;
        public Dictionary<string, BackupRegistryValue> OriginalValues { get; set; } =
            new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, BackupTweakState> Tweaks { get; set; } =
            new(StringComparer.Ordinal);
    }

    internal sealed class BackupTweakState
    {
        public DateTimeOffset? LastAppliedUtc { get; set; }
    }

    internal sealed class BackupRegistryValue
    {
        public bool Existed { get; set; }
        public RegistryValueKind? Kind { get; set; }
        public uint? Dword { get; set; }
        public long? Qword { get; set; }
        public string? String { get; set; }
        public string[]? MultiString { get; set; }
        public string? BinaryBase64 { get; set; }
    }
}
