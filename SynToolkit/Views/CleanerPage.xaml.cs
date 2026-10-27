#nullable enable

using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using SynToolkit.ViewModels;
using Windows.Storage;
using Windows.Storage.FileProperties;
using SynToolkit.Commands.ConfigurationButtonsCommand;

namespace SynToolkit.Views
{
    public sealed partial class CleanerPage : Page
    {
        private readonly CleanerPageViewModel _viewModel;
        private readonly ObservableCollection<DriveModel> _drives = new();
        private CancellationTokenSource _lifetimeCancellation = new();
        private bool _isRunningDiskCleanup;

        public CleanerPage()
        {
            InitializeComponent();
            NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Required;
            _viewModel = App._host.Services.GetRequiredService<CleanerPageViewModel>();
            DataContext = _viewModel;
            DrivesRepeater.ItemsSource = _drives;
        }

        private async void Page_Loaded(object sender, RoutedEventArgs e)
        {
            if (_lifetimeCancellation.IsCancellationRequested)
            {
                _lifetimeCancellation.Dispose();
                _lifetimeCancellation = new CancellationTokenSource();
            }

            try
            {
                await LoadDrivesAsync(_lifetimeCancellation.Token);
            }
            catch (OperationCanceledException)
            {
            }
        }

        private void Page_Unloaded(object sender, RoutedEventArgs e)
        {
            _lifetimeCancellation.Cancel();
            foreach (DriveModel drive in _drives)
            {
                drive.Icon = null;
            }

            _drives.Clear();
        }

        private async Task LoadDrivesAsync(CancellationToken cancellationToken)
        {
            _drives.Clear();
            foreach (DriveInfo drive in DriveInfo.GetDrives().Where(d => d.IsReady))
            {
                cancellationToken.ThrowIfCancellationRequested();
                double totalGiB = drive.TotalSize / 1073741824d;
                double freeGiB = drive.TotalFreeSpace / 1073741824d;

                DriveModel model = new()
                {
                    Name = drive.Name.TrimEnd('\\'),
                    Label = string.IsNullOrWhiteSpace(drive.VolumeLabel)
                        ? $"Local Disk ({drive.Name.TrimEnd('\\')})"
                        : $"{drive.VolumeLabel} ({drive.Name.TrimEnd('\\')})",
                    Total = totalGiB,
                    Free = $"{FormatSize(freeGiB)} free of {FormatSize(totalGiB)}",
                    Used = totalGiB - freeGiB
                };

                try
                {
                    StorageFolder folder = await StorageFolder.GetFolderFromPathAsync(drive.Name);
                    using StorageItemThumbnail? thumb = await folder.GetThumbnailAsync(ThumbnailMode.SingleItem, 32, ThumbnailOptions.UseCurrentScale);
                    if (thumb is not null)
                    {
                        BitmapImage bmp = new();
                        await bmp.SetSourceAsync(thumb);
                        cancellationToken.ThrowIfCancellationRequested();
                        model.Icon = bmp;
                    }
                }
                catch
                {
                    // Ignore thumbnail errors
                }

                cancellationToken.ThrowIfCancellationRequested();
                _drives.Add(model);
            }
        }

        private void UpdateDrives()
        {
            foreach (DriveInfo drive in DriveInfo.GetDrives().Where(d => d.IsReady))
            {
                DriveModel? model = _drives.FirstOrDefault(d => d.Name == drive.Name.TrimEnd('\\'));
                if (model is null) continue;

                double totalGiB = drive.TotalSize / 1073741824d;
                double freeGiB = drive.TotalFreeSpace / 1073741824d;

                model.Total = totalGiB;
                model.Used = totalGiB - freeGiB;
                model.Free = $"{FormatSize(freeGiB)} free of {FormatSize(totalGiB)}";
            }
        }

        private static string FormatSize(double sizeGiB)
        {
            if (sizeGiB < 1) return $"{sizeGiB * 1024:N2} MB";
            if (sizeGiB >= 1024) return $"{sizeGiB / 1024:N2} TB";
            return $"{sizeGiB:N2} GB";
        }

        private void LaunchWizTreeButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            var launchCommand = new LaunchPortableToolCommand("WizTree64.exe");
            if (launchCommand.CanExecute(null))
            {
                launchCommand.Execute(null);
            }
        }
        private async void RunDiskCleanupButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isRunningDiskCleanup) return;

            _isRunningDiskCleanup = true;
            RunDiskCleanupButton.IsEnabled = false;
            RunDiskCleanupButton.Content = "Cleaning up...";

            try
            {
                await Task.Run(async () =>
                {
                    // Kill TiWorker if running
                    foreach (Process proc in Process.GetProcessesByName("TiWorker"))
                    {
                        try
                        {
                            proc.Kill();
                            await proc.WaitForExitAsync();
                        }
                        catch { }
                    }

                    // Clean temp directories
                    CleanDirectory(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Logs"));
                    CleanDirectory(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Panther"));
                    CleanDirectory(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "LogFiles"));
                    CleanDirectory(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "SleepStudy"));
                    CleanDirectory(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SystemTemp"));
                    CleanDirectory(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp"));
                    CleanDirectory(Path.GetTempPath());

                    try
                    {
                        string? root = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.System));
                        if (root is not null)
                        {
                            File.Delete(Path.Combine(root, "DumpStack.log"));
                        }
                    }
                    catch { }

                    // Run Windows Disk Cleanup
                    string cleanmgrPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cleanmgr.exe");
                    using Process? cleanmgr = Process.Start(new ProcessStartInfo
                    {
                        FileName = cleanmgrPath,
                        Arguments = "/sagerun:0",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    });

                    if (cleanmgr is not null)
                    {
                        await cleanmgr.WaitForExitAsync();
                    }
                });

                UpdateDrives();

                SuccessInfoBar.Message = "Disk cleanup completed successfully.";
                SuccessInfoBar.IsOpen = true;
            }
            catch (Exception ex)
            {
                App.logger.Error(ex, "[DiskCleanup] Failed to run disk cleanup.");
            }
            finally
            {
                _isRunningDiskCleanup = false;
                RunDiskCleanupButton.IsEnabled = true;
                RunDiskCleanupButton.Content = "Run Disk Cleanup";
            }
        }

        private static void CleanDirectory(string path)
        {
            if (!Directory.Exists(path)) return;

            try
            {
                foreach (string file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                {
                    try { File.Delete(file); } catch { }
                }

                foreach (string dir in Directory.EnumerateDirectories(path, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length))
                {
                    try { Directory.Delete(dir, false); } catch { }
                }
            }
            catch { }
        }

        private async void ScanButton_Click(object sender, RoutedEventArgs e) => await _viewModel.ScanAsync();

        private void SelectAllButton_Click(object sender, RoutedEventArgs e) => _viewModel.SetAllSelected(true);

        private void SelectNoneButton_Click(object sender, RoutedEventArgs e) => _viewModel.SetAllSelected(false);

        private async void CleanSelectedButton_Click(object sender, RoutedEventArgs e)
        {
            ContentDialog confirmation = new()
            {
                XamlRoot = XamlRoot,
                Style = Microsoft.UI.Xaml.Application.Current.Resources["DefaultContentDialogStyle"] as Style,
                Title = "Clean selected junk files?",
                Content = "This permanently deletes files in the checked categories. Files currently in use are skipped automatically.",
                PrimaryButtonText = "Clean",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close
            };

            ContentDialogResult result = await confirmation.ShowAsync();
            if (result != ContentDialogResult.Primary)
            {
                return;
            }

            await _viewModel.CleanSelectedAsync();
            UpdateDrives();
        }
    }

    public partial class DriveModel : INotifyPropertyChanged
    {
        private double _total;
        private double _used;
        private string _free = "";
        private ImageSource? _icon;

        public string Name { get; set; } = "";
        public string Label { get; set; } = "";

        public double Total
        {
            get => _total;
            set { _total = value; OnPropertyChanged(nameof(Total)); }
        }

        public double Used
        {
            get => _used;
            set { _used = value; OnPropertyChanged(nameof(Used)); }
        }

        public ImageSource? Icon
        {
            get => _icon;
            set { _icon = value; OnPropertyChanged(nameof(Icon)); }
        }

        public string Free
        {
            get => _free;
            set { _free = value; OnPropertyChanged(nameof(Free)); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged(string propertyName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
