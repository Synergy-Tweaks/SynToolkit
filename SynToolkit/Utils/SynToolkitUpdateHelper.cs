#nullable enable

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace SynToolkit.Utils
{
    public sealed record SynToolkitUpdateStatus(
        Version CurrentVersion,
        Version? AvailableVersion,
        string? DownloadUrl)
    {
        public bool IsUpdateAvailable => AvailableVersion is not null &&
            AvailableVersion > CurrentVersion &&
            !string.IsNullOrWhiteSpace(DownloadUrl);
    }

    public static class SynToolkitUpdateHelper
    {
        private const string LatestReleaseUrl = "https://api.github.com/repos/Synergy-Tweaks/SynToolkit/releases/latest";
        private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(15);
        private static readonly HttpClient Client = CreateHttpClient();
        private static readonly SemaphoreSlim UpdateCheckGate = new(1, 1);
        private static SynToolkitUpdateStatus? _cachedStatus;
        private static DateTimeOffset _cacheExpiresUtc;

        public static async Task<SynToolkitUpdateStatus> CheckUpdatesAsync(
            bool forceRefresh = false,
            CancellationToken cancellationToken = default)
        {
            if (!forceRefresh && _cachedStatus is not null && DateTimeOffset.UtcNow < _cacheExpiresUtc)
            {
                return _cachedStatus;
            }

            await UpdateCheckGate.WaitAsync(cancellationToken);
            try
            {
                if (!forceRefresh && _cachedStatus is not null && DateTimeOffset.UtcNow < _cacheExpiresUtc)
                {
                    return _cachedStatus;
                }

                Version currentVersion = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0);
                string json = await Client.GetStringAsync(LatestReleaseUrl, cancellationToken);
                using JsonDocument release = JsonDocument.Parse(json);

                (string? assetName, string? downloadUrl) = release.RootElement
                    .GetProperty("assets")
                    .EnumerateArray()
                    .Select(asset =>
                    {
                        string? name = asset.TryGetProperty("name", out JsonElement nameProperty)
                            ? nameProperty.GetString()
                            : null;
                        string? url = asset.TryGetProperty("browser_download_url", out JsonElement urlProperty)
                            ? urlProperty.GetString()
                            : null;
                        return (name, url);
                    })
                    .Where(asset => IsSetupExecutable(asset.Item1) && !string.IsNullOrWhiteSpace(asset.Item2))
                    .FirstOrDefault();

                Version? version = TryGetSetupVersion(assetName, out Version? assetVersion)
                    ? assetVersion
                    : TryGetReleaseTagVersion(release.RootElement, out Version? tagVersion)
                        ? tagVersion
                        : null;

                _cachedStatus = new SynToolkitUpdateStatus(currentVersion, version, downloadUrl);
                _cacheExpiresUtc = DateTimeOffset.UtcNow.Add(CacheDuration);
                return _cachedStatus;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                App.logger.Error(exception, "Failed to check for SynToolkit updates.");
                Version currentVersion = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0);
                _cachedStatus = new SynToolkitUpdateStatus(currentVersion, null, null);
                _cacheExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(2);
                return _cachedStatus;
            }
            finally
            {
                UpdateCheckGate.Release();
            }
        }

        public static bool CheckUpdates() => CheckUpdatesAsync().GetAwaiter().GetResult().IsUpdateAvailable;

        public static async Task<bool> InstallUpdateAsync(CancellationToken cancellationToken = default)
        {
            SynToolkitUpdateStatus status = await CheckUpdatesAsync(cancellationToken: cancellationToken);
            if (!status.IsUpdateAvailable || string.IsNullOrWhiteSpace(status.DownloadUrl))
            {
                return false;
            }

            try
            {
                string tempDirectory = Path.Combine(Path.GetTempPath(), "SynToolkit", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tempDirectory);
                string installerPath = Path.Combine(tempDirectory, "SynToolkit-Setup.exe");

                using HttpResponseMessage response = await Client.GetAsync(
                    status.DownloadUrl,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);
                response.EnsureSuccessStatusCode();
                await using (Stream source = await response.Content.ReadAsStreamAsync(cancellationToken))
                await using (FileStream destination = new(
                    installerPath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 81920,
                    FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    await source.CopyToAsync(destination, cancellationToken);
                    await destination.FlushAsync(cancellationToken);
                }

                Process? installerProcess = Process.Start(new ProcessStartInfo
                {
                    FileName = installerPath,
                    Arguments = "/SILENT /NORESTART",
                    WorkingDirectory = tempDirectory,
                    UseShellExecute = true
                });

                if (installerProcess is null)
                {
                    throw new InvalidOperationException("The downloaded SynToolkit installer could not be started.");
                }

                App.ShutdownApplication();
                return true;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            catch (Exception exception)
            {
                App.logger.Error(exception, "Failed to install the SynToolkit update.");
                return false;
            }
        }

        private static bool IsSetupExecutable(string? assetName) =>
            !string.IsNullOrWhiteSpace(assetName) &&
            assetName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
            assetName.Contains("SynToolkit", StringComparison.OrdinalIgnoreCase);

        private static bool TryGetSetupVersion(string? assetName, out Version? version)
        {
            version = null;
            if (!IsSetupExecutable(assetName) || assetName is null)
            {
                return false;
            }

            Match versionMatch = Regex.Match(
                assetName,
                @"(?<!\d)(?<version>\d+\.\d+(?:\.\d+){0,2})(?!\d)",
                RegexOptions.CultureInvariant);
            return versionMatch.Success && Version.TryParse(versionMatch.Groups["version"].Value, out version);
        }

        private static bool TryGetReleaseTagVersion(JsonElement release, out Version? version)
        {
            version = null;
            string? tag = release.TryGetProperty("tag_name", out JsonElement tagProperty)
                ? tagProperty.GetString()
                : null;
            return Version.TryParse(tag?.Trim().TrimStart('v', 'V'), out version);
        }

        private static HttpClient CreateHttpClient()
        {
            HttpClient client = new()
            {
                Timeout = TimeSpan.FromSeconds(10)
            };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("SynToolkit/1.7");
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            return client;
        }
    }
}
