#nullable enable

using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SynToolkit.Utils;
using SynToolkit.ViewModels;
using WinRT.Interop;

namespace SynToolkit.Views
{
    public sealed partial class GamesPage : Page
    {
        private const string ExeFileFilter = "Executables (*.exe)|*.exe|Shortcuts (*.lnk)|*.lnk|All files (*.*)|*.*";

        public GamesPageViewModel ViewModel { get; }

        public GamesPage()
        {
            InitializeComponent();
            ViewModel = App._host.Services.GetRequiredService<GamesPageViewModel>();
            DataContext = ViewModel;
        }

        private async void Page_Loaded(object sender, RoutedEventArgs e)
        {
            await ViewModel.LoadAsync();
        }

        private async void ScanButton_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel.ScanCommand.CanExecute(null))
            {
                await ViewModel.ScanCommand.ExecuteAsync(null);
            }
        }

        private async void AddManualButton_Click(object sender, RoutedEventArgs e)
        {
            IntPtr hwnd = IntPtr.Zero;
            try
            {
                if (App.m_window is not null)
                {
                    hwnd = WindowNative.GetWindowHandle(App.m_window);
                }
            }
            catch
            {
                hwnd = IntPtr.Zero;
            }

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

            var dialog = new ContentDialog
            {
                Title = App.GetValueFromItemList("GamesPage_AddManual"),
                Content = nameBox,
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
            await ViewModel.AddManualAsync(name, path, iconPath ?? path);
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
