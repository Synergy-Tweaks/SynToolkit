#nullable enable

using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Principal;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace SynToolkit.Services
{
    public sealed record PowerPlanSnapshot(
        Guid? ActiveSchemeId,
        string ActiveSchemeName,
        bool IsSynToolkitPlanInstalled,
        bool IsSynToolkitPlanActive,
        bool HasSynToolkitSchemeConflict,
        Guid? PreviousSchemeId,
        string? PreviousSchemeName);

    public sealed record PowerPlanImportResult(Guid SchemeId, string SchemeName);

    public sealed record InstalledPowerPlan(Guid SchemeId, string Name);

    public sealed record PowerPlanValueChange(
        Guid SubgroupId, Guid SettingId, bool OnAc, uint PreviousValue, uint Value);

    public sealed record BundledPowerPlan(
        string FileName,
        string DisplayName,
        string Description,
        string FilePath,
        IReadOnlyList<string> ActiveSchemeNameHints);

    /// <summary>
    /// Imports and manages Windows power schemes without invoking cmd.exe.
    /// Mutations are serialized for the whole process and destructive operations
    /// are limited to a plan positively identified as SynToolkit-owned.
    /// </summary>
    public sealed class PowerPlanService
    {
        public static readonly Guid SynToolkitSchemeId = Guid.Parse("dab60367-53fe-4fbc-825e-521d80b4dbe1");
        public static readonly Guid BalancedSchemeId = Guid.Parse("381b4222-f694-41f0-9685-ff5bb260df2e");
        public static readonly Guid HighPerformanceSchemeId = Guid.Parse("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
        public static readonly Guid PowerSaverSchemeId = Guid.Parse("a1841308-3541-4fab-bc81-f71556f20b4a");
        public static readonly Guid UltimatePerformanceSchemeId = Guid.Parse("e9a42b02-d5df-448d-aa00-03f14749eb61");

        public static bool IsWindowsDefaultScheme(Guid schemeId) =>
            schemeId == BalancedSchemeId || schemeId == HighPerformanceSchemeId ||
            schemeId == PowerSaverSchemeId || schemeId == UltimatePerformanceSchemeId;

        private const string BuiltInSchemeName = "SynToolkit SOS Performance";
        private const string LegacyBuiltInSchemeName = "SynToolkit Performance";
        private const string BuiltInResourceName = "SynToolkit.PowerPlans.SOS.pow";
        private const string StateRegistryPath = @"SOFTWARE\SynToolkit\PowerPlans";
        private const string PreviousSchemeValueName = "PreviousSchemeGuid";
        private const string OwnedSchemeValueName = "OwnedSchemeGuid";
        internal const long MaximumPlanFileBytes = 64L * 1024L * 1024L;
        
        private static readonly string BundledPlansDirectory = Path.Combine(
            AppContext.BaseDirectory, "Assets", "PowerPlans");
        private static readonly string BundledPlansManifestPath = Path.Combine(
            BundledPlansDirectory, "manifest.json");

        private static readonly Regex GuidPattern = new(
            @"[0-9a-fA-F]{8}-(?:[0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly SemaphoreSlim OperationLock = new(1, 1);

        private readonly string _powerCfgPath = Path.Combine(Environment.SystemDirectory, "powercfg.exe");

        public bool CanMutatePowerPlans => IsCurrentProcessElevated();

        public IReadOnlyList<BundledPowerPlan> GetBundledPlans()
        {
            var plans = new List<BundledPowerPlan>();
            
            if (!Directory.Exists(BundledPlansDirectory))
                return plans;
            
            // Try to load manifest for display names and descriptions
            Dictionary<string, BundledPowerPlanManifestEntry>? manifest = null;
            if (File.Exists(BundledPlansManifestPath))
            {
                try
                {
                    string json = File.ReadAllText(BundledPlansManifestPath);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("plans", out var plansArray))
                    {
                        manifest = new Dictionary<string, BundledPowerPlanManifestEntry>(StringComparer.OrdinalIgnoreCase);
                        foreach (var planElement in plansArray.EnumerateArray())
                        {
                            string? fileName = planElement.TryGetProperty("fileName", out var fn) ? fn.GetString() : null;
                            string? displayName = planElement.TryGetProperty("displayName", out var dn) ? dn.GetString() : null;
                            string? description = planElement.TryGetProperty("description", out var desc) ? desc.GetString() : null;
                            IReadOnlyList<string> activeSchemeNameHints = planElement.TryGetProperty("activeSchemeNameHints", out var hintsElement) &&
                                hintsElement.ValueKind == JsonValueKind.Array
                                    ? hintsElement.EnumerateArray()
                                        .Where(element => element.ValueKind == JsonValueKind.String)
                                        .Select(element => element.GetString()?.Trim())
                                        .Where(hint => !string.IsNullOrWhiteSpace(hint))
                                        .Select(hint => hint!)
                                        .Distinct(StringComparer.OrdinalIgnoreCase)
                                        .ToArray()
                                    : Array.Empty<string>();
                            
                            if (!string.IsNullOrEmpty(fileName) && !string.IsNullOrEmpty(displayName))
                            {
                                manifest[fileName] = new BundledPowerPlanManifestEntry(
                                    displayName,
                                    description ?? "No description provided.",
                                    activeSchemeNameHints);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    App.logger.Debug(ex, "Failed to parse power plans manifest.");
                }
            }
            
            // Enumerate .pow files in the directory
            foreach (string filePath in Directory.EnumerateFiles(BundledPlansDirectory, "*.pow", SearchOption.TopDirectoryOnly))
            {
                string fileName = Path.GetFileName(filePath);
                
                // Skip the embedded SOS.pow if it somehow exists here
                if (fileName.Equals("SOS.pow", StringComparison.OrdinalIgnoreCase))
                    continue;
                
                string displayName;
                string description;
                IReadOnlyList<string> activeSchemeNameHints;
                
                if (manifest?.TryGetValue(fileName, out var info) == true)
                {
                    displayName = info.DisplayName;
                    description = info.Description;
                    activeSchemeNameHints = info.ActiveSchemeNameHints;
                }
                else
                {
                    // Fallback: use filename without extension as display name
                    displayName = Path.GetFileNameWithoutExtension(fileName);
                    description = "No description provided.";
                    activeSchemeNameHints = Array.Empty<string>();
                }
                
                plans.Add(new BundledPowerPlan(fileName, displayName, description, filePath, activeSchemeNameHints));
            }
            
            return plans.OrderBy(p => p.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private sealed record BundledPowerPlanManifestEntry(
            string DisplayName,
            string Description,
            IReadOnlyList<string> ActiveSchemeNameHints);

        public async Task<PowerPlanSnapshot> GetStateAsync(CancellationToken cancellationToken = default)
        {
            // The two queries are independent reads: run them together so one
            // slow powercfg spawn doesn't stack on top of the other.
            Task<ProcessResult> activeTask = RunPowerCfgAsync(
                cancellationToken,
                "read the active power plan",
                "/getactivescheme");
            Task<ProcessResult> listTask = RunPowerCfgAsync(
                cancellationToken,
                "list Windows power plans",
                "/list");
            await Task.WhenAll(activeTask, listTask);
            ProcessResult activeResult = await activeTask;
            ProcessResult listResult = await listTask;
            EnsureSuccess(activeResult, "read the active power plan");

            Guid? activeSchemeId = ExtractFirstGuid(activeResult.StandardOutput);
            if (activeSchemeId is null)
            {
                LogPowerCfgFailure("read the active power plan", activeResult);
                throw CreateMalformedOutputException("read the active power plan", activeResult);
            }
            IReadOnlyList<PowerSchemeInfo> schemes = ParseSchemeRecords(listResult.StandardOutput);
            PowerSchemeInfo? activeScheme = activeSchemeId is Guid activeId
                ? schemes.FirstOrDefault(scheme => scheme.Id == activeId)
                : null;
            string activeSchemeName = activeScheme?.Name
                ?? ExtractSchemeName(activeResult.StandardOutput, activeSchemeId);

            PowerSchemeInfo? fixedScheme = schemes.FirstOrDefault(scheme => scheme.Id == SynToolkitSchemeId);
            Guid? ownershipMarker = ReadRegistryGuid(OwnedSchemeValueName);
            bool isOwned = fixedScheme is not null && IsOwnedSynToolkitScheme(fixedScheme, ownershipMarker);
            bool hasConflict = fixedScheme is not null && !isOwned;

            Guid? previousSchemeId = ReadRegistryGuid(PreviousSchemeValueName);
            PowerSchemeInfo? previousScheme = previousSchemeId is Guid previousId && previousId != SynToolkitSchemeId
                ? schemes.FirstOrDefault(scheme => scheme.Id == previousId)
                : null;
            if (previousSchemeId is not null && previousScheme is null)
            {
                previousSchemeId = null;
            }

            return new PowerPlanSnapshot(
                activeSchemeId,
                activeSchemeName,
                isOwned,
                isOwned && activeSchemeId == SynToolkitSchemeId,
                hasConflict,
                previousSchemeId,
                previousScheme?.Name);
        }

        public async Task<IReadOnlyList<InstalledPowerPlan>> GetInstalledPlansAsync(
            CancellationToken cancellationToken = default) =>
            (await GetInstalledSchemesAsync(cancellationToken))
                .Select(scheme => new InstalledPowerPlan(scheme.Id, scheme.Name))
                .OrderBy(scheme => scheme.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();

        public async Task<PowerPlanInspection> ReadInstalledPlanAsync(
            Guid schemeId, CancellationToken cancellationToken = default)
        {
            InstalledPowerPlan plan = (await GetInstalledPlansAsync(cancellationToken))
                .FirstOrDefault(item => item.SchemeId == schemeId)
                ?? throw new InvalidOperationException("The power plan is no longer installed.");
            return await Task.Run(() => PowerPlanSettingsReader.ReadInstalledScheme(
                schemeId, plan.Name, cancellationToken), cancellationToken);
        }

        public async Task<InstalledPowerPlan> DuplicatePlanAsync(
            Guid sourceSchemeId, string name, bool activate,
            CancellationToken cancellationToken = default)
        {
            EnsureCanMutate("create a power plan");
            string safeName = ValidatePlanName(name);
            await OperationLock.WaitAsync(cancellationToken);
            Guid newSchemeId = Guid.NewGuid();
            bool created = false;
            try
            {
                IReadOnlyList<PowerSchemeInfo> schemes = await GetInstalledSchemesAsync(cancellationToken);
                if (!schemes.Any(scheme => scheme.Id == sourceSchemeId))
                {
                    throw new InvalidOperationException("The base power plan is no longer installed.");
                }

                await RunCheckedAsync(cancellationToken, "duplicate the power plan",
                    "/duplicatescheme", sourceSchemeId.ToString("D"), newSchemeId.ToString("D"));
                created = true;
                await RunCheckedAsync(cancellationToken, "name the new power plan",
                    "/changename", newSchemeId.ToString("D"), safeName);
                if (activate)
                {
                    await RunCheckedAsync(cancellationToken, "activate the new power plan",
                        "/setactive", newSchemeId.ToString("D"));
                    await VerifyActiveSchemeAsync(newSchemeId, cancellationToken);
                }
                return new InstalledPowerPlan(newSchemeId, safeName);
            }
            catch
            {
                if (created)
                {
                    try
                    {
                        await RunCheckedAsync(CancellationToken.None, "remove an incomplete power plan",
                            "/delete", newSchemeId.ToString("D"));
                    }
                    catch (Exception exception)
                    {
                        App.logger.Warn(exception, "Could not remove incomplete power plan {SchemeId}.", newSchemeId);
                    }
                }
                throw;
            }
            finally
            {
                OperationLock.Release();
            }
        }

        public async Task SavePlanValuesAsync(
            Guid schemeId, IReadOnlyList<PowerPlanValueChange> changes,
            CancellationToken cancellationToken = default)
        {
            EnsureCanMutate("save power-plan settings");
            if (IsWindowsDefaultScheme(schemeId))
            {
                throw new InvalidOperationException(
                    "Windows default plans cannot be changed here. Use Save as to make your own copy.");
            }
            if (changes.Count == 0) return;

            await OperationLock.WaitAsync(cancellationToken);
            List<PowerPlanValueChange> applied = new();
            try
            {
                if (!await IsSchemeInstalledAsync(schemeId, cancellationToken))
                {
                    throw new InvalidOperationException("The power plan is no longer installed.");
                }
                foreach (PowerPlanValueChange change in changes)
                {
                    if (change.Value == change.PreviousValue) continue;
                    await WritePlanValueAsync(schemeId, change, cancellationToken);
                    applied.Add(change);
                }
                PowerPlanSnapshot state = await GetStateAsync(cancellationToken);
                if (state.ActiveSchemeId == schemeId)
                {
                    await RunCheckedAsync(cancellationToken, "refresh the active power plan",
                        "/setactive", schemeId.ToString("D"));
                }
            }
            catch
            {
                foreach (PowerPlanValueChange change in applied.AsEnumerable().Reverse())
                {
                    try
                    {
                        await WritePlanValueAsync(schemeId,
                            change with { Value = change.PreviousValue }, CancellationToken.None);
                    }
                    catch (Exception exception)
                    {
                        App.logger.Error(exception, "Could not roll back a power setting in {SchemeId}.", schemeId);
                    }
                }
                if (applied.Count > 0)
                {
                    try
                    {
                        PowerPlanSnapshot state = await GetStateAsync(CancellationToken.None);
                        if (state.ActiveSchemeId == schemeId)
                        {
                            await RunCheckedAsync(CancellationToken.None,
                                "restore the active power plan", "/setactive", schemeId.ToString("D"));
                        }
                    }
                    catch (Exception exception)
                    {
                        App.logger.Error(exception, "Could not refresh rolled-back power plan {SchemeId}.", schemeId);
                    }
                }
                throw;
            }
            finally
            {
                OperationLock.Release();
            }
        }

        private Task WritePlanValueAsync(Guid schemeId, PowerPlanValueChange change,
            CancellationToken cancellationToken) =>
            RunCheckedAsync(cancellationToken, "save a power setting",
                change.OnAc ? "/setacvalueindex" : "/setdcvalueindex",
                schemeId.ToString("D"), change.SubgroupId.ToString("D"),
                change.SettingId.ToString("D"), change.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));

        private static string ValidatePlanName(string name)
        {
            string trimmed = name.Trim();
            if (trimmed.Length is < 1 or > 80 || trimmed.Any(char.IsControl))
            {
                throw new ArgumentException("Enter a power-plan name between 1 and 80 characters.", nameof(name));
            }
            return trimmed;
        }

        public async Task ImportBuiltInPlanAsync(CancellationToken cancellationToken = default)
        {
            EnsureCanMutate("import the SynToolkit power plan");
            await OperationLock.WaitAsync(cancellationToken);
            string tempPlanPath = CreateTemporaryPlanPath("SOS");

            try
            {
                await ExtractBuiltInPlanAsync(tempPlanPath, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();

                // Once mutation begins, page navigation must not interrupt the
                // transaction. Every powercfg call still has its own 30-second timeout.
                await ImportPlanCoreAsync(
                    tempPlanPath,
                    SynToolkitSchemeId,
                    brandAsSynToolkitPlan: true,
                    CancellationToken.None);
            }
            finally
            {
                OperationLock.Release();
                TryDeleteTemporaryFile(tempPlanPath);
            }
        }

        public async Task<PowerPlanImportResult> ImportCustomPlanAsync(
            string filePath,
            CancellationToken cancellationToken = default)
        {
            EnsureCanMutate("import a custom power plan");
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException("Choose a .pow file to import.", nameof(filePath));
            }

            string fullPath = Path.GetFullPath(filePath);
            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException("The selected power-plan file no longer exists.", fullPath);
            }

            if (!string.Equals(Path.GetExtension(fullPath), ".pow", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Only Windows .pow power-plan files can be imported.");
            }

            Guid importedSchemeId = Guid.NewGuid();
            string immutablePlanPath = CreateTemporaryPlanPath("Import");
            await OperationLock.WaitAsync(cancellationToken);

            try
            {
                await CopyPlanToImmutableSnapshotAsync(fullPath, immutablePlanPath, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();

                await ImportPlanCoreAsync(
                    immutablePlanPath,
                    importedSchemeId,
                    brandAsSynToolkitPlan: false,
                    CancellationToken.None);

                PowerPlanSnapshot state = await GetStateAsync(CancellationToken.None);
                return new PowerPlanImportResult(
                    importedSchemeId,
                    state.ActiveSchemeId == importedSchemeId
                        ? state.ActiveSchemeName
                        : Path.GetFileNameWithoutExtension(fullPath));
            }
            finally
            {
                OperationLock.Release();
                TryDeleteTemporaryFile(immutablePlanPath);
            }
        }

        public async Task ActivateSynToolkitPlanAsync(CancellationToken cancellationToken = default)
        {
            EnsureCanMutate("activate the SynToolkit power plan");
            await OperationLock.WaitAsync(cancellationToken);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                CancellationToken transactionToken = CancellationToken.None;
                PowerPlanSnapshot state = await GetStateAsync(transactionToken);
                EnsureManagedSynToolkitPlan(state);

                if (state.IsSynToolkitPlanActive)
                {
                    return;
                }

                if (state.ActiveSchemeId is Guid previousSchemeId)
                {
                    SaveRegistryGuid(PreviousSchemeValueName, previousSchemeId);
                }

                SaveRegistryGuid(OwnedSchemeValueName, SynToolkitSchemeId);
                await RunCheckedAsync(
                    transactionToken,
                    "activate the SynToolkit power plan",
                    "/setactive",
                    SynToolkitSchemeId.ToString("D"));
                await VerifyActiveSchemeAsync(SynToolkitSchemeId, transactionToken);
            }
            finally
            {
                OperationLock.Release();
            }
        }

        public async Task RestorePreviousPlanAsync(CancellationToken cancellationToken = default)
        {
            EnsureCanMutate("restore a previous power plan");
            await OperationLock.WaitAsync(cancellationToken);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                CancellationToken transactionToken = CancellationToken.None;
                Guid targetSchemeId = await ResolveFallbackSchemeAsync(transactionToken);
                await RunCheckedAsync(
                    transactionToken,
                    "restore a recovery power plan",
                    "/setactive",
                    targetSchemeId.ToString("D"));
                await VerifyActiveSchemeAsync(targetSchemeId, transactionToken);
            }
            finally
            {
                OperationLock.Release();
            }
        }

        public async Task ActivateBalancedPlanAsync(CancellationToken cancellationToken = default)
        {
            EnsureCanMutate("activate Windows Balanced");
            await OperationLock.WaitAsync(cancellationToken);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                CancellationToken transactionToken = CancellationToken.None;
                if (!await IsSchemeInstalledAsync(BalancedSchemeId, transactionToken))
                {
                    throw new InvalidOperationException("Windows Balanced is not available on this computer.");
                }

                await RunCheckedAsync(
                    transactionToken,
                    "activate Windows Balanced",
                    "/setactive",
                    BalancedSchemeId.ToString("D"));
                await VerifyActiveSchemeAsync(BalancedSchemeId, transactionToken);
            }
            finally
            {
                OperationLock.Release();
            }
        }

        public async Task RestoreDefaultSchemesAsync(CancellationToken cancellationToken = default)
        {
            EnsureCanMutate("restore the default power plans");
            await OperationLock.WaitAsync(cancellationToken);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                CancellationToken transactionToken = CancellationToken.None;
                await RunCheckedAsync(
                    transactionToken,
                    "restore the default power plans",
                    "/restoredefaultschemes");
                TryClearPowerPlanRegistryState();
            }
            finally
            {
                OperationLock.Release();
            }
        }

        public async Task RemoveSynToolkitPlanAsync(CancellationToken cancellationToken = default)
        {
            EnsureCanMutate("remove the SynToolkit power plan");
            await OperationLock.WaitAsync(cancellationToken);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                CancellationToken transactionToken = CancellationToken.None;
                PowerPlanSnapshot state = await GetStateAsync(transactionToken);
                if (state.HasSynToolkitSchemeConflict)
                {
                    throw new InvalidOperationException(
                        "Another power plan is using SynToolkit's reserved plan ID. SynToolkit will not modify or remove it.");
                }

                if (!state.IsSynToolkitPlanInstalled)
                {
                    TryClearPowerPlanRegistryState();
                    return;
                }

                if (state.IsSynToolkitPlanActive)
                {
                    Guid fallbackSchemeId = await ResolveFallbackSchemeAsync(transactionToken);
                    await RunCheckedAsync(
                        transactionToken,
                        "leave the SynToolkit power plan",
                        "/setactive",
                        fallbackSchemeId.ToString("D"));
                    await VerifyActiveSchemeAsync(fallbackSchemeId, transactionToken);
                }

                await RunCheckedAsync(
                    transactionToken,
                    "remove the SynToolkit power plan",
                    "/delete",
                    SynToolkitSchemeId.ToString("D"));
                await VerifySchemeAbsentAsync(SynToolkitSchemeId, transactionToken);
                TryClearPowerPlanRegistryState();
            }
            finally
            {
                OperationLock.Release();
            }
        }

        private async Task ImportPlanCoreAsync(
            string planPath,
            Guid destinationSchemeId,
            bool brandAsSynToolkitPlan,
            CancellationToken transactionToken)
        {
            PowerPlanSnapshot initialState = await GetStateAsync(transactionToken);
            if (destinationSchemeId == SynToolkitSchemeId && initialState.HasSynToolkitSchemeConflict)
            {
                throw new InvalidOperationException(
                    "Another power plan is using SynToolkit's reserved plan ID. Rename or remove that conflicting plan before importing SOS.pow.");
            }

            Guid? originalActiveSchemeId = initialState.ActiveSchemeId;
            bool existingManagedPlan = destinationSchemeId == SynToolkitSchemeId && initialState.IsSynToolkitPlanInstalled;
            bool existingPlanDeleted = false;
            bool importAttempted = false;
            bool mutationStarted = false;
            bool retainBackup = false;
            string? backupPlanPath = existingManagedPlan
                ? CreateTemporaryPlanPath("Backup")
                : null;

            try
            {
                if (backupPlanPath is not null)
                {
                    await RunCheckedAsync(
                        transactionToken,
                        "back up the existing SynToolkit power plan",
                        "/export",
                        backupPlanPath,
                        destinationSchemeId.ToString("D"));
                    EnsurePlanFileLooksValid(backupPlanPath, "Windows did not create a valid backup of the existing SynToolkit plan.");
                }

                mutationStarted = true;
                if (existingManagedPlan)
                {
                    if (initialState.IsSynToolkitPlanActive)
                    {
                        Guid fallbackSchemeId = await ResolveFallbackSchemeAsync(transactionToken);
                        await RunCheckedAsync(
                            transactionToken,
                            "leave the existing SynToolkit power plan",
                            "/setactive",
                            fallbackSchemeId.ToString("D"));
                        await VerifyActiveSchemeAsync(fallbackSchemeId, transactionToken);
                    }

                    await RunCheckedAsync(
                        transactionToken,
                        "replace the existing SynToolkit power plan",
                        "/delete",
                        destinationSchemeId.ToString("D"));
                    existingPlanDeleted = true;
                    await VerifySchemeAbsentAsync(destinationSchemeId, transactionToken);
                }

                importAttempted = true;
                await RunCheckedAsync(
                    transactionToken,
                    "import the selected .pow file",
                    "/import",
                    planPath,
                    destinationSchemeId.ToString("D"));
                await VerifySchemePresentAsync(destinationSchemeId, transactionToken);

                if (brandAsSynToolkitPlan)
                {
                    await RunCheckedAsync(
                        transactionToken,
                        "name the SynToolkit power plan",
                        "/changename",
                        destinationSchemeId.ToString("D"),
                        BuiltInSchemeName,
                        "Official SOS.pow plan managed by SynToolkit");
                }

                if (originalActiveSchemeId is Guid previousSchemeId && previousSchemeId != destinationSchemeId)
                {
                    SaveRegistryGuid(PreviousSchemeValueName, previousSchemeId);
                }

                if (brandAsSynToolkitPlan)
                {
                    SaveRegistryGuid(OwnedSchemeValueName, SynToolkitSchemeId);
                }

                await RunCheckedAsync(
                    transactionToken,
                    "activate the imported power plan",
                    "/setactive",
                    destinationSchemeId.ToString("D"));
                await VerifyActiveSchemeAsync(destinationSchemeId, transactionToken);
            }
            catch (Exception originalException)
            {
                RollbackResult rollbackResult = await TryRollbackImportAsync(
                    destinationSchemeId,
                    originalActiveSchemeId,
                    backupPlanPath,
                    existingManagedPlan,
                    existingPlanDeleted,
                    importAttempted,
                    mutationStarted);
                retainBackup = existingManagedPlan &&
                    existingPlanDeleted &&
                    !rollbackResult.Succeeded &&
                    backupPlanPath is not null &&
                    File.Exists(backupPlanPath);
                if (retainBackup)
                {
                    App.logger.Error(
                        "Retaining power-plan recovery backup {BackupFileName} because rollback did not fully restore the previous plan.",
                        Path.GetFileName(backupPlanPath));
                }

                string recoveryNotice = retainBackup
                    ? " A recovery backup was retained for support-assisted restoration."
                    : string.Empty;
                throw new InvalidOperationException(
                    $"{originalException.Message} {rollbackResult.Message}{recoveryNotice}".Trim(),
                    originalException);
            }
            finally
            {
                if (backupPlanPath is not null && !retainBackup)
                {
                    TryDeleteTemporaryFile(backupPlanPath);
                }
            }
        }

        private async Task<RollbackResult> TryRollbackImportAsync(
            Guid importedSchemeId,
            Guid? originalActiveSchemeId,
            string? backupPlanPath,
            bool existingManagedPlan,
            bool existingPlanDeleted,
            bool importAttempted,
            bool mutationStarted)
        {
            if (!mutationStarted)
            {
                return new RollbackResult(true, "No Windows power settings were changed.");
            }

            try
            {
                CancellationToken rollbackToken = CancellationToken.None;
                Guid safeSchemeId = await ResolveFallbackSchemeAsync(
                    rollbackToken,
                    originalActiveSchemeId == importedSchemeId ? null : originalActiveSchemeId);
                await RunCheckedAsync(
                    rollbackToken,
                    "restore a safe power plan during rollback",
                    "/setactive",
                    safeSchemeId.ToString("D"));
                await VerifyActiveSchemeAsync(safeSchemeId, rollbackToken);

                if (importAttempted && await IsSchemeInstalledAsync(importedSchemeId, rollbackToken))
                {
                    await RunCheckedAsync(
                        rollbackToken,
                        "remove the incomplete power plan during rollback",
                        "/delete",
                        importedSchemeId.ToString("D"));
                    await VerifySchemeAbsentAsync(importedSchemeId, rollbackToken);
                }

                if (existingManagedPlan && existingPlanDeleted)
                {
                    if (string.IsNullOrWhiteSpace(backupPlanPath) || !File.Exists(backupPlanPath))
                    {
                        throw new InvalidOperationException("The existing SynToolkit plan backup is unavailable.");
                    }

                    await RunCheckedAsync(
                        rollbackToken,
                        "restore the previous SynToolkit power plan",
                        "/import",
                        backupPlanPath,
                        importedSchemeId.ToString("D"));
                    await VerifySchemePresentAsync(importedSchemeId, rollbackToken);
                    SaveRegistryGuid(OwnedSchemeValueName, SynToolkitSchemeId);
                }
                else if (importedSchemeId == SynToolkitSchemeId && !existingManagedPlan)
                {
                    TryClearRegistryValue(OwnedSchemeValueName);
                }

                if (originalActiveSchemeId is Guid originalId &&
                    await IsSchemeInstalledAsync(originalId, rollbackToken))
                {
                    await RunCheckedAsync(
                        rollbackToken,
                        "restore the original active power plan",
                        "/setactive",
                        originalId.ToString("D"));
                    await VerifyActiveSchemeAsync(originalId, rollbackToken);
                }

                return new RollbackResult(
                    true,
                    existingManagedPlan && existingPlanDeleted
                        ? "The previous SynToolkit plan and active plan were restored."
                        : "Windows was returned to the previously active safe plan.");
            }
            catch (Exception rollbackException)
            {
                App.logger.Error(rollbackException, "Power-plan import rollback failed.");
                return new RollbackResult(
                    false,
                    "Automatic rollback did not fully complete; use the Recovery section or Windows power settings to select a safe plan.");
            }
        }

        private async Task<Guid> ResolveFallbackSchemeAsync(
            CancellationToken cancellationToken,
            Guid? preferredSchemeId = null)
        {
            IReadOnlyList<PowerSchemeInfo> schemes = await GetInstalledSchemesAsync(cancellationToken);

            if (preferredSchemeId is Guid preferred &&
                preferred != SynToolkitSchemeId &&
                schemes.Any(scheme => scheme.Id == preferred))
            {
                return preferred;
            }

            Guid? storedSchemeId = ReadRegistryGuid(PreviousSchemeValueName);
            if (storedSchemeId is Guid stored &&
                stored != SynToolkitSchemeId &&
                schemes.Any(scheme => scheme.Id == stored))
            {
                return stored;
            }

            if (schemes.Any(scheme => scheme.Id == BalancedSchemeId))
            {
                return BalancedSchemeId;
            }

            PowerSchemeInfo? firstSafeScheme = schemes.FirstOrDefault(scheme => scheme.Id != SynToolkitSchemeId);
            return firstSafeScheme?.Id
                ?? throw new InvalidOperationException("No alternative Windows power plan is available for recovery.");
        }

        private async Task<IReadOnlyList<PowerSchemeInfo>> GetInstalledSchemesAsync(CancellationToken cancellationToken)
        {
            ProcessResult result = await RunPowerCfgAsync(
                cancellationToken,
                "list Windows power plans",
                "/list");
            EnsureSuccess(result, "list Windows power plans");
            return ParseSchemeRecords(result.StandardOutput);
        }

        private async Task<bool> IsSchemeInstalledAsync(Guid schemeId, CancellationToken cancellationToken) =>
            (await GetInstalledSchemesAsync(cancellationToken)).Any(scheme => scheme.Id == schemeId);

        private async Task VerifySchemePresentAsync(Guid schemeId, CancellationToken cancellationToken)
        {
            if (!await IsSchemeInstalledAsync(schemeId, cancellationToken))
            {
                throw new InvalidOperationException($"Windows did not retain the imported power plan ({schemeId:D}).");
            }
        }

        private async Task VerifySchemeAbsentAsync(Guid schemeId, CancellationToken cancellationToken)
        {
            if (await IsSchemeInstalledAsync(schemeId, cancellationToken))
            {
                throw new InvalidOperationException($"Windows did not remove the power plan ({schemeId:D}).");
            }
        }

        private async Task VerifyActiveSchemeAsync(Guid expectedSchemeId, CancellationToken cancellationToken)
        {
            ProcessResult result = await RunPowerCfgAsync(
                cancellationToken,
                "verify the active power plan",
                "/getactivescheme");
            EnsureSuccess(result, "verify the active power plan");
            Guid? actualSchemeId = ExtractFirstGuid(result.StandardOutput);
            if (actualSchemeId is null)
            {
                LogPowerCfgFailure("verify the active power plan", result);
                throw CreateMalformedOutputException("verify the active power plan", result);
            }

            if (actualSchemeId != expectedSchemeId)
            {
                throw new InvalidOperationException(
                    $"Windows did not activate the requested power plan ({expectedSchemeId:D}).");
            }
        }

        private static void EnsureManagedSynToolkitPlan(PowerPlanSnapshot state)
        {
            if (state.HasSynToolkitSchemeConflict)
            {
                throw new InvalidOperationException(
                    "Another power plan is using SynToolkit's reserved plan ID. SynToolkit will not modify it.");
            }

            if (!state.IsSynToolkitPlanInstalled)
            {
                throw new InvalidOperationException("Import the SynToolkit SOS plan before activating it.");
            }
        }

        private static bool IsOwnedSynToolkitScheme(PowerSchemeInfo scheme, Guid? ownershipMarker) =>
            scheme.Id == SynToolkitSchemeId &&
            (ownershipMarker == SynToolkitSchemeId ||
             string.Equals(scheme.Name, BuiltInSchemeName, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(scheme.Name, LegacyBuiltInSchemeName, StringComparison.OrdinalIgnoreCase));

        private static IReadOnlyList<PowerSchemeInfo> ParseSchemeRecords(string output)
        {
            Dictionary<Guid, PowerSchemeInfo> schemes = new();
            foreach (string line in (output ?? string.Empty).Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
            {
                Match match = GuidPattern.Match(line);
                if (!match.Success || !Guid.TryParse(match.Value, out Guid schemeId))
                {
                    continue;
                }

                string name = ExtractSchemeName(line, schemeId);
                schemes[schemeId] = new PowerSchemeInfo(schemeId, name);
            }

            return schemes.Values.ToArray();
        }

        private static async Task ExtractBuiltInPlanAsync(string destinationPath, CancellationToken cancellationToken)
        {
            await using Stream source = Assembly.GetExecutingAssembly().GetManifestResourceStream(BuiltInResourceName)
                ?? throw new InvalidOperationException("The built-in SOS.pow resource is missing from SynToolkit.");
            await CopyStreamToNewPlanFileAsync(source, destinationPath, cancellationToken);
            EnsurePlanFileLooksValid(destinationPath, "The embedded SOS.pow resource is empty or too large.");
        }

        private static async Task CopyPlanToImmutableSnapshotAsync(
            string sourcePath,
            string destinationPath,
            CancellationToken cancellationToken)
        {
            FileInfo sourceInfo = new(sourcePath);
            if (sourceInfo.Length <= 0 || sourceInfo.Length > MaximumPlanFileBytes)
            {
                throw new InvalidDataException("The selected .pow file is empty or larger than 64 MB.");
            }

            await using FileStream source = new(
                sourcePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            await CopyStreamToNewPlanFileAsync(source, destinationPath, cancellationToken);
            EnsurePlanFileLooksValid(destinationPath, "The selected .pow file could not be copied safely.");
        }

        private static async Task CopyStreamToNewPlanFileAsync(
            Stream source,
            string destinationPath,
            CancellationToken cancellationToken)
        {
            await using FileStream destination = new(
                destinationPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            await CopyBoundedAsync(source, destination, cancellationToken);
            await destination.FlushAsync(cancellationToken);
        }

        internal static async Task CopyBoundedAsync(
            Stream source,
            Stream destination,
            CancellationToken cancellationToken)
        {
            byte[] buffer = new byte[81920];
            long totalBytes = 0;
            while (true)
            {
                int bytesRead = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                if (bytesRead == 0)
                {
                    break;
                }

                totalBytes += bytesRead;
                if (totalBytes > MaximumPlanFileBytes)
                {
                    throw new InvalidDataException("The selected .pow file is larger than 64 MB.");
                }

                await destination.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
            }

            if (totalBytes == 0)
            {
                throw new InvalidDataException("The selected .pow file is empty.");
            }
        }

        private static void EnsurePlanFileLooksValid(string path, string errorMessage)
        {
            FileInfo planFile = new(path);
            if (!planFile.Exists || planFile.Length <= 0 || planFile.Length > MaximumPlanFileBytes)
            {
                throw new InvalidDataException(errorMessage);
            }
        }

        private async Task RunCheckedAsync(
            CancellationToken cancellationToken,
            string action,
            params string[] arguments)
        {
            ProcessResult result = await RunPowerCfgAsync(cancellationToken, action, arguments);
            EnsureSuccess(result, action);
        }

        private async Task<ProcessResult> RunPowerCfgAsync(
            CancellationToken cancellationToken,
            string action,
            params string[] arguments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ProcessStartInfo startInfo = new()
            {
                FileName = _powerCfgPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            foreach (string argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using Process process = new() { StartInfo = startInfo };
            bool started;
            try
            {
                started = process.Start();
            }
            catch (Exception exception)
            {
                ProcessResult startFailure = new(
                    -1,
                    string.Empty,
                    RedactPowerCfgOutput(exception.Message, arguments));
                LogPowerCfgFailure(action, startFailure);
                throw CreatePowerCfgException(action, startFailure, exception);
            }

            if (!started)
            {
                ProcessResult startFailure = new(-1, string.Empty, "Windows could not start powercfg.exe.");
                LogPowerCfgFailure(action, startFailure);
                throw CreatePowerCfgException(action, startFailure);
            }

            Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
            Task<string> errorTask = process.StandardError.ReadToEndAsync();
            using CancellationTokenSource timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(TimeSpan.FromSeconds(30));

            try
            {
                await process.WaitForExitAsync(timeoutSource.Token);
            }
            catch (OperationCanceledException)
            {
                await TerminateAndDrainProcessAsync(process, outputTask, errorTask);
                ProcessResult interruptedResult = new(
                    -1,
                    RedactPowerCfgOutput(GetCompletedOutput(outputTask), arguments),
                    RedactPowerCfgOutput(GetCompletedOutput(errorTask), arguments));
                if (cancellationToken.IsCancellationRequested)
                {
                    ProcessResult cancelledResult = string.IsNullOrWhiteSpace(interruptedResult.StandardError)
                        ? interruptedResult with { StandardError = "The powercfg operation was cancelled." }
                        : interruptedResult;
                    LogPowerCfgFailure(action, cancelledResult);
                    throw;
                }

                ProcessResult timeoutResult = string.IsNullOrWhiteSpace(interruptedResult.StandardError)
                    ? interruptedResult with { StandardError = "powercfg.exe did not finish within 30 seconds." }
                    : interruptedResult;
                LogPowerCfgFailure(action, timeoutResult);
                throw CreatePowerCfgException(action, timeoutResult);
            }

            ProcessResult result = new(
                process.ExitCode,
                RedactPowerCfgOutput(await outputTask, arguments),
                RedactPowerCfgOutput(await errorTask, arguments));
            if (result.ExitCode != 0)
            {
                LogPowerCfgFailure(action, result);
            }

            return result;
        }

        private static async Task TerminateAndDrainProcessAsync(
            Process process,
            Task<string> outputTask,
            Task<string> errorTask)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // The process may have exited between the checks.
            }

            try
            {
                using CancellationTokenSource cleanupTimeout = new(TimeSpan.FromSeconds(5));
                await process.WaitForExitAsync(cleanupTimeout.Token);
            }
            catch
            {
                // Stream continuations below observe any later task faults.
            }

            try
            {
                await Task.WhenAll(outputTask, errorTask).WaitAsync(TimeSpan.FromSeconds(2));
            }
            catch
            {
                _ = outputTask.ContinueWith(
                    task => _ = task.Exception,
                    CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
                _ = errorTask.ContinueWith(
                    task => _ = task.Exception,
                    CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }
        }

        private static void EnsureSuccess(ProcessResult result, string action)
        {
            if (result.ExitCode == 0)
            {
                return;
            }

            throw CreatePowerCfgException(action, result);
        }

        private static InvalidOperationException CreatePowerCfgException(
            string action,
            ProcessResult result,
            Exception? innerException = null) =>
            new(
                $"Windows could not {action} (powercfg exit code {result.ExitCode}). " +
                BuildPowerCfgFailureDetail(result.StandardOutput, result.StandardError),
                innerException);

        private static InvalidOperationException CreateMalformedOutputException(
            string action,
            ProcessResult result) =>
            new(
                $"Windows returned an unreadable response while trying to {action} " +
                $"(powercfg exit code {result.ExitCode}). No active power-plan ID was present. " +
                "Refresh the status, then restart SynToolkit as administrator if the problem continues. " +
                BuildPowerCfgFailureDetail(result.StandardOutput, result.StandardError));

        internal static string BuildPowerCfgFailureDetail(string standardOutput, string standardError)
        {
            List<string> details = new(2);
            if (!string.IsNullOrWhiteSpace(standardError))
            {
                details.Add($"Error: {standardError.Trim()}");
            }

            if (!string.IsNullOrWhiteSpace(standardOutput))
            {
                details.Add($"Output: {standardOutput.Trim()}");
            }

            return details.Count == 0
                ? "No diagnostic output was returned; verify administrator access and that the power-plan file is valid."
                : string.Join(" ", details);
        }

        internal static string RedactPowerCfgOutput(string output, IEnumerable<string> arguments)
        {
            string redacted = output ?? string.Empty;
            foreach (string argument in arguments)
            {
                if (string.IsNullOrWhiteSpace(argument) || !Path.IsPathFullyQualified(argument))
                {
                    continue;
                }

                redacted = redacted.Replace(argument, "<power-plan-file>", StringComparison.OrdinalIgnoreCase);
            }

            return redacted;
        }

        private static void LogPowerCfgFailure(string action, ProcessResult result)
        {
            App.logger.Error(
                "powercfg failed. Action: {Action}; ExitCode: {ExitCode}; StandardOutput: {StandardOutput}; StandardError: {StandardError}",
                action,
                result.ExitCode,
                string.IsNullOrWhiteSpace(result.StandardOutput) ? "<empty>" : result.StandardOutput.Trim(),
                string.IsNullOrWhiteSpace(result.StandardError) ? "<empty>" : result.StandardError.Trim());
        }

        private static string GetCompletedOutput(Task<string> outputTask) =>
            outputTask.Status == TaskStatus.RanToCompletion
                ? outputTask.Result
                : string.Empty;

        private static bool IsCurrentProcessElevated()
        {
            try
            {
                using WindowsIdentity identity = WindowsIdentity.GetCurrent();
                return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch (Exception exception)
            {
                App.logger.Error(exception, "Unable to determine whether SynToolkit is running with administrator access.");
                return false;
            }
        }

        private void EnsureCanMutate(string action)
        {
            if (!CanMutatePowerPlans)
            {
                throw new InvalidOperationException(
                    $"Administrator access is required to {action}. Restart SynToolkit as administrator.");
            }
        }

        internal static Guid? ExtractFirstGuid(string value)
        {
            Match match = GuidPattern.Match(value ?? string.Empty);
            return match.Success && Guid.TryParse(match.Value, out Guid schemeId)
                ? schemeId
                : null;
        }

        private static string ExtractSchemeName(string value, Guid? schemeId)
        {
            int openParenthesis = value.IndexOf('(');
            int closeParenthesis = value.LastIndexOf(')');
            if (openParenthesis >= 0 && closeParenthesis > openParenthesis)
            {
                return value.Substring(openParenthesis + 1, closeParenthesis - openParenthesis - 1).Trim();
            }

            return schemeId?.ToString("D") ?? "Unavailable";
        }

        private static Guid? ReadRegistryGuid(string valueName)
        {
            using RegistryKey? key = Registry.LocalMachine.OpenSubKey(StateRegistryPath);
            return Guid.TryParse(key?.GetValue(valueName) as string, out Guid schemeId)
                ? schemeId
                : null;
        }

        private static void SaveRegistryGuid(string valueName, Guid schemeId)
        {
            if (valueName == PreviousSchemeValueName && schemeId == SynToolkitSchemeId)
            {
                return;
            }

            using RegistryKey key = Registry.LocalMachine.CreateSubKey(StateRegistryPath, writable: true);
            key.SetValue(valueName, schemeId.ToString("D"), RegistryValueKind.String);
        }

        private static void TryClearRegistryValue(string valueName)
        {
            try
            {
                using RegistryKey? key = Registry.LocalMachine.OpenSubKey(StateRegistryPath, writable: true);
                key?.DeleteValue(valueName, throwOnMissingValue: false);
            }
            catch (Exception exception)
            {
                App.logger.Debug(exception, "Unable to clear power-plan state value {ValueName}.", valueName);
            }
        }

        private static void TryClearPowerPlanRegistryState()
        {
            TryClearRegistryValue(PreviousSchemeValueName);
            TryClearRegistryValue(OwnedSchemeValueName);
        }

        private static string CreateTemporaryPlanPath(string purpose) =>
            Path.Combine(Path.GetTempPath(), $"SynToolkit-{purpose}-{Guid.NewGuid():N}.pow");

        private static void TryDeleteTemporaryFile(string path)
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
                App.logger.Debug(exception, "Unable to remove temporary power-plan file {Path}.", path);
            }
        }

        private sealed record PowerSchemeInfo(Guid Id, string Name);
        private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
        private sealed record RollbackResult(bool Succeeded, string Message);
    }
}
