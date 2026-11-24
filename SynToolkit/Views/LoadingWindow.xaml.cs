using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using SynToolkit.Utils;
using System;
using System.IO;
using System.Threading.Tasks;
using Windows.Graphics;
using WinUIEx;

namespace SynToolkit
{
    /// <summary>
    /// Startup splash shown while configuration services initialize.
    /// </summary>
    public sealed partial class LoadingWindow : Window
    {
        private const int WindowSize = 250;

        public LoadingWindow()
        {
            this.InitializeComponent();
            BackdropHelper.ApplySafeMicaFallback(this, RootGrid);
            LoadText();
            ApplyWindowIcon();

            var manager = WindowManager.Get(this);
            manager.Width = WindowSize;
            manager.MinWidth = WindowSize;
            manager.Height = WindowSize;
            manager.MinHeight = WindowSize;
            manager.IsResizable = false;
            manager.IsMaximizable = false;
            manager.IsTitleBarVisible = false;

            ExtendsContentIntoTitleBar = true;
            // Place it after activation: WinUIEx applies its DIP size during
            // activation, so centring before that used the wrong size and left the
            // splash half off-screen with the ring/text out of the visible area.
            Activated += OnFirstActivated;
        }

        private void OnFirstActivated(object sender, WindowActivatedEventArgs args)
        {
            Activated -= OnFirstActivated;
            CenterWindowOnScreen();
        }

        private void LoadText()
        {
            StartingServices.Text = App.GetValueFromItemList("StartingServices");
        }

        /// <summary>
        /// Holds startup until the splash has actually painted. Without this the
        /// window only ever shows its backdrop, because the main window is built on
        /// the UI thread before the first frame reaches the screen.
        /// </summary>
        public async Task WaitUntilRenderedAsync()
        {
            if (!RootGrid.IsLoaded)
            {
                TaskCompletionSource loaded = new(TaskCreationOptions.RunContinuationsAsynchronously);
                void OnLoaded(object sender, RoutedEventArgs e) => loaded.TrySetResult();
                RootGrid.Loaded += OnLoaded;
                await Task.WhenAny(loaded.Task, Task.Delay(5000));
                RootGrid.Loaded -= OnLoaded;
            }

            // Loaded fires before the compositor presents the frame.
            await Task.Delay(350);
        }

        private void ApplyWindowIcon()
        {
            string iconPath = Path.Combine(AppContext.BaseDirectory, "assets", "logo", "SynToolkit.ico");
            if (File.Exists(iconPath))
            {
                AppWindow.SetIcon(iconPath);
            }
        }

        private void CenterWindowOnScreen()
        {
            AppWindow appWindow = AppWindow;
            // Read the real (physical pixel) size here: WinUIEx has applied its DIP
            // size by the time the window activates, so this is the true size fed to
            // the compositor. Centring a guessed size leaves the splash off-centre.
            SizeInt32 size = appWindow.Size;
            if (size.Width <= 0 || size.Height <= 0)
            {
                size = new SizeInt32(WindowSize, WindowSize);
                appWindow.Resize(size);
            }

            RectInt32 work = DisplayArea.GetFromWindowId(
                appWindow.Id,
                DisplayAreaFallback.Nearest).WorkArea;
            appWindow.Move(new PointInt32(
                work.X + Math.Max(0, (work.Width - size.Width) / 2),
                work.Y + Math.Max(0, (work.Height - size.Height) / 2)));
        }
    }
}
