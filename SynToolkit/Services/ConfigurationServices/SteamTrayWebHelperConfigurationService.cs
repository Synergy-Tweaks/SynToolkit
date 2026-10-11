#nullable enable

using SynToolkit.Services.SteamTrayWebHelper;
using SynToolkit.Stores;
using SynToolkit.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace SynToolkit.Services.ConfigurationServices
{
    /// <summary>
    /// Installs SteamTrayWebHelper's umpdc.dll beside steam.exe (download + SHA-256 verify).
    /// Enabled when the DLL is present. Reversible by deleting that one file.
    /// Based on https://github.com/NobbyBollocks/SteamTrayWebHelper (GPL-3.0; not affiliated with Valve).
    /// </summary>
    internal sealed class SteamTrayWebHelperConfigurationService : IConfigurationService
    {
        private static readonly HttpClient Http = CreateHttpClient();

        private readonly ConfigurationStore _store;

        public SteamTrayWebHelperConfigurationService(
            [FromKeyedServices("SteamTrayWebHelper")] ConfigurationStore store)
        {
            _store = store;
        }

        public bool IsEnabled()
        {
            SteamTrayWebHelperPaths.SteamPathResult steam = ResolveSteamOrThrow();
            return SteamTrayWebHelperPaths.IsDllInstalled(steam.Directory, File.Exists);
        }

        public void Enable()
        {
            SteamTrayWebHelperPaths.SteamPathResult steam = ResolveSteamOrThrow();
            string destination = SteamTrayWebHelperPaths.GetDllPath(steam.Directory);

            if (File.Exists(destination)
                && FileMatchesExpectedHash(destination))
            {
                _store.CurrentSetting = true;
                App.logger.Info("[SteamTrayWebHelper] umpdc.dll already present with expected hash.");
                return;
            }

            EnsureSteamClosedForFileChange(isInstall: true);

            string tempRoot = Path.Combine(Path.GetTempPath(), "SynToolkit", "SteamTrayWebHelper");
            Directory.CreateDirectory(tempRoot);
            string tempDll = Path.Combine(tempRoot, SteamTrayWebHelperConstants.DllFileName);

            try
            {
                DownloadAndVerifyDll(tempDll);
                File.Copy(tempDll, destination, overwrite: true);
                App.logger.Info("[SteamTrayWebHelper] Installed {0}", destination);
            }
            catch (UnauthorizedAccessException exception)
            {
                throw new InvalidOperationException(
                    "Unable to write umpdc.dll into the Steam folder. SynToolkit needs permission to modify that directory.",
                    exception);
            }
            finally
            {
                TryDeleteFile(tempDll);
            }

            _store.CurrentSetting = IsEnabled();
            if (!_store.CurrentSetting)
            {
                throw new InvalidOperationException("umpdc.dll was downloaded but could not be verified in the Steam folder.");
            }
        }

        public void Disable()
        {
            SteamTrayWebHelperPaths.SteamPathResult steam = ResolveSteamOrThrow();
            string destination = SteamTrayWebHelperPaths.GetDllPath(steam.Directory);

            if (!File.Exists(destination))
            {
                _store.CurrentSetting = false;
                return;
            }

            EnsureSteamClosedForFileChange(isInstall: false);

            try
            {
                File.Delete(destination);
                App.logger.Info("[SteamTrayWebHelper] Removed {0}", destination);
            }
            catch (FileNotFoundException)
            {
                // Idempotent: already gone.
            }
            catch (DirectoryNotFoundException)
            {
                // Idempotent: Steam folder missing mid-flight.
            }
            catch (IOException exception)
            {
                throw new IOException(
                    "Unable to delete umpdc.dll because it is locked. Close Steam completely (including the tray icon) and try again.",
                    exception);
            }
            catch (UnauthorizedAccessException exception)
            {
                throw new InvalidOperationException(
                    "Unable to delete umpdc.dll from the Steam folder. SynToolkit needs permission to modify that directory.",
                    exception);
            }

            _store.CurrentSetting = IsEnabled();
        }

        private static SteamTrayWebHelperPaths.SteamPathResult ResolveSteamOrThrow()
        {
            SteamTrayWebHelperPaths.SteamPathResult result = SteamTrayWebHelperPaths.ResolveSteamDirectory(
                ReadHkcuSteamPath,
                ReadHklmInstallPath,
                Directory.Exists,
                File.Exists);

            if (!result.SteamExeExists)
            {
                throw new NotSupportedException(
                    "Steam was not found. Install the 64-bit Steam client, or ensure steam.exe is available, then reopen this tweak.");
            }

            return result;
        }

        private static string? ReadHkcuSteamPath()
        {
            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
                return key?.GetValue("SteamPath")?.ToString();
            }
            catch (Exception exception)
            {
                App.logger.Debug(exception, "[SteamTrayWebHelper] Unable to read HKCU SteamPath.");
                return null;
            }
        }

        private static string? ReadHklmInstallPath()
        {
            try
            {
                using RegistryKey? key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam");
                return key?.GetValue("InstallPath")?.ToString();
            }
            catch (Exception exception)
            {
                App.logger.Debug(exception, "[SteamTrayWebHelper] Unable to read HKLM InstallPath.");
                return null;
            }
        }

        private static void EnsureSteamClosedForFileChange(bool isInstall)
        {
            if (!IsAnySteamProcessRunning())
            {
                return;
            }

            string action = isInstall ? "installed or replaced" : "removed";
            bool confirmed = ConfirmOnUiThread(
                title: App.GetValueFromItemList("SteamTrayWebHelper_CloseSteamTitle"),
                content: string.Format(
                    App.GetValueFromItemList("SteamTrayWebHelper_CloseSteamDesc"),
                    action),
                primaryText: App.GetValueFromItemList("SteamTrayWebHelper_CloseSteamConfirm"));

            if (!confirmed)
            {
                throw new OperationCanceledException(
                    "Cancelled. Close Steam completely (including the tray icon) and try again.");
            }

            TryGracefulSteamShutdown();
            if (WaitUntilSteamExits(SteamTrayWebHelperConstants.SteamShutdownWaitSeconds))
            {
                return;
            }

            foreach (string processName in SteamTrayWebHelperConstants.SteamProcessNames)
            {
                try
                {
                    ProcessHelper.KillProcessByName(processName + ".exe");
                }
                catch (Exception exception)
                {
                    App.logger.Warn(exception, "[SteamTrayWebHelper] Failed to terminate {0}.", processName);
                }
            }

            if (!WaitUntilSteamExits(5))
            {
                throw new InvalidOperationException(
                    "Steam is still running and could not be closed. Exit Steam from the tray icon and try again.");
            }
        }

        private static void TryGracefulSteamShutdown()
        {
            try
            {
                SteamTrayWebHelperPaths.SteamPathResult steam = SteamTrayWebHelperPaths.ResolveSteamDirectory(
                    ReadHkcuSteamPath,
                    ReadHklmInstallPath,
                    Directory.Exists,
                    File.Exists);
                string steamExe = Path.Combine(steam.Directory, SteamTrayWebHelperConstants.SteamExecutableName);
                if (!File.Exists(steamExe))
                {
                    return;
                }

                // Launch -shutdown as the interactive user so Steam is not elevated.
                try
                {
                    InteractiveUserProcessHelper.RunAsInteractiveUser(steamExe, "-shutdown", timeoutMilliseconds: 5_000);
                }
                catch (TimeoutException)
                {
                    // Expected: the shutdown helper may outlive a short wait.
                }
            }
            catch (Exception exception)
            {
                App.logger.Warn(exception, "[SteamTrayWebHelper] Graceful Steam shutdown failed; will force-close if needed.");
            }
        }

        private static bool IsAnySteamProcessRunning()
        {
            foreach (string processName in SteamTrayWebHelperConstants.SteamProcessNames)
            {
                Process[] processes = Process.GetProcessesByName(processName);
                try
                {
                    if (processes.Length > 0)
                    {
                        return true;
                    }
                }
                finally
                {
                    foreach (Process process in processes)
                    {
                        process.Dispose();
                    }
                }
            }

            return false;
        }

        private static bool WaitUntilSteamExits(int seconds)
        {
            for (int i = 0; i < seconds; i++)
            {
                if (!IsAnySteamProcessRunning())
                {
                    return true;
                }

                Thread.Sleep(1_000);
            }

            return !IsAnySteamProcessRunning();
        }

        private static void DownloadAndVerifyDll(string destinationPath)
        {
            try
            {
                using HttpResponseMessage response = Http
                    .GetAsync(SteamTrayWebHelperConstants.DllDownloadUrl)
                    .GetAwaiter()
                    .GetResult();
                response.EnsureSuccessStatusCode();
                byte[] bytes = response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
                File.WriteAllBytes(destinationPath, bytes);
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or IOException)
            {
                throw new InvalidOperationException(
                    "Unable to download umpdc.dll from GitHub. Check your network connection and try again.",
                    exception);
            }

            string actualHash = ComputeFileSha256Hex(destinationPath);
            if (!SteamTrayWebHelperPaths.HashesMatch(SteamTrayWebHelperConstants.ExpectedSha256, actualHash))
            {
                TryDeleteFile(destinationPath);
                throw new InvalidDataException(
                    "Downloaded umpdc.dll failed the pinned SHA-256 integrity check. The file was discarded.");
            }

            string? publishedHash = TryDownloadPublishedSha256();
            if (publishedHash is not null
                && !SteamTrayWebHelperPaths.HashesMatch(SteamTrayWebHelperConstants.ExpectedSha256, publishedHash))
            {
                TryDeleteFile(destinationPath);
                throw new InvalidDataException(
                    "The published umpdc.dll.sha256 does not match SynToolkit's pinned hash. Refusing to install.");
            }

            if (publishedHash is not null
                && !SteamTrayWebHelperPaths.HashesMatch(publishedHash, actualHash))
            {
                TryDeleteFile(destinationPath);
                throw new InvalidDataException(
                    "Downloaded umpdc.dll does not match the published .sha256 file. The file was discarded.");
            }
        }

        private static string? TryDownloadPublishedSha256()
        {
            try
            {
                using HttpResponseMessage response = Http
                    .GetAsync(SteamTrayWebHelperConstants.Sha256DownloadUrl)
                    .GetAwaiter()
                    .GetResult();
                if (!response.IsSuccessStatusCode)
                {
                    App.logger.Debug(
                        "[SteamTrayWebHelper] No published .sha256 asset ({0}); relying on pinned hash.",
                        (int)response.StatusCode);
                    return null;
                }

                string body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                return SteamTrayWebHelperPaths.TryParseSha256FileContents(body);
            }
            catch (Exception exception)
            {
                App.logger.Debug(exception, "[SteamTrayWebHelper] Unable to fetch published .sha256; relying on pinned hash.");
                return null;
            }
        }

        private static bool FileMatchesExpectedHash(string path)
        {
            try
            {
                return SteamTrayWebHelperPaths.HashesMatch(
                    SteamTrayWebHelperConstants.ExpectedSha256,
                    ComputeFileSha256Hex(path));
            }
            catch (Exception exception)
            {
                App.logger.Debug(exception, "[SteamTrayWebHelper] Unable to hash existing DLL.");
                return false;
            }
        }

        private static string ComputeFileSha256Hex(string path)
        {
            using FileStream stream = File.OpenRead(path);
            byte[] hash = SHA256.HashData(stream);
            return Convert.ToHexString(hash);
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception exception)
            {
                App.logger.Debug(exception, "[SteamTrayWebHelper] Unable to delete temporary file {0}.", path);
            }
        }

        private static bool ConfirmOnUiThread(string title, string content, string primaryText)
        {
            DispatcherQueue? queue = App.m_window?.DispatcherQueue ?? DispatcherQueue.GetForCurrentThread();
            if (queue is null)
            {
                throw new InvalidOperationException("Unable to show the Steam close confirmation dialog.");
            }

            if (queue.HasThreadAccess)
            {
                return ShowConfirmDialogAsync(title, content, primaryText).GetAwaiter().GetResult();
            }

            TaskCompletionSource<bool> completion = new();
            if (!queue.TryEnqueue(async () =>
                {
                    try
                    {
                        completion.TrySetResult(await ShowConfirmDialogAsync(title, content, primaryText));
                    }
                    catch (Exception exception)
                    {
                        completion.TrySetException(exception);
                    }
                }))
            {
                throw new InvalidOperationException("Unable to open the Steam close confirmation dialog on the UI thread.");
            }

            return completion.Task.GetAwaiter().GetResult();
        }

        private static async Task<bool> ShowConfirmDialogAsync(string title, string content, string primaryText)
        {
            ContentDialog dialog = new()
            {
                Title = title,
                Content = content,
                PrimaryButtonText = primaryText,
                CloseButtonText = App.GetValueFromItemList("Later"),
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = App.XamlRoot,
                Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
            };

            ContentDialogResult result = await dialog.ShowAsync();
            return result == ContentDialogResult.Primary;
        }

        private static HttpClient CreateHttpClient()
        {
            HttpClient client = new()
            {
                Timeout = TimeSpan.FromSeconds(SteamTrayWebHelperConstants.DownloadTimeoutSeconds),
            };
            client.DefaultRequestHeaders.UserAgent.ParseAdd(SteamTrayWebHelperConstants.UserAgent);
            return client;
        }
    }
}
