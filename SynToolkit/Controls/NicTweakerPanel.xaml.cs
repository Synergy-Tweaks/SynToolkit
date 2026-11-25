#nullable enable

using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using SynToolkit.Services;
using SynToolkit.Services.NicTweaker;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace SynToolkit.Controls;

public sealed partial class NicTweakerPanel : UserControl
{
    private readonly INicTweakerService _service;
    private readonly ObservableCollection<NicSectionViewModel> _sections = [];
    private readonly ObservableCollection<NicResultRowViewModel> _results = [];
    private readonly ObservableCollection<string> _presetNames = [];
    private readonly SemaphoreSlim _busyGate = new(1, 1);
    private readonly Dictionary<string, NicTweakRowViewModel> _rowsById = new(StringComparer.OrdinalIgnoreCase);

    private NicAdapterInfo? _selectedAdapter;

    public NicTweakerPanel()
    {
        InitializeComponent();
        _service = App._host.Services.GetRequiredService<INicTweakerService>();
        SectionItemsControl.ItemsSource = _sections;
        ResultsItemsControl.ItemsSource = _results;
        PresetCombo.ItemsSource = _presetNames;
        _results.CollectionChanged += (_, _) => UpdateResultsVisibility();
        Loaded += NicTweakerPanel_Loaded;
    }

    private async void NicTweakerPanel_Loaded(object sender, RoutedEventArgs e)
    {
        ReadOnlyBar.IsOpen = !NicTweakerService.IsElevated();
        await RunBusyAsync(async () =>
        {
            await _service.EnsureInitializedAsync();
            await LoadAdaptersAsync();
            await LoadPresetNamesAsync();
            BuildRows();
            await RefreshCurrentStatesAsync();
        });
        UpdateResultsVisibility();
    }

    private void BuildRows()
    {
        _sections.Clear();
        _rowsById.Clear();
        foreach (IGrouping<NicTweakSection, NicTweakDefinition> group in _service.Catalog.GroupBy(static item => item.Section))
        {
            NicSectionViewModel section = new() { Header = ToHeader(group.Key) };
            foreach (NicTweakDefinition definition in group)
            {
                NicTweakRowViewModel row = new()
                {
                    Id = definition.Id,
                    Label = definition.Label,
                    AllowFreeText = definition.AllowFreeText || definition.Options.Count == 0
                };
                foreach (NicTweakOption option in definition.Options)
                {
                    row.Options.Add(option.Display);
                }
                section.Rows.Add(row);
                _rowsById[definition.Id] = row;
            }
            _sections.Add(section);
        }
    }

    private static string ToHeader(NicTweakSection section) => section switch
    {
        NicTweakSection.AdapterAdvanced => "Adapter advanced properties",
        NicTweakSection.PowerSaving => "Power saving",
        NicTweakSection.GlobalTcp => "Global TCP",
        NicTweakSection.Rss => "RSS",
        NicTweakSection.Interface => "NetIPInterface",
        NicTweakSection.Offload => "Global offload",
        _ => section.ToString()
    };

    private async Task LoadAdaptersAsync()
    {
        IReadOnlyList<NicAdapterInfo> adapters = await _service.GetAdaptersAsync();
        AdapterCombo.ItemsSource = adapters;
        AdapterCombo.SelectedItem = adapters.FirstOrDefault();
        _selectedAdapter = adapters.FirstOrDefault();
    }

    private async Task RefreshCurrentStatesAsync()
    {
        if (_selectedAdapter is null)
        {
            return;
        }

        string family = "IPv4";
        IDictionary<string, NicTweakReadState> current = await _service.ReadCurrentAsync(_selectedAdapter, family);
        foreach ((string id, NicTweakRowViewModel row) in _rowsById)
        {
            if (!current.TryGetValue(id, out NicTweakReadState? state))
            {
                continue;
            }

            row.CurrentDisplay = state.CurrentDisplay;
            row.IsEditable = !state.IsUnsupported && NicTweakerService.IsElevated();
            if (string.IsNullOrWhiteSpace(row.TargetDisplay))
            {
                row.TargetDisplay = state.CurrentDisplay;
                if (!row.Options.Contains(row.TargetDisplay))
                {
                    row.Options.Insert(0, row.TargetDisplay);
                }
            }
        }
    }

    private async Task LoadPresetNamesAsync()
    {
        IReadOnlyList<string> names = await _service.ListPresetNamesAsync();
        _presetNames.Clear();
        foreach (string name in names)
        {
            _presetNames.Add(name);
        }
    }

    private async void AdapterCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AdapterCombo.SelectedItem is NicAdapterInfo adapter)
        {
            _selectedAdapter = adapter;
            await RunBusyAsync(RefreshCurrentStatesAsync);
        }
    }

    private async void RefreshAdaptersButton_Click(object sender, RoutedEventArgs e)
    {
        await RunBusyAsync(async () =>
        {
            await LoadAdaptersAsync();
            await RefreshCurrentStatesAsync();
            ShowResult("Adapters refreshed.", InfoBarSeverity.Success);
        });
    }

    private async void LoadPresetButton_Click(object sender, RoutedEventArgs e)
    {
        if (PresetCombo.SelectedItem is not string presetName)
        {
            ShowResult("Select a preset first.", InfoBarSeverity.Warning);
            return;
        }

        await RunBusyAsync(async () =>
        {
            NicPreset? preset = await _service.LoadPresetAsync(presetName);
            if (preset is null)
            {
                ShowResult($"Preset '{presetName}' was not found.", InfoBarSeverity.Warning);
                return;
            }

            foreach ((string id, string? value) in preset.Values)
            {
                if (!_rowsById.TryGetValue(id, out NicTweakRowViewModel? row))
                {
                    continue;
                }
                row.TargetDisplay = value ?? "(not set)";
                if (!row.Options.Contains(row.TargetDisplay))
                {
                    row.Options.Insert(0, row.TargetDisplay);
                }
            }

            ShowResult($"Loaded {presetName} preset into target values.", InfoBarSeverity.Success);
        });
    }

    private async void ApplyButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedAdapter is null)
        {
            ShowResult("Select an adapter first.", InfoBarSeverity.Warning);
            return;
        }

        await RunBusyAsync(async () =>
        {
            Dictionary<string, string?> desired = new(StringComparer.OrdinalIgnoreCase);
            foreach ((string id, NicTweakRowViewModel row) in _rowsById)
            {
                if (!row.IsEditable)
                {
                    continue;
                }
                desired[id] = row.TargetDisplay;
            }

            NicApplyBatchResult apply = await _service.ApplyAsync(_selectedAdapter, "IPv4", desired);
            _results.Clear();
            foreach (NicApplyItemResult item in apply.Items)
            {
                _results.Add(new NicResultRowViewModel
                {
                    Label = item.Label,
                    Status = item.Status,
                    Message = item.Message
                });
            }

            await RefreshCurrentStatesAsync();
            int failed = apply.Items.Count(static item => item.Status.Equals("Failed", StringComparison.OrdinalIgnoreCase));
            ShowResult(
                failed == 0 ? "NIC tweaks applied." : $"NIC tweaks applied with {failed} failure(s).",
                failed == 0 ? InfoBarSeverity.Success : InfoBarSeverity.Warning);
        });
    }

    private async void BackupButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedAdapter is null)
        {
            ShowResult("Select an adapter first.", InfoBarSeverity.Warning);
            return;
        }

        await RunBusyAsync(async () =>
        {
            await _service.BackupNowAsync(_selectedAdapter, "Manual backup");
            ShowResult("Snapshot backup created.", InfoBarSeverity.Success);
        });
    }

    private async void RevertLastButton_Click(object sender, RoutedEventArgs e)
    {
        await RunBusyAsync(async () =>
        {
            await _service.RevertLastAsync();
            await RefreshCurrentStatesAsync();
            ShowResult("Reverted using latest snapshot.", InfoBarSeverity.Success);
        });
    }

    private async void RestoreSnapshotButton_Click(object sender, RoutedEventArgs e)
    {
        await RunBusyAsync(async () =>
        {
            IReadOnlyList<NicSnapshot> snapshots = await _service.ListSnapshotsAsync();
            if (snapshots.Count == 0)
            {
                ShowResult("No snapshots are available.", InfoBarSeverity.Warning);
                return;
            }

            ComboBox picker = new()
            {
                MinWidth = 420,
                ItemsSource = snapshots
                    .Select(static item => $"{item.CreatedUtc:yyyy-MM-dd HH:mm:ss} - {item.Label}")
                    .ToList(),
                SelectedIndex = 0
            };

            ContentDialog dialog = new()
            {
                XamlRoot = XamlRoot,
                Title = "Choose snapshot to restore",
                PrimaryButtonText = "Restore",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                Content = picker
            };

            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }

            NicSnapshot snapshot = snapshots[picker.SelectedIndex];
            await _service.RestoreSnapshotAsync(snapshot.Id);
            await RefreshCurrentStatesAsync();
            ShowResult($"Restored snapshot: {snapshot.Label}", InfoBarSeverity.Success);
        });
    }

    private async void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedAdapter is null)
        {
            ShowResult("Select an adapter first.", InfoBarSeverity.Warning);
            return;
        }

        FileSavePicker picker = new();
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.m_window));
        picker.SuggestedStartLocation = PickerLocationId.Desktop;
        picker.SuggestedFileName = $"SynToolkitNIC_{DateTime.Now:yyyyMMdd-HHmmss}";
        picker.FileTypeChoices.Add("NIC Tweaker config", [".nttweak"]);
        Windows.Storage.StorageFile? file = await picker.PickSaveFileAsync();
        if (file is null)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            await _service.ExportConfigAsync(_selectedAdapter, "IPv4", file.Path);
            ShowResult($"Exported config: {Path.GetFileName(file.Path)}", InfoBarSeverity.Success);
        });
    }

    private async void ImportButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedAdapter is null)
        {
            ShowResult("Select an adapter first.", InfoBarSeverity.Warning);
            return;
        }

        FileOpenPicker picker = new();
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.m_window));
        picker.SuggestedStartLocation = PickerLocationId.Desktop;
        picker.FileTypeFilter.Add(".nttweak");
        picker.FileTypeFilter.Add(".json");
        Windows.Storage.StorageFile? file = await picker.PickSingleFileAsync();
        if (file is null)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            NicImportPreview preview = await _service.BuildImportPreviewAsync(file.Path, _selectedAdapter, "IPv4");
            ContentDialog dialog = new()
            {
                XamlRoot = XamlRoot,
                Title = "Import preview",
                PrimaryButtonText = "Apply",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                Content = new ScrollViewer
                {
                    MaxHeight = 300,
                    Content = new TextBlock
                    {
                        Text = BuildPreviewText(preview),
                        TextWrapping = TextWrapping.Wrap
                    }
                }
            };

            ContentDialogResult choice = await dialog.ShowAsync();
            if (choice != ContentDialogResult.Primary)
            {
                return;
            }

            NicApplyBatchResult result = await _service.ApplyImportAsync(_selectedAdapter, "IPv4", preview.Rows);
            _results.Clear();
            foreach (NicApplyItemResult item in result.Items)
            {
                _results.Add(new NicResultRowViewModel
                {
                    Label = item.Label,
                    Status = item.Status,
                    Message = item.Message
                });
            }

            await RefreshCurrentStatesAsync();
            ShowResult("Imported configuration applied.", InfoBarSeverity.Success);
        });
    }

    private static string BuildPreviewText(NicImportPreview preview)
    {
        StringBuilder text = new();
        if (!string.IsNullOrWhiteSpace(preview.AdapterWarning))
        {
            text.AppendLine(preview.AdapterWarning);
            text.AppendLine();
        }

        foreach (NicImportDiffRow row in preview.Rows.Take(120))
        {
            text.AppendLine($"{row.Label}: {row.CurrentDisplay} -> {row.ImportedDisplay}");
        }

        if (preview.Skipped.Count > 0)
        {
            text.AppendLine();
            text.AppendLine("Skipped:");
            foreach (string skipped in preview.Skipped.Take(40))
            {
                text.AppendLine("- " + skipped);
            }
        }

        return text.ToString();
    }

    private async void RestorePointButton_Click(object sender, RoutedEventArgs e)
    {
        await RunBusyAsync(async () =>
        {
            RestorePointCreateResult result = await _service.CreateRestorePointAsync($"SynToolkit NIC Tweaker {DateTime.Now:yyyy-MM-dd HH:mm}");
            bool ok = result.Status is RestorePointOperationStatus.Success or RestorePointOperationStatus.Unverified;
            ShowResult(
                ok ? "Restore point requested successfully." : $"Restore point failed: {result.Status}",
                ok ? InfoBarSeverity.Success : InfoBarSeverity.Warning);
        });
    }

    private async Task RunBusyAsync(Func<Task> operation)
    {
        await _busyGate.WaitAsync();
        SetBusyState(true);
        try
        {
            await operation();
        }
        catch (Exception ex)
        {
            ShowResult(BuildFriendlyError(ex), InfoBarSeverity.Error);
        }
        finally
        {
            SetBusyState(false);
            _busyGate.Release();
        }
    }

    private static string BuildFriendlyError(Exception ex)
    {
        if (!string.IsNullOrWhiteSpace(ex.Message))
        {
            return ex.Message;
        }

        if (ex.InnerException is not null && !string.IsNullOrWhiteSpace(ex.InnerException.Message))
        {
            return ex.InnerException.Message;
        }

        return $"Operation failed ({ex.GetType().Name}). Check NIC Tweaker logs for details.";
    }

    private void SetBusyState(bool busy)
    {
        BusyRing.IsActive = busy;
        ApplyButton.IsEnabled = !busy && NicTweakerService.IsElevated();
        RevertLastButton.IsEnabled = !busy && NicTweakerService.IsElevated();
        BackupButton.IsEnabled = !busy && _selectedAdapter is not null;
        RestoreSnapshotButton.IsEnabled = !busy && NicTweakerService.IsElevated();
        ExportButton.IsEnabled = !busy && _selectedAdapter is not null;
        ImportButton.IsEnabled = !busy && NicTweakerService.IsElevated() && _selectedAdapter is not null;
        RestorePointButton.IsEnabled = !busy;
        RefreshAdaptersButton.IsEnabled = !busy;
        LoadPresetButton.IsEnabled = !busy;
        AdapterCombo.IsEnabled = !busy;
        PresetCombo.IsEnabled = !busy;
    }

    private void ShowResult(string message, InfoBarSeverity severity)
    {
        ResultBar.Severity = severity;
        ResultBar.Title = severity switch
        {
            InfoBarSeverity.Success => "Done",
            InfoBarSeverity.Warning => "Warning",
            InfoBarSeverity.Error => "Error",
            _ => "Info"
        };
        ResultBar.Message = message;
        ResultBar.IsOpen = true;
    }

    private void UpdateResultsVisibility()
    {
        LastApplyResultsExpander.Visibility = _results.Count == 0
            ? Visibility.Collapsed
            : Visibility.Visible;
    }
}

public sealed partial class NicSectionViewModel : ObservableObject
{
    [ObservableProperty]
    public partial string Header { get; set; } = string.Empty;

    public ObservableCollection<NicTweakRowViewModel> Rows { get; } = [];
}

public sealed partial class NicTweakRowViewModel : ObservableObject
{
    [ObservableProperty]
    public partial string Id { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Label { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string CurrentDisplay { get; set; } = "(not set)";

    [ObservableProperty]
    public partial string TargetDisplay { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool AllowFreeText { get; set; }

    [ObservableProperty]
    public partial bool IsEditable { get; set; } = true;

    public ObservableCollection<string> Options { get; } = [];
}

public sealed class NicResultRowViewModel
{
    public string Label { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
}

public sealed class StringEqualsConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        string left = value?.ToString() ?? string.Empty;
        string right = parameter?.ToString() ?? string.Empty;
        return left.Equals(right, StringComparison.OrdinalIgnoreCase);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
