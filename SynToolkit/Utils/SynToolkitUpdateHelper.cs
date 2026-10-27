#nullable enable

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using SynToolkit.Services.SynergyOsUpdate;

namespace SynToolkit.Utils
{
    public sealed record SynToolkitUpdateStatus(
        Version CurrentVersion,
        Version? AvailableVersion,
        string? DownloadUrl,
        long? ExpectedSize = null,
        string? ExpectedDigest = null)
    {
        /// <summary>
        /// Uses the same SemVer comparison as the SynToolkit update notice
        /// (4-part assembly versions vs 3-part tags compare correctly).
        /// </summary>
        public bool IsUpdateAvailable =>
            AvailableVersion is not null &&
            !string.IsNullOrWhiteSpace(DownloadUrl) &&
            SynergyOsSemVer.IsNewer(AvailableVersion.ToString(), CurrentVersion.ToString());
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

                (string? assetName, string? downloadUrl, long? size, string? digest) = release.RootElement
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
                        long? length = asset.TryGetProperty("size", out JsonElement sizeProperty)
                            && sizeProperty.TryGetInt64(out long parsedSize)
                                ? parsedSize
                                : null;
                        string? assetDigest = asset.TryGetProperty("digest", out JsonElement digestProperty)
                            ? digestProperty.GetString()
                            : null;
                        return (name, url, length, assetDigest);
                    })
                    .Where(asset => IsSetupExecutable(asset.name) && IsTrustedDownloadUrl(asset.url))
                    .FirstOrDefault();

                // Take the version from the same asset that will be executed, so
                // the download and the version check can never disagree.
                Version? version = assetName is not null && TryGetSetupVersion(assetName, out Version? assetVersion)
                    ? assetVersion
                    : TryResolveReleaseVersion(release.RootElement, out Version? resolved)
                        ? resolved
                        : null;

                _ = assetName;
                _cachedStatus = new SynToolkitUpdateStatus(currentVersion, version, downloadUrl, size, digest);
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

        public static async Task<bool> InstallUpdateAsync(CancellationToken cancellationToken = default)
        {
            SynToolkitUpdateStatus status = await CheckUpdatesAsync(cancellationToken: cancellationToken);
            if (!status.IsUpdateAvailable || string.IsNullOrWhiteSpace(status.DownloadUrl))
            {
                return false;
            }

            string tempDirectory = Path.Combine(Path.GetTempPath(), "SynToolkit", Guid.NewGuid().ToString("N"));
            string installerPath = Path.Combine(tempDirectory, "SynToolkit-Setup.exe");
            bool installerStarted = false;

            try
            {
                Directory.CreateDirectory(tempDirectory);

                using (HttpResponseMessage response = await Client.GetAsync(
                    status.DownloadUrl,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken))
                {
                    response.EnsureSuccessStatusCode();

                    // Redirects are followed automatically, so check where the bytes
                    // actually came from before writing them to disk.
                    if (!IsTrustedDownloadUrl(response.RequestMessage?.RequestUri?.AbsoluteUri))
                    {
                        throw new InvalidOperationException("The update download came from an untrusted host.");
                    }

                    await using Stream source = await response.Content.ReadAsStreamAsync(cancellationToken);
                    await using FileStream destination = new(
                        installerPath,
                        FileMode.Create,
                        FileAccess.Write,
                        FileShare.None,
                        bufferSize: 81920,
                        FileOptions.Asynchronous | FileOptions.SequentialScan);
                    await source.CopyToAsync(destination, cancellationToken);
                    await destination.FlushAsync(cancellationToken);
                }

                long downloadedSize = new FileInfo(installerPath).Length;
                if (downloadedSize == 0 ||
                    (status.ExpectedSize is long expectedSize && downloadedSize != expectedSize))
                {
                    throw new InvalidOperationException(
                        $"The update download is incomplete ({downloadedSize} of {status.ExpectedSize?.ToString() ?? "unknown"} bytes).");
                }

                if (!string.IsNullOrWhiteSpace(status.ExpectedDigest))
                {
                    string actualDigest = await ComputeSha256Async(installerPath, cancellationToken);
                    if (!DigestMatches(status.ExpectedDigest, actualDigest))
                    {
                        throw new InvalidOperationException("The update download failed its SHA-256 integrity check.");
                    }
                }

                using Process? installerProcess = Process.Start(new ProcessStartInfo
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

                installerStarted = true;

                // Give the installer a moment to fail fast (blocked, bad args), but
                // don't hold the app open while it replaces our files.
                using (CancellationTokenSource settleGate = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    settleGate.CancelAfter(TimeSpan.FromSeconds(10));
                    try
                    {
                        await installerProcess.WaitForExitAsync(settleGate.Token);
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                    {
                        // Still installing; hand over to it.
                    }
                }

                if (installerProcess.HasExited && installerProcess.ExitCode != 0)
                {
                    throw new InvalidOperationException(
                        $"The SynToolkit installer stopped with exit code {installerProcess.ExitCode}.");
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
            finally
            {
                if (!installerStarted)
                {
                    TryDeleteDirectory(tempDirectory);
                }
            }
        }

        private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
        {
            await using FileStream stream = File.OpenRead(path);
            byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken);
            return Convert.ToHexString(hash);
        }

        private static bool DigestMatches(string expectedDigest, string actualHexDigest)
        {
            const string sha256Prefix = "sha256:";
            string expected = expectedDigest.Trim();
            if (!expected.StartsWith(sha256Prefix, StringComparison.OrdinalIgnoreCase))
            {
                // GitHub only publishes sha256 digests today; anything else can't be checked.
                App.logger.Warn($"[Update] Ignoring unsupported digest algorithm: {expectedDigest}");
                return true;
            }

            return string.Equals(
                expected[sha256Prefix.Length..].Trim(),
                actualHexDigest,
                StringComparison.OrdinalIgnoreCase);
        }

        private static void TryDeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }
            }
            catch (Exception exception)
            {
                App.logger.Debug(exception, "[Update] Could not clean up {Path}.", path);
            }
        }

        /// <summary>
        /// Resolves the release version the same way Needs Attention does:
        /// installer asset name first, then the release tag. Shared with the
        /// SynToolkit update notice so both signals agree.
        /// </summary>
        public static bool TryResolveReleaseVersion(JsonElement release, out Version? version)
        {
            version = null;

            if (release.TryGetProperty("assets", out JsonElement assets) &&
                assets.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement asset in assets.EnumerateArray())
                {
                    string? name = asset.TryGetProperty("name", out JsonElement nameProperty)
                        ? nameProperty.GetString()
                        : null;
                    if (TryGetSetupVersion(name, out Version? assetVersion) && assetVersion is not null)
                    {
                        version = assetVersion;
                        return true;
                    }
                }
            }

            return TryGetReleaseTagVersion(release, out version) && version is not null;
        }

        /// <summary>
        /// Normalized display string for the same rules as <see cref="TryResolveReleaseVersion"/>.
        /// </summary>
        public static string? TryResolveReleaseVersionDisplay(JsonElement release)
        {
            if (!TryResolveReleaseVersion(release, out Version? version) || version is null)
            {
                return null;
            }

            return SynergyOsSemVer.NormalizeDisplay(version.ToString());
        }

        private static bool IsSetupExecutable(string? assetName) =>
            !string.IsNullOrWhiteSpace(assetName) &&
            assetName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
            assetName.StartsWith("SynToolkit-Setup", StringComparison.OrdinalIgnoreCase);

        private static bool IsTrustedDownloadUrl(string? url) =>
            Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) &&
            uri.Scheme == Uri.UriSchemeHttps &&
            (uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) ||
             uri.Host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase));

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
            if (!versionMatch.Success)
            {
                return false;
            }

            string? normalized = SynergyOsSemVer.NormalizeDisplay(versionMatch.Groups["version"].Value);
            return normalized is not null && Version.TryParse(normalized, out version);
        }

        private static bool TryGetReleaseTagVersion(JsonElement release, out Version? version)
        {
            version = null;
            string? tag = release.TryGetProperty("tag_name", out JsonElement tagProperty)
                ? tagProperty.GetString()
                : null;
            string? normalized = SynergyOsSemVer.NormalizeDisplay(tag);
            return normalized is not null && Version.TryParse(normalized, out version);
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
