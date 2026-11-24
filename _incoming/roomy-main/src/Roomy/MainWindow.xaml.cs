using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Roomy.Core;
using Roomy.ViewModels;
using Roomy.Views;
using Roomy.Localization;

namespace Roomy;

public partial class MainWindow : Window
{
    readonly MainVM _vm;

    public MainWindow(MainVM vm)
    {
        InitializeComponent();
        Controls.FitToScreen.Apply(this);
        _vm = vm;
        DataContext = vm;
        vm.PropertyChanged += OnVmChanged;
        vm.ShareRequested += (_, _) => Share();
        SourceInitialized += (_, _) => ApplyDwm();
        StateChanged += (_, _) => OnWindowStateChanged();
        ShowView(vm.State, animate: false);
    }

    void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainVM.State)) ShowView(_vm.State, animate: true);
        else if (e.PropertyName == nameof(MainVM.ToastVisible))
            ToastHost.BeginAnimation(OpacityProperty,
                new DoubleAnimation(_vm.ToastVisible ? 1 : 0, TimeSpan.FromMilliseconds(220)));
    }

    FrameworkElement ViewFor(AppState s) => s switch
    {
        AppState.Scanning => ScanView,
        AppState.Results => ResultsView,
        AppState.Cleaning => CleanView,
        AppState.Done => DoneView,
        _ => HomeView,
    };

    void ShowView(AppState state, bool animate)
    {
        foreach (var v in new FrameworkElement[] { HomeView, ScanView, ResultsView, CleanView, DoneView })
            v.Visibility = Visibility.Collapsed;

        var target = ViewFor(state);
        target.Visibility = Visibility.Visible;
        if (state == AppState.Results) CleanupScroll.ScrollToTop();

        if (animate)
        {
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var slide = new TranslateTransform(0, 16);
            target.RenderTransform = slide;
            target.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(320)) { EasingFunction = ease });
            slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(16, 0, TimeSpan.FromMilliseconds(420)) { EasingFunction = ease });
        }

        if (state == AppState.Done && DoneCheck.RenderTransform is ScaleTransform st)
        {
            var pop = new DoubleAnimation(0.3, 1, TimeSpan.FromMilliseconds(650))
            {
                EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.55 },
            };
            st.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
            st.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
        }
    }

    // ---------- window chrome ----------

    void ApplyDwm()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        int dark = 1, round = 2;
        try
        {
            Native.DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int));   // DWMWA_USE_IMMERSIVE_DARK_MODE
            Native.DwmSetWindowAttribute(hwnd, 33, ref round, sizeof(int));  // DWMWA_WINDOW_CORNER_PREFERENCE = round
        }
        catch { }
    }

    void OnWindowStateChanged()
    {
        Root.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
        MaxButton.Content = WindowState == WindowState.Maximized ? "" : "";
    }

    void Min_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    void Max_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    void Close_Click(object sender, RoutedEventArgs e) => Close();

    // ---------- share card ----------

    BitmapSource RenderShareCard()
    {
        var drive = _vm.SystemDrive;
        var card = new ShareCard
        {
            DataContext = new ShareData(
                Format.Bytes(_vm.Freed),
                drive is null ? S.OnMyPc : Loc.Format("OnMyDrive", drive.Letter),
                _vm.BeforeFraction, _vm.AfterFraction, _vm.BeforeText, _vm.AfterText,
                _vm.BeforeBrush ?? Res.Brush("DangerGradient")),
        };
        card.Measure(new Size(ShareCard.W, ShareCard.H));
        card.Arrange(new Rect(0, 0, ShareCard.W, ShareCard.H));
        card.UpdateLayout();
        Dispatcher.Invoke(() => { }, DispatcherPriority.Loaded);
        card.UpdateLayout();
        var rtb = new RenderTargetBitmap(ShareCard.W * 2, ShareCard.H * 2, 192, 192, PixelFormats.Pbgra32);
        rtb.Render(card);
        rtb.Freeze();
        return rtb;
    }

    void Share()
    {
        try
        {
            var img = RenderShareCard();
            Clipboard.SetImage(img);
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Roomy");
            Directory.CreateDirectory(dir);
            SavePng(img, Path.Combine(dir, $"roomy-freed-{DateTime.Now:yyyyMMdd-HHmmss}.png"));
            _vm.ShowToast(S.CopiedToast);
        }
        catch (Exception ex)
        {
            _vm.ShowToast(S.CantMakeImage + ex.Message);
        }
    }

    static void SavePng(BitmapSource img, string path)
    {
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(img));
        using var fs = File.Create(path);
        enc.Save(fs);
    }

    // ---------- screenshots of every screen (demo data only) ----------

    public async Task RunScreenshotsAsync(string dir)
    {
        Directory.CreateDirectory(dir);
        try
        {
            await Task.Delay(1600);
            Snap(dir, "01-home");

            _vm.ScanCommand.Execute(null);
            await Task.Delay(1500);
            Snap(dir, "02-scanning");

            await WaitFor(() => _vm.State == AppState.Results);
            await Task.Delay(1700);
            Snap(dir, "03-results");

            _vm.ExpandOnly("installers");
            var installers = _vm.Categories.FirstOrDefault(c => c.Model.Id == "installers");
            if (installers != null)
                foreach (var i in installers.Items.Take(5)) i.IsSelected = true;
            await Task.Delay(300);
            if (installers != null)
            {
                var idx = _vm.Categories.IndexOf(installers);
                CleanupScroll.ScrollToVerticalOffset(Math.Max(0, idx * 104 - 20));
            }
            await Task.Delay(900);
            Snap(dir, "04-review");

            if (installers != null)
                foreach (var i in installers.Items) i.IsSelected = false;
            if (_vm.Categories.FirstOrDefault(c => c.Model.Id == "hibernation") is { } hibernation)
            {
                _vm.ExpandOnly("hibernation");
                await Task.Delay(300);
                CleanupScroll.ScrollToVerticalOffset(Math.Max(0, _vm.Categories.IndexOf(hibernation) * 104 - 20));
                await Task.Delay(900);
                Snap(dir, "04b-hibernation");
            }

            _vm.ExpandOnly("");
            CleanupScroll.ScrollToTop();

            _vm.IsSpaceTab = true;
            await WaitFor(() => _vm.SpaceMap.HasData);
            await Task.Delay(1300);
            Snap(dir, "05-space-map");

            _vm.IsCleanupTab = true;
            await Task.Delay(500);
            _vm.CleanCommand.Execute(null);
            await WaitFor(() => _vm.State == AppState.Cleaning);
            await Task.Delay(1200);
            Snap(dir, "06-cleaning");

            await WaitFor(() => _vm.State == AppState.Done);
            await Task.Delay(2200);
            Snap(dir, "07-done");

            SavePng(RenderShareCard(), Path.Combine(dir, "08-share-card.png"));
        }
        finally
        {
            Application.Current.Shutdown();
        }
    }

    static async Task WaitFor(Func<bool> condition, int timeoutMs = 30000)
    {
        var sw = Stopwatch.StartNew();
        while (!condition() && sw.ElapsedMilliseconds < timeoutMs) await Task.Delay(50);
    }

    void Snap(string dir, string name)
    {
        Root.UpdateLayout();
        int w = (int)Math.Ceiling(Root.ActualWidth), h = (int)Math.Ceiling(Root.ActualHeight);
        var rtb = new RenderTargetBitmap(w * 2, h * 2, 192, 192, PixelFormats.Pbgra32);
        rtb.Render(Root);
        SavePng(rtb, Path.Combine(dir, name + ".png"));
    }
}
