#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using SynToolkit.Models.GpuDrivers;
using SynToolkit.Services.GpuDrivers;
using SynToolkit.Services.NvidiaProfileInspector;

namespace SynToolkit.ViewModels
{
    public enum GpuVendorSelection
    {
        None,
        AMD,
        NVIDIA,
    }

    public partial class GpuPageViewModel : ObservableObject
    {
        private readonly IGpuDriverCatalogService _gpuDriverCatalogService;
        private readonly IGpuDriverPackageService _gpuDriverPackageService;
        private CancellationTokenSource? _removalCancellationTokenSource;

        [ObservableProperty]
        public partial GpuVendorSelection SelectedVendor { get; set; } = GpuVendorSelection.None;

        [ObservableProperty]
        public partial bool HasAmdGpu { get; set; }

        [ObservableProperty]
        public partial bool HasNvidiaGpu { get; set; }

        public bool NoAmdGpuDetected => !HasAmdGpu;
        public bool NoNvidiaGpuDetected => !HasNvidiaGpu;

        [ObservableProperty]
        public partial bool IsBusy { get; set; }

        [ObservableProperty]
        public partial bool HasError { get; set; }

        [ObservableProperty]
        public partial string StatusMessage { get; set; } = string.Empty;

        [ObservableProperty]
        public partial string DetectedGpuSummary { get; set; } = L("GpuPage_DetectingGpus");

        [ObservableProperty]
        public partial GpuDeviceInfo? SelectedGpuDevice { get; set; }

        [ObservableProperty]
        public partial GpuDriverOption? SelectedDriver { get; set; }

        [ObservableProperty]
        public partial GpuPreparedPackage? PreparedPackage { get; set; }

        [ObservableProperty]
        public partial string PreparedPackageNote { get; set; } = string.Empty;

        [ObservableProperty]
        public partial string SelectedDebloatModeText { get; set; } = L("GpuPage_DebloatStripped");

        [ObservableProperty]
        public partial bool RunVendorUninstaller { get; set; } = true;

        [ObservableProperty]
        public partial bool RemoveDriverStorePackages { get; set; } = true;

        [ObservableProperty]
        public partial bool RemoveServices { get; set; } = true;

        [ObservableProperty]
        public partial bool RemoveLeftoverFiles { get; set; } = true;

        [ObservableProperty]
        public partial bool RemoveRegistryEntries { get; set; } = true;

        [ObservableProperty]
        public partial bool BlockAutomaticReinstall { get; set; }

        [ObservableProperty]
        public partial bool IsRemovingDriver { get; set; }

        [ObservableProperty]
        public partial bool HasCompletedRemoval { get; set; }

        [ObservableProperty]
        public partial string NvidiaApplyResultSummary { get; set; } = string.Empty;

        public bool HasNvidiaApplyResult => !string.IsNullOrEmpty(NvidiaApplyResultSummary);
        partial void OnNvidiaApplyResultSummaryChanged(string value) => OnPropertyChanged(nameof(HasNvidiaApplyResult));

        [ObservableProperty]
        public partial string NipFilePath { get; set; } = string.Empty;

        [ObservableProperty]
        public partial bool IsApplyingNvidiaProfiles { get; set; }

        [ObservableProperty]
        public partial string NewProfileName { get; set; } = string.Empty;

        [ObservableProperty]
        public partial string NewProfileExecutablesText { get; set; } = string.Empty;

        public ObservableCollection<GpuDeviceInfo> GpuDevices { get; } = new();
        public ObservableCollection<GpuDriverOption> AvailableDrivers { get; } = new();
        public ObservableCollection<GpuPackageComponent> DebloatComponents { get; } = new();
        public ObservableCollection<GpuRemovalLogEntry> RemovalLogs { get; } = new();
        public ObservableCollection<string> DebloatModeOptions { get; } = new();
        public ObservableCollection<NvidiaProfile> NvidiaProfiles { get; } = new();
        public ObservableCollection<BundledNvidiaProfileFile> BundledProfiles { get; } = new();
        public ObservableCollection<NvidiaProfileSetting> NewProfileSettings { get; } = new();

        public string CurrentVendorTitle => SelectedVendor switch
        {
            GpuVendorSelection.AMD => L("GpuPage_AmdDriversTitle"),
            GpuVendorSelection.NVIDIA => L("GpuPage_NvidiaDriversTitle"),
            _ => L("GpuPage_GpuDriversTitle")
        };

        public string CurrentVendorSubtitle => SelectedVendor switch
        {
            GpuVendorSelection.AMD => L("GpuPage_AmdDriversSubtitle"),
            GpuVendorSelection.NVIDIA => L("GpuPage_NvidiaDriversSubtitle"),
            _ => string.Empty
        };

        public bool IsAmdVendorSelected => SelectedVendor == GpuVendorSelection.AMD;
        public bool IsNvidiaVendorSelected => SelectedVendor == GpuVendorSelection.NVIDIA;
        public bool HasPreparedPackage => PreparedPackage is not null;
        public string SelectedGpuDisplayName => SelectedGpuDevice?.DisplayName ?? L("GpuPage_NoGpuSelected");
        public string SelectedGpuVendorName => SelectedGpuDevice is null
            ? L("GpuPage_UnsupportedVendor")
            : SelectedGpuDevice.IsNvidia
                ? "NVIDIA"
                : SelectedGpuDevice.IsAmd
                    ? "AMD"
                    : L("GpuPage_UnsupportedVendor");
        public string PreparedPackageSummary
        {
            get
            {
                if (PreparedPackage is null)
                    return string.Empty;

                string baseSummary = PreparedPackage.IsManualImport
                    ? Lf(
                        "GpuPage_PreparedManualPackageFormat",
                        string.IsNullOrWhiteSpace(PreparedPackage.DriverVersion) ? L("GpuPage_Unknown") : PreparedPackage.DriverVersion,
                        PreparedPackage.SourceLabel,
                        PreparedPackage.ExtractedPath)
                    : Lf("GpuPage_PreparedPackageFormat", PreparedPackage.Vendor, PreparedPackage.ExtractedPath);

                return string.IsNullOrWhiteSpace(PreparedPackageNote)
                    ? baseSummary
                    : $"{baseSummary} {PreparedPackageNote}";
            }
        }
        public string RemovalActionSummary => string.Join(
            ", ",
            GetEnabledRemovalActions());
        public string RemovalActionSummaryText
        {
            get
            {
                string summary = RemovalActionSummary;
                return string.IsNullOrWhiteSpace(summary)
                    ? L("GpuPage_NoCleanupSteps")
                    : Lf("GpuPage_WillRunFormat", summary);
            }
        }

        public GpuDebloatMode SelectedDebloatMode
        {
            get
            {
                if (IsDebloatMode(SelectedDebloatModeText, "GpuPage_DebloatStock", "Stock"))
                {
                    return GpuDebloatMode.Stock;
                }

                if (IsDebloatMode(SelectedDebloatModeText, "GpuPage_DebloatCustom", "Custom"))
                {
                    return GpuDebloatMode.Custom;
                }

                return GpuDebloatMode.Stripped;
            }
        }

        public GpuPageViewModel(IGpuDriverCatalogService gpuDriverCatalogService, IGpuDriverPackageService gpuDriverPackageService)
        {
            _gpuDriverCatalogService = gpuDriverCatalogService;
            _gpuDriverPackageService = gpuDriverPackageService;

            string stripped = L("GpuPage_DebloatStripped");
            DebloatModeOptions.Add(stripped);
            DebloatModeOptions.Add(L("GpuPage_DebloatStock"));
            DebloatModeOptions.Add(L("GpuPage_DebloatCustom"));
            SelectedDebloatModeText = stripped;

            foreach (BundledNvidiaProfileFile bundledProfile in NvidiaProfileGalleryService.GetBundledProfiles())
            {
                BundledProfiles.Add(bundledProfile);
            }
        }

        partial void OnHasAmdGpuChanged(bool value) => OnPropertyChanged(nameof(NoAmdGpuDetected));
        partial void OnHasNvidiaGpuChanged(bool value) => OnPropertyChanged(nameof(NoNvidiaGpuDetected));
        partial void OnSelectedVendorChanged(GpuVendorSelection value)
        {
            OnPropertyChanged(nameof(CurrentVendorTitle));
            OnPropertyChanged(nameof(CurrentVendorSubtitle));
            OnPropertyChanged(nameof(IsAmdVendorSelected));
            OnPropertyChanged(nameof(IsNvidiaVendorSelected));
            SelectFirstDeviceForCurrentVendor();
        }

        partial void OnSelectedGpuDeviceChanged(GpuDeviceInfo? value)
        {
            OnPropertyChanged(nameof(SelectedGpuDisplayName));
            OnPropertyChanged(nameof(SelectedGpuVendorName));
        }

        partial void OnRunVendorUninstallerChanged(bool value) => OnRemovalOptionsChanged();
        partial void OnRemoveDriverStorePackagesChanged(bool value) => OnRemovalOptionsChanged();
        partial void OnRemoveServicesChanged(bool value) => OnRemovalOptionsChanged();
        partial void OnRemoveLeftoverFilesChanged(bool value) => OnRemovalOptionsChanged();
        partial void OnRemoveRegistryEntriesChanged(bool value) => OnRemovalOptionsChanged();
        partial void OnBlockAutomaticReinstallChanged(bool value) => OnRemovalOptionsChanged();

        partial void OnPreparedPackageChanged(GpuPreparedPackage? value)
        {
            OnPropertyChanged(nameof(HasPreparedPackage));
            OnPropertyChanged(nameof(PreparedPackageSummary));
        }

        partial void OnPreparedPackageNoteChanged(string value) => OnPropertyChanged(nameof(PreparedPackageSummary));

        partial void OnSelectedDebloatModeTextChanged(string value)
        {
            RefreshDebloatComponentsForSelectedMode();
        }

        public async Task LoadGpuDevicesAsync()
        {
            HasError = false;
            try
            {
                IReadOnlyList<GpuDeviceInfo> devices = await _gpuDriverCatalogService.GetGpuDevicesAsync();
                GpuDevices.Clear();
                foreach (GpuDeviceInfo device in devices)
                {
                    GpuDevices.Add(device);
                }

                HasAmdGpu = GpuDevices.Any(device => device.IsAmd);
                HasNvidiaGpu = GpuDevices.Any(device => device.IsNvidia);
                DetectedGpuSummary = GpuDevices.Count == 0
                    ? L("GpuPage_NoGpuDetected")
                    : Lf("GpuPage_DetectedFormat", string.Join(", ", GpuDevices.Select(device => device.DisplayName)));

                SelectFirstDeviceForCurrentVendor();
            }
            catch (Exception exception)
            {
                App.logger.Error(exception, "[GPU] GPU device discovery failed.");
                StatusMessage = exception.Message;
                HasError = true;
            }
        }

        public void SelectVendor(GpuVendorSelection vendor)
        {
            SelectedVendor = vendor;
        }

        public void ReturnToLandingPage()
        {
            SelectedVendor = GpuVendorSelection.None;
        }

        public async Task LoadDriversAsync()
        {
            HasError = false;
            AvailableDrivers.Clear();
            SelectedDriver = null;

            if (SelectedGpuDevice is null)
            {
                StatusMessage = L("GpuPage_NoCompatibleGpu");
                return;
            }

            IsBusy = true;
            try
            {
                GpuDriverLookupResult result = await _gpuDriverCatalogService.GetDriversAsync(SelectedGpuDevice);
                foreach (GpuDriverOption driver in SortDriversNewestFirst(result.Drivers))
                {
                    AvailableDrivers.Add(driver);
                }

                SelectedDriver = AvailableDrivers.FirstOrDefault();
                StatusMessage = result.Message;
            }
            catch (Exception exception)
            {
                App.logger.Error(exception, "[GPU] Driver lookup failed.");
                StatusMessage = exception.Message;
                HasError = true;
            }
            finally
            {
                IsBusy = false;
            }
        }

        public async Task PrepareSelectedDriverAsync()
        {
            if (SelectedDriver is null)
            {
                StatusMessage = L("GpuPage_ChooseDriverFirst");
                return;
            }

            HasError = false;
            IsBusy = true;
            try
            {
                Progress<double> progress = new(value =>
                {
                    int percent = Math.Clamp((int)Math.Round(value * 100), 0, 100);
                    StatusMessage = percent >= 100
                        ? L("GpuPage_ExtractingPackage")
                        : Lf("GpuPage_DownloadingPackageFormat", percent);
                });

                PreparedPackage = await _gpuDriverPackageService.PreparePackageAsync(SelectedDriver, progress);
                PreparedPackageNote = string.Empty;
                SelectedDebloatModeText = L("GpuPage_DebloatStripped");
                RefreshDebloatComponentsForSelectedMode();
                StatusMessage = PreparedPackageSummary;
            }
            catch (Exception exception)
            {
                App.logger.Error(exception, "[GPU] Driver preparation failed.");
                StatusMessage = exception.Message;
                HasError = true;
            }
            finally
            {
                IsBusy = false;
            }
        }

        public async Task LaunchPreparedInstallAsync()
        {
            if (PreparedPackage is null)
            {
                StatusMessage = L("GpuPage_PrepareBeforeInstall");
                return;
            }

            HasError = false;
            IsBusy = true;
            try
            {
                await _gpuDriverPackageService.RefreshPreparedPackageAsync(PreparedPackage);
                RefreshDebloatComponentsForSelectedMode();

                if (SelectedDebloatMode != GpuDebloatMode.Stock)
                {
                    _gpuDriverPackageService.ApplyDebloat(
                        PreparedPackage.Vendor,
                        PreparedPackage.ExtractedPath,
                        DebloatComponents.ToList());
                }

                _gpuDriverPackageService.LaunchExtractedSetup(PreparedPackage.Vendor, PreparedPackage.ExtractedPath);
                StatusMessage = L("GpuPage_InstallerLaunched");
            }
            catch (Exception exception)
            {
                App.logger.Error(exception, "[GPU] Launching the prepared driver installer failed.");
                StatusMessage = exception.Message;
                HasError = true;
            }
            finally
            {
                IsBusy = false;
            }
        }

        public async Task ImportAmdInstallerAsync(string installerPath)
        {
            HasError = false;
            IsBusy = true;
            try
            {
                Progress<double> progress = new(value =>
                {
                    int percent = Math.Clamp((int)Math.Round(value * 100), 0, 100);
                    StatusMessage = percent >= 90
                        ? L("GpuPage_ValidatingImportedPackage")
                        : Lf("GpuPage_ImportingPackageFormat", percent);
                });

                PreparedPackage = await _gpuDriverPackageService.ImportAmdInstallerAsync(installerPath, progress);
                ApplyImportedPackageUiState();
            }
            catch (Exception exception)
            {
                App.logger.Error(exception, "[GPU] Manual AMD installer import failed.");
                StatusMessage = exception.Message;
                HasError = true;
            }
            finally
            {
                IsBusy = false;
            }
        }

        public async Task ImportAmdExtractedFolderAsync(string folderPath)
        {
            HasError = false;
            IsBusy = true;
            try
            {
                Progress<double> progress = new(value =>
                {
                    int percent = Math.Clamp((int)Math.Round(value * 100), 0, 100);
                    StatusMessage = percent >= 90
                        ? L("GpuPage_ValidatingImportedPackage")
                        : Lf("GpuPage_ImportingPackageFormat", percent);
                });

                PreparedPackage = await _gpuDriverPackageService.ImportAmdExtractedFolderAsync(folderPath, progress);
                ApplyImportedPackageUiState();
            }
            catch (Exception exception)
            {
                App.logger.Error(exception, "[GPU] Manual AMD folder import failed.");
                StatusMessage = exception.Message;
                HasError = true;
            }
            finally
            {
                IsBusy = false;
            }
        }

        public GpuPageUiRestoreState CaptureUiState(int tabIndex)
        {
            return new GpuPageUiRestoreState
            {
                SelectedVendor = SelectedVendor switch
                {
                    GpuVendorSelection.AMD => GpuVendorSelectionSnapshot.AMD,
                    GpuVendorSelection.NVIDIA => GpuVendorSelectionSnapshot.NVIDIA,
                    _ => GpuVendorSelectionSnapshot.None,
                },
                TabIndex = tabIndex,
                SelectedGpuPnpDeviceId = SelectedGpuDevice?.PnpDeviceId,
                SelectedGpuDisplayName = SelectedGpuDevice?.DisplayName,
                SelectedDriverVersion = SelectedDriver?.Version,
                SelectedDriverDownloadUrl = SelectedDriver?.DownloadUrl,
                SelectedDebloatModeText = SelectedDebloatModeText,
                StatusMessage = StatusMessage,
                PreparedPackageNote = PreparedPackageNote,
                HasError = HasError,
                PreparedPackage = PreparedPackage is null
                    ? null
                    : new GpuPreparedPackageSnapshot
                    {
                        Vendor = PreparedPackage.Vendor,
                        InstallerPath = PreparedPackage.InstallerPath,
                        ExtractedPath = PreparedPackage.ExtractedPath,
                        SourceFolderPath = PreparedPackage.SourceFolderPath,
                        DriverVersion = PreparedPackage.DriverVersion,
                        SourceLabel = PreparedPackage.SourceLabel,
                        IsManualImport = PreparedPackage.IsManualImport,
                    },
                DebloatSelections = DebloatComponents
                    .Select(component => new GpuComponentSelectionSnapshot
                    {
                        Kind = component.Kind,
                        Name = component.Name,
                        FullPath = component.FullPath,
                        IsSelected = component.IsSelected,
                    })
                    .ToList(),
            };
        }

        public async Task RestoreUiStateAsync(GpuPageUiRestoreState state)
        {
            SelectedVendor = state.SelectedVendor switch
            {
                GpuVendorSelectionSnapshot.AMD => GpuVendorSelection.AMD,
                GpuVendorSelectionSnapshot.NVIDIA => GpuVendorSelection.NVIDIA,
                _ => GpuVendorSelection.None,
            };

            if (SelectedVendor != GpuVendorSelection.None)
            {
                GpuDeviceInfo? device = null;
                if (!string.IsNullOrWhiteSpace(state.SelectedGpuPnpDeviceId))
                {
                    device = GpuDevices.FirstOrDefault(candidate =>
                        string.Equals(candidate.PnpDeviceId, state.SelectedGpuPnpDeviceId, StringComparison.OrdinalIgnoreCase));
                }

                if (device is null && !string.IsNullOrWhiteSpace(state.SelectedGpuDisplayName))
                {
                    device = GpuDevices.FirstOrDefault(candidate =>
                        string.Equals(candidate.DisplayName, state.SelectedGpuDisplayName, StringComparison.OrdinalIgnoreCase));
                }

                if (device is not null)
                    SelectedGpuDevice = device;

                await LoadDriversAsync();

                if (!string.IsNullOrWhiteSpace(state.SelectedDriverVersion) ||
                    !string.IsNullOrWhiteSpace(state.SelectedDriverDownloadUrl))
                {
                    SelectedDriver = AvailableDrivers.FirstOrDefault(driver =>
                        (!string.IsNullOrWhiteSpace(state.SelectedDriverDownloadUrl) &&
                         string.Equals(driver.DownloadUrl, state.SelectedDriverDownloadUrl, StringComparison.OrdinalIgnoreCase)) ||
                        string.Equals(driver.Version, state.SelectedDriverVersion, StringComparison.OrdinalIgnoreCase))
                        ?? SelectedDriver;
                }
            }

            if (state.PreparedPackage is not null &&
                !string.IsNullOrWhiteSpace(state.PreparedPackage.ExtractedPath) &&
                Directory.Exists(state.PreparedPackage.ExtractedPath))
            {
                PreparedPackage = new GpuPreparedPackage
                {
                    Vendor = state.PreparedPackage.Vendor,
                    InstallerPath = state.PreparedPackage.InstallerPath,
                    ExtractedPath = state.PreparedPackage.ExtractedPath,
                    SourceFolderPath = state.PreparedPackage.SourceFolderPath,
                    DriverVersion = state.PreparedPackage.DriverVersion,
                    SourceLabel = state.PreparedPackage.SourceLabel,
                    IsManualImport = state.PreparedPackage.IsManualImport,
                    Components = [],
                };

                if (!string.IsNullOrWhiteSpace(state.SelectedDebloatModeText))
                    SelectedDebloatModeText = state.SelectedDebloatModeText;

                PreparedPackageNote = state.PreparedPackageNote ?? string.Empty;
                RefreshDebloatComponentsForSelectedMode();
                ApplyDebloatSelectionSnapshot(state.DebloatSelections);
            }

            StatusMessage = string.IsNullOrWhiteSpace(state.StatusMessage)
                ? PreparedPackageSummary
                : state.StatusMessage;
            HasError = state.HasError;
        }

        private void ApplyImportedPackageUiState()
        {
            AmdPackageMatchSummary match = _gpuDriverPackageService.EvaluateAmdPackageMatch(
                PreparedPackage?.Components ?? Array.Empty<GpuPackageComponent>());

            if (match.Quality == AmdPackageMatchQuality.FullMatch)
            {
                SelectedDebloatModeText = L("GpuPage_DebloatStripped");
                PreparedPackageNote = L("GpuPage_ImportMatchFull");
            }
            else if (match.Quality == AmdPackageMatchQuality.PartialMatch)
            {
                SelectedDebloatModeText = L("GpuPage_DebloatCustom");
                PreparedPackageNote = L("GpuPage_ImportMatchPartial");
            }
            else
            {
                SelectedDebloatModeText = L("GpuPage_DebloatCustom");
                PreparedPackageNote = L("GpuPage_ImportMatchNone");
            }

            RefreshDebloatComponentsForSelectedMode();
            StatusMessage = PreparedPackageSummary;
        }

        private void ApplyDebloatSelectionSnapshot(IReadOnlyList<GpuComponentSelectionSnapshot> selections)
        {
            if (selections.Count == 0 || SelectedDebloatMode != GpuDebloatMode.Custom)
                return;

            foreach (GpuPackageComponent component in DebloatComponents)
            {
                GpuComponentSelectionSnapshot? match = selections.FirstOrDefault(selection =>
                    selection.Kind == component.Kind &&
                    (string.Equals(selection.FullPath, component.FullPath, StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(selection.Name, component.Name, StringComparison.OrdinalIgnoreCase)));
                if (match is not null)
                    component.IsSelected = match.IsSelected;
            }
        }

        public async Task ReloadPreparedPackageAsync()
        {
            if (PreparedPackage is null)
            {
                return;
            }

            IsBusy = true;
            try
            {
                await _gpuDriverPackageService.RefreshPreparedPackageAsync(PreparedPackage);
                RefreshDebloatComponentsForSelectedMode();
                StatusMessage = L("GpuPage_PackageRefreshed");
            }
            catch (Exception exception)
            {
                App.logger.Error(exception, "[GPU] Refreshing the prepared package failed.");
                StatusMessage = exception.Message;
                HasError = true;
            }
            finally
            {
                IsBusy = false;
            }
        }

        public void RefreshDebloatComponentsForSelectedMode()
        {
            DebloatComponents.Clear();
            if (PreparedPackage is null)
            {
                return;
            }

            IReadOnlyList<GpuPackageComponent> components = SelectedDebloatMode switch
            {
                GpuDebloatMode.Stock => _gpuDriverPackageService.GetPackageComponents(PreparedPackage.Vendor, PreparedPackage.ExtractedPath, selectPreset: false),
                GpuDebloatMode.Custom => _gpuDriverPackageService.GetPackageComponents(PreparedPackage.Vendor, PreparedPackage.ExtractedPath, selectPreset: false),
                _ => _gpuDriverPackageService.GetPackageComponents(PreparedPackage.Vendor, PreparedPackage.ExtractedPath, selectPreset: true)
            };

            foreach (GpuPackageComponent component in components)
            {
                component.IsSelectionLocked = SelectedDebloatMode != GpuDebloatMode.Custom;
                DebloatComponents.Add(component);
            }
        }

        public async Task RunRemovalAsync()
        {
            if (SelectedGpuDevice is null)
            {
                StatusMessage = L("GpuPage_NoGpuForRemoval");
                return;
            }

            HasCompletedRemoval = false;
            RemovalLogs.Clear();
            IsRemovingDriver = true;
            HasError = false;
            _removalCancellationTokenSource?.Cancel();
            _removalCancellationTokenSource = new CancellationTokenSource();

            try
            {
                GpuRemovalOptions options = new()
                {
                    Vendor = GpuDriverRemovalService.DetectVendor(SelectedGpuDevice),
                    RunVendorUninstaller = RunVendorUninstaller,
                    RemoveDriverStorePackages = RemoveDriverStorePackages,
                    RemoveServices = RemoveServices,
                    RemoveLeftoverFiles = RemoveLeftoverFiles,
                    RemoveRegistryEntries = RemoveRegistryEntries,
                    BlockAutomaticReinstall = BlockAutomaticReinstall,
                };

                Progress<GpuRemovalLogEntry> progress = new(entry => RemovalLogs.Add(entry));
                GpuRemovalResult result = await GpuDriverRemovalService.RemoveAsync(options, progress, _removalCancellationTokenSource.Token);
                StatusMessage = result.Summary;
                HasCompletedRemoval = result.Completed;
            }
            catch (OperationCanceledException)
            {
                StatusMessage = L("GpuPage_RemovalCancelled");
            }
            catch (Exception exception)
            {
                App.logger.Error(exception, "[GPU] Driver removal failed.");
                StatusMessage = exception.Message;
                HasError = true;
            }
            finally
            {
                IsRemovingDriver = false;
            }
        }

        public Task<(bool Success, string Output)> RebootAfterRemovalAsync() => GpuDriverRemovalService.RebootNowAsync();

        public async Task LoadBundledProfileAsync(BundledNvidiaProfileFile bundledProfile) =>
            await LoadNipFileAsync(bundledProfile.FullPath);

        public void AddNewSettingRow() => NewProfileSettings.Add(new NvidiaProfileSetting());

        public void RemoveNewSettingRow(NvidiaProfileSetting setting) => NewProfileSettings.Remove(setting);

        public async Task ExportLoadedProfilesAsync(string exportFilePath)
        {
            HasError = false;
            try
            {
                List<NvidiaProfile> profiles = NvidiaProfiles.ToList();
                if (profiles.Count == 0)
                {
                    throw new InvalidOperationException(L("GpuPage_LoadNipBeforeExport"));
                }

                await Task.Run(() => NvidiaProfilePreviewService.SaveProfiles(profiles, exportFilePath));

                int settingCount = profiles.Sum(profile => profile.Settings.Count);
                StatusMessage = Lf("GpuPage_ExportedLoadedFormat", settingCount, profiles.Count, exportFilePath);
            }
            catch (Exception exception)
            {
                App.logger.Error(exception, "[GPU] Exporting loaded .nip profiles failed.");
                StatusMessage = exception.Message;
                HasError = true;
            }
        }

        public async Task ExportNewProfileAsync(string exportFilePath)
        {
            HasError = false;
            try
            {
                if (string.IsNullOrWhiteSpace(NewProfileName))
                {
                    throw new InvalidOperationException(L("GpuPage_EnterProfileName"));
                }

                if (NewProfileSettings.Count == 0)
                {
                    throw new InvalidOperationException(L("GpuPage_AddSettingBeforeExport"));
                }

                NvidiaProfile profile = new()
                {
                    ProfileName = NewProfileName.Trim(),
                    Executeables = NewProfileExecutablesText
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .ToList(),
                    Settings = NewProfileSettings.ToList(),
                };

                await Task.Run(() => NvidiaProfilePreviewService.SaveProfiles(new List<NvidiaProfile> { profile }, exportFilePath));
                StatusMessage = Lf("GpuPage_ExportedNewFormat", profile.ProfileName, exportFilePath);
            }
            catch (Exception exception)
            {
                App.logger.Error(exception, "[GPU] Exporting a new .nip profile failed.");
                StatusMessage = exception.Message;
                HasError = true;
            }
        }

        public async Task LoadNipFileAsync(string nipFilePath)
        {
            HasError = false;
            NipFilePath = nipFilePath;
            try
            {
                List<NvidiaProfile> profiles = await Task.Run(() => NvidiaProfilePreviewService.LoadProfiles(nipFilePath));
                NvidiaProfiles.Clear();
                foreach (NvidiaProfile profile in profiles)
                {
                    NvidiaProfiles.Add(profile);
                }
            }
            catch (Exception exception)
            {
                App.logger.Error(exception, "[GPU] Reading a .nip profile file failed.");
                StatusMessage = exception.Message;
                HasError = true;
            }
        }

        public async Task ApplyNvidiaProfilesAsync()
        {
            HasError = false;
            NvidiaApplyResultSummary = string.Empty;
            IsApplyingNvidiaProfiles = true;
            try
            {
                List<NvidiaProfile> profiles = NvidiaProfiles.ToList();
                List<NvidiaProfileApplyResult> results = await Task.Run(() => NvidiaProfileApplyService.Apply(profiles));

                int settingsApplied = results.Sum(profile => profile.Settings.Count(setting => setting.Applied));
                int settingsSkipped = results.Sum(profile => profile.Settings.Count(setting => !setting.Applied));
                int profilesCreated = results.Count(profile => profile.ProfileCreated);

                NvidiaApplyResultSummary = settingsSkipped == 0
                    ? Lf("GpuPage_AppliedProfilesFormat", settingsApplied, results.Count, profilesCreated)
                    : Lf("GpuPage_AppliedProfilesSkippedFormat", settingsApplied, results.Count, profilesCreated, settingsSkipped);

                if (settingsSkipped > 0)
                {
                    foreach (NvidiaProfileApplyResult profile in results)
                    {
                        foreach (NvidiaSettingApplyResult setting in profile.Settings.Where(setting => !setting.Applied))
                        {
                            App.logger.Warn("[GPU] Skipped NVIDIA setting '{0}' on profile '{1}': {2}", setting.SettingName, profile.ProfileName, setting.SkipReason);
                        }
                    }
                }
            }
            catch (Exception exception)
            {
                App.logger.Error(exception, "[GPU] Applying NVIDIA profiles to the driver failed.");
                StatusMessage = exception.Message;
                HasError = true;
            }
            finally
            {
                IsApplyingNvidiaProfiles = false;
            }
        }

        private void SelectFirstDeviceForCurrentVendor()
        {
            if (SelectedVendor == GpuVendorSelection.None)
            {
                SelectedGpuDevice = null;
                return;
            }

            SelectedGpuDevice = SelectedVendor switch
            {
                GpuVendorSelection.AMD => GpuDevices.FirstOrDefault(device => device.IsAmd),
                GpuVendorSelection.NVIDIA => GpuDevices.FirstOrDefault(device => device.IsNvidia),
                _ => null,
            };
        }

        private IEnumerable<string> GetEnabledRemovalActions()
        {
            if (RunVendorUninstaller)
                yield return L("GpuPage_ActionVendorUninstall");
            if (RemoveDriverStorePackages)
                yield return L("GpuPage_ActionDriverStorePurge");
            if (RemoveServices)
                yield return L("GpuPage_ActionServiceRemoval");
            if (RemoveLeftoverFiles)
                yield return L("GpuPage_ActionFileCleanup");
            if (RemoveRegistryEntries)
                yield return L("GpuPage_ActionRegistryCleanup");
            if (BlockAutomaticReinstall)
                yield return L("GpuPage_ActionAutoReinstallBlock");
        }

        private void OnRemovalOptionsChanged()
        {
            OnPropertyChanged(nameof(RemovalActionSummary));
            OnPropertyChanged(nameof(RemovalActionSummaryText));
        }

        private static IEnumerable<GpuDriverOption> SortDriversNewestFirst(IEnumerable<GpuDriverOption> drivers) =>
            drivers
                .OrderByDescending(ParseDriverVersion)
                .ThenByDescending(ParseDriverReleaseDate)
                .ThenByDescending(driver =>
                    driver.Name.Contains("Game Ready", StringComparison.OrdinalIgnoreCase) ? 1 : 0);

        private static Version ParseDriverVersion(GpuDriverOption driver) =>
            Version.TryParse(driver.Version, out Version? parsed) ? parsed : new Version();

        private static DateTime ParseDriverReleaseDate(GpuDriverOption driver) =>
            DateTime.TryParse(
                driver.ReleaseDate,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal,
                out DateTime parsed)
                ? parsed
                : DateTime.MinValue;

        private static string L(string key) => App.GetValueFromItemList(key);

        private static string Lf(string key, params object[] args)
        {
            try
            {
                return string.Format(CultureInfo.CurrentCulture, L(key), args);
            }
            catch (FormatException)
            {
                return L(key);
            }
        }

        private static bool IsDebloatMode(string text, string key, string englishFallback) =>
            string.Equals(text, L(key), StringComparison.OrdinalIgnoreCase) ||
            string.Equals(text, englishFallback, StringComparison.OrdinalIgnoreCase);
    }
}
