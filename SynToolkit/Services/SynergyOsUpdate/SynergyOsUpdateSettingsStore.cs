#nullable enable

using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SynToolkit.Services.SynergyOsUpdate
{
    public sealed class SynergyOsCachedRelease
    {
        public string Version { get; set; } = string.Empty;
        public string HtmlUrl { get; set; } = string.Empty;
        public DateTimeOffset? PublishedAtUtc { get; set; }
    }

    public sealed class SynergyOsUpdateSettingsDocument
    {
        public bool Enabled { get; set; } = true;
        public DateTimeOffset? LastSuccessfulCheckUtc { get; set; }
        public string? ETag { get; set; }
        public SynergyOsCachedRelease? CachedRelease { get; set; }
        public string? SkippedVersion { get; set; }
        public string? SnoozedVersion { get; set; }
        public DateTimeOffset? SnoozeUntilUtc { get; set; }
        public DateTimeOffset? RateLimitRetryAfterUtc { get; set; }
    }

    public sealed class SynergyOsUpdateSettingsStore
    {
        private static readonly JsonSerializerOptions SerializerOptions = new()
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        private readonly object _lock = new();
        private readonly string _settingsPath;
        private SynergyOsUpdateSettingsDocument _document;

        public SynergyOsUpdateSettingsStore(string? settingsPath = null)
        {
            _settingsPath = settingsPath ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SynToolkit",
                "synergyos-update-settings.json");
            _document = Load();
        }

        public SynergyOsUpdateSettingsDocument Snapshot()
        {
            lock (_lock)
            {
                return Clone(_document);
            }
        }

        public bool IsEnabled
        {
            get
            {
                lock (_lock)
                {
                    return _document.Enabled;
                }
            }
            set
            {
                lock (_lock)
                {
                    _document.Enabled = value;
                    SaveUnlocked();
                }
            }
        }

        public void MarkSuccessfulCheck(DateTimeOffset utcNow, string? etag, SynergyOsCachedRelease? release)
        {
            lock (_lock)
            {
                _document.LastSuccessfulCheckUtc = utcNow;
                if (!string.IsNullOrWhiteSpace(etag))
                {
                    _document.ETag = etag;
                }

                if (release is not null)
                {
                    _document.CachedRelease = CloneRelease(release);
                }

                SaveUnlocked();
            }
        }

        public void SetRateLimitRetryAfter(DateTimeOffset retryAfterUtc)
        {
            lock (_lock)
            {
                _document.RateLimitRetryAfterUtc = retryAfterUtc;
                SaveUnlocked();
            }
        }

        public void ClearRateLimitRetryAfter()
        {
            lock (_lock)
            {
                _document.RateLimitRetryAfterUtc = null;
                SaveUnlocked();
            }
        }

        public void SkipVersion(string version)
        {
            lock (_lock)
            {
                _document.SkippedVersion = version;
                if (string.Equals(_document.SnoozedVersion, version, StringComparison.OrdinalIgnoreCase))
                {
                    _document.SnoozedVersion = null;
                    _document.SnoozeUntilUtc = null;
                }

                SaveUnlocked();
            }
        }

        public void SnoozeVersion(string version, DateTimeOffset untilUtc)
        {
            lock (_lock)
            {
                _document.SnoozedVersion = version;
                _document.SnoozeUntilUtc = untilUtc;
                SaveUnlocked();
            }
        }

        private SynergyOsUpdateSettingsDocument Load()
        {
            try
            {
                if (!File.Exists(_settingsPath))
                {
                    return new SynergyOsUpdateSettingsDocument();
                }

                string json = File.ReadAllText(_settingsPath);
                SynergyOsUpdateSettingsDocument? document =
                    JsonSerializer.Deserialize<SynergyOsUpdateSettingsDocument>(json, SerializerOptions);
                return document ?? new SynergyOsUpdateSettingsDocument();
            }
            catch
            {
                return new SynergyOsUpdateSettingsDocument();
            }
        }

        private void SaveUnlocked()
        {
            try
            {
                string? directory = Path.GetDirectoryName(_settingsPath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(
                    _settingsPath,
                    JsonSerializer.Serialize(_document, SerializerOptions));
            }
            catch
            {
                // Persistence failures must never crash the app.
            }
        }

        private static SynergyOsUpdateSettingsDocument Clone(SynergyOsUpdateSettingsDocument source) =>
            new()
            {
                Enabled = source.Enabled,
                LastSuccessfulCheckUtc = source.LastSuccessfulCheckUtc,
                ETag = source.ETag,
                CachedRelease = CloneRelease(source.CachedRelease),
                SkippedVersion = source.SkippedVersion,
                SnoozedVersion = source.SnoozedVersion,
                SnoozeUntilUtc = source.SnoozeUntilUtc,
                RateLimitRetryAfterUtc = source.RateLimitRetryAfterUtc
            };

        private static SynergyOsCachedRelease? CloneRelease(SynergyOsCachedRelease? source) =>
            source is null
                ? null
                : new SynergyOsCachedRelease
                {
                    Version = source.Version,
                    HtmlUrl = source.HtmlUrl,
                    PublishedAtUtc = source.PublishedAtUtc
                };
    }
}
