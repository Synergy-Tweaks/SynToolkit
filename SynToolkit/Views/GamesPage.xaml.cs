#nullable enable

using System;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using SynToolkit.Utils;
using SynToolkit.ViewModels;
using WinRT.Interop;

namespace SynToolkit.Views
{
    public sealed partial class GamesPage : Page
    {
        private const string ExeFileFilter = "Executables (*.exe)|*.exe|Shortcuts (*.lnk)|*.lnk|All files (*.*)|*.*";
        private const string ImageFileFilter =
            "Images (*.png;*.jpg;*.jpeg;*.webp;*.bmp)|*.png;*.jpg;*.jpeg;*.webp;*.bmp|All files (*.*)|*.*";

        public GamesPageViewModel ViewModel { get; }

        public GamesPage()
        {
            InitializeComponent();
            ViewModel = App._host.Services.GetRequiredService<GamesPageViewModel>();
            DataContext = ViewModel;
            ViewModel.PropertyChanged += ViewModel_PropertyChanged;
            UpdateViewModeToggleChrome();
        }

        private async void Page_Loaded(object sender, RoutedEventArgs e)
        {
            await ViewModel.LoadAsync();
            UpdateViewModeToggleChrome();
        }

        private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(GamesPageViewModel.IsGridView) or nameof(GamesPageViewModel.IsListView))
            {
                UpdateViewModeToggleChrome();
            }
        }

        private void UpdateViewModeToggleChrome()
        {
            Brush accent = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"];
            Brush transparent = new SolidColorBrush(Microsoft.UI.Colors.Transparent);

            ListViewButton.Background = ViewModel.IsListView ? accent : transparent;
            GridViewButton.Background = ViewModel.IsGridView ? accent : transparent;
        }

        private void ListViewButton_Click(object sender, RoutedEventArgs e) => ViewModel.IsGridView = false;

        private void GridViewButton_Click(object sender, RoutedEventArgs e) => ViewModel.IsGridView = true;

        private async void ScanButton_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel.ScanCommand.CanExecute(null))
            {
                await ViewModel.ScanCommand.ExecuteAsync(null);
            }
        }

        private void GameTile_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            if (sender is FrameworkElement root && root.FindName("PlayOverlay") is UIElement overlay)
            {
                overlay.Opacity = 1;
            }
        }

        private void GameTile_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            if (sender is FrameworkElement root && root.FindName("PlayOverlay") is UIElement overlay)
            {
                overlay.Opacity = 0;
            }
        }

        private async void AddManualButton_Click(object sender, RoutedEventArgs e)
        {
            IntPtr hwnd = TryGetWindowHandle();

            string? path = NativeFileDialogHelper.ShowOpenFileDialog(hwnd, ExeFileFilter);
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            if (path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
            {
                string? resolved = TryResolveShortcut(path);
                if (string.IsNullOrWhiteSpace(resolved))
                {
                    ViewModel.HasError = true;
                    ViewModel.StatusMessage = App.GetValueFromItemList("GamesPage_ShortcutResolveFailed");
                    return;
                }

                path = resolved;
            }

            string defaultName = Path.GetFileNameWithoutExtension(path);
            var nameBox = new TextBox
            {
                Text = defaultName,
                PlaceholderText = App.GetValueFromItemList("GamesPage_NamePlaceholder")
            };

            var coverPathBox = new TextBox
            {
                IsReadOnly = true,
                PlaceholderText = App.GetValueFromItemList("GamesPage_CoverOptionalPlaceholder")
            };

            var pickCoverButton = new Button
            {
                Content = App.GetValueFromItemList("GamesPage_PickCover"),
                Margin = new Thickness(0, 8, 0, 0)
            };
            string? selectedCoverPath = null;
            pickCoverButton.Click += (_, _) =>
            {
                string? cover = NativeFileDialogHelper.ShowOpenFileDialog(TryGetWindowHandle(), ImageFileFilter);
                if (string.IsNullOrWhiteSpace(cover))
                {
                    return;
                }

                selectedCoverPath = cover;
                coverPathBox.Text = cover;
            };

            var panel = new StackPanel { Spacing = 8 };
            panel.Children.Add(nameBox);
            panel.Children.Add(coverPathBox);
            panel.Children.Add(pickCoverButton);

            var dialog = new ContentDialog
            {
                Title = App.GetValueFromItemList("GamesPage_AddManual"),
                Content = panel,
                PrimaryButtonText = App.GetValueFromItemList("GamesPage_Add"),
                CloseButtonText = App.GetValueFromItemList("Cancel"),
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = XamlRoot
            };

            ContentDialogResult result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary)
            {
                return;
            }

            string name = string.IsNullOrWhiteSpace(nameBox.Text) ? defaultName : nameBox.Text.Trim();
            string? iconPath = TryCacheExeIcon(path);
            await ViewModel.AddManualAsync(name, path, iconPath ?? path, selectedCoverPath);
        }

        private static IntPtr TryGetWindowHandle()
        {
            try
            {
                if (App.m_window is not null)
                {
                    return WindowNative.GetWindowHandle(App.m_window);
                }
            }
            catch
            {
                // Ignore and fall through.
            }

            return IntPtr.Zero;
        }

        private static string? TryResolveShortcut(string shortcutPath)
        {
            try
            {
                Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType is null)
                {
                    return null;
                }

                dynamic shell = Activator.CreateInstance(shellType)!;
                dynamic shortcut = shell.CreateShortcut(shortcutPath);
                string? target = shortcut.TargetPath as string;
                return string.IsNullOrWhiteSpace(target) ? null : target;
            }
            catch (Exception exception)
            {
                App.logger.Debug(exception, $"Failed to resolve shortcut: {shortcutPath}");
                return null;
            }
        }

        private static string? TryCacheExeIcon(string executablePath)
        {
            try
            {
                using System.Drawing.Icon? icon = System.Drawing.Icon.ExtractAssociatedIcon(executablePath);
                if (icon is null)
                {
                    return null;
                }

                string cacheDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "SynToolkit",
                    "GameIcons");
                Directory.CreateDirectory(cacheDir);
                string fileName = Convert.ToHexString(
                    System.Security.Cryptography.SHA256.HashData(
                        System.Text.Encoding.UTF8.GetBytes(executablePath.ToLowerInvariant())))
                    .Substring(0, 16) + ".ico";
                string dest = Path.Combine(cacheDir, fileName);
                using FileStream stream = File.Create(dest);
                icon.Save(stream);
                return dest;
            }
            catch (Exception exception)
            {
                App.logger.Debug(exception, "Failed to extract executable icon.");
                return null;
            }
        }
    }
}
