using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using SynToolkit.Utils;
using System;
using System.IO;
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
            // Move only — SetWindowPos sizing fights WinUIEx DIP sizing on scaled displays
            // and was clipping the ProgressRing/text out of the client area.
            CenterWindowOnScreen();
        }

        private void LoadText()
        {
            StartingServices.Text = App.GetValueFromItemList("StartingServices");
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
            DisplayArea displayArea = DisplayArea.GetFromWindowId(
                appWindow.Id,
                DisplayAreaFallback.Nearest);
            SizeInt32 size = appWindow.Size;
            if (size.Width <= 0 || size.Height <= 0)
            {
                size = new SizeInt32(WindowSize, WindowSize);
            }

            int x = displayArea.WorkArea.X + Math.Max(0, (displayArea.WorkArea.Width - size.Width) / 2);
            int y = displayArea.WorkArea.Y + Math.Max(0, (displayArea.WorkArea.Height - size.Height) / 2);
            appWindow.Move(new PointInt32(x, y));
        }
    }
}
