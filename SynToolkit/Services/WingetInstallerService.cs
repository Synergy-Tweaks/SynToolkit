#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Principal;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using SynToolkit.Utils;
using YamlDotNet.Serialization;

namespace SynToolkit.Services
{
    public enum WingetInstallDisposition
    {
        Completed,
        AlreadySatisfied,
        Failed
    }

    public sealed record WingetInstallResult(bool Succeeded, int ExitCode, string Output, WingetInstallDisposition Disposition);

    public sealed record CuratedPackageProbe(
        string PackageIdentifier,
        IReadOnlyList<string> InstalledDisplayNamePrefixes,
        string PackageSource = "winget");

    public sealed record CuratedPackageStatus(
        string PackageIdentifier,
        bool IsInstalled,
        bool IsUpdateAvailable,
        string? InstalledVersion,
        string? AvailableVersion,
        bool IsUpdateCheckComplete);

    /// <summary>
    /// Installs curated packages through the Windows Package Manager community source.
    /// Each invocation is non-interactive and can be canceled with its process tree.
    /// </summary>
    public sealed class WingetInstallerService
    {
        private static readonly TimeSpan InstallTimeout = TimeSpan.FromMinutes(15);
        private static readonly TimeSpan UninstallTimeout = TimeSpan.FromMinutes(15);
        private static readonly HttpClient ManifestClient = CreateManifestClient();
        private static readonly HttpClient ManifestCatalogClient = CreateManifestCatalogClient();
        private static readonly SemaphoreSlim ManifestLookupSemaphore = new(4, 4);
        private readonly AppFetchService _appFetchService;
        private readonly ConcurrentDictionary<string, string> _latestVersionCache =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, WingetPackageInstallMetadata> _installMetadataCache =
            new(StringComparer.OrdinalIgnoreCase);
        private bool? _isWingetAvailable;

        public WingetInstallerService(AppFetchService appFetchService)
        {
            _appFetchService = appFetchService;
        }

        public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
        {
            if (_isWingetAvailable.HasValue)
            {
                return _isWingetAvailable.Value;
            }

            try
            {
                WingetInstallResult result = await RunWingetAsync(["--version"], TimeSpan.FromSeconds(15), false, cancellationToken);
                _isWingetAvailable = result.Succeeded;
            }
            catch (Win32Exception exception)
            {
                App.logger.Warn(exception, "[Installers] Windows Package Manager was not found.");
                _isWingetAvailable = false;
            }

            return _isWingetAvailable.Value;
        }

        public async Task<WingetInstallResult> InstallAsync(
            string packageIdentifier,
            string packageSource = "winget",
            string? silentArgumentsOverride = null,
            bool isUpdate = false,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(packageIdentifier);

            if (await IsAvailableAsync(cancellationToken))
            {
                WingetPackageInstallMetadata installMetadata = await GetInstallMetadataAsync(packageIdentifier, cancellationToken);
                return await RunWingetAsync(
                    BuildInstallArguments(packageIdentifier, packageSource, isUpdate, installMetadata),
                    InstallTimeout,
                    installMetadata.MustRunUnelevated,
                    cancellationToken);
            }

            return string.Equals(packageSource, "winget", StringComparison.OrdinalIgnoreCase)
                ? await InstallFromPackageManifestAsync(packageIdentifier, silentArgumentsOverride, progress, cancellationToken)
                : new WingetInstallResult(false, -1, "Microsoft Store installs require Windows Package Manager.", WingetInstallDisposition.Failed);
        }

        public async Task<WingetInstallResult> UninstallAsync(
            string packageIdentifier,
            IReadOnlyList<string> installedDisplayNamePrefixes,
            string packageSource = "winget",
            CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(packageIdentifier);
            ArgumentNullException.ThrowIfNull(installedDisplayNamePrefixes);

            WingetInstallResult? wingetResult = null;
            if (await IsAvailableAsync(cancellationToken))
            {
                wingetResult = await RunWingetAsync(
                    [
                        "uninstall",
                        "--exact",
                        "--id",
                        packageIdentifier,
                        "--source",
                        packageSource,
                        "--silent",
                        "--accept-source-agreements",
                        "--disable-interactivity"
                    ],
                    UninstallTimeout,
                    false,
                    cancellationToken);

                if (wingetResult.Succeeded)
                {
                    return wingetResult;
                }

                // A canceled uninstall must stay canceled. Other failures may mean
                // WinGet cannot correlate a registry-installed copy with its package ID.
                if (unchecked((uint)wingetResult.ExitCode) == 0x8A15010C)
                {
                    return wingetResult;
                }
            }

            InstalledDesktopApplication? installedApplication = await Task.Run(
                () => FindInstalledApplication(installedDisplayNamePrefixes),
                cancellationToken);
            if (installedApplication == null)
            {
                return new WingetInstallResult(true, 0, "The app is no longer detected on this PC.", WingetInstallDisposition.Completed);
            }

            string? uninstallCommand = !string.IsNullOrWhiteSpace(installedApplication.QuietUninstallString)
                ? installedApplication.QuietUninstallString
                : installedApplication.UninstallString;
            if (string.IsNullOrWhiteSpace(uninstallCommand))
            {
                return wingetResult ?? new WingetInstallResult(
                    false,
                    -1,
                    "Windows did not provide an uninstall command for this app.",
                    WingetInstallDisposition.Failed);
            }

            return await RunRegisteredUninstallerAsync(uninstallCommand, cancellationToken);
        }

        public async Task<IReadOnlyList<CuratedPackageStatus>> DetectPackageStatusesAsync(
            IReadOnlyList<CuratedPackageProbe> probes,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(probes);

            IReadOnlyList<InstalledDesktopApplication> installedApplications = await Task.Run(
                ReadInstalledDesktopApplications,
                cancellationToken);

            Task<CuratedPackageStatus>[] statusTasks = probes
                .Select(probe => DetectPackageStatusAsync(probe, installedApplications, cancellationToken))
                .ToArray();
            return await Task.WhenAll(statusTasks);
        }

        private async Task<CuratedPackageStatus> DetectPackageStatusAsync(
            CuratedPackageProbe probe,
            IReadOnlyList<InstalledDesktopApplication> installedApplications,
            CancellationToken cancellationToken)
        {
            InstalledDesktopApplication? installedApplication = installedApplications
                .Where(application => probe.InstalledDisplayNamePrefixes.Any(prefix =>
                    MatchesInstalledDisplayName(application.DisplayName, prefix)))
                .OrderByDescending(application => ParseLooseVersion(application.DisplayVersion))
                .FirstOrDefault();

            if (installedApplication == null)
            {
                return new CuratedPackageStatus(probe.PackageIdentifier, false, false, null, null, true);
            }

            if (string.Equals(probe.PackageSource, "msstore", StringComparison.OrdinalIgnoreCase))
            {
                return new CuratedPackageStatus(
                    probe.PackageIdentifier,
                    true,
                    false,
                    installedApplication.DisplayVersion,
                    null,
                    false);
            }

            string? availableVersion = null;
            bool isUpdateAvailable = false;
            bool isUpdateCheckComplete = false;
            try
            {
                availableVersion = await GetLatestPackageVersionAsync(probe.PackageIdentifier, cancellationToken);
                if (TryParseLooseVersion(installedApplication.DisplayVersion, out Version installedVersion) &&
                    TryParseLooseVersion(availableVersion, out Version publishedVersion))
                {
                    isUpdateCheckComplete = true;
                    isUpdateAvailable = publishedVersion > installedVersion;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                App.logger.Debug(
                    exception,
                    "[Installers] Unable to check the published version for {PackageIdentifier}.",
                    probe.PackageIdentifier);
            }

            return new CuratedPackageStatus(
                probe.PackageIdentifier,
                true,
                isUpdateAvailable,
                installedApplication.DisplayVersion,
                availableVersion,
                isUpdateCheckComplete);
        }

        private async Task<WingetInstallResult> InstallFromPackageManifestAsync(
            string packageIdentifier,
            string? silentArgumentsOverride,
            IProgress<double>? progress,
            CancellationToken cancellationToken)
        {
            try
            {
                AppFetchService.StorePackageDto? compatiblePackage =
                    await ResolveLatestPackageAsync(packageIdentifier, silentArgumentsOverride, cancellationToken);

                if (compatiblePackage == null)
                {
                    return new WingetInstallResult(false, -1, "No compatible installer was found for this system architecture.", WingetInstallDisposition.Failed);
                }

                await _appFetchService.DownloadAndInstallPackagesAsync(
                    [compatiblePackage],
                    progress ?? new Progress<double>(_ => { }),
                    cancellationToken);

                return new WingetInstallResult(true, 0, "Installed directly from the Microsoft package manifest.", WingetInstallDisposition.Completed);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                App.logger.Error(
                    exception,
                    "[Installers] Direct package-manifest installation failed for {PackageIdentifier}.",
                    packageIdentifier);
                return new WingetInstallResult(false, -1, exception.Message, WingetInstallDisposition.Failed);
            }
        }

        private async Task<WingetPackageInstallMetadata> GetInstallMetadataAsync(
            string packageIdentifier,
            CancellationToken cancellationToken)
        {
            if (_installMetadataCache.TryGetValue(packageIdentifier, out WingetPackageInstallMetadata? cachedMetadata))
            {
                return cachedMetadata;
            }

            try
            {
                ResolvedWingetManifest resolvedManifest = await ResolveInstallerManifestAsync(packageIdentifier, cancellationToken);
                WingetInstallerManifest manifest = resolvedManifest.Manifest;
                WingetInstallerEntry? installer = resolvedManifest.CompatibleInstaller;

                string? scope = installer?.Scope ?? manifest.Scope;
                bool installLocationRequired = installer?.InstallLocationRequired ?? manifest.InstallLocationRequired ?? false;
                string? defaultInstallLocation = installer?.InstallationMetadata?.DefaultInstallLocation
                    ?? manifest.InstallationMetadata?.DefaultInstallLocation;
                string? elevationRequirement = installer?.ElevationRequirement ?? manifest.ElevationRequirement;

                WingetPackageInstallMetadata metadata = new(
                    scope,
                    installLocationRequired,
                    ResolveInstallLocation(packageIdentifier, scope, defaultInstallLocation),
                    elevationRequirement,
                    MustRunUnelevated(scope, elevationRequirement));
                _installMetadataCache.TryAdd(packageIdentifier, metadata);
                return metadata;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                App.logger.Debug(
                    exception,
                    "[Installers] Unable to resolve WinGet manifest install metadata for {PackageIdentifier}; using default invocation.",
                    packageIdentifier);
                return new WingetPackageInstallMetadata(null, false, null, null, false);
            }
        }

        private async Task<AppFetchService.StorePackageDto?> ResolveLatestPackageAsync(
            string packageIdentifier,
            string? silentArgumentsOverride,
            CancellationToken cancellationToken)
        {
            ResolvedWingetManifest resolvedManifest = await ResolveInstallerManifestAsync(packageIdentifier, cancellationToken);
            WingetInstallerManifest manifest = resolvedManifest.Manifest;
            WingetInstallerEntry? compatibleInstaller = resolvedManifest.CompatibleInstaller;
            if (compatibleInstaller == null || string.IsNullOrWhiteSpace(compatibleInstaller.InstallerUrl))
            {
                return null;
            }

            string installerType = compatibleInstaller.InstallerType ?? manifest.InstallerType ?? string.Empty;
            string extension = Path.GetExtension(new Uri(compatibleInstaller.InstallerUrl).AbsolutePath).TrimStart('.');
            if (string.IsNullOrWhiteSpace(extension))
            {
                extension = installerType.Equals("wix", StringComparison.OrdinalIgnoreCase) ? "msi" : "exe";
            }

            string? silentArguments =
                compatibleInstaller.InstallerSwitches?.Silent ??
                compatibleInstaller.InstallerSwitches?.SilentWithProgress ??
                manifest.InstallerSwitches?.Silent ??
                manifest.InstallerSwitches?.SilentWithProgress ??
                silentArgumentsOverride ??
                GetDefaultSilentArguments(installerType);

            return new AppFetchService.StorePackageDto
            {
                Name = packageIdentifier + "-" + compatibleInstaller.Architecture,
                FileExtension = extension,
                ResourceUri = compatibleInstaller.InstallerUrl,
                LastModified = DateTime.UtcNow,
                PackageId = "xp-curated-" + packageIdentifier,
                Checksum = compatibleInstaller.InstallerSha256,
                CommandLines = silentArguments
            };
        }

        private async Task<ResolvedWingetManifest> ResolveInstallerManifestAsync(
            string packageIdentifier,
            CancellationToken cancellationToken)
        {
            string packagePath = GetPackageManifestPath(packageIdentifier);
            string latestVersion = await GetLatestPackageVersionAsync(packageIdentifier, cancellationToken);
            string escapedVersion = Uri.EscapeDataString(latestVersion);
            string manifestFileName = Uri.EscapeDataString(packageIdentifier + ".installer.yaml");
            string manifestUrl =
                $"https://raw.githubusercontent.com/microsoft/winget-pkgs/master/{packagePath}/{escapedVersion}/{manifestFileName}";
            string manifestYaml = await ManifestClient.GetStringAsync(manifestUrl, cancellationToken);

            IDeserializer deserializer = new DeserializerBuilder()
                .IgnoreUnmatchedProperties()
                .Build();
            WingetInstallerManifest manifest = deserializer.Deserialize<WingetInstallerManifest>(manifestYaml);
            return new ResolvedWingetManifest(manifest, SelectCompatibleInstaller(manifest.Installers));
        }

        private async Task<string> GetLatestPackageVersionAsync(
            string packageIdentifier,
            CancellationToken cancellationToken)
        {
            if (_latestVersionCache.TryGetValue(packageIdentifier, out string? cachedVersion))
            {
                return cachedVersion;
            }

            string packagePath = GetPackageManifestPath(packageIdentifier);
            string catalogUrl = $"https://github.com/microsoft/winget-pkgs/tree/master/{packagePath}";

            await ManifestLookupSemaphore.WaitAsync(cancellationToken);
            try
            {
                string catalogHtml = await ManifestCatalogClient.GetStringAsync(catalogUrl, cancellationToken);
                string versionLinkPrefix = $"/microsoft/winget-pkgs/tree/master/{packagePath}/";
                string? latestVersion = Regex.Matches(
                        catalogHtml,
                        Regex.Escape(versionLinkPrefix) + "([^\"?#/<>&]+)",
                        RegexOptions.CultureInvariant)
                    .Select(match => Uri.UnescapeDataString(match.Groups[1].Value))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Where(version => TryParseVersion(version, out _))
                    .OrderByDescending(ParseVersion)
                    .FirstOrDefault();

                if (latestVersion == null)
                {
                    throw new InvalidOperationException(
                        $"No published WinGet manifest was found for {packageIdentifier}.");
                }

                _latestVersionCache.TryAdd(packageIdentifier, latestVersion);
                return latestVersion;
            }
            finally
            {
                ManifestLookupSemaphore.Release();
            }
        }

        private static string GetPackageManifestPath(string packageIdentifier)
        {
            string[] identifierSegments = packageIdentifier.Split('.');
            string escapedPackagePath = string.Join("/", identifierSegments.Select(Uri.EscapeDataString));
            string partition = char.ToLowerInvariant(packageIdentifier[0]).ToString();
            return $"manifests/{partition}/{escapedPackagePath}";
        }

        private static IReadOnlyList<InstalledDesktopApplication> ReadInstalledDesktopApplications()
        {
            List<InstalledDesktopApplication> applications = new();
            int readableRootCount = 0;
            readableRootCount += ReadUninstallRegistryView(
                applications, RegistryHive.LocalMachine, RegistryView.Registry64) ? 1 : 0;
            readableRootCount += ReadUninstallRegistryView(
                applications, RegistryHive.LocalMachine, RegistryView.Registry32) ? 1 : 0;
            readableRootCount += ReadUninstallRegistryView(
                applications, RegistryHive.CurrentUser, RegistryView.Registry64) ? 1 : 0;
            readableRootCount += ReadUninstallRegistryView(
                applications, RegistryHive.CurrentUser, RegistryView.Registry32) ? 1 : 0;

            if (readableRootCount == 0)
            {
                throw new InvalidOperationException("Windows did not allow access to the installed-program registry.");
            }

            return applications;
        }

        private static InstalledDesktopApplication? FindInstalledApplication(
            IReadOnlyList<string> installedDisplayNamePrefixes) =>
            ReadInstalledDesktopApplications()
                .Where(application => installedDisplayNamePrefixes.Any(prefix =>
                    MatchesInstalledDisplayName(application.DisplayName, prefix)))
                .OrderByDescending(application => ParseLooseVersion(application.DisplayVersion))
                .FirstOrDefault();

        private static bool ReadUninstallRegistryView(
            ICollection<InstalledDesktopApplication> applications,
            RegistryHive hive,
            RegistryView view)
        {
            try
            {
                using RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, view);
                using RegistryKey? uninstallKey = baseKey.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                if (uninstallKey == null)
                {
                    return false;
                }

                foreach (string subKeyName in uninstallKey.GetSubKeyNames())
                {
                    try
                    {
                        using RegistryKey? applicationKey = uninstallKey.OpenSubKey(subKeyName);
                        string? displayName = applicationKey?.GetValue("DisplayName") as string;
                        if (string.IsNullOrWhiteSpace(displayName))
                        {
                            continue;
                        }

                        applications.Add(new InstalledDesktopApplication(
                            displayName.Trim(),
                            (applicationKey?.GetValue("DisplayVersion") as string)?.Trim(),
                            (applicationKey?.GetValue("UninstallString") as string)?.Trim(),
                            (applicationKey?.GetValue("QuietUninstallString") as string)?.Trim()));
                    }
                    catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or SecurityException)
                    {
                        App.logger.Debug(exception, "[Installers] Unable to read uninstall entry {EntryName}.", subKeyName);
                    }
                }

                return true;
            }
            catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or SecurityException)
            {
                App.logger.Debug(
                    exception,
                    "[Installers] Unable to read the {Hive}/{View} uninstall registry.",
                    hive,
                    view);
                return false;
            }
        }

        private static bool MatchesInstalledDisplayName(string displayName, string prefix) =>
            displayName.Equals(prefix, StringComparison.OrdinalIgnoreCase) ||
            displayName.StartsWith(prefix + " ", StringComparison.OrdinalIgnoreCase) ||
            displayName.StartsWith(prefix + " (", StringComparison.OrdinalIgnoreCase) ||
            displayName.StartsWith(prefix + ".", StringComparison.OrdinalIgnoreCase);

        private static async Task<WingetInstallResult> RunRegisteredUninstallerAsync(
            string commandLine,
            CancellationToken cancellationToken)
        {
            if (!TryCreateUninstallStartInfo(commandLine, out ProcessStartInfo startInfo))
            {
                return new WingetInstallResult(false, -1, "Windows provided an invalid uninstall command.", WingetInstallDisposition.Failed);
            }

            using Process process = new() { StartInfo = startInfo };
            try
            {
                if (!process.Start())
                {
                    return new WingetInstallResult(false, -1, "Unable to start the app's uninstaller.", WingetInstallDisposition.Failed);
                }

                using CancellationTokenSource timeoutSource =
                    CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutSource.CancelAfter(UninstallTimeout);
                try
                {
                    await process.WaitForExitAsync(timeoutSource.Token);
                }
                catch (OperationCanceledException)
                {
                    TryKillProcessTree(process);
                    if (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }

                    return new WingetInstallResult(
                        false,
                        -1,
                        $"Uninstallation timed out after {UninstallTimeout.TotalMinutes:0} minutes.",
                        WingetInstallDisposition.Failed);
                }

                bool succeeded = process.ExitCode is 0 or 1641 or 3010;
                return new WingetInstallResult(
                    succeeded,
                    process.ExitCode,
                    succeeded
                        ? "The app's registered uninstaller completed."
                        : $"The app's registered uninstaller exited with code {process.ExitCode}.",
                    succeeded ? WingetInstallDisposition.Completed : WingetInstallDisposition.Failed);
            }
            catch (Win32Exception exception)
            {
                App.logger.Warn(exception, "[Installers] Unable to start a registered app uninstaller.");
                return new WingetInstallResult(false, exception.NativeErrorCode, exception.Message, WingetInstallDisposition.Failed);
            }
        }

        private static bool TryCreateUninstallStartInfo(
            string commandLine,
            out ProcessStartInfo startInfo)
        {
            startInfo = null!;
            string expandedCommand = Environment.ExpandEnvironmentVariables(commandLine).Trim();
            if (expandedCommand.Length == 0)
            {
                return false;
            }

            string executable;
            string arguments;
            Match quotedExecutable = Regex.Match(
                expandedCommand,
                @"^\s*""(?<executable>[^""]+\.exe)""\s*(?<arguments>.*)$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (quotedExecutable.Success)
            {
                executable = quotedExecutable.Groups["executable"].Value;
                arguments = quotedExecutable.Groups["arguments"].Value;
            }
            else
            {
                Match executableMatch = Regex.Match(
                    expandedCommand,
                    @"^\s*(?<executable>.+?\.exe)\s*(?<arguments>.*)$",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                if (!executableMatch.Success)
                {
                    return false;
                }

                executable = executableMatch.Groups["executable"].Value.Trim().Trim('"');
                arguments = executableMatch.Groups["arguments"].Value.Trim();
            }

            if (Path.GetFileName(executable).Equals("msiexec.exe", StringComparison.OrdinalIgnoreCase))
            {
                arguments = Regex.Replace(
                    arguments,
                    @"(^|\s)/I(?=\s*\{)",
                    "$1/X",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            }

            startInfo = new ProcessStartInfo(executable)
            {
                Arguments = arguments,
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Normal
            };
            return true;
        }

        private static Version ParseLooseVersion(string? value) =>
            TryParseLooseVersion(value, out Version version) ? version : new Version(0, 0);

        private static bool TryParseLooseVersion(string? value, out Version version)
        {
            version = new Version(0, 0);
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            Match match = Regex.Match(value, @"\d+(?:\.\d+)+", RegexOptions.CultureInvariant);
            if (!match.Success)
            {
                return false;
            }

            string normalizedVersion = string.Join('.', match.Value.Split('.').Take(4));
            return Version.TryParse(normalizedVersion, out version!);
        }

        private static string? GetDefaultSilentArguments(string installerType) =>
            installerType.ToLowerInvariant() switch
            {
                "nullsoft" => "/S",
                "inno" => "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-",
                "burn" => "/quiet /norestart",
                _ => null
            };

        private static WingetInstallerEntry? SelectCompatibleInstaller(
            IReadOnlyList<WingetInstallerEntry>? installers)
        {
            if (installers == null || installers.Count == 0)
            {
                return null;
            }

            List<WingetInstallerEntry> supportedInstallers = installers
                .Where(installer =>
                {
                    if (!Uri.TryCreate(installer.InstallerUrl, UriKind.Absolute, out Uri? installerUri))
                    {
                        return false;
                    }

                    string extension = Path.GetExtension(installerUri.AbsolutePath);
                    return extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) ||
                        extension.Equals(".msi", StringComparison.OrdinalIgnoreCase);
                })
                .ToList();

            if (supportedInstallers.Count == 0)
            {
                return null;
            }

            string[] preferredArchitectures = RuntimeInformation.OSArchitecture switch
            {
                Architecture.Arm64 => ["arm64", "x64", "x86", "neutral"],
                Architecture.X86 => ["x86", "neutral"],
                _ => ["x64", "x86", "neutral"]
            };

            foreach (string architecture in preferredArchitectures)
            {
                WingetInstallerEntry? match = supportedInstallers
                    .Where(installer => installer.Architecture?.Equals(architecture, StringComparison.OrdinalIgnoreCase) == true)
                    .OrderBy(GetLocalePreference)
                    .FirstOrDefault();
                if (match != null)
                {
                    return match;
                }
            }

            return supportedInstallers.OrderBy(GetLocalePreference).FirstOrDefault();
        }

        private static int GetLocalePreference(WingetInstallerEntry installer)
        {
            if (installer.InstallerLocale?.Equals(CultureInfo.CurrentUICulture.Name, StringComparison.OrdinalIgnoreCase) == true)
            {
                return 0;
            }

            if (installer.InstallerLocale?.Equals("en-US", StringComparison.OrdinalIgnoreCase) == true)
            {
                return 1;
            }

            return string.IsNullOrWhiteSpace(installer.InstallerLocale) ? 2 : 3;
        }

        private static bool TryParseVersion(string value, out Version version) =>
            TryParseLooseVersion(value.TrimStart('v', 'V'), out version);

        private static Version ParseVersion(string value) =>
            TryParseVersion(value, out Version version) ? version : new Version(0, 0);

        private static HttpClient CreateManifestClient()
        {
            HttpClient client = new();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("SynToolkit/1.7");
            client.DefaultRequestHeaders.Accept.ParseAdd("application/octet-stream");
            return client;
        }

        private static HttpClient CreateManifestCatalogClient()
        {
            HttpClient client = new();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("SynToolkit/1.7");
            client.DefaultRequestHeaders.Accept.ParseAdd("text/html");
            return client;
        }

        private static IEnumerable<string> BuildInstallArguments(
            string packageIdentifier,
            string packageSource,
            bool isUpdate,
            WingetPackageInstallMetadata installMetadata)
        {
            List<string> arguments =
            [
                isUpdate ? "upgrade" : "install",
                "--exact",
                "--id",
                packageIdentifier,
                "--source",
                packageSource,
                "--silent",
                "--accept-source-agreements",
                "--accept-package-agreements",
                "--disable-interactivity"
            ];

            if (!string.IsNullOrWhiteSpace(installMetadata.Scope))
            {
                arguments.Add("--scope");
                arguments.Add(installMetadata.Scope);
            }

            if (installMetadata.InstallLocationRequired && !string.IsNullOrWhiteSpace(installMetadata.InstallLocation))
            {
                arguments.Add("--location");
                arguments.Add(installMetadata.InstallLocation);
            }

            return arguments;
        }

        private static async Task<WingetInstallResult> RunWingetAsync(
            IEnumerable<string> arguments,
            TimeSpan timeout,
            bool runAsInteractiveUser,
            CancellationToken cancellationToken)
        {
            if (runAsInteractiveUser)
            {
                return await RunWingetAsInteractiveUserAsync(arguments, timeout, cancellationToken);
            }

            ProcessStartInfo startInfo = new("winget.exe")
            {
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            foreach (string argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using Process process = new() { StartInfo = startInfo };
            if (!process.Start())
            {
                throw new InvalidOperationException("Unable to start Windows Package Manager.");
            }

            Task<string> standardOutputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            Task<string> standardErrorTask = process.StandardError.ReadToEndAsync(cancellationToken);

            using CancellationTokenSource timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(timeout);

            try
            {
                await process.WaitForExitAsync(timeoutSource.Token);
            }
            catch (OperationCanceledException)
            {
                TryKillProcessTree(process);

                if (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }

                return new WingetInstallResult(
                    false,
                    -1,
                    $"The package operation timed out after {timeout.TotalMinutes:0} minutes.",
                    WingetInstallDisposition.Failed);
            }

            string standardOutput = await standardOutputTask;
            string standardError = await standardErrorTask;
            string output = string.Join(
                Environment.NewLine,
                new[] { standardOutput.Trim(), standardError.Trim() }.Where(value => !string.IsNullOrWhiteSpace(value)));

            App.logger.Info(
                "[Installers] winget finished with exit code {ExitCode}.\n{Output}",
                process.ExitCode,
                output);

            return CreateWingetResult(process.ExitCode, output);
        }

        private static async Task<WingetInstallResult> RunWingetAsInteractiveUserAsync(
            IEnumerable<string> arguments,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            int timeoutMilliseconds = timeout.TotalMilliseconds >= int.MaxValue
                ? int.MaxValue
                : (int)timeout.TotalMilliseconds;

            CommandResult result = await Task.Run(
                () => InteractiveUserProcessHelper.RunAsInteractiveUserResult(
                    "winget.exe",
                    arguments,
                    timeoutMilliseconds),
                cancellationToken);

            if (result.TimedOut)
            {
                return new WingetInstallResult(false, -1, result.CombinedOutput, WingetInstallDisposition.Failed);
            }

            App.logger.Info(
                "[Installers] winget (interactive user) finished with exit code {ExitCode}.\n{Output}",
                result.ExitCode,
                result.CombinedOutput);

            return CreateWingetResult(result.ExitCode, result.CombinedOutput);
        }

        private static WingetInstallResult CreateWingetResult(int exitCode, string output)
        {
            WingetInstallDisposition disposition = unchecked((uint)exitCode) switch
            {
                0x8A15002B or 0x8A150061 => WingetInstallDisposition.AlreadySatisfied,
                _ when exitCode == 0 => WingetInstallDisposition.Completed,
                _ => WingetInstallDisposition.Failed
            };

            return new WingetInstallResult(
                disposition != WingetInstallDisposition.Failed,
                exitCode,
                output,
                disposition);
        }

        private static string? ResolveInstallLocation(string packageIdentifier, string? scope, string? manifestDefaultInstallLocation)
        {
            if (!string.IsNullOrWhiteSpace(manifestDefaultInstallLocation))
            {
                return Environment.ExpandEnvironmentVariables(manifestDefaultInstallLocation);
            }

            string leafName = packageIdentifier.Split('.').LastOrDefault() ?? packageIdentifier;
            return string.Equals(scope, "user", StringComparison.OrdinalIgnoreCase)
                ? Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Programs",
                    leafName)
                : Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    leafName);
        }

        private static bool MustRunUnelevated(string? scope, string? elevationRequirement)
        {
            if (!IsRunningElevated())
            {
                return false;
            }

            return string.Equals(scope, "user", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(elevationRequirement, "elevationProhibited", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsRunningElevated()
        {
            try
            {
                using WindowsIdentity identity = WindowsIdentity.GetCurrent();
                WindowsPrincipal principal = new(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }

        private static void TryKillProcessTree(Process process)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(5_000);
                }
            }
            catch (Exception exception)
            {
                App.logger.Warn(exception, "[Installers] Unable to stop the canceled installer process.");
            }
        }

        public sealed class WingetInstallerManifest
        {
            public string? InstallerType { get; set; }

            public string? Scope { get; set; }

            public bool? InstallLocationRequired { get; set; }

            public string? ElevationRequirement { get; set; }

            public WingetInstallerSwitches? InstallerSwitches { get; set; }

            public WingetInstallationMetadata? InstallationMetadata { get; set; }

            public List<WingetInstallerEntry>? Installers { get; set; }
        }

        public sealed class WingetInstallerEntry
        {
            public string? Architecture { get; set; }

            public string? InstallerLocale { get; set; }

            public string? InstallerType { get; set; }

            public string? Scope { get; set; }

            public bool? InstallLocationRequired { get; set; }

            public string? ElevationRequirement { get; set; }

            public string? InstallerUrl { get; set; }

            public string? InstallerSha256 { get; set; }

            public WingetInstallerSwitches? InstallerSwitches { get; set; }

            public WingetInstallationMetadata? InstallationMetadata { get; set; }
        }

        public sealed class WingetInstallerSwitches
        {
            public string? Silent { get; set; }

            public string? SilentWithProgress { get; set; }
        }

        public sealed class WingetInstallationMetadata
        {
            public string? DefaultInstallLocation { get; set; }
        }

        private sealed record InstalledDesktopApplication(
            string DisplayName,
            string? DisplayVersion,
            string? UninstallString,
            string? QuietUninstallString);

        private sealed record ResolvedWingetManifest(
            WingetInstallerManifest Manifest,
            WingetInstallerEntry? CompatibleInstaller);

        private sealed record WingetPackageInstallMetadata(
            string? Scope,
            bool InstallLocationRequired,
            string? InstallLocation,
            string? ElevationRequirement,
            bool MustRunUnelevated);
    }
}
