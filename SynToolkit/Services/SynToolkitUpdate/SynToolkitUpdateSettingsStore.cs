#nullable enable

using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SynToolkit.Services.SynToolkitUpdate
{
    public sealed class SynToolkitCachedRelease
    {
        public string Version { get; set; } = string.Empty;
        public string HtmlUrl { get; set; } = string.Empty;
        public DateTimeOffset? PublishedAtUtc { get; set; }
    }

    public sealed class SynToolkitUpdateSettingsDocument
    {
        public bool Enabled { get; set; } = true;
        public DateTimeOffset? LastSuccessfulCheckUtc { get; set; }
        public string? ETag { get; set; }
        public SynToolkitCachedRelease? CachedRelease { get; set; }
        public string? SkippedVersion { get; set; }
        public string? SnoozedVersion { get; set; }
        public DateTimeOffset? SnoozeUntilUtc { get; set; }
        public DateTimeOffset? RateLimitRetryAfterUtc { get; set; }
    }

    public sealed class SynToolkitUpdateSettingsStore
    {
        private static readonly JsonSerializerOptions SerializerOptions = new()
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        private readonly object _lock = new();
        private readonly string _settingsPath;
        private SynToolkitUpdateSettingsDocument _document;

        public SynToolkitUpdateSettingsStore(string? settingsPath = null)
        {
            _settingsPath = settingsPath ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SynToolkit",
                "syntoolkit-update-settings.json");
            _document = Load();
        }

        public SynToolkitUpdateSettingsDocument Snapshot()
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

        public void MarkSuccessfulCheck(DateTimeOffset utcNow, string? etag, SynToolkitCachedRelease? release)
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

        private SynToolkitUpdateSettingsDocument Load()
        {
            try
            {
                if (!File.Exists(_settingsPath))
                {
                    return new SynToolkitUpdateSettingsDocument();
                }

                string json = File.ReadAllText(_settingsPath);
                SynToolkitUpdateSettingsDocument? document =
                    JsonSerializer.Deserialize<SynToolkitUpdateSettingsDocument>(json, SerializerOptions);
                if (document is null)
                {
                    return new SynToolkitUpdateSettingsDocument();
                }

                if (document.CachedRelease is not null)
                {
                    document.CachedRelease.HtmlUrl = SynToolkitUpdateConstants.ReleasesPageUrl;
                }

                return document;
            }
            catch (Exception exception)
            {
                // A corrupt file used to reset every setting silently (updates back
                // on, skipped version forgotten). Log it so it can be diagnosed.
                App.logger.Warn(exception, "[Update] Could not read {Path}; starting from defaults.", _settingsPath);
                return new SynToolkitUpdateSettingsDocument();
            }
        }

        private void SaveUnlocked()
        {
            string tempPath = _settingsPath + ".tmp";
            try
            {
                string? directory = Path.GetDirectoryName(_settingsPath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // Write beside the target, then swap, so a crash mid-write can't
                // leave a truncated settings file behind.
                File.WriteAllText(
                    tempPath,
                    JsonSerializer.Serialize(_document, SerializerOptions));
                File.Move(tempPath, _settingsPath, overwrite: true);
            }
            catch (Exception exception)
            {
                // Persistence failures must never crash the app.
                App.logger.Debug(exception, "[Update] Could not save update settings to {Path}.", _settingsPath);
                try
                {
                    if (File.Exists(tempPath))
                    {
                        File.Delete(tempPath);
                    }
                }
                catch
                {
                    // Nothing else to do.
                }
            }
        }

        private static SynToolkitUpdateSettingsDocument Clone(SynToolkitUpdateSettingsDocument source) =>
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

        private static SynToolkitCachedRelease? CloneRelease(SynToolkitCachedRelease? source) =>
            source is null
                ? null
                : new SynToolkitCachedRelease
                {
                    Version = source.Version,
                    HtmlUrl = SynToolkitUpdateConstants.ReleasesPageUrl,
                    PublishedAtUtc = source.PublishedAtUtc
                };
    }
}
