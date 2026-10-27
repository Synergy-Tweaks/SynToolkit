#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.ServiceProcess;
using System.Threading;
using Microsoft.Win32;
using SynToolkit.Models.Startup;
using SynToolkit.Utils;

namespace SynToolkit.Services.Startup
{
    public sealed class StartupScanResult
    {
        public StartupScanResult(IReadOnlyList<StartupEntry> entries, IReadOnlyList<string> failures)
        {
            Entries = entries;
            Failures = failures;
        }

        public IReadOnlyList<StartupEntry> Entries { get; }

        public IReadOnlyList<string> Failures { get; }
    }

    public sealed class StartupToggleResult
    {
        public bool Succeeded { get; init; }
        public bool NeedsAdministrator { get; init; }
        public bool IsEnabled { get; init; }
        public string Location { get; init; } = "";
        public string Command { get; init; } = "";
    }

    /// <summary>
    /// Reads and toggles startup entries. Run keys and Startup-folder items use
    /// StartupApproved, which is the same on/off state Task Manager shows.
    /// </summary>
    public sealed class StartupScannerService
    {
        private static readonly HashSet<string> KnownSafePublishers = new(StringComparer.OrdinalIgnoreCase)
        {
            "Microsoft Corporation", "Microsoft", "Google LLC", "Apple Inc.",
            "Adobe Inc.", "Adobe Systems", "NVIDIA Corporation", "Intel Corporation",
            "AMD", "Realtek", "Logitech", "Razer", "Dell", "HP", "Lenovo"
        };

        private static readonly HashSet<string> HighRiskKeywords = new(StringComparer.OrdinalIgnoreCase)
        {
            "updater", "update", "helper", "launcher", "agent", "daemon",
            "toolbar", "coupon", "deal", "offer", "search", "browser"
        };

        public bool IsAdministrator => IsCurrentProcessElevated();

        public StartupScanResult ScanAll(CancellationToken cancellationToken)
        {
            List<StartupEntry> entries = new();
            List<string> failures = new();

            Collect(entries, failures, "Registry", cancellationToken, () => ScanRegistry(cancellationToken, failures));
            Collect(entries, failures, "Startup folder", cancellationToken, () => ScanStartupFolders(cancellationToken, failures));
            Collect(entries, failures, "Scheduled tasks", cancellationToken, () => ScanScheduledTasks(cancellationToken));
            Collect(entries, failures, "Services", cancellationToken, () => ScanServices(cancellationToken));
            return new StartupScanResult(entries, failures);
        }

        public StartupToggleResult ToggleEntry(StartupEntry entry)
        {
            if (entry.RequiresAdministrator && !IsAdministrator)
            {
                return new StartupToggleResult { NeedsAdministrator = true, IsEnabled = entry.IsEnabled, Location = entry.Location, Command = entry.Command };
            }

            bool wasEnabled = entry.IsEnabled;
            App.logger.Info(
                "[Startup] Toggling {Category} '{Name}' at {Location}. Enabled before change: {Enabled}. Command: {Command}",
                entry.Category,
                entry.Name,
                entry.Location,
                wasEnabled,
                entry.Command);

            try
            {
                StartupToggleResult result = entry.Category switch
                {
                    StartupCategory.Registry => ToggleRegistryEntry(entry),
                    StartupCategory.StartupFolder => ToggleStartupFolderEntry(entry),
                    StartupCategory.ScheduledTask => ToggleScheduledTask(entry),
                    StartupCategory.Service => ToggleService(entry),
                    _ => Failed(entry)
                };

                App.logger.Info(
                    "[Startup] Toggle of '{Name}' {Outcome}. Enabled after change: {Enabled}. Location: {Location}",
                    entry.Name,
                    result.Succeeded ? "succeeded" : "failed",
                    result.IsEnabled,
                    result.Location);
                return result;
            }
            catch (Exception exception)
            {
                App.logger.Warn(exception, "[Startup] Toggle of '{Name}' failed.", entry.Name);
                return Failed(entry);
            }
        }

        private static void Collect(
            List<StartupEntry> entries,
            List<string> failures,
            string source,
            CancellationToken cancellationToken,
            Func<List<StartupEntry>> scan)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                entries.AddRange(scan());
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                App.logger.Warn(exception, "[Startup] {Source} scan failed.", source);
                if (!failures.Contains(source))
                {
                    failures.Add(source);
                }
            }
        }

        private List<StartupEntry> ScanRegistry(CancellationToken cancellationToken, List<string> failures)
        {
            List<StartupEntry> entries = new();
            entries.AddRange(ScanRegistryKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", false, cancellationToken, failures));
            entries.AddRange(ScanRegistryKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce", false, cancellationToken, failures));
            entries.AddRange(ScanRegistryKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true, cancellationToken, failures));
            // 32-bit machine-wide autostarts; the scanner otherwise misses every
            // entry written by a 32-bit installer.
            entries.AddRange(ScanRegistryKey(@"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run", true, cancellationToken, failures));
            entries.AddRange(ScanRegistryKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce", true, cancellationToken, failures));
            return entries;
        }

        private List<StartupEntry> ScanRegistryKey(string keyPath, bool localMachine, CancellationToken cancellationToken, List<string> failures)
        {
            List<StartupEntry> entries = new();
            RegistryKey hive = localMachine ? Registry.LocalMachine : Registry.CurrentUser;
            string hivePrefix = localMachine ? "HKLM" : "HKCU";
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                using RegistryKey? key = hive.OpenSubKey(keyPath, false);
                if (key != null)
                {
                    foreach (string valueName in key.GetValueNames())
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        string command = key.GetValue(valueName)?.ToString() ?? "";
                        if (!TryResolveInstalledFile(command, out string filePath))
                        {
                            // Keep orphans: they are the main reason to open a startup
                            // manager, and AssessRisk flags the missing file as High.
                            App.logger.Debug("[Startup] Found leftover registry value {Name}. Command: {Command}", valueName, command);
                        }

                        bool enabled = IsStartupApprovedEnabled(hive, StartupApprovedLeaf(keyPath), valueName);
                        entries.Add(CreateRegistryEntry(valueName, command, filePath, $"{hivePrefix}\\{keyPath}", enabled, localMachine));
                    }
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                App.logger.Warn(exception, "[Startup] Could not read {Hive}\\{Key}.", hivePrefix, keyPath);
                failures.Add($"{hivePrefix} {Path.GetFileName(keyPath)}");
            }

            string disabledKeyPath = DisabledKeyPath(keyPath);
            try
            {
                using RegistryKey? disabledKey = hive.OpenSubKey(disabledKeyPath, false);
                if (disabledKey != null)
                {
                    foreach (string valueName in disabledKey.GetValueNames())
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        string command = disabledKey.GetValue(valueName)?.ToString() ?? "";
                        if (!TryResolveInstalledFile(command, out string filePath))
                        {
                            App.logger.Debug("[Startup] Found leftover disabled registry value {Name}. Command: {Command}", valueName, command);
                        }

                        entries.Add(CreateRegistryEntry(valueName, command, filePath, $"{hivePrefix}\\{disabledKeyPath}", false, localMachine));
                    }
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                App.logger.Warn(exception, "[Startup] Could not read disabled key {Hive}\\{Key}.", hivePrefix, disabledKeyPath);
            }

            return entries;
        }

        private StartupEntry CreateRegistryEntry(string name, string command, string filePath, string location, bool isEnabled, bool localMachine)
        {
            return new StartupEntry
            {
                Name = name,
                Command = command,
                FilePath = filePath,
                Publisher = GetFilePublisher(filePath),
                Category = StartupCategory.Registry,
                Location = location,
                IsEnabled = isEnabled,
                Risk = AssessRisk(name, filePath, command),
                Description = GetFileDescription(filePath),
                RequiresAdministrator = localMachine
            };
        }

        private List<StartupEntry> ScanStartupFolders(CancellationToken cancellationToken, List<string> failures)
        {
            List<StartupEntry> entries = new();
            string commonStartup = Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup);
            string[] folders =
            {
                Environment.GetFolderPath(Environment.SpecialFolder.Startup),
                commonStartup
            };

            foreach (string folder in folders)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!Directory.Exists(folder))
                {
                    continue;
                }

                bool requiresAdministrator = string.Equals(folder, commonStartup, StringComparison.OrdinalIgnoreCase);
                string[] files;
                try
                {
                    files = Directory.GetFiles(folder);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    App.logger.Warn(exception, "[Startup] Could not read startup folder {Folder}.", folder);
                    failures.Add(requiresAdministrator ? "Common Startup folder" : "User Startup folder");
                    continue;
                }

                foreach (string file in files)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        bool isDisabled = file.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);
                        string cleanFile = file;
                        while (cleanFile.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
                        {
                            cleanFile = cleanFile[..^9];
                        }

                        string fileName = Path.GetFileName(cleanFile);
                        if (fileName.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        string filePath;
                        if (cleanFile.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
                        {
                            string target = ResolveShortcut(file);
                            if (!File.Exists(target))
                            {
                                App.logger.Debug("[Startup] Skipping leftover startup shortcut {File}. Target: {Target}", file, target);
                                continue;
                            }

                            filePath = target;
                        }
                        else if (File.Exists(cleanFile))
                        {
                            filePath = cleanFile;
                        }
                        else if (File.Exists(file))
                        {
                            filePath = file;
                        }
                        else
                        {
                            continue;
                        }

                        entries.Add(new StartupEntry
                        {
                            Name = Path.GetFileNameWithoutExtension(cleanFile),
                            Command = file,
                            FilePath = filePath,
                            Publisher = GetFilePublisher(filePath),
                            Category = StartupCategory.StartupFolder,
                            Location = folder,
                            IsEnabled = !isDisabled && IsStartupFolderEnabled(folder, fileName, requiresAdministrator),
                            Risk = AssessRisk(Path.GetFileName(cleanFile), filePath, cleanFile),
                            Description = GetFileDescription(filePath),
                            RequiresAdministrator = requiresAdministrator
                        });
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException)
                    {
                        App.logger.Warn(exception, "[Startup] Could not read startup item {File}.", file);
                    }
                }
            }

            return entries;
        }

        private List<StartupEntry> ScanScheduledTasks(CancellationToken cancellationToken)
        {
            List<StartupEntry> entries = new();
            using Microsoft.Win32.TaskScheduler.TaskService taskService = new();
            foreach (Microsoft.Win32.TaskScheduler.Task task in taskService.RootFolder.AllTasks)
            {
                cancellationToken.ThrowIfCancellationRequested();
                bool hasLogonTrigger = task.Definition.Triggers
                    .Any(trigger => trigger.TriggerType == Microsoft.Win32.TaskScheduler.TaskTriggerType.Logon
                                 || trigger.TriggerType == Microsoft.Win32.TaskScheduler.TaskTriggerType.Boot);
                if (!hasLogonTrigger)
                {
                    continue;
                }

                Microsoft.Win32.TaskScheduler.ExecAction? action = task.Definition.Actions
                    .OfType<Microsoft.Win32.TaskScheduler.ExecAction>()
                    .FirstOrDefault();
                string rawPath = action?.Path ?? "";
                string filePath = "";
                if (!string.IsNullOrWhiteSpace(rawPath) && !TryResolveInstalledFile(rawPath, out filePath))
                {
                    App.logger.Debug("[Startup] Found leftover scheduled task {Name}. Path: {Path}", task.Name, rawPath);
                }

                entries.Add(new StartupEntry
                {
                    Name = task.Name,
                    Command = $"{action?.Path} {action?.Arguments}".Trim(),
                    FilePath = filePath,
                    Publisher = GetFilePublisher(filePath),
                    Category = StartupCategory.ScheduledTask,
                    Location = task.Path,
                    IsEnabled = task.Enabled,
                    Risk = AssessRisk(task.Name, filePath, action?.Path ?? ""),
                    Description = task.Definition.RegistrationInfo.Description ?? "",
                    RequiresAdministrator = true
                });
            }

            return entries;
        }

        private List<StartupEntry> ScanServices(CancellationToken cancellationToken)
        {
            List<StartupEntry> entries = new();
            ServiceController[] services = ServiceController.GetServices();
            try
            {
                foreach (ServiceController service in services)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (service.StartType is not (ServiceStartMode.Automatic or ServiceStartMode.Disabled))
                    {
                        continue;
                    }

                    try
                    {
                        string regPath = $@"SYSTEM\CurrentControlSet\Services\{service.ServiceName}";
                        using RegistryKey? key = Registry.LocalMachine.OpenSubKey(regPath);
                        string imagePath = key?.GetValue("ImagePath")?.ToString() ?? "";
                        if (!TryResolveInstalledFile(imagePath, out string filePath))
                        {
                            App.logger.Debug("[Startup] Found leftover service {Service}. Image: {Image}", service.ServiceName, imagePath);
                        }

                        string publisher = GetFilePublisher(filePath);
                        if (KnownSafePublishers.Contains(publisher) && publisher.Contains("Microsoft", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        entries.Add(new StartupEntry
                        {
                            Name = service.DisplayName,
                            Command = imagePath,
                            FilePath = filePath,
                            Publisher = publisher,
                            Category = StartupCategory.Service,
                            Location = $@"HKLM\{regPath}",
                            IsEnabled = service.StartType != ServiceStartMode.Disabled,
                            Risk = AssessRisk(service.ServiceName, filePath, imagePath),
                            Description = service.DisplayName,
                            StartupDelay = "On Boot",
                            RequiresAdministrator = true
                        });
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException)
                    {
                        App.logger.Debug(exception, "[Startup] Could not read service {Service}.", service.ServiceName);
                    }
                }
            }
            finally
            {
                foreach (ServiceController service in services)
                {
                    service.Dispose();
                }
            }

            return entries;
        }

        private static StartupToggleResult ToggleRegistryEntry(StartupEntry entry)
        {
            if (!TryParseHive(entry.Location, out RegistryKey? hive, out string currentPath) || hive is null)
            {
                return Failed(entry);
            }

            string activePath = ToActiveRunPath(currentPath);
            bool storedInDisabledKey = !string.Equals(currentPath, activePath, StringComparison.OrdinalIgnoreCase);
            bool enable = !entry.IsEnabled;
            string prefix = entry.Location.StartsWith("HKLM", StringComparison.OrdinalIgnoreCase) ? "HKLM" : "HKCU";
            string leaf = StartupApprovedLeaf(activePath);

            if (storedInDisabledKey)
            {
                if (!enable)
                {
                    return new StartupToggleResult
                    {
                        Succeeded = true,
                        IsEnabled = false,
                        Location = entry.Location,
                        Command = entry.Command
                    };
                }

                if (!MoveRegistryValue(hive, currentPath, activePath, entry.Name))
                {
                    return Failed(entry);
                }
            }

            if (!TrySetStartupApproved(hive, leaf, entry.Name, enable))
            {
                return Failed(entry);
            }

            bool nowEnabled = IsStartupApprovedEnabled(hive, leaf, entry.Name);
            if (nowEnabled != enable)
            {
                return Failed(entry);
            }

            return new StartupToggleResult
            {
                Succeeded = true,
                IsEnabled = nowEnabled,
                Location = $"{prefix}\\{(storedInDisabledKey ? activePath : currentPath)}",
                Command = entry.Command
            };
        }

        private static StartupToggleResult ToggleStartupFolderEntry(StartupEntry entry)
        {
            string file = entry.Command;
            if (!File.Exists(file))
            {
                return Failed(entry);
            }

            string? directory = Path.GetDirectoryName(file);
            if (string.IsNullOrEmpty(directory))
            {
                return Failed(entry);
            }

            string baseName = Path.GetFileName(file);
            while (baseName.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
            {
                baseName = baseName[..^9];
            }

            bool enable = !entry.IsEnabled;
            string resultFile = file;
            if (file.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase) && enable)
            {
                string restored = Path.Combine(directory, baseName);
                if (File.Exists(restored))
                {
                    // Another file already owns the enabled name. Enabling this
                    // entry would silently point it at that file instead.
                    return Failed(entry);
                }

                File.Move(file, restored);

                if (!File.Exists(restored))
                {
                    return Failed(entry);
                }

                resultFile = restored;
            }

            bool common = string.Equals(
                entry.Location,
                Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup),
                StringComparison.OrdinalIgnoreCase);
            RegistryKey hive = common ? Registry.LocalMachine : Registry.CurrentUser;
            if (!TrySetStartupApproved(hive, "StartupFolder", baseName, enable))
            {
                return Failed(entry);
            }

            bool nowEnabled = IsStartupFolderEnabled(entry.Location, baseName, common) &&
                              !resultFile.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);
            if (nowEnabled != enable)
            {
                return Failed(entry);
            }

            return new StartupToggleResult
            {
                Succeeded = true,
                IsEnabled = nowEnabled,
                Location = entry.Location,
                Command = resultFile
            };
        }

        private static StartupToggleResult ToggleScheduledTask(StartupEntry entry)
        {
            using Microsoft.Win32.TaskScheduler.TaskService taskService = new();
            Microsoft.Win32.TaskScheduler.Task? task = taskService.GetTask(entry.Location);
            if (task is null)
            {
                return Failed(entry);
            }

            bool requested = !entry.IsEnabled;
            task.Enabled = requested;
            Microsoft.Win32.TaskScheduler.Task? updated = taskService.GetTask(entry.Location);
            if (updated is null || updated.Enabled != requested)
            {
                return Failed(entry);
            }

            return new StartupToggleResult
            {
                Succeeded = true,
                IsEnabled = updated.Enabled,
                Location = entry.Location,
                Command = entry.Command
            };
        }

        private static StartupToggleResult ToggleService(StartupEntry entry)
        {
            string? serviceName = ServiceNameFromLocation(entry.Location);
            ServiceController[] services = ServiceController.GetServices();
            ServiceController? service = services.FirstOrDefault(candidate =>
                string.Equals(candidate.ServiceName, serviceName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(candidate.DisplayName, entry.Name, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(candidate.ServiceName, entry.Name, StringComparison.OrdinalIgnoreCase));
            string? resolvedName = service?.ServiceName;
            foreach (ServiceController candidate in services)
            {
                candidate.Dispose();
            }

            if (string.IsNullOrEmpty(resolvedName))
            {
                return Failed(entry);
            }

            string regPath = $@"SYSTEM\CurrentControlSet\Services\{resolvedName}";
            using RegistryKey? key = Registry.LocalMachine.OpenSubKey(regPath, writable: true);
            if (key is null)
            {
                return Failed(entry);
            }

            int previous = Convert.ToInt32(key.GetValue("Start") ?? -1);
            int requested;
            if (entry.IsEnabled)
            {
                requested = 4;
                // Remember the original start type so Enable can put it back
                // instead of forcing Automatic.
                if (previous is >= 0 and <= 3)
                {
                    RegistryHelper.SetValue(ServiceStartStoreKey, resolvedName, previous, RegistryValueKind.DWord);
                }
            }
            else
            {
                requested = 2;
                if (RegistryHelper.TryReadValue(ServiceStartStoreKey, resolvedName, out object? saved)
                    && saved is int savedValue
                    && savedValue is >= 0 and <= 3)
                {
                    requested = savedValue;
                }
            }

            key.SetValue("Start", requested, RegistryValueKind.DWord);
            int actual = Convert.ToInt32(key.GetValue("Start") ?? -1);
            App.logger.Info(
                "[Startup] Service {Service} Start value changed from {Previous} to {Actual} (requested {Requested}).",
                resolvedName,
                previous,
                actual,
                requested);
            if (actual != requested)
            {
                return Failed(entry);
            }

            if (!entry.IsEnabled)
            {
                RegistryHelper.DeleteValue(ServiceStartStoreKey, resolvedName);
            }

            return new StartupToggleResult
            {
                Succeeded = true,
                IsEnabled = actual != 4,
                Location = $@"HKLM\{regPath}",
                Command = entry.Command
            };
        }

        private const string StartupApprovedRoot = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved";

        private static string StartupApprovedLeaf(string runKeyPath)
        {
            if (runKeyPath.Contains("WOW6432Node", StringComparison.OrdinalIgnoreCase))
            {
                return "Run32";
            }

            if (runKeyPath.EndsWith("RunOnce", StringComparison.OrdinalIgnoreCase))
            {
                return "RunOnce";
            }

            return "Run";
        }

        private static bool IsStartupFolderEnabled(string folder, string fileName, bool commonStartup)
        {
            RegistryKey hive = commonStartup ? Registry.LocalMachine : Registry.CurrentUser;
            return IsStartupApprovedEnabled(hive, "StartupFolder", fileName);
        }

        private static bool IsStartupApprovedEnabled(RegistryKey hive, string leaf, string valueName)
        {
            try
            {
                using RegistryKey? key = hive.OpenSubKey($@"{StartupApprovedRoot}\{leaf}", writable: false);
                if (key?.GetValue(valueName) is not byte[] data || data.Length == 0)
                {
                    return true;
                }

                return data[0] != 0x03;
            }
            catch (Exception exception)
            {
                App.logger.Debug(exception, "[Startup] Could not read StartupApproved\\{Leaf}\\{Name}.", leaf, valueName);
                return true;
            }
        }

        private static bool TrySetStartupApproved(RegistryKey hive, string leaf, string valueName, bool enabled)
        {
            using RegistryKey key = hive.CreateSubKey($@"{StartupApprovedRoot}\{leaf}", writable: true);
            byte[] data = new byte[12];
            data[0] = enabled ? (byte)0x02 : (byte)0x03;
            if (!enabled)
            {
                byte[] disabledAt = BitConverter.GetBytes(DateTime.UtcNow.ToFileTimeUtc());
                Buffer.BlockCopy(disabledAt, 0, data, 4, 8);
            }

            key.SetValue(valueName, data, RegistryValueKind.Binary);
            return IsStartupApprovedEnabled(hive, leaf, valueName) == enabled;
        }

        private static bool MoveRegistryValue(RegistryKey hive, string sourcePath, string destinationPath, string valueName)
        {
            using RegistryKey? source = hive.OpenSubKey(sourcePath, writable: true);
            if (source?.GetValue(valueName) is not object value)
            {
                return false;
            }

            RegistryValueKind kind = source.GetValueKind(valueName);
            using RegistryKey destination = hive.CreateSubKey(destinationPath, writable: true);
            destination.SetValue(valueName, value, kind);
            source.DeleteValue(valueName, throwOnMissingValue: false);
            using RegistryKey? check = hive.OpenSubKey(destinationPath, writable: false);
            return check?.GetValue(valueName) is not null;
        }

        private static string DisabledKeyPath(string activeKeyPath) =>
            activeKeyPath.Replace("\\Run", "\\Run-Disabled").Replace("\\RunOnce", "\\RunOnce-Disabled");

        private static string ToActiveRunPath(string path)
        {
            if (path.Contains("\\Run-DisabledOnce", StringComparison.OrdinalIgnoreCase))
            {
                return path.Replace("\\Run-DisabledOnce", "\\RunOnce");
            }

            if (path.Contains("\\RunOnce-Disabled", StringComparison.OrdinalIgnoreCase))
            {
                return path.Replace("\\RunOnce-Disabled", "\\RunOnce");
            }

            if (path.Contains("\\Run-Disabled", StringComparison.OrdinalIgnoreCase))
            {
                return path.Replace("\\Run-Disabled", "\\Run");
            }

            return path;
        }

        private static bool TryParseHive(string location, out RegistryKey? hive, out string path)
        {
            hive = null;
            path = "";
            if (location.StartsWith("HKLM\\", StringComparison.OrdinalIgnoreCase))
            {
                hive = Registry.LocalMachine;
                path = location[5..];
                return true;
            }

            if (location.StartsWith("HKCU\\", StringComparison.OrdinalIgnoreCase))
            {
                hive = Registry.CurrentUser;
                path = location[5..];
                return true;
            }

            return false;
        }

        private static string? ServiceNameFromLocation(string location)
        {
            const string marker = @"\Services\";
            int index = location.LastIndexOf(marker, StringComparison.OrdinalIgnoreCase);
            return index < 0 ? null : location[(index + marker.Length)..];
        }

        private static StartupToggleResult Failed(StartupEntry entry) => new()
        {
            IsEnabled = entry.IsEnabled,
            Location = entry.Location,
            Command = entry.Command
        };

        private static bool TryResolveInstalledFile(string command, out string installedPath)
        {
            installedPath = "";
            if (string.IsNullOrWhiteSpace(command))
            {
                return false;
            }

            string text = NormalizeCommand(command);
            if (text.Length == 0)
            {
                return false;
            }

            if (text[0] == '"')
            {
                int end = text.IndexOf('"', 1);
                string quoted = end > 1 ? text[1..end] : text.Trim('"');
                installedPath = quoted;
                return File.Exists(quoted);
            }

            string[] parts = text.Split(' ');
            string accumulated = "";
            string? found = null;
            foreach (string part in parts)
            {
                if (part.Length == 0)
                {
                    continue;
                }

                accumulated = accumulated.Length == 0 ? part : accumulated + " " + part;
                if (File.Exists(accumulated))
                {
                    found = accumulated;
                }
            }

            if (found != null)
            {
                installedPath = found;
                return true;
            }

            string first = parts[0].Trim('"');
            if (!Path.IsPathRooted(first) && first.IndexOf('\\') < 0 && first.IndexOf('/') < 0)
            {
                string fileName = Path.GetFileName(first);
                foreach (string directory in SearchDirectories())
                {
                    string candidate = Path.Combine(directory, fileName);
                    if (File.Exists(candidate))
                    {
                        installedPath = candidate;
                        return true;
                    }
                }
            }

            // Best-effort target for orphaned entries: the longest prefix that
            // looks like an executable, so risk assessment and the UI can show it.
            installedPath = first;
            for (int i = 1; i < parts.Length; i++)
            {
                string candidate = string.Join(" ", parts, 0, i + 1);
                if (candidate.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    installedPath = candidate;
                }
            }

            return false;
        }

        private const string ServiceStartStoreKey = @"HKLM\SOFTWARE\SynToolkit\Startup\ServiceStart";

        private static string NormalizeCommand(string command)
        {
            string text = command.Trim();
            if (text.StartsWith(@"\??\", StringComparison.Ordinal))
            {
                text = text[4..];
            }

            text = Environment.ExpandEnvironmentVariables(text);
            string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            if (text.StartsWith(@"\SystemRoot\", StringComparison.OrdinalIgnoreCase))
            {
                text = windows + text[@"\SystemRoot".Length..];
            }
            else if (text.StartsWith("System32\\", StringComparison.OrdinalIgnoreCase) ||
                     text.StartsWith("SysWOW64\\", StringComparison.OrdinalIgnoreCase))
            {
                text = Path.Combine(windows, text);
            }

            return text;
        }

        private static IEnumerable<string> SearchDirectories()
        {
            yield return Environment.SystemDirectory;
            yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SysWOW64");
            string? path = Environment.GetEnvironmentVariable("PATH");
            if (string.IsNullOrEmpty(path))
            {
                yield break;
            }

            foreach (string directory in path.Split(Path.PathSeparator))
            {
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    yield return directory.Trim();
                }
            }
        }

        private static string ResolveShortcut(string lnkPath)
        {
            if (!lnkPath.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) &&
                !lnkPath.EndsWith(".lnk.disabled", StringComparison.OrdinalIgnoreCase))
            {
                return lnkPath;
            }

            try
            {
                Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType != null)
                {
                    object? shell = Activator.CreateInstance(shellType);
                    object? shortcut = shell is null
                        ? null
                        : shellType.InvokeMember(
                            "CreateShortcut",
                            System.Reflection.BindingFlags.InvokeMethod,
                            null,
                            shell,
                            new object[] { lnkPath });
                    if (shortcut?.GetType().InvokeMember(
                            "TargetPath",
                            System.Reflection.BindingFlags.GetProperty,
                            null,
                            shortcut,
                            null) is string target &&
                        !string.IsNullOrWhiteSpace(target))
                    {
                        return target;
                    }
                }
            }
            catch (Exception exception)
            {
                App.logger.Debug(exception, "[Startup] Could not resolve shortcut {File} through the shell.", lnkPath);
            }

            try
            {
                byte[] bytes = File.ReadAllBytes(lnkPath);
                if (bytes.Length < 76)
                {
                    return "";
                }

                uint linkFlags = BitConverter.ToUInt32(bytes, 20);
                bool hasIdList = (linkFlags & 0x01) != 0;
                int offset = 76;
                if (hasIdList)
                {
                    if (offset + 2 > bytes.Length)
                    {
                        return "";
                    }

                    ushort idListSize = BitConverter.ToUInt16(bytes, offset);
                    offset += 2 + idListSize;
                }

                bool hasLinkInfo = (linkFlags & 0x02) != 0;
                if (hasLinkInfo && offset + 28 <= bytes.Length)
                {
                    uint localBasePathOffset = BitConverter.ToUInt32(bytes, offset + 16);
                    if (localBasePathOffset > 0)
                    {
                        string ansiPath = ReadNullTerminated(bytes, (int)(offset + localBasePathOffset), unicode: false);
                        if (!string.IsNullOrWhiteSpace(ansiPath))
                        {
                            return ansiPath;
                        }
                    }

                    uint headerSize = BitConverter.ToUInt32(bytes, offset + 4);
                    if (headerSize >= 0x24 && offset + 32 <= bytes.Length)
                    {
                        uint unicodeOffset = BitConverter.ToUInt32(bytes, offset + 28);
                        if (unicodeOffset > 0)
                        {
                            string unicodePath = ReadNullTerminated(bytes, (int)(offset + unicodeOffset), unicode: true);
                            if (!string.IsNullOrWhiteSpace(unicodePath))
                            {
                                return unicodePath;
                            }
                        }
                    }
                }

                return "";
            }
            catch
            {
                return "";
            }
        }

        private static string ReadNullTerminated(byte[] bytes, int start, bool unicode)
        {
            if (start < 0 || start >= bytes.Length)
            {
                return "";
            }

            if (!unicode)
            {
                int end = Array.IndexOf(bytes, (byte)0, start);
                return end > start
                    ? System.Text.Encoding.Default.GetString(bytes, start, end - start)
                    : "";
            }

            int index = start;
            while (index + 1 < bytes.Length && (bytes[index] != 0 || bytes[index + 1] != 0))
            {
                index += 2;
            }

            return index > start
                ? System.Text.Encoding.Unicode.GetString(bytes, start, index - start)
                : "";
        }

        private static string GetFilePublisher(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                return "Unknown";
            }

            try
            {
                System.Diagnostics.FileVersionInfo info = System.Diagnostics.FileVersionInfo.GetVersionInfo(filePath);
                return string.IsNullOrWhiteSpace(info.CompanyName) ? "Unknown" : info.CompanyName;
            }
            catch
            {
                return "Unknown";
            }
        }

        private static string GetFileDescription(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                return "";
            }

            try
            {
                System.Diagnostics.FileVersionInfo info = System.Diagnostics.FileVersionInfo.GetVersionInfo(filePath);
                return info.FileDescription ?? "";
            }
            catch
            {
                return "";
            }
        }

        private static RiskLevel AssessRisk(string name, string filePath, string command)
        {
            string publisher = GetFilePublisher(filePath);
            if (KnownSafePublishers.Contains(publisher))
            {
                return RiskLevel.Safe;
            }

            if (!File.Exists(filePath) && !string.IsNullOrEmpty(filePath))
            {
                return RiskLevel.High;
            }

            if (publisher == "Unknown")
            {
                return RiskLevel.Unknown;
            }

            string loweredName = name.ToLowerInvariant();
            foreach (string keyword in HighRiskKeywords)
            {
                if (loweredName.Contains(keyword.ToLowerInvariant()))
                {
                    return RiskLevel.Medium;
                }
            }

            return RiskLevel.Low;
        }

        private static bool IsCurrentProcessElevated()
        {
            try
            {
                using System.Security.Principal.WindowsIdentity identity = System.Security.Principal.WindowsIdentity.GetCurrent();
                return new System.Security.Principal.WindowsPrincipal(identity).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
            catch (Exception exception)
            {
                App.logger.Error(exception, "[Startup] Unable to determine administrator access.");
                return false;
            }
        }
    }
}
