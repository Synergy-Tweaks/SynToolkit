#nullable enable

using CommunityToolkit.Mvvm.ComponentModel;
using SynToolkit.Services.GpuDrivers;
using System.Collections.ObjectModel;

namespace SynToolkit.ViewModels;

public enum NvidiaTweaksFilterOption
{
    All,
    Applied,
    NotApplied,
    Different
}

public partial class NvidiaTweakRowViewModel : ObservableObject
{
    private readonly NvidiaTweakCatalogItem _item;

    [ObservableProperty]
    public partial NvidiaTweakValueState State { get; set; } = NvidiaTweakValueState.NotSet;

    [ObservableProperty]
    public partial bool IsRestartPending { get; set; }

    [ObservableProperty]
    public partial string CurrentValueText { get; set; } = "Not set";

    [ObservableProperty]
    public partial bool CanWrite { get; set; }

    public NvidiaTweakRowViewModel(NvidiaTweakCatalogItem item)
    {
        _item = item;
    }

    public NvidiaTweakCatalogItem CatalogItem => _item;
    public string Id => _item.Definition.Id;
    public NvidiaTweakGroup Group => _item.Definition.Group;
    public string Name => _item.Metadata.DisplayName;
    public string Description => _item.Metadata.Description;
    public NvidiaTweakRiskLevel Risk => _item.Metadata.RiskLevel;
    public string RecommendedValueText => _item.Definition.RecommendedValueText;
    public string LocationText => _item.Definition.LocationText;
    public bool SupportsCustomValue => _item.Definition.SupportsCustomValue;
    public string RecommendedDisplay => $"Recommended: {RecommendedValueText}";
    public string CurrentDisplay => $"Current: {CurrentValueText}";

    public string RiskText => Risk switch
    {
        NvidiaTweakRiskLevel.Advanced => "Advanced",
        NvidiaTweakRiskLevel.Caution => "Caution",
        _ => "Safe"
    };

    public string StatusText => IsRestartPending
        ? "Restart pending"
        : State switch
        {
            NvidiaTweakValueState.Applied => "Applied",
            NvidiaTweakValueState.Partial => "Partial",
            NvidiaTweakValueState.NotSet => "Not set",
            _ => "Different"
        };

    public void ApplySnapshot(NvidiaTweakValueSnapshot? snapshot)
    {
        if (snapshot is null)
        {
            State = NvidiaTweakValueState.NotSet;
            IsRestartPending = false;
            CurrentValueText = "Not set";
            OnPropertyChanged(nameof(StatusText));
            return;
        }

        State = snapshot.State;
        IsRestartPending = snapshot.RestartPending;
        CurrentValueText = snapshot.CurrentValueText;
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(CurrentDisplay));
    }

    public bool Matches(string query, NvidiaTweaksFilterOption filter)
    {
        bool filterMatch = filter switch
        {
            NvidiaTweaksFilterOption.Applied => State == NvidiaTweakValueState.Applied && !IsRestartPending,
            NvidiaTweaksFilterOption.NotApplied => State == NvidiaTweakValueState.NotSet,
            NvidiaTweaksFilterOption.Different => State is NvidiaTweakValueState.Different or NvidiaTweakValueState.Partial || IsRestartPending,
            _ => true
        };
        if (!filterMatch)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            return true;
        }

        return Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            Description.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            Id.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            LocationText.Contains(query, StringComparison.OrdinalIgnoreCase);
    }
}

public partial class NvidiaTweakGroupViewModel : ObservableObject
{
    [ObservableProperty]
    public partial string Header { get; set; } = string.Empty;

    public NvidiaTweakGroup Group { get; init; }
    public ObservableCollection<NvidiaTweakRowViewModel> AllRows { get; } = new();
    public ObservableCollection<NvidiaTweakRowViewModel> VisibleRows { get; } = new();

    public void ApplyFilter(string query, NvidiaTweaksFilterOption filter)
    {
        VisibleRows.Clear();
        foreach (NvidiaTweakRowViewModel row in AllRows.Where(row => row.Matches(query, filter)))
        {
            VisibleRows.Add(row);
        }
        OnPropertyChanged(nameof(CountLabel));
        OnPropertyChanged(nameof(HasVisibleRows));
    }

    public string CountLabel
    {
        get
        {
            int applied = AllRows.Count(row => row.State == NvidiaTweakValueState.Applied && !row.IsRestartPending);
            int pending = AllRows.Count(row => row.IsRestartPending);
            if (pending > 0)
            {
                return $"{applied}/{AllRows.Count} applied · {pending} pending restart";
            }

            return $"{applied}/{AllRows.Count} applied";
        }
    }

    public bool HasVisibleRows => VisibleRows.Count > 0;
}

public partial class NvidiaPerformanceTweaksPageViewModel : ObservableObject
{
    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial bool IsElevated { get; set; }

    [ObservableProperty]
    public partial bool HasNvidiaGpu { get; set; }

    [ObservableProperty]
    public partial bool IsNotBusy { get; set; } = true;

    [ObservableProperty]
    public partial bool CanWrite { get; set; }

    [ObservableProperty]
    public partial bool ShowNoNvidiaMessage { get; set; }

    [ObservableProperty]
    public partial string ActiveGpuName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string DriverClassPath { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool DriverClassMatchedByInf { get; set; }

    [ObservableProperty]
    public partial string SummaryText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasRestartPending { get; set; }

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial NvidiaTweaksFilterOption Filter { get; set; } = NvidiaTweaksFilterOption.All;

    [ObservableProperty]
    public partial bool ResultBarOpen { get; set; }

    [ObservableProperty]
    public partial string ResultTitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ResultMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial Microsoft.UI.Xaml.Controls.InfoBarSeverity ResultSeverity { get; set; } =
        Microsoft.UI.Xaml.Controls.InfoBarSeverity.Informational;

    [ObservableProperty]
    public partial bool ReadOnlyBarOpen { get; set; }

    public string NoNvidiaMessage =>
        "No supported NVIDIA GPU driver class key was found on this machine. This page is available only when an NVIDIA adapter is detected.";

    public ObservableCollection<NvidiaTweakGroupViewModel> Groups { get; } = [];

    partial void OnSearchTextChanged(string value) => RefreshFilter();
    partial void OnFilterChanged(NvidiaTweaksFilterOption value) => RefreshFilter();

    public void RefreshFilter()
    {
        foreach (NvidiaTweakGroupViewModel group in Groups)
        {
            group.ApplyFilter(SearchText.Trim(), Filter);
        }
    }

    partial void OnHasNvidiaGpuChanged(bool value)
    {
        ShowNoNvidiaMessage = !value;
    }

    public void ShowResult(string title, string message, Microsoft.UI.Xaml.Controls.InfoBarSeverity severity)
    {
        ResultTitle = title;
        ResultMessage = message;
        ResultSeverity = severity;
        ResultBarOpen = true;
    }
}
