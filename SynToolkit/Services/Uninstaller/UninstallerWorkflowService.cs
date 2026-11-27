#nullable enable

using Microsoft.Win32;
using SynToolkit.Utils;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.Management.Deployment;

namespace SynToolkit.Services.Uninstaller;

public enum InstalledAppKind
{
    Win32,
    Appx
}

public sealed record UninstallableAppInfo(
    string DisplayName,
    InstalledAppKind Kind,
    string? DisplayVersion,
    string? Publisher,
    string? DisplayIcon,
    string? InstallLocation,
    string? UninstallString,
    string? QuietUninstallString,
    string? AppxPackageFullName,
    string? AppxPackageName,
    bool IsRisky);

public sealed record UninstallExecutionResult(
    bool Succeeded,
    int ExitCode,
    string Message,
    string? StdOut,
    string? StdErr);

public interface IUninstallerWorkflowService
{
    Task<IReadOnlyList<UninstallableAppInfo>> GetInstalledAppsAsync(CancellationToken cancellationToken = default);
    Task<UninstallExecutionResult> UninstallAsync(UninstallableAppInfo app, CancellationToken cancellationToken = default);
}

public sealed class UninstallerWorkflowService : IUninstallerWorkflowService
{
    private const string UninstallRegistryPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
    private static readonly TimeSpan Win32UninstallTimeout = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan AppxUninstallTimeout = TimeSpan.FromMinutes(10);

    private static readonly string[] RiskyNamePatterns =
    [
        "nvidia",
        "amd chipset",
        "realtek ethernet",
        "webview2",
        ".net",
        "visual c++",
        "vc++",
        "redistributable",
        "xna framework",
        "directx",
        "intel chipset",
        "runtime"
    ];

    private readonly IDismService _dismService;

    public UninstallerWorkflowService(IDismService dismService)
    {
        _dismService = dismService;
    }

    public async Task<IReadOnlyList<UninstallableAppInfo>> GetInstalledAppsAsync(
        CancellationToken cancellationToken = default)
    {
        // Registry and PackageManager reads are independent; run them side by side.
        Task<IReadOnlyList<UninstallableAppInfo>> win32Task = Task.Run(ReadWin32Apps, cancellationToken);
        Task<IReadOnlyList<UninstallableAppInfo>> appxTask = Task.Run(() => ReadAppxApps(cancellationToken), cancellationToken);
        await Task.WhenAll(win32Task, appxTask);

        IReadOnlyList<UninstallableAppInfo> win32Apps = win32Task.Result;
        IEnumerable<UninstallableAppInfo> appxApps = appxTask.Result
            .Where(appx => !IsShellExtensionOfWin32App(appx, win32Apps));

        return win32Apps
            .Concat(appxApps)
            .GroupBy(
                app => app.Kind + "|" + app.DisplayName.Trim(),
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(app => ParseLooseVersion(app.DisplayVersion))
                .First())
            .OrderBy(app => app.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<UninstallExecutionResult> UninstallAsync(
        UninstallableAppInfo app,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.Kind switch
        {
            InstalledAppKind.Appx => await UninstallAppxAsync(app, cancellationToken),
            _ => await UninstallWin32Async(app, cancellationToken)
        };
    }

    public static bool IsRisky(string? displayName, string? publisher)
    {
        string haystack = string.Join(
            " ",
            new[] { displayName ?? string.Empty, publisher ?? string.Empty }
            .Where(value => !string.IsNullOrWhiteSpace(value)));

        return RiskyNamePatterns.Any(pattern =>
            haystack.Contains(pattern, StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<UninstallableAppInfo> ReadWin32Apps()
    {
        List<UninstallableAppInfo> apps = new();
        int readableRootCount = 0;
        readableRootCount += ReadUninstallRegistryView(apps, RegistryHive.LocalMachine, RegistryView.Registry64) ? 1 : 0;
        readableRootCount += ReadUninstallRegistryView(apps, RegistryHive.LocalMachine, RegistryView.Registry32) ? 1 : 0;
        readableRootCount += ReadUninstallRegistryView(apps, RegistryHive.CurrentUser, RegistryView.Registry64) ? 1 : 0;
        readableRootCount += ReadUninstallRegistryView(apps, RegistryHive.CurrentUser, RegistryView.Registry32) ? 1 : 0;

        if (readableRootCount == 0)
        {
            throw new InvalidOperationException("Windows did not allow access to the installed-program registry.");
        }

        return apps;
    }

    /// <summary>
    /// Desktop apps such as WinRAR, 7-Zip or VS Code register a tiny "sparse" Appx
    /// package only to add a Windows 11 context-menu entry. Listing it as a second
    /// Store app is confusing and removing it does not uninstall anything, so hide
    /// it when a Win32 entry from the same publisher has the same name.
    /// </summary>
    internal static bool IsShellExtensionOfWin32App(
        UninstallableAppInfo appx,
        IReadOnlyList<UninstallableAppInfo> win32Apps)
    {
        if (string.IsNullOrWhiteSpace(appx.Publisher))
        {
            return false;
        }

        string appxName = NormalizeAppName(appx.DisplayName);
        if (appxName.Length == 0)
        {
            return false;
        }

        return win32Apps.Any(win32 =>
            string.Equals(win32.Publisher?.Trim(), appx.Publisher.Trim(), StringComparison.OrdinalIgnoreCase) &&
            NormalizeAppName(win32.DisplayName).StartsWith(appxName, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeAppName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        // "WinRAR 7.01 (64-bit)" -> "winrar"
        string cleaned = Regex.Replace(name, @"\([^)]*\)", " ", RegexOptions.CultureInvariant);
        cleaned = Regex.Replace(cleaned, @"\bv?\d+(\.\d+)*\b", " ", RegexOptions.CultureInvariant);
        return Regex.Replace(cleaned, @"\s+", " ").Trim().ToLowerInvariant();
    }

    private static IReadOnlyList<UninstallableAppInfo> ReadAppxApps(CancellationToken cancellationToken)
    {
        var packageManager = new PackageManager();
        List<UninstallableAppInfo> apps = new();

        foreach (Package package in packageManager.FindPackagesForUser(string.Empty))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                // Cheap identity checks first; Status/DisplayName/InstalledLocation hit
                // the deployment service and resource loader, so only pay for them
                // on packages that can actually be listed.
                if (package.IsFramework ||
                    package.IsResourcePackage ||
                    package.IsBundle ||
                    package.SignatureKind == PackageSignatureKind.System ||
                    !package.Status.VerifyIsOK())
                {
                    continue;
                }

                string displayName = string.IsNullOrWhiteSpace(package.DisplayName)
                    ? package.Id.Name
                    : package.DisplayName;

                string version = $"{package.Id.Version.Major}.{package.Id.Version.Minor}.{package.Id.Version.Build}.{package.Id.Version.Revision}";
                apps.Add(new UninstallableAppInfo(
                    displayName.Trim(),
                    InstalledAppKind.Appx,
                    version,
                    package.PublisherDisplayName,
                    null,
                    package.InstalledLocation?.Path,
                    null,
                    null,
                    package.Id.FullName,
                    package.Id.Name,
                    IsRisky(displayName, package.PublisherDisplayName)));
            }
            catch (Exception exception)
            {
                App.logger.Debug(exception, "[Uninstaller] Skipping unreadable Appx package entry.");
            }
        }

        return apps;
    }

    private async Task<UninstallExecutionResult> UninstallAppxAsync(
        UninstallableAppInfo app,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(app.AppxPackageFullName))
        {
            return new UninstallExecutionResult(false, -1, "Appx package identity is missing.", null, null);
        }

        try
        {
            App.logger.Info("[Uninstaller] Removing Appx package {PackageFullName}.", app.AppxPackageFullName);
            var manager = new PackageManager();
            var removeOperation = manager.RemovePackageAsync(app.AppxPackageFullName);

            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(AppxUninstallTimeout);
            await removeOperation.AsTask(timeoutSource.Token);

            // Best-effort: remove provisioned copy too when elevated.
            if (IsRunningElevated() && !string.IsNullOrWhiteSpace(app.AppxPackageName))
            {
                try
                {
                    _dismService.RemoveProvisionedAppxPackage(app.AppxPackageName);
                }
                catch (Exception exception)
                {
                    App.logger.Debug(
                        exception,
                        "[Uninstaller] Provisioned Appx removal skipped for {PackageName}.",
                        app.AppxPackageName);
                }
            }

            return new UninstallExecutionResult(true, 0, "Appx package removed.", null, null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            App.logger.Error(exception, "[Uninstaller] Appx uninstall failed for {AppName}.", app.DisplayName);
            return new UninstallExecutionResult(false, -1, exception.Message, null, null);
        }
    }

    private static async Task<UninstallExecutionResult> UninstallWin32Async(
        UninstallableAppInfo app,
        CancellationToken cancellationToken)
    {
        string? commandLine = !string.IsNullOrWhiteSpace(app.QuietUninstallString)
            ? app.QuietUninstallString
            : app.UninstallString;
        if (string.IsNullOrWhiteSpace(commandLine))
        {
            return new UninstallExecutionResult(false, -1, "Windows did not provide an uninstall command.", null, null);
        }

        bool commandIsAlreadyQuiet = !string.IsNullOrWhiteSpace(app.QuietUninstallString);
        if (!TryCreateUninstallStartInfo(commandLine, commandIsAlreadyQuiet, out ProcessStartInfo startInfo))
        {
            return new UninstallExecutionResult(false, -1, "Windows provided an invalid uninstall command.", null, null);
        }

        App.logger.Info(
            "[Uninstaller] Running uninstall command for {AppName}: {FileName} {Arguments}",
            app.DisplayName,
            startInfo.FileName,
            startInfo.Arguments);

        try
        {
            return await RunUninstallProcessAsync(app, startInfo, cancellationToken);
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == ErrorElevationRequired)
        {
            // The uninstaller's manifest asks for admin and we are not elevated:
            // ShellExecute with "runas" shows one UAC prompt instead of failing.
            App.logger.Info("[Uninstaller] {AppName} needs elevation, retrying with runas.", app.DisplayName);
            ProcessStartInfo elevated = new(startInfo.FileName)
            {
                Arguments = startInfo.Arguments,
                UseShellExecute = true,
                Verb = "runas"
            };

            try
            {
                return await RunUninstallProcessAsync(app, elevated, cancellationToken);
            }
            catch (Win32Exception elevationException)
            {
                string reason = elevationException.NativeErrorCode == ErrorCancelled
                    ? "Administrator permission was declined."
                    : elevationException.Message;
                return new UninstallExecutionResult(false, elevationException.NativeErrorCode, reason, null, null);
            }
        }
        catch (Win32Exception exception)
        {
            App.logger.Warn(exception, "[Uninstaller] Unable to start uninstaller for {AppName}.", app.DisplayName);
            return new UninstallExecutionResult(false, exception.NativeErrorCode, exception.Message, null, null);
        }
    }

    private const int ErrorElevationRequired = 740;
    private const int ErrorCancelled = 1223;

    private static async Task<UninstallExecutionResult> RunUninstallProcessAsync(
        UninstallableAppInfo app,
        ProcessStartInfo startInfo,
        CancellationToken cancellationToken)
    {
        using Process process = new() { StartInfo = startInfo };
        if (!process.Start())
        {
            return new UninstallExecutionResult(false, -1, "Unable to start the app uninstaller.", null, null);
        }

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(Win32UninstallTimeout);

        try
        {
            await process.WaitForExitAsync(timeoutSource.Token);
        }
        catch (OperationCanceledException)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            TryKillProcessTree(process);
            return new UninstallExecutionResult(
                false,
                -1,
                $"Uninstallation timed out after {Win32UninstallTimeout.TotalMinutes:0} minutes.",
                null,
                null);
        }

        bool succeeded = process.ExitCode is 0 or 3010 or 1641;
        string message = succeeded
            ? "The app uninstaller completed."
            : process.ExitCode == 1602
                ? "The uninstall was cancelled in the app's uninstaller."
                : $"The app uninstaller exited with code {process.ExitCode}.";

        App.logger.Info(
            "[Uninstaller] Finished uninstall for {AppName} with exit code {ExitCode}.",
            app.DisplayName,
            process.ExitCode);

        return new UninstallExecutionResult(succeeded, process.ExitCode, message, null, null);
    }

    private static bool ReadUninstallRegistryView(
        ICollection<UninstallableAppInfo> applications,
        RegistryHive hive,
        RegistryView view)
    {
        try
        {
            using RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, view);
            using RegistryKey? uninstallKey = baseKey.OpenSubKey(UninstallRegistryPath);
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

                    object? systemComponent = applicationKey?.GetValue("SystemComponent");
                    if (systemComponent is int systemComponentInt
                        ? systemComponentInt == 1
                        : string.Equals(systemComponent?.ToString(), "1", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(applicationKey?.GetValue("ParentKeyName") as string))
                    {
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(applicationKey?.GetValue("ReleaseType") as string))
                    {
                        continue;
                    }

                    string? uninstallString = (applicationKey?.GetValue("UninstallString") as string)?.Trim();
                    string? quietUninstallString = (applicationKey?.GetValue("QuietUninstallString") as string)?.Trim();
                    if (string.IsNullOrWhiteSpace(uninstallString) && string.IsNullOrWhiteSpace(quietUninstallString))
                    {
                        continue;
                    }

                    string cleanName = displayName.Trim();
                    string? publisher = (applicationKey?.GetValue("Publisher") as string)?.Trim();
                    applications.Add(new UninstallableAppInfo(
                        cleanName,
                        InstalledAppKind.Win32,
                        (applicationKey?.GetValue("DisplayVersion") as string)?.Trim(),
                        publisher,
                        (applicationKey?.GetValue("DisplayIcon") as string)?.Trim(),
                        (applicationKey?.GetValue("InstallLocation") as string)?.Trim(),
                        uninstallString,
                        quietUninstallString,
                        null,
                        null,
                        IsRisky(cleanName, publisher)));
                }
                catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or SecurityException)
                {
                    App.logger.Debug(exception, "[Uninstaller] Unable to read uninstall entry {EntryName}.", subKeyName);
                }
            }

            return true;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or SecurityException)
        {
            App.logger.Debug(exception, "[Uninstaller] Unable to read the {Hive}/{View} uninstall registry.", hive, view);
            return false;
        }
    }

    private static bool TryCreateUninstallStartInfo(
        string commandLine,
        bool commandAlreadyQuiet,
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
            arguments = quotedExecutable.Groups["arguments"].Value.Trim();
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

        string executableName = Path.GetFileName(executable);
        if (executableName.Equals("msiexec.exe", StringComparison.OrdinalIgnoreCase))
        {
            arguments = Regex.Replace(
                arguments,
                @"(^|\s)/I(?=\s*\{)",
                "$1/X",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            if (!arguments.Contains("/qn", StringComparison.OrdinalIgnoreCase))
            {
                arguments = $"{arguments} /qn /norestart".Trim();
            }
        }
        else if (!commandAlreadyQuiet && !ContainsSilentSwitch(arguments))
        {
            arguments = $"{arguments} {GetDefaultSilentSwitches(executableName)}".Trim();
        }

        // The window is NOT hidden and output is NOT redirected on purpose:
        // - an uninstaller that ignores the silent switch used to run invisibly and
        //   sit there until the 15 minute timeout, waiting on a dialog nobody saw;
        // - redirected pipes are inherited by helper processes many uninstallers
        //   spawn, so WaitForExit kept blocking until those exited too.
        // Silent uninstallers still show nothing; the rest show their own UI.
        startInfo = new ProcessStartInfo(executable)
        {
            Arguments = arguments,
            UseShellExecute = false
        };
        return true;
    }

    internal static string GetDefaultSilentSwitches(string executableName)
    {
        // Inno Setup always names its uninstaller unins000.exe, unins001.exe, ...
        if (Regex.IsMatch(executableName, @"^unins\d{3}\.exe$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            return "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-";
        }

        // NSIS uninstallers (uninst.exe, uninstall.exe, "Uninstall Foo.exe") only take /S.
        if (executableName.StartsWith("uninst", StringComparison.OrdinalIgnoreCase))
        {
            return "/S";
        }

        return "/quiet";
    }

    private static bool ContainsSilentSwitch(string arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments))
        {
            return false;
        }

        return arguments.Contains("/quiet", StringComparison.OrdinalIgnoreCase) ||
            arguments.Contains("/silent", StringComparison.OrdinalIgnoreCase) ||
            arguments.Contains("/verysilent", StringComparison.OrdinalIgnoreCase) ||
            Regex.IsMatch(arguments, @"(^|\s)/S($|\s)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static Version ParseLooseVersion(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return new Version(0, 0);
        }

        Match match = Regex.Match(value, @"\d+(?:\.\d+)+", RegexOptions.CultureInvariant);
        if (!match.Success)
        {
            return new Version(0, 0);
        }

        string normalizedVersion = string.Join('.', match.Value.Split('.').Take(4));
        return Version.TryParse(normalizedVersion, out Version? version)
            ? version
            : new Version(0, 0);
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
            App.logger.Warn(exception, "[Uninstaller] Unable to stop timed-out uninstall process.");
        }
    }
}
