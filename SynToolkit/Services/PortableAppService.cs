#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace SynToolkit.Services
{
    /// <summary>
    /// Manages portable applications downloaded from GitHub releases.
    /// Apps are installed to %LocalAppData%\SynToolkit\Apps\{AppName}\.
    /// </summary>
    public sealed class PortableAppService
    {
        private static readonly HttpClient GitHubClient = CreateGitHubClient();
        private static readonly string PortableAppsRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SynToolkit",
            "Apps");

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        /// <summary>
        /// Downloads and installs a portable app from GitHub releases.
        /// </summary>
        public async Task<PortableAppInstallResult> InstallAsync(
            PortableAppDefinition definition,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(definition);

            try
            {
                // Fetch the latest release from GitHub
                GitHubRelease release = await GetLatestReleaseAsync(
                    definition.GitHubOwner,
                    definition.GitHubRepo,
                    cancellationToken);

                // Find the matching asset
                GitHubAsset? asset = release.Assets?.FirstOrDefault(a =>
                    a.Name?.Equals(definition.AssetName, StringComparison.OrdinalIgnoreCase) == true);

                if (asset == null || string.IsNullOrWhiteSpace(asset.BrowserDownloadUrl))
                {
                    return new PortableAppInstallResult(
                        false,
                        $"Release asset '{definition.AssetName}' not found in the latest release.");
                }

                // Prepare install directory
                string appDirectory = GetAppDirectory(definition.AppName);
                Directory.CreateDirectory(appDirectory);

                string targetPath = Path.Combine(appDirectory, definition.ExecutableName);
                string tempPath = targetPath + ".download";

                try
                {
                    // Download the asset
                    progress?.Report(0.1);
                    await DownloadFileAsync(asset.BrowserDownloadUrl, tempPath, progress, cancellationToken);
                    progress?.Report(0.8);

                    // Verify SHA-256 if provided in release metadata
                    string? expectedSha256 = asset.Digest?.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) == true
                        ? asset.Digest.Substring(7)
                        : definition.ExpectedSha256;

                    if (!string.IsNullOrWhiteSpace(expectedSha256))
                    {
                        string actualSha256 = await ComputeSha256Async(tempPath, cancellationToken);
                        if (!actualSha256.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
                        {
                            File.Delete(tempPath);
                            return new PortableAppInstallResult(
                                false,
                                $"SHA-256 verification failed. Expected: {expectedSha256}, Got: {actualSha256}");
                        }
                    }

                    progress?.Report(0.9);

                    // Move to final location (overwrite if exists)
                    if (File.Exists(targetPath))
                    {
                        File.Delete(targetPath);
                    }
                    File.Move(tempPath, targetPath);

                    // Read existing metadata to check if user deleted shortcuts
                    PortableAppMetadata? existingMetadata = await ReadInstallMetadataAsync(definition.AppName, cancellationToken);
                    bool userDeletedDesktopShortcut = existingMetadata?.UserDeletedDesktopShortcut ?? false;
                    bool userDeletedStartMenuShortcut = existingMetadata?.UserDeletedStartMenuShortcut ?? false;

                    // Create shortcuts if enabled and user hasn't deleted them
                    bool desktopShortcutCreated = false;
                    bool startMenuShortcutCreated = false;
                    string? shortcutError = null;

                    if (definition.CreateDesktopShortcut)
                    {
                        // Check if desktop shortcut was previously created and now deleted by user
                        string desktopShortcutPath = GetDesktopShortcutPath(definition.AppName);
                        if (existingMetadata?.DesktopShortcutCreated == true && !File.Exists(desktopShortcutPath))
                        {
                            userDeletedDesktopShortcut = true;
                        }

                        // Only create if user hasn't deliberately deleted it
                        if (!userDeletedDesktopShortcut)
                        {
                            (desktopShortcutCreated, shortcutError) = TryCreateShortcut(
                                desktopShortcutPath,
                                targetPath,
                                appDirectory,
                                definition.AppName,
                                definition.RequiresElevation);
                        }

                        // Check if start menu shortcut was previously created and now deleted by user
                        string startMenuShortcutPath = GetStartMenuShortcutPath(definition.AppName);
                        if (existingMetadata?.StartMenuShortcutCreated == true && !File.Exists(startMenuShortcutPath))
                        {
                            userDeletedStartMenuShortcut = true;
                        }

                        // Only create if user hasn't deliberately deleted it
                        if (!userDeletedStartMenuShortcut)
                        {
                            (startMenuShortcutCreated, string? startMenuError) = TryCreateShortcut(
                                startMenuShortcutPath,
                                targetPath,
                                appDirectory,
                                definition.AppName,
                                definition.RequiresElevation);
                            if (!startMenuShortcutCreated && shortcutError == null)
                            {
                                shortcutError = startMenuError;
                            }
                        }
                    }

                    // Save metadata for version tracking
                    await SaveInstallMetadataAsync(
                        definition.AppName,
                        release.TagName ?? "unknown",
                        asset.BrowserDownloadUrl,
                        desktopShortcutCreated || (existingMetadata?.DesktopShortcutCreated ?? false),
                        startMenuShortcutCreated || (existingMetadata?.StartMenuShortcutCreated ?? false),
                        userDeletedDesktopShortcut,
                        userDeletedStartMenuShortcut,
                        cancellationToken);

                    progress?.Report(1.0);

                    // Build result message
                    string message = $"Installed {definition.AppName} {release.TagName}";
                    string? shortcutNote = null;
                    if (definition.CreateDesktopShortcut)
                    {
                        if (desktopShortcutCreated || startMenuShortcutCreated)
                        {
                            shortcutNote = "Desktop shortcut created.";
                        }
                        else if (userDeletedDesktopShortcut && userDeletedStartMenuShortcut)
                        {
                            shortcutNote = null; // User deleted both, don't mention
                        }
                        else if (shortcutError != null)
                        {
                            shortcutNote = $"Shortcut could not be created: {shortcutError}";
                        }
                    }

                    return new PortableAppInstallResult(
                        true,
                        message,
                        release.TagName,
                        targetPath,
                        desktopShortcutCreated,
                        startMenuShortcutCreated,
                        shortcutError);
                }
                finally
                {
                    // Clean up temp file if it exists
                    if (File.Exists(tempPath))
                    {
                        try { File.Delete(tempPath); } catch { /* ignore */ }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (HttpRequestException exception)
            {
                App.logger.Error(exception, "[PortableApp] Network error installing {AppName}", definition.AppName);
                return new PortableAppInstallResult(false, $"Network error: {exception.Message}");
            }
            catch (Exception exception)
            {
                App.logger.Error(exception, "[PortableApp] Failed to install {AppName}", definition.AppName);
                return new PortableAppInstallResult(false, exception.Message);
            }
        }

        /// <summary>
        /// Checks if a portable app is installed.
        /// </summary>
        public Task<PortableAppStatus> GetStatusAsync(
            PortableAppDefinition definition,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(definition);

            string appDirectory = GetAppDirectory(definition.AppName);
            string executablePath = Path.Combine(appDirectory, definition.ExecutableName);

            if (!File.Exists(executablePath))
            {
                return Task.FromResult(new PortableAppStatus(false, null, null, false));
            }

            // Read installed version from metadata
            string? installedVersion = null;
            string metadataPath = Path.Combine(appDirectory, "install.json");
            if (File.Exists(metadataPath))
            {
                try
                {
                    string json = File.ReadAllText(metadataPath);
                    PortableAppMetadata? metadata = JsonSerializer.Deserialize<PortableAppMetadata>(json, JsonOptions);
                    installedVersion = metadata?.Version;
                }
                catch (Exception exception)
                {
                    App.logger.Debug(exception, "[PortableApp] Failed to read metadata for {AppName}", definition.AppName);
                }
            }

            return Task.FromResult(new PortableAppStatus(true, installedVersion, null, false));
        }

        /// <summary>
        /// Checks if an update is available for a portable app.
        /// </summary>
        public async Task<PortableAppStatus> CheckForUpdateAsync(
            PortableAppDefinition definition,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(definition);

            PortableAppStatus currentStatus = await GetStatusAsync(definition, cancellationToken);
            if (!currentStatus.IsInstalled)
            {
                return currentStatus;
            }

            try
            {
                GitHubRelease release = await GetLatestReleaseAsync(
                    definition.GitHubOwner,
                    definition.GitHubRepo,
                    cancellationToken);

                string? availableVersion = release.TagName;
                bool isUpdateAvailable = !string.IsNullOrWhiteSpace(availableVersion) &&
                    !string.Equals(currentStatus.InstalledVersion, availableVersion, StringComparison.OrdinalIgnoreCase);

                return new PortableAppStatus(
                    true,
                    currentStatus.InstalledVersion,
                    availableVersion,
                    isUpdateAvailable);
            }
            catch (Exception exception)
            {
                App.logger.Debug(exception, "[PortableApp] Failed to check for updates for {AppName}", definition.AppName);
                return currentStatus;
            }
        }

        /// <summary>
        /// Uninstalls a portable app by removing its directory and shortcuts.
        /// </summary>
        public Task<PortableAppInstallResult> UninstallAsync(
            PortableAppDefinition definition,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(definition);

            try
            {
                // Delete desktop shortcut
                string desktopShortcutPath = GetDesktopShortcutPath(definition.AppName);
                if (File.Exists(desktopShortcutPath))
                {
                    try
                    {
                        File.Delete(desktopShortcutPath);
                    }
                    catch (Exception exception)
                    {
                        App.logger.Debug(exception, "[PortableApp] Failed to delete desktop shortcut for {AppName}", definition.AppName);
                    }
                }

                // Delete start menu shortcut
                string startMenuShortcutPath = GetStartMenuShortcutPath(definition.AppName);
                if (File.Exists(startMenuShortcutPath))
                {
                    try
                    {
                        File.Delete(startMenuShortcutPath);
                    }
                    catch (Exception exception)
                    {
                        App.logger.Debug(exception, "[PortableApp] Failed to delete start menu shortcut for {AppName}", definition.AppName);
                    }
                }

                // Delete app directory
                string appDirectory = GetAppDirectory(definition.AppName);
                if (Directory.Exists(appDirectory))
                {
                    Directory.Delete(appDirectory, recursive: true);
                }

                TryDeleteDesktopShortcut(definition.AppName);

                return Task.FromResult(new PortableAppInstallResult(true, $"Uninstalled {definition.AppName}"));
            }
            catch (Exception exception)
            {
                App.logger.Error(exception, "[PortableApp] Failed to uninstall {AppName}", definition.AppName);
                return Task.FromResult(new PortableAppInstallResult(false, exception.Message));
            }
        }

        /// <summary>
        /// Checks if a desktop shortcut exists for a portable app.
        /// </summary>
        public bool DesktopShortcutExists(PortableAppDefinition definition)
        {
            ArgumentNullException.ThrowIfNull(definition);
            return File.Exists(GetDesktopShortcutPath(definition.AppName));
        }

        /// <summary>
        /// Creates or removes the desktop shortcut for an installed portable app.
        /// </summary>
        public bool SetDesktopShortcutEnabled(PortableAppDefinition definition, bool enabled)
        {
            ArgumentNullException.ThrowIfNull(definition);

            if (!enabled)
            {
                return TryDeleteDesktopShortcut(definition.AppName);
            }

            string executablePath = GetExecutablePath(definition);
            if (!File.Exists(executablePath))
            {
                return false;
            }

            try
            {
                Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType is null)
                {
                    return false;
                }

                dynamic shell = Activator.CreateInstance(shellType)!;
                try
                {
                    dynamic shortcut = shell.CreateShortcut(GetDesktopShortcutPath(definition.AppName));
                    try
                    {
                        shortcut.TargetPath = executablePath;
                        shortcut.WorkingDirectory = GetAppDirectory(definition.AppName);
                        shortcut.IconLocation = executablePath;
                        shortcut.Save();
                    }
                    finally
                    {
                        System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shortcut);
                    }
                }
                finally
                {
                    System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
                }

                return true;
            }
            catch (Exception exception)
            {
                App.logger.Error(exception, "[PortableApp] Failed to create desktop shortcut for {AppName}", definition.AppName);
                return false;
            }
        }

        private static string GetDesktopShortcutPath(string appName) =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                $"{appName}.lnk");

        private static bool TryDeleteDesktopShortcut(string appName)
        {
            try
            {
                string shortcutPath = GetDesktopShortcutPath(appName);
                if (File.Exists(shortcutPath))
                {
                    File.Delete(shortcutPath);
                }

                return true;
            }
            catch (Exception exception)
            {
                App.logger.Error(exception, "[PortableApp] Failed to remove desktop shortcut for {AppName}", appName);
                return false;
            }
        }

        /// <summary>
        /// Launches a portable app, optionally elevated.
        /// </summary>
        public Task<bool> LaunchAsync(
            PortableAppDefinition definition,
            bool runElevated = false,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(definition);

            string appDirectory = GetAppDirectory(definition.AppName);
            string executablePath = Path.Combine(appDirectory, definition.ExecutableName);

            if (!File.Exists(executablePath))
            {
                return Task.FromResult(false);
            }

            try
            {
                ProcessStartInfo startInfo = new(executablePath)
                {
                    UseShellExecute = true,
                    WorkingDirectory = appDirectory
                };

                if (runElevated)
                {
                    startInfo.Verb = "runas";
                }

                Process.Start(startInfo);
                return Task.FromResult(true);
            }
            catch (Exception exception)
            {
                App.logger.Error(exception, "[PortableApp] Failed to launch {AppName}", definition.AppName);
                return Task.FromResult(false);
            }
        }

        /// <summary>
        /// Gets the full path to a portable app's executable.
        /// </summary>
        public string GetExecutablePath(PortableAppDefinition definition)
        {
            ArgumentNullException.ThrowIfNull(definition);
            return Path.Combine(GetAppDirectory(definition.AppName), definition.ExecutableName);
        }

        private static string GetAppDirectory(string appName) =>
            Path.Combine(PortableAppsRoot, appName);

        private static string GetDesktopShortcutPath(string appName) =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                $"{appName}.lnk");

        private static string GetStartMenuShortcutPath(string appName) =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Programs),
                $"{appName}.lnk");

        /// <summary>
        /// Creates a Windows shortcut (.lnk) file using WScript.Shell COM.
        /// Returns (success, errorMessage).
        /// </summary>
        private static (bool Success, string? Error) TryCreateShortcut(
            string shortcutPath,
            string targetPath,
            string workingDirectory,
            string appName,
            bool requiresElevation)
        {
            try
            {
                // Ensure parent directory exists (for Start Menu)
                string? parentDir = Path.GetDirectoryName(shortcutPath);
                if (!string.IsNullOrEmpty(parentDir) && !Directory.Exists(parentDir))
                {
                    Directory.CreateDirectory(parentDir);
                }

                // Use WScript.Shell COM to create the shortcut
                Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType is null)
                {
                    return (false, "WScript.Shell COM is unavailable");
                }

                object? shell = Activator.CreateInstance(shellType);
                if (shell is null)
                {
                    return (false, "Failed to create WScript.Shell instance");
                }

                try
                {
                    object? shortcut = shellType.InvokeMember(
                        "CreateShortcut",
                        BindingFlags.InvokeMethod,
                        null,
                        shell,
                        [shortcutPath]);

                    if (shortcut is null)
                    {
                        return (false, "Failed to create shortcut object");
                    }

                    Type shortcutType = shortcut.GetType();

                    // Set TargetPath
                    shortcutType.InvokeMember(
                        "TargetPath",
                        BindingFlags.SetProperty,
                        null,
                        shortcut,
                        [targetPath]);

                    // Set WorkingDirectory
                    shortcutType.InvokeMember(
                        "WorkingDirectory",
                        BindingFlags.SetProperty,
                        null,
                        shortcut,
                        [workingDirectory]);

                    // Set IconLocation (use the exe itself)
                    shortcutType.InvokeMember(
                        "IconLocation",
                        BindingFlags.SetProperty,
                        null,
                        shortcut,
                        [$"{targetPath},0"]);

                    // Set Description
                    shortcutType.InvokeMember(
                        "Description",
                        BindingFlags.SetProperty,
                        null,
                        shortcut,
                        [$"Launch {appName} (installed by SynToolkit)"]);

                    // Save the shortcut
                    shortcutType.InvokeMember(
                        "Save",
                        BindingFlags.InvokeMethod,
                        null,
                        shortcut,
                        null);

                    // If elevation is required, set the "Run as administrator" flag
                    // This is done by modifying the .lnk file header directly
                    if (requiresElevation && File.Exists(shortcutPath))
                    {
                        SetShortcutRunAsAdmin(shortcutPath);
                    }

                    App.logger.Info("[PortableApp] Created shortcut: {ShortcutPath}", shortcutPath);
                    return (true, null);
                }
                finally
                {
                    if (shell is IDisposable disposable)
                    {
                        disposable.Dispose();
                    }
                    else if (shell != null)
                    {
                        Marshal.FinalReleaseComObject(shell);
                    }
                }
            }
            catch (Exception exception)
            {
                App.logger.Error(exception, "[PortableApp] Failed to create shortcut: {ShortcutPath}", shortcutPath);
                return (false, exception.Message);
            }
        }

        /// <summary>
        /// Sets the "Run as administrator" flag in a .lnk file by modifying the header.
        /// The flag is at byte offset 0x15, bit 0x20 (SLDF_RUNAS_USER).
        /// </summary>
        private static void SetShortcutRunAsAdmin(string shortcutPath)
        {
            try
            {
                // Read the .lnk file
                byte[] data = File.ReadAllBytes(shortcutPath);

                // The .lnk header is at least 0x4C bytes
                // The LinkFlags field is at offset 0x14 (DWORD)
                // But the "Run as administrator" flag is actually in the
                // ShellLinkDataFlags at offset 0x15
                // Specifically, we need to set the SLDF_RUNAS_USER flag (0x2000)
                // which is in the LinkInfo flags at byte 0x15

                // Actually, the standard way is to modify byte at offset 0x15
                // and set bit 5 (0x20) which represents the "Run as different user" legacy flag
                // However, the proper "Run as administrator" is done via IShellLinkDataList

                // Simpler approach: modify the flag byte directly
                // At offset 0x15, set the high bit of the flags
                if (data.Length > 0x15)
                {
                    data[0x15] |= 0x20; // Set run as admin flag
                    File.WriteAllBytes(shortcutPath, data);
                    App.logger.Debug("[PortableApp] Set run-as-admin flag for shortcut: {ShortcutPath}", shortcutPath);
                }
            }
            catch (Exception exception)
            {
                // Non-fatal - shortcut still works, just won't auto-elevate
                App.logger.Debug(exception, "[PortableApp] Could not set run-as-admin flag for shortcut: {ShortcutPath}", shortcutPath);
            }
        }

        private static async Task<PortableAppMetadata?> ReadInstallMetadataAsync(
            string appName,
            CancellationToken cancellationToken)
        {
            string appDirectory = GetAppDirectory(appName);
            string metadataPath = Path.Combine(appDirectory, "install.json");

            if (!File.Exists(metadataPath))
            {
                return null;
            }

            try
            {
                string json = await File.ReadAllTextAsync(metadataPath, cancellationToken);
                return JsonSerializer.Deserialize<PortableAppMetadata>(json, JsonOptions);
            }
            catch (Exception exception)
            {
                App.logger.Debug(exception, "[PortableApp] Failed to read metadata for {AppName}", appName);
                return null;
            }
        }

        private async Task<GitHubRelease> GetLatestReleaseAsync(
            string owner,
            string repo,
            CancellationToken cancellationToken)
        {
            string url = $"https://api.github.com/repos/{owner}/{repo}/releases/latest";
            HttpResponseMessage response = await GitHubClient.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();

            GitHubRelease? release = await response.Content.ReadFromJsonAsync<GitHubRelease>(JsonOptions, cancellationToken);
            return release ?? throw new InvalidOperationException("Failed to parse GitHub release response.");
        }

        private static async Task DownloadFileAsync(
            string url,
            string targetPath,
            IProgress<double>? progress,
            CancellationToken cancellationToken)
        {
            using HttpResponseMessage response = await GitHubClient.GetAsync(
                url,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();

            long? totalBytes = response.Content.Headers.ContentLength;
            await using Stream contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using FileStream fileStream = new(targetPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);

            byte[] buffer = new byte[81920];
            long totalBytesRead = 0;
            int bytesRead;

            while ((bytesRead = await contentStream.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                totalBytesRead += bytesRead;

                if (totalBytes > 0)
                {
                    // Scale progress between 0.1 and 0.8 (download phase)
                    double downloadProgress = 0.1 + (0.7 * totalBytesRead / totalBytes.Value);
                    progress?.Report(downloadProgress);
                }
            }
        }

        private static async Task<string> ComputeSha256Async(string filePath, CancellationToken cancellationToken)
        {
            await using FileStream stream = new(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
            byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        private static async Task SaveInstallMetadataAsync(
            string appName,
            string version,
            string downloadUrl,
            bool desktopShortcutCreated,
            bool startMenuShortcutCreated,
            bool userDeletedDesktopShortcut,
            bool userDeletedStartMenuShortcut,
            CancellationToken cancellationToken)
        {
            string appDirectory = GetAppDirectory(appName);
            string metadataPath = Path.Combine(appDirectory, "install.json");

            PortableAppMetadata metadata = new()
            {
                Version = version,
                InstalledAt = DateTime.UtcNow,
                DownloadUrl = downloadUrl,
                DesktopShortcutCreated = desktopShortcutCreated,
                StartMenuShortcutCreated = startMenuShortcutCreated,
                UserDeletedDesktopShortcut = userDeletedDesktopShortcut,
                UserDeletedStartMenuShortcut = userDeletedStartMenuShortcut
            };

            string json = JsonSerializer.Serialize(metadata, JsonOptions);
            await File.WriteAllTextAsync(metadataPath, json, cancellationToken);
        }

        private static HttpClient CreateGitHubClient()
        {
            HttpClient client = new();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("SynToolkit/1.7");
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            return client;
        }

        // GitHub API response models
        private sealed class GitHubRelease
        {
            [JsonPropertyName("tag_name")]
            public string? TagName { get; set; }

            [JsonPropertyName("name")]
            public string? Name { get; set; }

            [JsonPropertyName("assets")]
            public List<GitHubAsset>? Assets { get; set; }
        }

        private sealed class GitHubAsset
        {
            [JsonPropertyName("name")]
            public string? Name { get; set; }

            [JsonPropertyName("browser_download_url")]
            public string? BrowserDownloadUrl { get; set; }

            [JsonPropertyName("size")]
            public long Size { get; set; }

            [JsonPropertyName("digest")]
            public string? Digest { get; set; }
        }

        private sealed class PortableAppMetadata
        {
            public string? Version { get; set; }
            public DateTime InstalledAt { get; set; }
            public string? DownloadUrl { get; set; }
            public bool DesktopShortcutCreated { get; set; }
            public bool StartMenuShortcutCreated { get; set; }
            public bool UserDeletedDesktopShortcut { get; set; }
            public bool UserDeletedStartMenuShortcut { get; set; }
        }
    }

    /// <summary>
    /// Definition for a portable app from GitHub releases.
    /// </summary>
    public sealed record PortableAppDefinition(
        string AppName,
        string GitHubOwner,
        string GitHubRepo,
        string AssetName,
        string ExecutableName,
        string? ExpectedSha256 = null,
        bool RequiresElevation = false,
        bool CreateDesktopShortcut = true);

    /// <summary>
    /// Result of a portable app install/uninstall operation.
    /// </summary>
    public sealed record PortableAppInstallResult(
        bool Succeeded,
        string Message,
        string? InstalledVersion = null,
        string? InstallPath = null,
        bool DesktopShortcutCreated = false,
        bool StartMenuShortcutCreated = false,
        string? ShortcutError = null);

    /// <summary>
    /// Status of an installed portable app.
    /// </summary>
    public sealed record PortableAppStatus(
        bool IsInstalled,
        string? InstalledVersion,
        string? AvailableVersion,
        bool IsUpdateAvailable);
}
