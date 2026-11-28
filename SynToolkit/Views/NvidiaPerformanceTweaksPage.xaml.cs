#nullable enable

using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SynToolkit.Controls;
using SynToolkit.Services.GpuDrivers;
using SynToolkit.ViewModels;
using System.Diagnostics;
using System.Text;
using Windows.ApplicationModel.DataTransfer;
using Microsoft.Win32;
using Microsoft.Extensions.DependencyInjection;

namespace SynToolkit.Views;

public sealed partial class NvidiaPerformanceTweaksPage : Page
{
    private readonly INvidiaPerformanceTweaksService _service;
    private readonly Dictionary<string, NvidiaTweakRowViewModel> _rowsById = new(StringComparer.OrdinalIgnoreCase);

    public NvidiaPerformanceTweaksPageViewModel ViewModel { get; } = new();

    public int FilterIndex
    {
        get => (int)ViewModel.Filter;
        set => ViewModel.Filter = (NvidiaTweaksFilterOption)Math.Clamp(value, 0, 3);
    }

    public NvidiaPerformanceTweaksPage()
    {
        InitializeComponent();
        _service = App._host.Services.GetRequiredService<INvidiaPerformanceTweaksService>();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        SetBusy(true);
        try
        {
            NvidiaTweakCheckResult result = await _service.CheckAllAsync(null);
            BuildRowsFromResult(result);
            ApplyResultSummary(result);

            ViewModel.IsElevated = result.IsElevated;
            ViewModel.ReadOnlyBarOpen = !result.IsElevated;
            ViewModel.HasNvidiaGpu = result.HasNvidiaGpu;
            ViewModel.ActiveGpuName = result.ActiveGpuName;
            ViewModel.DriverClassPath = result.DriverClassPath;
            ViewModel.DriverClassMatchedByInf = result.MatchedByInf;
            ViewModel.CanWrite = result.IsElevated && !ViewModel.IsBusy;
            UpdateRowWriteState();
            ViewModel.ShowResult(
                "Scan complete",
                string.IsNullOrWhiteSpace(result.Message) ? "Tweak states refreshed successfully." : result.Message,
                InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            ViewModel.ShowResult("Failed to load tweaks", ex.Message, InfoBarSeverity.Error);
        }
        finally
        {
            SetBusy(false);
            RefreshComputedBindings();
        }
    }

    private void BuildRowsFromResult(NvidiaTweakCheckResult result)
    {
        ViewModel.Groups.Clear();
        _rowsById.Clear();

        foreach (IGrouping<NvidiaTweakGroup, NvidiaTweakCatalogItem> group in _service.Catalog.GroupBy(item => item.Definition.Group))
        {
            NvidiaTweakGroupViewModel groupVm = new()
            {
                Group = group.Key,
                Header = GroupHeader(group.Key)
            };
            foreach (NvidiaTweakCatalogItem item in group)
            {
                NvidiaTweakRowViewModel row = new(item);
                if (result.Values.TryGetValue(item.Definition.Id, out NvidiaTweakValueSnapshot? snapshot))
                {
                    row.ApplySnapshot(snapshot);
                }
                groupVm.AllRows.Add(row);
                _rowsById[item.Definition.Id] = row;
            }

            groupVm.ApplyFilter(ViewModel.SearchText, ViewModel.Filter);
            ViewModel.Groups.Add(groupVm);
        }

        ViewModel.RefreshFilter();
    }

    private void ApplyResultSummary(NvidiaTweakCheckResult result)
    {
        int total = _rowsById.Count;
        int applied = _rowsById.Values.Count(row => row.State == NvidiaTweakValueState.Applied && !row.IsRestartPending);
        int pending = _rowsById.Values.Count(row => row.IsRestartPending);
        ViewModel.SummaryText = $"{applied} of {total}";
        ViewModel.HasRestartPending = pending > 0;
    }

    private async Task RefreshSingleAsync(string tweakId)
    {
        (bool success, string message, NvidiaTweakValueSnapshot? value) = await _service.CheckItemAsync(null, tweakId);
        if (success && _rowsById.TryGetValue(tweakId, out NvidiaTweakRowViewModel? row))
        {
            row.ApplySnapshot(value);
        }
        ViewModel.RefreshFilter();
        RecomputeSummary();
    }

    private void SetBusy(bool busy)
    {
        ViewModel.IsBusy = busy;
        ViewModel.IsNotBusy = !busy;
        ViewModel.CanWrite = ViewModel.IsElevated && !busy;
        UpdateRowWriteState();
        RefreshComputedBindings();
    }

    private void UpdateRowWriteState()
    {
        bool canWrite = ViewModel.IsElevated && !ViewModel.IsBusy;
        foreach (NvidiaTweakRowViewModel row in _rowsById.Values)
        {
            row.CanWrite = canWrite;
        }
    }

    private void RefreshComputedBindings()
    {
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Normal, () =>
        {
            Bindings.Update();
        });
    }

    private string GroupHeader(NvidiaTweakGroup group) => group switch
    {
        NvidiaTweakGroup.NvidiaSettings => "NVIDIA Settings",
        NvidiaTweakGroup.RmPowerFeature => "RM Power Features",
        NvidiaTweakGroup.Telemetry => "Telemetry",
        NvidiaTweakGroup.Ecc => "ECC",
        NvidiaTweakGroup.Hdcp => "HDCP",
        _ => group.ToString()
    };

    private async void CheckAllButton_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        NvidiaTweakCheckResult result = await _service.CheckAllAsync(null);
        BuildRowsFromResult(result);
        ApplyResultSummary(result);
        ViewModel.IsElevated = result.IsElevated;
        ViewModel.ReadOnlyBarOpen = !result.IsElevated;
        ViewModel.CanWrite = result.IsElevated && !ViewModel.IsBusy;
        UpdateRowWriteState();
        ViewModel.ShowResult(
            "Check complete",
            string.IsNullOrWhiteSpace(result.Message) ? "Tweak states refreshed successfully." : result.Message,
            InfoBarSeverity.Success);
    });

    private async void VerifyButton_Click(object sender, RoutedEventArgs e) => await CheckAllButton_ClickAsync("Verification complete");

    private async Task CheckAllButton_ClickAsync(string title)
    {
        await RunAsync(async () =>
        {
            NvidiaTweakCheckResult result = await _service.CheckAllAsync(null);
            BuildRowsFromResult(result);
            ApplyResultSummary(result);
            ViewModel.ShowResult(title, result.Message, InfoBarSeverity.Success);
        });
    }

    private async void ApplyItemButton_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetTweakId(sender, out string tweakId))
        {
            await ApplyItemAsync(tweakId);
        }
    }

    private async void RevertItemButton_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetTweakId(sender, out string tweakId))
        {
            await RevertItemAsync(tweakId);
        }
    }

    private async void CheckItemButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetTweakId(sender, out string tweakId))
        {
            return;
        }

        await RunAsync(async () =>
        {
            await RefreshSingleAsync(tweakId);
            ViewModel.ShowResult("Check complete", $"Verified {tweakId}.", InfoBarSeverity.Informational);
        });
    }

    private async void CustomValueButton_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetTweakId(sender, out string tweakId))
        {
            await ShowCustomValueDialogAsync(tweakId);
        }
    }

    private async void ApplyGroupButton_Click(object sender, RoutedEventArgs e)
    {
        if (!CanWriteAction())
        {
            return;
        }

        if (sender is not Button { Tag: NvidiaTweakGroup group })
        {
            return;
        }

        bool confirmed = await ConfirmGroupActionAsync(group, true);
        if (!confirmed)
        {
            return;
        }

        await RunAsync(async () =>
        {
            NvidiaActionResult result = await _service.ApplyGroupAsync(null, group);
            await CheckAllButton_ClickAsync("Group applied and verified");
            ViewModel.ShowResult(result.Success ? "Group applied" : "Group apply failed", result.Message, result.Success ? InfoBarSeverity.Success : InfoBarSeverity.Error);
        });
    }

    private async void RevertGroupButton_Click(object sender, RoutedEventArgs e)
    {
        if (!CanWriteAction())
        {
            return;
        }

        if (sender is not Button { Tag: NvidiaTweakGroup group })
        {
            return;
        }

        bool confirmed = await ConfirmGroupActionAsync(group, false);
        if (!confirmed)
        {
            return;
        }

        await RunAsync(async () =>
        {
            NvidiaActionResult result = await _service.RevertGroupAsync(null, group);
            await CheckAllButton_ClickAsync("Group reverted and verified");
            ViewModel.ShowResult(result.Success ? "Group reverted" : "Group revert failed", result.Message, result.Success ? InfoBarSeverity.Success : InfoBarSeverity.Error);
        });
    }

    private async void ExportRegButton_Click(object sender, RoutedEventArgs e)
    {
        await RunAsync(async () =>
        {
            NvidiaRegExportResult exportResult = await _service.ExportAppliedTweaksAsRegAsync(null);
            ViewModel.ShowResult(
                exportResult.Success ? "Export complete" : "Export failed",
                exportResult.Message,
                exportResult.Success ? InfoBarSeverity.Success : InfoBarSeverity.Warning);
        });
    }

    private async void RestoreOriginalsButton_Click(object sender, RoutedEventArgs e)
    {
        if (!CanWriteAction())
        {
            return;
        }

        bool confirmed = await ConfirmAsync(
            "Restore all original values?",
            "This restores every recorded original NVIDIA registry value from SynToolkit backup and re-verifies state.");
        if (!confirmed)
        {
            return;
        }

        await RunAsync(async () =>
        {
            NvidiaActionResult result = await _service.RestoreAllOriginalsAsync(null);
            await CheckAllButton_ClickAsync("Original values restored and verified");
            ViewModel.ShowResult(result.Success ? "Restore complete" : "Restore failed", result.Message, result.Success ? InfoBarSeverity.Success : InfoBarSeverity.Error);
        });
    }

    private async void OpenInRegeditButton_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetTweakId(sender, out string tweakId))
        {
            await OpenInRegeditAsync(tweakId);
        }
    }

    private async void CopyRegistryPathButton_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetTweakId(sender, out string tweakId))
        {
            await CopyRegistryPathAsync(tweakId);
        }
    }

    private async Task RunAsync(Func<Task> action)
    {
        SetBusy(true);
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            ViewModel.ShowResult("Operation failed", ex.Message, InfoBarSeverity.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private bool TryGetTweakId(object sender, out string tweakId)
    {
        tweakId = string.Empty;
        if (sender is not Button { Tag: string tag } || string.IsNullOrWhiteSpace(tag))
        {
            return false;
        }

        tweakId = tag;
        return true;
    }

    private async void TweakCard_ActionRequested(object sender, TweakCardActionEventArgs e)
    {
        switch (e.ActionType)
        {
            case TweakCardActionType.Apply:
                await ApplyItemAsync(e.TweakId);
                break;
            case TweakCardActionType.Revert:
                await RevertItemAsync(e.TweakId);
                break;
            case TweakCardActionType.Check:
                await RunAsync(async () =>
                {
                    await RefreshSingleAsync(e.TweakId);
                    ViewModel.ShowResult("Check complete", $"Verified {e.TweakId}.", InfoBarSeverity.Informational);
                });
                break;
            case TweakCardActionType.CustomValue:
                await ShowCustomValueDialogAsync(e.TweakId);
                break;
            case TweakCardActionType.CopyRegistryPath:
                await CopyRegistryPathAsync(e.TweakId);
                break;
            case TweakCardActionType.OpenInRegedit:
                await OpenInRegeditAsync(e.TweakId);
                break;
        }
    }

    private static bool TryParseHex(string text, out uint value)
    {
        text = text.Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return uint.TryParse(text[2..], System.Globalization.NumberStyles.HexNumber, null, out value);
        }

        return uint.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out value);
    }

    private async Task<bool> ConfirmAsync(string title, string message)
    {
        ContentDialog dialog = new()
        {
            XamlRoot = XamlRoot,
            Title = title,
            Content = message,
            PrimaryButtonText = "Continue",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private async Task<bool> ConfirmGroupActionAsync(NvidiaTweakGroup group, bool apply)
    {
        List<NvidiaTweakRowViewModel> rows = _rowsById.Values.Where(row => row.Group == group).ToList();
        StringBuilder builder = new();
        builder.AppendLine(apply ? "Apply all tweaks in this group:" : "Revert all tweaks in this group:");
        builder.AppendLine();
        foreach (NvidiaTweakRowViewModel row in rows)
        {
            builder.AppendLine($"- {row.Name} ({row.Id})");
        }

        return await ConfirmAsync(apply ? "Apply group tweaks?" : "Revert group tweaks?", builder.ToString());
    }

    private void RecomputeSummary()
    {
        int total = _rowsById.Count;
        int applied = _rowsById.Values.Count(row => row.State == NvidiaTweakValueState.Applied && !row.IsRestartPending);
        int pending = _rowsById.Values.Count(row => row.IsRestartPending);
        ViewModel.SummaryText = $"{applied} of {total}";
        ViewModel.HasRestartPending = pending > 0;
    }

    private async Task ApplyItemAsync(string tweakId)
    {
        if (!CanWriteAction() || !_rowsById.TryGetValue(tweakId, out NvidiaTweakRowViewModel? row))
        {
            return;
        }

        if (row.Risk is NvidiaTweakRiskLevel.Caution or NvidiaTweakRiskLevel.Advanced)
        {
            bool confirmed = await ConfirmAsync(
                "Apply risky tweak?",
                $"{row.Name}\n\n{row.Description}\n\nThis tweak is marked as {row.RiskText}. Continue?");
            if (!confirmed)
            {
                return;
            }
        }

        await RunAsync(async () =>
        {
            NvidiaActionResult result = await _service.ApplyItemAsync(null, tweakId);
            await RefreshSingleAsync(tweakId);
            ViewModel.ShowResult(result.Success ? "Applied" : "Apply failed", result.Message, result.Success ? InfoBarSeverity.Success : InfoBarSeverity.Error);
        });
    }

    private async Task RevertItemAsync(string tweakId)
    {
        if (!CanWriteAction())
        {
            return;
        }

        await RunAsync(async () =>
        {
            NvidiaActionResult result = await _service.RevertItemAsync(null, tweakId);
            await RefreshSingleAsync(tweakId);
            ViewModel.ShowResult(result.Success ? "Reverted" : "Revert failed", result.Message, result.Success ? InfoBarSeverity.Success : InfoBarSeverity.Error);
        });
    }

    private async Task ShowCustomValueDialogAsync(string tweakId)
    {
        if (!CanWriteAction())
        {
            return;
        }

        ContentDialog dialog = new()
        {
            XamlRoot = XamlRoot,
            Title = "Set custom DWORD value",
            PrimaryButtonText = "Apply",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary
        };
        TextBox box = new()
        {
            PlaceholderText = "0x00000000",
            Text = "0x00000000"
        };
        dialog.Content = box;

        ContentDialogResult response = await dialog.ShowAsync();
        if (response != ContentDialogResult.Primary)
        {
            return;
        }

        string text = box.Text.Trim();
        if (!TryParseHex(text, out uint value))
        {
            ViewModel.ShowResult("Invalid value", "Enter a valid hexadecimal DWORD value (for example 0x20).", InfoBarSeverity.Warning);
            return;
        }

        await RunAsync(async () =>
        {
            NvidiaActionResult result = await _service.SetItemValueAsync(null, tweakId, $"0x{value:X8}");
            await RefreshSingleAsync(tweakId);
            ViewModel.ShowResult(result.Success ? "Custom value applied" : "Custom value failed", result.Message, result.Success ? InfoBarSeverity.Success : InfoBarSeverity.Error);
        });
    }

    private async Task OpenInRegeditAsync(string tweakId)
    {
        (bool success, string message, IReadOnlyList<NvidiaResolvedTarget> targets) = await _service.ResolveTargetsAsync(null, tweakId);
        if (!success)
        {
            ViewModel.ShowResult("Unavailable", message, InfoBarSeverity.Warning);
            return;
        }
        NvidiaResolvedTarget? target = targets.FirstOrDefault();
        if (target is null)
        {
            ViewModel.ShowResult("Unavailable", "No registry target was resolved for this tweak.", InfoBarSeverity.Warning);
            return;
        }

        string root = target.Hive == RegistryHive.LocalMachine ? "HKEY_LOCAL_MACHINE" : "HKEY_CURRENT_USER";
        string keyPath = $"{root}\\{target.Path}";
        try
        {
            using Microsoft.Win32.RegistryKey regedit = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Applets\Regedit",
                true)!;
            regedit.SetValue("LastKey", keyPath, Microsoft.Win32.RegistryValueKind.String);
            Process.Start(new ProcessStartInfo("regedit.exe") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ViewModel.ShowResult("Regedit launch failed", ex.Message, InfoBarSeverity.Error);
        }
    }

    private async Task CopyRegistryPathAsync(string tweakId)
    {
        (bool success, string message, IReadOnlyList<NvidiaResolvedTarget> targets) = await _service.ResolveTargetsAsync(null, tweakId);
        if (!success)
        {
            ViewModel.ShowResult("Unavailable", message, InfoBarSeverity.Warning);
            return;
        }
        if (targets.Count == 0)
        {
            ViewModel.ShowResult("Unavailable", "No registry target was resolved for this tweak.", InfoBarSeverity.Warning);
            return;
        }

        string text = string.Join(Environment.NewLine, targets.Select(t =>
            $"{(t.Hive == RegistryHive.LocalMachine ? "HKLM" : "HKCU")}\\{t.Path}\\{t.ValueName}"));
        DataPackage package = new();
        package.SetText(text);
        Clipboard.SetContent(package);
        ViewModel.ShowResult("Copied", "Registry path copied to clipboard.", InfoBarSeverity.Success);
    }

    private bool CanWriteAction()
    {
        if (ViewModel.IsBusy)
        {
            return false;
        }

        if (ViewModel.IsElevated)
        {
            return true;
        }

        ViewModel.ShowResult(
            "Administrator required",
            "Run SynToolkit as administrator to apply or revert NVIDIA tweaks.",
            InfoBarSeverity.Warning);
        return false;
    }

}
