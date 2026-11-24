#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SynToolkit.Services;
using SynToolkit.Utils;

namespace SynToolkit.Views
{
    internal sealed class WallpaperItem
    {
        public required string FilePath { get; init; }
        public required string DisplayName { get; init; }
        public bool IsAddTile { get; init; }
        public bool IsDeletable { get; init; }
        public Microsoft.UI.Xaml.Media.ImageSource? ThumbnailSource { get; init; }
        public bool IsSelected { get; set; }
        public Microsoft.UI.Xaml.Media.Brush BorderBrush => IsSelected
            ? (Microsoft.UI.Xaml.Media.Brush)Microsoft.UI.Xaml.Application.Current.Resources["AccentFillColorDefaultBrush"]
            : (Microsoft.UI.Xaml.Media.Brush)Microsoft.UI.Xaml.Application.Current.Resources["ControlStrokeColorDefaultBrush"];
        public Thickness BorderThickness => IsSelected ? new Thickness(2) : new Thickness(1);
    }

    public sealed partial class AdjustmentsPage : Page
    {
        private sealed record WallpaperFitSelection(string Name, WallpaperFitMode FitMode);

        private const string ImageFileFilter = "Image files (*.jpg;*.jpeg;*.png;*.bmp)|*.jpg;*.jpeg;*.png;*.bmp";

        private bool _isPageLoaded;
        private bool _hasLoadedWallpapers;
        private string? _currentWallpaperPath;

        public AdjustmentsPage()
        {
            InitializeComponent();
            NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Disabled;
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            _isPageLoaded = true;
            bool isElevated = IsCurrentProcessElevated();
            ElevationInfoBar.IsOpen = !isElevated;
            SetActionsEnabled(isElevated);
            if (!_hasLoadedWallpapers)
            {
                LoadWallpapers();
            }
        }

        private void Page_Unloaded(object sender, RoutedEventArgs e)
        {
            _isPageLoaded = false;

        }

        private void SetActionsEnabled(bool enabled)
        {
            ChangePasswordButton.IsEnabled = enabled;
            ChangeDisplayNameButton.IsEnabled = enabled;
            ChangeAdminPasswordButton.IsEnabled = enabled;
            ChangeProfilePictureButton.IsEnabled = enabled;
            ChangeLockscreenImageButton.IsEnabled = enabled;
            AddKeyboardLanguageButton.IsEnabled = enabled;
            RemoveKeyboardLanguageButton.IsEnabled = enabled;
        }

        private static bool IsCurrentProcessElevated()
        {
            try
            {
                using WindowsIdentity identity = WindowsIdentity.GetCurrent();
                return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch (Exception exception)
            {
                App.logger.Error(exception, "[Adjustments] Unable to determine whether SynToolkit is running elevated.");
                return false;
            }
        }

        private async void ChangePasswordButton_Click(object sender, RoutedEventArgs e)
        {
            string[]? values = await ShowFormDialogAsync("Change password", "Change", ("New password", true));
            if (values is null)
            {
                return;
            }

            await RunActionAsync(
                () => UserIdentityService.ChangePassword(values[0]),
                "Password changed.");
        }

        private async void ChangeDisplayNameButton_Click(object sender, RoutedEventArgs e)
        {
            string[]? values = await ShowFormDialogAsync("Change display name", "Change", ("New display name", false));
            if (values is null)
            {
                return;
            }

            await RunActionAsync(
                () => UserIdentityService.ChangeDisplayName(values[0]),
                "Display name changed.");
        }

        private async void ChangeAdminPasswordButton_Click(object sender, RoutedEventArgs e)
        {
            string[]? values = await ShowFormDialogAsync("Change Administrator password", "Change", ("New Administrator password", true));
            if (values is null)
            {
                return;
            }

            await RunActionAsync(
                () => UserIdentityService.ChangeAdministratorPassword(values[0]),
                "Administrator password changed.");
        }

        private async void ChangeProfilePictureButton_Click(object sender, RoutedEventArgs e)
        {
            string? filePath = ShowImagePicker();
            if (filePath is null)
            {
                return;
            }

            await RunActionAsync(
                async () =>
                {
                    string? sid = WindowsIdentity.GetCurrent().User?.Value;
                    if (string.IsNullOrEmpty(sid))
                    {
                        throw new InvalidOperationException("Unable to resolve the signed-in user's SID.");
                    }

                    string profileFolder = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                    await ProfileImageService.SetProfilePictureAsync(filePath, sid, profileFolder);
                },
                "Profile picture changed.");
        }

        private async void ChangeLockscreenImageButton_Click(object sender, RoutedEventArgs e)
        {
            string? filePath = ShowImagePicker();
            if (filePath is null)
            {
                return;
            }

            WallpaperFitSelection? fitSelection = await ShowWallpaperFitDialogAsync(filePath, includeName: false, isLockscreen: true);
            if (fitSelection is null)
            {
                return;
            }

            ContentDialog blurDialog = new()
            {
                XamlRoot = XamlRoot,
                Style = Microsoft.UI.Xaml.Application.Current.Resources["DefaultContentDialogStyle"] as Style,
                Title = "Remove lock screen blur?",
                Content = "Windows applies an acrylic blur effect over the lock screen image by default.",
                PrimaryButtonText = "Remove blur",
                SecondaryButtonText = "Keep blur",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary
            };
            ContentDialogResult blurResult = await blurDialog.ShowAsync();
            if (blurResult != ContentDialogResult.Primary && blurResult != ContentDialogResult.Secondary)
            {
                return;
            }
            bool removeBlur = blurResult == ContentDialogResult.Primary;

            await RunActionAsync(
                async () =>
                {
                    await LockscreenImageService.SetLockscreenImageAsync(filePath, removeBlur, fitSelection.FitMode);
                },
                "Lock screen image changed.");
        }

        private async void AddKeyboardLanguageButton_Click(object sender, RoutedEventArgs e)
        {
            string[]? values = await ShowFormDialogAsync(
                "Add keyboard language",
                "Add",
                ("Language tag:keyboard ID, e.g. en-US:00000409", false));
            if (values is null)
            {
                return;
            }

            ContentDialog defaultDialog = new()
            {
                XamlRoot = XamlRoot,
                Style = Microsoft.UI.Xaml.Application.Current.Resources["DefaultContentDialogStyle"] as Style,
                Title = "Set as default input method?",
                PrimaryButtonText = "Set as default",
                SecondaryButtonText = "Don't set as default",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Secondary
            };
            ContentDialogResult defaultResult = await defaultDialog.ShowAsync();
            if (defaultResult != ContentDialogResult.Primary && defaultResult != ContentDialogResult.Secondary)
            {
                return;
            }
            bool setAsDefault = defaultResult == ContentDialogResult.Primary;

            await RunActionAsync(
                () => KeyboardLanguageService.AddKeyboardLanguage(values[0].Trim(), setAsDefault),
                "Keyboard language added.");
        }

        private async void RemoveKeyboardLanguageButton_Click(object sender, RoutedEventArgs e)
        {
            string[]? values = await ShowFormDialogAsync(
                "Remove keyboard language",
                "Remove",
                ("Language tag:keyboard ID, e.g. en-US:00000409", false));
            if (values is null)
            {
                return;
            }

            await RunActionAsync(
                () => KeyboardLanguageService.RemoveKeyboardLanguage(values[0].Trim()),
                "Keyboard language removed.");
        }

        private string? ShowImagePicker()
        {
            if (App.m_window is null)
            {
                throw new InvalidOperationException("SynToolkit has no active window to host the file picker.");
            }

            IntPtr windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(App.m_window);
            if (windowHandle == IntPtr.Zero)
            {
                throw new InvalidOperationException("Unable to get a window handle for the file picker.");
            }

            return NativeFileDialogHelper.ShowOpenFileDialog(windowHandle, ImageFileFilter);
        }

        private async Task<string[]?> ShowFormDialogAsync(string title, string primaryButtonText, params (string Label, bool IsPassword)[] fields)
        {
            StackPanel panel = new() { Spacing = 10 };
            Control[] inputs = new Control[fields.Length];
            for (int i = 0; i < fields.Length; i++)
            {
                panel.Children.Add(new TextBlock { Text = fields[i].Label, TextWrapping = TextWrapping.Wrap });
                if (fields[i].IsPassword)
                {
                    PasswordBox box = new();
                    inputs[i] = box;
                    panel.Children.Add(box);
                }
                else
                {
                    TextBox box = new();
                    inputs[i] = box;
                    panel.Children.Add(box);
                }
            }

            ContentDialog dialog = new()
            {
                XamlRoot = XamlRoot,
                Style = Microsoft.UI.Xaml.Application.Current.Resources["DefaultContentDialogStyle"] as Style,
                Title = title,
                Content = panel,
                PrimaryButtonText = primaryButtonText,
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary
            };

            ContentDialogResult result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary)
            {
                return null;
            }

            return inputs.Select(control => control is PasswordBox passwordBox ? passwordBox.Password : ((TextBox)control).Text).ToArray();
        }

        private async Task<WallpaperFitSelection?> ShowWallpaperFitDialogAsync(string wallpaperPath, bool includeName = true, bool isLockscreen = false)
        {
            bool isCustomWallpaper = WindowsWallpaperService.IsCustomWallpaperPath(wallpaperPath);
            TextBox? nameBox = includeName
                ? new TextBox
            {
                Text = GetWallpaperDialogName(wallpaperPath),
                IsReadOnly = !isCustomWallpaper,
                HorizontalAlignment = HorizontalAlignment.Stretch
            }
                : null;

            ComboBox fitPicker = new()
            {
                ItemsSource = Enum.GetNames<WallpaperFitMode>(),
                SelectedIndex = (int)WindowsWallpaperService.GetCurrentFitMode(),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            StackPanel panel = new() { Spacing = 10 };
            if (nameBox is not null)
            {
                panel.Children.Add(new TextBlock { Text = "Wallpaper Name" });
                panel.Children.Add(nameBox);
            }
            panel.Children.Add(new TextBlock
            {
                Text = $"Choose a fit for your {(isLockscreen ? "lock screen" : "desktop")} image",
                TextWrapping = TextWrapping.Wrap
            });
            panel.Children.Add(fitPicker);

            ContentDialog dialog = new()
            {
                XamlRoot = XamlRoot,
                Style = Microsoft.UI.Xaml.Application.Current.Resources["DefaultContentDialogStyle"] as Style,
                Title = isLockscreen ? "Lock screen image fit" : "Desktop image fit",
                Content = panel,
                PrimaryButtonText = "Confirm",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary
            };

            ContentDialogResult result = await dialog.ShowAsync();
            return result == ContentDialogResult.Primary &&
                fitPicker.SelectedIndex >= 0 &&
                (nameBox is null || !string.IsNullOrWhiteSpace(nameBox.Text))
                ? new WallpaperFitSelection(nameBox?.Text.Trim() ?? string.Empty, (WallpaperFitMode)fitPicker.SelectedIndex)
                : null;
        }

        private static string GetWallpaperDialogName(string wallpaperPath)
        {
            if (!WindowsWallpaperService.IsCustomWallpaperPath(wallpaperPath))
            {
                return WindowsWallpaperService.GetDisplayName(wallpaperPath);
            }

            string displayName = WindowsWallpaperService.GetDisplayName(wallpaperPath);
            if (!string.Equals(displayName, "Custom wallpaper", StringComparison.OrdinalIgnoreCase))
            {
                return displayName;
            }

            IReadOnlyList<string> customWallpapers = WindowsWallpaperService.GetCustomWallpapers();
            int index = customWallpapers
                .Select((path, itemIndex) => new { path, itemIndex })
                .FirstOrDefault(item => string.Equals(item.path, wallpaperPath, StringComparison.OrdinalIgnoreCase))?
                .itemIndex ?? customWallpapers.Count;

            return $"Custom Wallpaper ({index + 1})";
        }

        private async Task RunActionAsync(Action action, string successMessage) =>
            await RunActionAsync(() => { action(); return Task.CompletedTask; }, successMessage);

        private async Task RunActionAsync(Func<Task> action, string successMessage)
        {
            SetActionsEnabled(false);
            OperationInfoBar.IsOpen = false;
            try
            {
                await Task.Run(action);
                if (_isPageLoaded)
                {
                    ShowResult("Done", successMessage, InfoBarSeverity.Success);
                }
            }
            catch (Exception exception)
            {
                App.logger.Error(exception, "[Adjustments] An action failed.");
                if (_isPageLoaded)
                {
                    ShowResult("Action failed", exception.Message, InfoBarSeverity.Error);
                }
            }
            finally
            {
                if (_isPageLoaded)
                {
                    SetActionsEnabled(IsCurrentProcessElevated());
                }
            }
        }

        private void ShowResult(string title, string message, InfoBarSeverity severity)
        {
            OperationInfoBar.Title = title;
            OperationInfoBar.Message = message;
            OperationInfoBar.Severity = severity;
            OperationInfoBar.IsOpen = true;
        }

        private void LoadWallpapers()
        {
            WindowsWallpaperService.EnsureCustomWallpapersDirectory();
            _currentWallpaperPath = WindowsWallpaperService.GetCurrentWallpaper();
            UpdateCurrentWallpaperPreview();

            List<WallpaperItem> wallpaperItems = WindowsWallpaperService.GetAvailableWallpapers()
                .Select(path => CreateWallpaperItem(path, isDeletable: false))
                .ToList();

            WallpaperGridView.ItemsSource = wallpaperItems;
            WallpaperEmptyMessage.Visibility = wallpaperItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            WallpaperGridView.Visibility = wallpaperItems.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

            RefreshCustomWallpaperGrid();
            UpdateRestorePreviousButtonState();
            _hasLoadedWallpapers = true;
        }

        private void RefreshCustomWallpaperGrid()
        {
            List<WallpaperItem> customItems = WindowsWallpaperService.GetCustomWallpapers()
                .Select(path => CreateWallpaperItem(path, isDeletable: true))
                .ToList();
            customItems.Add(new WallpaperItem
            {
                FilePath = string.Empty,
                DisplayName = "Add wallpaper",
                IsAddTile = true,
                IsDeletable = false
            });
            CustomWallpaperGridView.ItemsSource = customItems;
        }

        private WallpaperItem CreateWallpaperItem(string path, bool isDeletable) => new()
        {
            FilePath = path,
            DisplayName = WindowsWallpaperService.GetDisplayName(path),
            ThumbnailSource = CreateThumbnail(path, decodePixelWidth: 320),
            IsSelected = IsCurrentWallpaper(path),
            IsDeletable = isDeletable
        };

        private static Microsoft.UI.Xaml.Media.ImageSource? CreateThumbnail(string path, int decodePixelWidth)
        {
            try
            {
                Microsoft.UI.Xaml.Media.Imaging.BitmapImage thumbnail = new()
                {
                    DecodePixelWidth = decodePixelWidth,
                    UriSource = new Uri(Path.GetFullPath(path))
                };
                return thumbnail;
            }
            catch
            {
                return null;
            }
        }

        private void UpdateCurrentWallpaperPreview()
        {
            CurrentWallpaperName.Text = WindowsWallpaperService.GetCurrentWallpaperTitle(_currentWallpaperPath);
            CurrentWallpaperPath.Text = WindowsWallpaperService.GetCurrentWallpaperSubtitle(_currentWallpaperPath);

            // Only show a real thumbnail for known bundled/custom files. Unmatched
            // Windows wallpapers (slideshow, solid color, transcoded cache) use the
            // placeholder so the box is never a blank gray rectangle.
            string? knownPath = WindowsWallpaperService.FindKnownWallpaperPath(_currentWallpaperPath);
            bool showThumbnail = TrySetPreviewImage(knownPath);
            CurrentWallpaperPlaceholder.Visibility = showThumbnail ? Visibility.Collapsed : Visibility.Visible;
            CurrentWallpaperImageHost.Visibility = showThumbnail ? Visibility.Visible : Visibility.Collapsed;
        }

        private bool TrySetPreviewImage(string? path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                CurrentWallpaperPreview.ImageSource = null;
                return false;
            }

            try
            {
                CurrentWallpaperPreview.ImageSource = CreateThumbnail(path, decodePixelWidth: 400);
                if (CurrentWallpaperPreview.ImageSource is null)
                {
                    return false;
                }
                return true;
            }
            catch
            {
                CurrentWallpaperPreview.ImageSource = null;
                return false;
            }
        }

        private void UpdateRestorePreviousButtonState()
        {
            RestorePreviousButton.IsEnabled = WindowsWallpaperService.CanRestorePreviousWallpaper(_currentWallpaperPath);
        }

        private bool IsCurrentWallpaper(string path)
        {
            if (string.IsNullOrEmpty(_currentWallpaperPath) || string.IsNullOrEmpty(path))
                return false;

            string? knownCurrent = WindowsWallpaperService.FindKnownWallpaperPath(_currentWallpaperPath);
            string compareAgainst = knownCurrent ?? _currentWallpaperPath;

            try
            {
                return string.Equals(
                    Path.GetFullPath(path),
                    Path.GetFullPath(compareAgainst),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private void RefreshWallpapersButton_Click(object sender, RoutedEventArgs e)
        {
            LoadWallpapers();
        }

        private async void RestorePreviousButton_Click(object sender, RoutedEventArgs e)
        {
            string? previousPath = WindowsWallpaperService.GetPreviousWallpaperPath();
            if (string.IsNullOrEmpty(previousPath) || !File.Exists(previousPath))
            {
                ShowResult("Restore failed", "Previous wallpaper is no longer available.", InfoBarSeverity.Error);
                UpdateRestorePreviousButtonState();
                return;
            }

            await ApplyWallpaperAsync(previousPath, "Restored your previous wallpaper.", promptForFit: false);
        }

        private async void WallpaperGridView_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is not WallpaperItem item || item.IsAddTile)
                return;

            await ApplyWallpaperAsync(item.FilePath);
        }

        private async void CustomWallpaperGridView_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is not WallpaperItem item || item.IsAddTile)
                return;

            await ApplyWallpaperAsync(item.FilePath);
        }

        private async void AddCustomWallpaperButton_Click(object sender, RoutedEventArgs e)
        {
            await AddCustomWallpaperAsync();
        }

        private async void DeleteCustomWallpaperButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.DataContext is not WallpaperItem item || !item.IsDeletable)
            {
                return;
            }

            ContentDialog confirmation = new()
            {
                XamlRoot = XamlRoot,
                Style = Microsoft.UI.Xaml.Application.Current.Resources["DefaultContentDialogStyle"] as Style,
                Title = "Remove wallpaper?",
                Content = $"Remove \"{item.DisplayName}\" from Your Wallpapers? This deletes the imported file from SynToolkit. Your current desktop wallpaper will not change.",
                PrimaryButtonText = "Remove",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close
            };

            ContentDialogResult result;
            try
            {
                result = await confirmation.ShowAsync();
            }
            catch (Exception exception)
            {
                App.logger.Error(exception, "[Adjustments] The remove-wallpaper confirmation could not be displayed.");
                ShowResult("Confirmation unavailable", exception.Message, InfoBarSeverity.Error);
                return;
            }

            if (result != ContentDialogResult.Primary)
            {
                return;
            }

            try
            {
                bool deletedWasApplied = IsCurrentWallpaper(item.FilePath);
                WallpaperApplyResult deleteResult = await Task.Run(() => WindowsWallpaperService.DeleteCustomWallpaper(item.FilePath));
                RefreshCustomWallpaperGrid();
                UpdateRestorePreviousButtonState();

                if (deletedWasApplied)
                {
                    UpdateCurrentWallpaperPreview();
                }

                if (deleteResult.Success)
                {
                    ShowResult("Done", deleteResult.Message, InfoBarSeverity.Success);
                }
                else
                {
                    ShowResult("Couldn't remove wallpaper", deleteResult.Message, InfoBarSeverity.Error);
                }
            }
            catch (Exception exception)
            {
                App.logger.Error(exception, "[Adjustments] Custom wallpaper delete failed.");
                ShowResult("Couldn't remove wallpaper", exception.Message, InfoBarSeverity.Error);
            }
        }

        private async Task AddCustomWallpaperAsync()
        {
            // Finish the click/ItemClick pass before showing a modal dialog.
            // Opening WinForms OpenFileDialog synchronously inside a GridView
            // click can return without a path, and the dialog itself can fire
            // Page.Unloaded (clearing _isPageLoaded) so UI updates get skipped.
            await Task.Yield();

            string? filePath;
            try
            {
                filePath = ShowImagePicker();
            }
            catch (Exception exception)
            {
                App.logger.Error(exception, "[Adjustments] Custom wallpaper picker failed.");
                ShowResult("Couldn't add wallpaper", exception.Message, InfoBarSeverity.Error);
                return;
            }

            if (string.IsNullOrWhiteSpace(filePath))
            {
                return;
            }

            SetActionsEnabled(false);
            RestorePreviousButton.IsEnabled = false;
            OperationInfoBar.IsOpen = false;

            try
            {
                WallpaperImportResult result = await Task.Run(() => WindowsWallpaperService.ImportCustomWallpaper(filePath));

                LoadWallpapers();
                if (!result.Success || string.IsNullOrEmpty(result.ImportedPath))
                {
                    ShowResult("Couldn't add wallpaper", result.Message, InfoBarSeverity.Error);
                    return;
                }

                string displayName = WindowsWallpaperService.GetDisplayName(result.ImportedPath);
                await ApplyWallpaperAsync(
                    result.ImportedPath,
                    successMessage: $"{displayName} added and applied as your wallpaper.");
            }
            catch (Exception exception)
            {
                App.logger.Error(exception, "[Adjustments] Custom wallpaper import failed.");
                ShowResult("Couldn't add wallpaper", exception.Message, InfoBarSeverity.Error);
            }
            finally
            {
                SetActionsEnabled(IsCurrentProcessElevated());
                UpdateRestorePreviousButtonState();
            }
        }

        private async Task ApplyWallpaperAsync(string wallpaperPath, string? successMessage = null, bool promptForFit = true)
        {
            WallpaperFitMode fitMode;
            string pathToApply = wallpaperPath;
            if (promptForFit)
            {
                WallpaperFitSelection? selection = await ShowWallpaperFitDialogAsync(wallpaperPath);
                if (selection is null)
                {
                    return;
                }

                fitMode = selection.FitMode;
                if (WindowsWallpaperService.IsCustomWallpaperPath(wallpaperPath))
                {
                    WallpaperRenameResult renameResult = await Task.Run(() =>
                        WindowsWallpaperService.RenameCustomWallpaper(wallpaperPath, selection.Name));
                    if (!renameResult.Success || string.IsNullOrWhiteSpace(renameResult.RenamedPath))
                    {
                        ShowResult("Wallpaper name unavailable", renameResult.Message, InfoBarSeverity.Error);
                        return;
                    }

                    pathToApply = renameResult.RenamedPath;
                    RefreshCustomWallpaperGrid();
                }
            }
            else
            {
                fitMode = WindowsWallpaperService.GetCurrentFitMode();
            }

            SetActionsEnabled(false);
            RestorePreviousButton.IsEnabled = false;
            OperationInfoBar.IsOpen = false;

            try
            {
                WallpaperApplyResult result = await Task.Run(() => WindowsWallpaperService.Apply(pathToApply, fitMode));

                if (result.Success)
                {
                    _currentWallpaperPath = pathToApply;
                    UpdateCurrentWallpaperPreview();
                    RefreshCustomWallpaperGrid();
                    UpdateWallpaperActiveStates();
                    UpdateRestorePreviousButtonState();
                    string message = successMessage is not null && WindowsWallpaperService.IsCustomWallpaperPath(pathToApply)
                        ? $"{WindowsWallpaperService.GetDisplayName(pathToApply)} added and applied as your wallpaper."
                        : successMessage ?? result.Message;
                    ShowResult("Done", message, InfoBarSeverity.Success);
                }
                else
                {
                    ShowResult("Wallpaper change failed", result.Message, InfoBarSeverity.Error);
                }
            }
            catch (Exception exception)
            {
                App.logger.Error(exception, "[Adjustments] Wallpaper apply failed.");
                ShowResult("Wallpaper change failed", exception.Message, InfoBarSeverity.Error);
            }
            finally
            {
                SetActionsEnabled(IsCurrentProcessElevated());
                UpdateRestorePreviousButtonState();
            }
        }

        private void UpdateWallpaperActiveStates()
        {
            RefreshGridSelection(WallpaperGridView);
            RefreshGridSelection(CustomWallpaperGridView);
        }

        private void RefreshGridSelection(GridView gridView)
        {
            if (gridView.ItemsSource is not List<WallpaperItem> items)
                return;

            List<WallpaperItem> updated = items
                .Select(item => item.IsAddTile
                    ? item
                    : new WallpaperItem
                    {
                        FilePath = item.FilePath,
                        DisplayName = item.DisplayName,
                        ThumbnailSource = item.ThumbnailSource,
                        IsSelected = IsCurrentWallpaper(item.FilePath),
                        IsDeletable = item.IsDeletable
                    })
                .ToList();

            gridView.ItemsSource = null;
            gridView.ItemsSource = updated;
        }

        private void WallpaperBox_PointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (sender is Grid grid)
            {
                var deleteBtn = grid.Children.OfType<Button>().FirstOrDefault(b => b.Name == "DeleteCustomWallpaperButton");
                if (deleteBtn != null)
                {
                    deleteBtn.Opacity = 1;
                }
            }
        }

        private void WallpaperBox_PointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (sender is Grid grid)
            {
                var deleteBtn = grid.Children.OfType<Button>().FirstOrDefault(b => b.Name == "DeleteCustomWallpaperButton");
                if (deleteBtn != null)
                {
                    deleteBtn.Opacity = 0;
                }
            }
        }
    }
}
