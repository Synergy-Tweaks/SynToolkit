// Portions adapted from Roomy (MIT) © 2026 krezio — https://github.com/krezio/roomy
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using SynToolkit.Drives.Core;
using SynToolkit.Services;

#nullable enable

namespace SynToolkit.ViewModels;

public enum DrivesCleanerSection
{
    Cleanup = 0,
    SpaceMap = 1
}

public partial class DrivesCleanerViewModel : ObservableObject
{
    private readonly DrivesCleanerService _service;

    public DrivesCleanerViewModel(DrivesCleanerService service)
    {
        _service = service;
        IsElevated = service.IsElevated;
        LoadDrives();
    }

    [ObservableProperty]
    public partial bool IsElevated { get; set; }

    [ObservableProperty]
    public partial bool IsScanning { get; set; }

    [ObservableProperty]
    public partial bool IsCleaning { get; set; }

    [ObservableProperty]
    public partial bool HasResults { get; set; }

    [ObservableProperty]
    public partial bool HasError { get; set; }

    [ObservableProperty]
    public partial string ErrorMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string StatusText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string LastResultText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasLastResult { get; set; }

    [ObservableProperty]
    public partial DrivesCleanerSection SelectedSection { get; set; } = DrivesCleanerSection.Cleanup;

    [ObservableProperty]
    public partial int SelectedSectionIndex { get; set; }

    [ObservableProperty]
    public partial bool IsSpaceMapScanning { get; set; }

    [ObservableProperty]
    public partial string SpaceMapStatus { get; set; } = string.Empty;

    [ObservableProperty]
    public partial DriveItemViewModel? SelectedDrive { get; set; }

    public ObservableCollection<DriveItemViewModel> Drives { get; } = [];
    public ObservableCollection<CleanerCategoryViewModel> Categories { get; } = [];
    public ObservableCollection<SpaceMapRowViewModel> SpaceMapRows { get; } = [];
    public ObservableCollection<BigFileViewModel> BiggestFiles { get; } = [];

    public bool IsCleanupSection => SelectedSection == DrivesCleanerSection.Cleanup;
    public bool IsSpaceMapSection => SelectedSection == DrivesCleanerSection.SpaceMap;
    public bool CanSwitchToSpaceMap => !IsCleaning;

    public bool HasCategories => Categories.Count > 0;
    public bool HasSpaceMapData => SpaceMapRows.Count > 0;
    public long SelectedTotal => Categories.Sum(category => category.SelectedSize);
    public int SelectedCount => Categories.Sum(category => category.SelectedCount);
    public long SafeTotal => Categories.Where(category => category.IsSafe && category.CanSelect).Sum(category => category.Size);
    public long ReviewTotal => Categories.Where(category => !category.IsSafe && category.CanSelect).Sum(category => category.Size);
    public string SelectedTotalText => Format.Bytes(SelectedTotal);
    public string AppliedSummary => $"{SelectedCount} selected · {SelectedTotalText}";
    public string ReviewSummary => $"Safe: {Format.Bytes(SafeTotal)} · Plus {Format.Bytes(ReviewTotal)} more to review";
    public bool CanClean => SelectedTotal > 0 && !IsCleaning && !IsScanning;
    public string CleanButtonText => CanClean ? $"Clean up {SelectedTotalText}" : "Pick something";

    partial void OnSelectedSectionChanged(DrivesCleanerSection value)
    {
        int next = (int)value;
        if (SelectedSectionIndex != next)
        {
            SelectedSectionIndex = next;
        }

        OnPropertyChanged(nameof(IsCleanupSection));
        OnPropertyChanged(nameof(IsSpaceMapSection));
    }

    partial void OnSelectedSectionIndexChanged(int value)
    {
        value = Math.Clamp(value, 0, 1);
        DrivesCleanerSection section = (DrivesCleanerSection)value;
        if (SelectedSection != section)
        {
            SelectedSection = section;
        }
    }

    partial void OnIsCleaningChanged(bool value)
    {
        OnPropertyChanged(nameof(CanSwitchToSpaceMap));
        OnPropertyChanged(nameof(CanClean));
        OnPropertyChanged(nameof(CleanButtonText));
    }

    [RelayCommand]
    private void ShowCleanupSection() => SelectedSection = DrivesCleanerSection.Cleanup;

    [RelayCommand]
    private void ShowSpaceMapSection()
    {
        if (CanSwitchToSpaceMap)
        {
            SelectedSection = DrivesCleanerSection.SpaceMap;
        }
    }

    [RelayCommand]
    private void LoadDrives()
    {
        Drives.Clear();
        foreach (DriveInfo drive in DriveInfo.GetDrives().Where(drive => drive.IsReady && drive.DriveType == DriveType.Fixed))
        {
            Drives.Add(new DriveItemViewModel(drive.Name, drive.VolumeLabel, drive.TotalSize, drive.TotalFreeSpace));
        }

        SelectedDrive = Drives.FirstOrDefault(drive => string.Equals(drive.Root, Env.SystemRoot, StringComparison.OrdinalIgnoreCase))
            ?? Drives.FirstOrDefault();
        OnPropertyChanged(nameof(Drives));
    }

    [RelayCommand]
    private async Task ScanAsync()
    {
        if (IsScanning)
        {
            return;
        }

        IsScanning = true;
        HasError = false;
        ErrorMessage = string.Empty;
        StatusText = DrivesText.Get("DrivesCleaner_StatusScanning", "Scanning...");
        LastResultText = string.Empty;
        HasLastResult = false;
        try
        {
            IReadOnlyList<CleanupCategory> results = await _service.ScanAsync();
            IEnumerable<CleanupCategory> ordered = results
                .Where(category => category.Safety == Safety.Safe && !category.Blocked)
                .OrderByDescending(category => category.Size)
                .Concat(results.Where(category => category.Safety == Safety.Review && !category.Blocked).OrderByDescending(category => category.Size))
                .Concat(results.Where(category => category.Blocked));

            Categories.Clear();
            foreach (CleanupCategory category in ordered)
            {
                Categories.Add(new CleanerCategoryViewModel(category, this));
            }

            HasResults = true;
            StatusText = DrivesText.Format("DrivesCleaner_StatusFound", "Found {0} categories.", Categories.Count);
            OnPropertyChanged(nameof(HasCategories));
            Recalc();
        }
        catch (Exception ex)
        {
            HasError = true;
            ErrorMessage = ex.Message;
            StatusText = DrivesText.Get("DrivesCleaner_StatusFailed", "Scan failed.");
        }
        finally
        {
            IsScanning = false;
            OnPropertyChanged(nameof(CanClean));
            OnPropertyChanged(nameof(CleanButtonText));
        }
    }

    [RelayCommand]
    private void SelectSafe()
    {
        foreach (CleanerCategoryViewModel category in Categories)
        {
            category.SetSelected(category.IsSafe && category.CanSelect);
        }

        Recalc();
    }

    [RelayCommand]
    private void ClearSelection()
    {
        foreach (CleanerCategoryViewModel category in Categories)
        {
            category.SetSelected(false);
        }

        Recalc();
    }

    public List<CleanJob> BuildSelectedJobs() => Categories
        .Where(category => category.CanSelect && category.SelectedSize > 0)
        .Select(category => new CleanJob(category.Model, category.SelectedItems()))
        .Where(job => job.Items.Count > 0)
        .ToList();

    public List<CleanupItem> SelectedReviewItems() => Categories
        .Where(category => category.CanSelect && !category.IsSafe)
        .SelectMany(category => category.SelectedItems())
        .ToList();

    [RelayCommand]
    private async Task CleanSelectedAsync()
    {
        if (IsCleaning)
        {
            return;
        }

        List<CleanJob> jobs = BuildSelectedJobs();
        if (jobs.Count == 0)
        {
            return;
        }

        IsCleaning = true;
        StatusText = DrivesText.Get("DrivesCleaner_StatusCleaning", "Cleaning...");
        try
        {
            Progress<(double Fraction, string Step)> progress = new(step =>
            {
                StatusText = step.Step;
            });

            CleanResult result = await _service.CleanAsync(jobs, progress);
            LastResultText = DrivesText.Format(
                "DrivesCleaner_StatusDone",
                "Freed {0} ({1} files).",
                Format.Bytes(result.Freed),
                result.Files);
            HasLastResult = true;

            await ScanAsync();
            LoadDrives();
        }
        catch (Exception ex)
        {
            HasError = true;
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsCleaning = false;
            OnPropertyChanged(nameof(CanClean));
            OnPropertyChanged(nameof(CleanButtonText));
        }
    }

    [RelayCommand]
    private async Task ScanSpaceMapAsync()
    {
        if (SelectedDrive is null || IsSpaceMapScanning)
        {
            return;
        }

        IsSpaceMapScanning = true;
        SpaceMapStatus = DrivesText.Get("DrivesCleaner_SpaceMapScanning", "Scanning drive map...");
        try
        {
            SpaceNode root = await _service.ScanSpaceMapAsync(SelectedDrive.Root);
            List<SpaceNode> nodes = root.Children.OrderByDescending(node => node.Size).Take(80).ToList();
            SpaceMapRows.Clear();
            foreach (SpaceNode node in nodes)
            {
                SpaceMapRows.Add(new SpaceMapRowViewModel(node.Name, node.FullPath, node.Size, root.Size));
            }

            BiggestFiles.Clear();
            SpaceScanner scanner = new();
            _ = await Task.Run(() => scanner.Scan(SelectedDrive.Root));
            foreach (BigFile file in scanner.BigFiles)
            {
                BiggestFiles.Add(new BigFileViewModel(file.FullPath, file.Size));
            }

            SpaceMapStatus = DrivesText.Get("DrivesCleaner_SpaceMapReady", "Space map ready.");
            OnPropertyChanged(nameof(HasSpaceMapData));
        }
        catch (Exception ex)
        {
            SpaceMapStatus = ex.Message;
        }
        finally
        {
            IsSpaceMapScanning = false;
        }
    }

    [RelayCommand]
    private void OpenWindowsStorageSettings() => Shell.Open("ms-settings:storagesense");

    [RelayCommand]
    private async Task TurnOffHibernationAsync(CleanerCategoryViewModel? category)
    {
        if (category is null || IsCleaning)
        {
            return;
        }

        bool done = await Hibernation.SetAsync(false);
        if (!done || Hibernation.IsOn)
        {
            HasError = true;
            ErrorMessage = "Hibernation settings were not changed.";
            return;
        }

        await ScanAsync();
        LoadDrives();
    }

    [RelayCommand]
    private void RestartAsAdmin()
    {
        try
        {
            ProcessStartInfo startInfo = new(Environment.ProcessPath ?? "SynToolkit.exe")
            {
                UseShellExecute = true,
                Verb = "runas"
            };
            _ = Process.Start(startInfo);
        }
        catch
        {
        }
    }

    public void Recalc()
    {
        OnPropertyChanged(nameof(SelectedTotal));
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(SelectedTotalText));
        OnPropertyChanged(nameof(AppliedSummary));
        OnPropertyChanged(nameof(SafeTotal));
        OnPropertyChanged(nameof(ReviewTotal));
        OnPropertyChanged(nameof(ReviewSummary));
        OnPropertyChanged(nameof(CanClean));
        OnPropertyChanged(nameof(CleanButtonText));
    }
}

public partial class CleanerCategoryViewModel : ObservableObject
{
    private const int MaxVisible = 40;
    private readonly DrivesCleanerViewModel _owner;

    public CleanerCategoryViewModel(CleanupCategory model, DrivesCleanerViewModel owner)
    {
        Model = model;
        _owner = owner;
        bool selected = model.SelectedByDefault && CanSelect;
        foreach (CleanupItem item in model.Items)
        {
            CleanerItemViewModel vm = new(item, this);
            vm.SetSilently(selected);
            Items.Add(vm);
        }

        IsExpanded = model.SelectedByDefault && CanSelect;
        ToggleExpandCommand = new RelayCommand(() => IsExpanded = !IsExpanded);
    }

    public CleanupCategory Model { get; }
    public List<CleanerItemViewModel> Items { get; } = [];
    public IEnumerable<CleanerItemViewModel> VisibleItems => Items.Take(MaxVisible);
    public bool HasHidden => Items.Count > MaxVisible;
    public string HiddenText => $"+{Items.Count - MaxVisible} more";

    public string Title => Model.Title;
    public string Description => Model.Description;
    public string Note => Model.Note ?? string.Empty;
    public bool HasNote => !string.IsNullOrWhiteSpace(Model.Note);
    public string Glyph => Model.Glyph;
    public bool IsSafe => Model.Safety == Safety.Safe;
    public bool IsBlocked => Model.Blocked;
    public bool IsSteam => Model.Action == CleanAction.SteamUninstall;
    public bool IsSettings => Model.Action == CleanAction.OpenSettings;
    public bool IsHibernation => Model.Action == CleanAction.TurnOffHibernation;
    public bool CanSelect => !Model.Blocked && Model.Size > 0 && Model.Action is CleanAction.Delete or CleanAction.EmptyRecycleBin;
    public bool ItemsSelectable => CanSelect && Items.Count > 1;
    public bool ShowItemList => Model.ShowItems && Items.Count > 0;
    public bool ShowDetails => IsExpanded && (ShowItemList || IsSettings || IsHibernation || IsBlocked);
    public string SafetyText => Model.Blocked
        ? DrivesText.Get("DrivesCleaner_NeedsAdmin", "Needs admin")
        : IsSafe
            ? DrivesText.Get("DrivesCleaner_Safe", "Safe")
            : DrivesText.Get("DrivesCleaner_Review", "Review");
    public long Size => Model.Size;
    public string SizeText => Model.Blocked ? "N/A" : Format.Bytes(Size);
    public int SelectedCount => CanSelect ? Items.Count(item => item.IsSelected) : 0;
    public long SelectedSize => CanSelect ? Items.Where(item => item.IsSelected).Sum(item => item.Model.Size) : 0;
    public bool CanRestartAsAdmin => !Admin.IsElevated;

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    partial void OnIsExpandedChanged(bool value)
    {
        OnPropertyChanged(nameof(ChevronGlyph));
        OnPropertyChanged(nameof(ShowDetails));
    }

    public string ChevronGlyph => IsExpanded ? "\uE70E" : "\uE70D";

    public bool? IsSelected
    {
        get
        {
            if (!CanSelect)
            {
                return false;
            }

            int selected = Items.Count(item => item.IsSelected);
            return selected == 0 ? false : selected == Items.Count ? true : null;
        }
        set
        {
            if (!CanSelect)
            {
                return;
            }

            bool next = value == true;
            foreach (CleanerItemViewModel item in Items)
            {
                item.SetSilently(next);
            }
            Changed();
        }
    }

    public void SetSelected(bool selected)
    {
        if (!CanSelect)
        {
            return;
        }

        foreach (CleanerItemViewModel item in Items)
        {
            item.SetSilently(selected);
        }
        Changed();
    }

    public IReadOnlyList<CleanupItem> SelectedItems() =>
        Items.Where(item => item.IsSelected).Select(item => item.Model).ToList();

    internal void Changed()
    {
        OnPropertyChanged(nameof(IsSelected));
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(SelectedSize));
        _owner.Recalc();
    }

    public IRelayCommand ToggleExpandCommand { get; }
    public IRelayCommand OpenSettingsCommand => _owner.OpenWindowsStorageSettingsCommand;
    public IAsyncRelayCommand TurnOffHibernationCommand => _owner.TurnOffHibernationCommand;
    public IRelayCommand RestartAdminCommand => _owner.RestartAsAdminCommand;
}

public partial class CleanerItemViewModel : ObservableObject
{
    private readonly CleanerCategoryViewModel _parent;

    public CleanerItemViewModel(CleanupItem model, CleanerCategoryViewModel parent)
    {
        Model = model;
        _parent = parent;
        RevealCommand = new RelayCommand(() => Shell.Reveal(Model.Path));
        UninstallCommand = new RelayCommand(() =>
        {
            if (!string.IsNullOrWhiteSpace(Model.AppId) && Model.AppId != "0")
            {
                Shell.Open($"steam://uninstall/{Model.AppId}");
            }
        });
    }

    public CleanupItem Model { get; }
    public string Name => Model.Name;
    public string Detail => Model.Detail ?? Model.Path;
    public string PathText => Model.Path;
    public string SizeText => Format.Bytes(Model.Size);
    public bool Selectable => _parent.ItemsSelectable;
    public bool IsSteam => _parent.IsSteam && !string.IsNullOrWhiteSpace(Model.AppId);

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetProperty(ref _isSelected, value))
            {
                _parent.Changed();
            }
        }
    }

    internal void SetSilently(bool selected)
    {
        if (_isSelected == selected)
        {
            return;
        }

        _isSelected = selected;
        OnPropertyChanged(nameof(IsSelected));
    }

    public IRelayCommand RevealCommand { get; }
    public IRelayCommand UninstallCommand { get; }
}

public sealed class DriveItemViewModel
{
    public DriveItemViewModel(string root, string label, long total, long free)
    {
        Root = root;
        Label = string.IsNullOrWhiteSpace(label)
            ? DrivesText.Get("DrivesCleaner_LocalDisk", "Local Disk")
            : label;
        Total = total;
        Free = free;
    }

    public string Root { get; }
    public string Label { get; }
    public long Total { get; }
    public long Free { get; }
    public long Used => Math.Max(0, Total - Free);
    public string Title => $"{Label} ({Root.TrimEnd('\\')})";
    public string Subtitle => $"{Format.Bytes(Free)} free of {Format.Bytes(Total)}";
}

public sealed class SpaceMapRowViewModel
{
    public SpaceMapRowViewModel(string name, string path, long size, long total)
    {
        Name = name;
        Path = path;
        Size = size;
        Total = total;
    }

    public string Name { get; }
    public string Path { get; }
    public long Size { get; }
    public long Total { get; }
    public string SizeText => Format.Bytes(Size);
    public string PercentText => Total <= 0 ? "0%" : $"{Size * 100d / Total:0.#}%";
}

public sealed class BigFileViewModel
{
    public BigFileViewModel(string path, long size)
    {
        Path = path;
        Size = size;
    }

    public string Path { get; }
    public long Size { get; }
    public string Name => System.IO.Path.GetFileName(Path);
    public string Folder => System.IO.Path.GetDirectoryName(Path) ?? string.Empty;
    public string SizeText => Format.Bytes(Size);
}
