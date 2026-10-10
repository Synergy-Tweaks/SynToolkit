#nullable enable

using SynToolkit.Services.Win32Priority;
using SynToolkit.Stores;
using SynToolkit.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SynToolkit.Services.ConfigurationServices
{
    /// <summary>
    /// Foreground App Boost dropdown for Win32PrioritySeparation (ControlSet001).
    /// </summary>
    internal sealed class ForegroundAppBoostConfigurationService :
        IMultiOptionConfigurationServices,
        IPromptingMultiOptionConfigurationService
    {
        private const string KeyPath = @"HKLM\SYSTEM\ControlSet001\Control\PriorityControl";
        private const string ValueName = "Win32PrioritySeparation";

        private readonly MultiOptionConfigurationStore _store;
        private readonly List<string> _options;

        public ForegroundAppBoostConfigurationService(
            [FromKeyedServices("ProgramPriority")] MultiOptionConfigurationStore store)
        {
            Win32PrioritySeparationValues.EnsurePresetValuesUnique();

            _store = store;
            _options = new List<string>(Win32PrioritySeparationValues.DropdownLabels);
            _store.Options = _options;
        }

        public bool IsCustomPromptOption(int statusIndex) =>
            statusIndex == Win32PrioritySeparationValues.CustomOptionIndex
            || (statusIndex >= 0
                && statusIndex < _options.Count
                && Win32PrioritySeparationValues.IsCustomLabel(_options[statusIndex]));

        public void ChangeStatus(int status)
        {
            if (IsCustomPromptOption(status))
            {
                throw new InvalidOperationException(
                    "Custom Value must be applied through the custom-value dialog.");
            }

            if (status < 0 || status >= Win32PrioritySeparationValues.Presets.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(status));
            }

            int target = Win32PrioritySeparationValues.Presets[status].Value;
            ApplyRawValue(target);
        }

        public string Status()
        {
            Win32PrioritySeparationValues.DetectionResult detection = DetectFromRegistry();
            SyncCustomOptionLabel(detection);
            _store.CurrentSetting = detection.PresetLabel;
            return detection.PresetLabel;
        }

        public string GetStatusWarning() => DetectFromRegistry().Warning ?? string.Empty;

        public Win32PrioritySeparationValues.DetectionResult DetectFromRegistry()
        {
            object? raw = null;
            try
            {
                raw = RegistryHelper.GetValue(KeyPath, ValueName);
            }
            catch (Exception exception)
            {
                App.logger.Warn(exception, "[ForegroundAppBoost] Unable to read Win32PrioritySeparation.");
            }

            return Win32PrioritySeparationValues.DetectCurrentState(raw);
        }

        public void ApplyRawValue(int value)
        {
            Win32PrioritySeparationValues.DetectionResult before = DetectFromRegistry();
            if (before.RawValue == value
                && before.Kind is not Win32PrioritySeparationValues.DetectionKind.Unsupported)
            {
                // Already matches; still sync UI labels.
                SyncCustomOptionLabel(before);
                _store.CurrentSetting = before.PresetLabel;
                return;
            }

            RegistryHelper.SetValue(KeyPath, ValueName, value, RegistryValueKind.DWord);

            object? readBack = RegistryHelper.GetValue(KeyPath, ValueName);
            Win32PrioritySeparationValues.DetectionResult after =
                Win32PrioritySeparationValues.DetectCurrentState(readBack);

            if (after.RawValue != value)
            {
                throw new InvalidOperationException(
                    "Win32PrioritySeparation write could not be verified. The registry value did not match what was written.");
            }

            SyncCustomOptionLabel(after);
            _store.CurrentSetting = after.PresetLabel;
            App.ReportConfigurationActionSuccess(
                "Foreground App Boost updated. New processes pick this up immediately; a sign-out can help stubborn apps.");
        }

        public async Task<int?> PromptCustomValueAsync()
        {
            Win32PrioritySeparationValues.DetectionResult detection = DetectFromRegistry();
            int prefill = Win32PrioritySeparationValues.PrefillCustomDialogValue(detection);

            DispatcherQueue? queue = App.m_window?.DispatcherQueue ?? DispatcherQueue.GetForCurrentThread();
            if (queue is null || queue.HasThreadAccess)
            {
                return await ShowCustomValueDialogAsync(prefill);
            }

            TaskCompletionSource<int?> completion = new();
            if (!queue.TryEnqueue(async () =>
                {
                    try
                    {
                        completion.TrySetResult(await ShowCustomValueDialogAsync(prefill));
                    }
                    catch (Exception exception)
                    {
                        completion.TrySetException(exception);
                    }
                }))
            {
                throw new InvalidOperationException("Unable to open the custom value dialog on the UI thread.");
            }

            return await completion.Task;
        }

        private void SyncCustomOptionLabel(Win32PrioritySeparationValues.DetectionResult detection)
        {
            for (int i = 0; i < Win32PrioritySeparationValues.DropdownLabels.Count; i++)
            {
                if (i >= _options.Count)
                {
                    _options.Add(Win32PrioritySeparationValues.DropdownLabels[i]);
                }
                else
                {
                    _options[i] = Win32PrioritySeparationValues.DropdownLabels[i];
                }
            }

            if ((detection.Kind is Win32PrioritySeparationValues.DetectionKind.Custom
                    or Win32PrioritySeparationValues.DetectionKind.Unsupported)
                && detection.RawValue is int raw)
            {
                _options[Win32PrioritySeparationValues.CustomOptionIndex] =
                    Win32PrioritySeparationValues.FormatCustomLabel(raw);
            }
        }

        private static async Task<int?> ShowCustomValueDialogAsync(int prefill)
        {
            TextBox input = new()
            {
                Text = prefill.ToString(System.Globalization.CultureInfo.InvariantCulture),
                PlaceholderText = "Try 36",
            };

            TextBlock validation = new()
            {
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 80, 80)),
                TextWrapping = TextWrapping.Wrap,
                Visibility = Visibility.Collapsed,
                Margin = new Thickness(0, 8, 0, 0),
            };

            TextBlock helper = new()
            {
                Text = "Updates the Win32PrioritySeparation DWORD directly. Switch back to Default if responsiveness gets worse.",
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.8,
                Margin = new Thickness(0, 0, 0, 8),
            };

            TextBlock context = new()
            {
                Text = Win32PrioritySeparationValues.BuildCustomAllowedValuesHelperText(),
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.8,
                Margin = new Thickness(0, 0, 0, 12),
            };

            void RefreshValidation()
            {
                bool valid = Win32PrioritySeparationValues.TryValidateCustomInput(
                    input.Text,
                    out _,
                    out string? error);
                validation.Text = error ?? string.Empty;
                validation.Visibility = valid ? Visibility.Collapsed : Visibility.Visible;
            }

            input.TextChanged += (_, _) => RefreshValidation();
            RefreshValidation();

            StackPanel panel = new() { Spacing = 4 };
            panel.Children.Add(helper);
            panel.Children.Add(context);
            panel.Children.Add(input);
            panel.Children.Add(validation);

            ContentDialog dialog = new()
            {
                Title = "Custom Foreground App Boost",
                Content = panel,
                PrimaryButtonText = "Apply",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = App.XamlRoot,
                Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
            };

            dialog.PrimaryButtonClick += (sender, args) =>
            {
                if (!Win32PrioritySeparationValues.TryValidateCustomInput(
                        input.Text,
                        out _,
                        out string? error))
                {
                    args.Cancel = true;
                    validation.Text = error ?? "Invalid value.";
                    validation.Visibility = Visibility.Visible;
                }
            };

            ContentDialogResult result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary)
            {
                return null;
            }

            if (!Win32PrioritySeparationValues.TryValidateCustomInput(input.Text, out int value, out _))
            {
                return null;
            }

            return value;
        }
    }
}
