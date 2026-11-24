using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Roomy.ViewModels;
using Roomy.Localization;

namespace Roomy;

/// <summary>
/// Films the real app (demo data only) with a fake cursor, piping frames to ffmpeg at a steady 30 fps.
/// `--record out.mp4` makes the website demo video.
/// </summary>
public partial class MainWindow
{
    const double RecordScale = 1.5;
    const int Fps = 30;

    Stopwatch? _recClock;
    readonly Dictionary<string, double> _events = new();

    void Mark(string name)
    {
        if (_recClock != null) _events[name] = _recClock.Elapsed.TotalSeconds;
    }

    public async Task RecordDemoAsync(string outFile)
    {
        try { await RecordScriptAsync("demo", outFile); }
        finally { Application.Current.Shutdown(); }
    }

    /// <summary>Records one scripted run and returns the time (seconds into the video) of each marked moment.</summary>
    public async Task<Dictionary<string, double>> RecordScriptAsync(string script, string outFile)
    {
        FakeCursorLayer.Visibility = Visibility.Visible;
        Canvas.SetLeft(FakeCursor, Root.ActualWidth * 0.78);
        Canvas.SetTop(FakeCursor, Root.ActualHeight * 0.92);
        await Task.Delay(1000);

        Func<Task>? extra = null, preroll = null;
        ExtraScript(script, ref extra, ref preroll);
        if (preroll != null) await preroll();

        int w = (int)Math.Round(Root.ActualWidth * RecordScale) / 2 * 2;
        int h = (int)Math.Round(Root.ActualHeight * RecordScale) / 2 * 2;
        var ffmpeg = Process.Start(new ProcessStartInfo("ffmpeg",
            $"-y -loglevel error -f rawvideo -pix_fmt bgra -s {w}x{h} -r {Fps} -i - " +
            $"-c:v libx264 -preset slow -crf 18 -pix_fmt yuv420p -movflags +faststart \"{outFile}\"")
        {
            RedirectStandardInput = true, UseShellExecute = false, CreateNoWindow = true,
        })!;

        byte[]? latest = null;
        var gate = new object();
        var recording = true;
        var clock = new Stopwatch();
        _events.Clear();
        _recClock = clock;

        var grabbing = true;
        var grabber = GrabLoop(w, h, b => { lock (gate) latest = b; }, () => grabbing);
        while (true) { lock (gate) if (latest != null) break; await Task.Delay(5); }

        clock.Start();
        var writer = Task.Run(() =>
        {
            var stdin = ffmpeg.StandardInput.BaseStream;
            long frame = 0;
            while (recording)
            {
                var wait = frame * 1000.0 / Fps - clock.Elapsed.TotalMilliseconds;
                if (wait > 1) Thread.Sleep((int)wait);
                byte[] f;
                lock (gate) f = latest!;
                stdin.Write(f, 0, f.Length);
                frame++;
            }
            stdin.Flush();
            stdin.Close();
        });

        try
        {
            await (extra != null ? extra() : DemoScript());
            Mark("end");
        }
        finally
        {
            grabbing = false;
            await grabber;
            recording = false;
            await writer;
            await ffmpeg.WaitForExitAsync();
            _recClock = null;
        }
        return new Dictionary<string, double>(_events);
    }

    async Task GrabLoop(int w, int h, Action<byte[]> publish, Func<bool> keepGoing)
    {
        var rtb = new RenderTargetBitmap(w, h, 96 * RecordScale, 96 * RecordScale, PixelFormats.Pbgra32);
        var stride = w * 4;
        var bg = new DrawingVisual();
        while (keepGoing())
        {
            rtb.Clear();
            using (var dc = bg.RenderOpen())
                dc.DrawRectangle((Brush)FindResource("BgBrush"), null, new Rect(0, 0, Root.ActualWidth, Root.ActualHeight));
            rtb.Render(bg);
            rtb.Render(Root);
            var buf = new byte[stride * h];
            rtb.CopyPixels(buf, stride, 0);
            publish(buf);
            await Task.Delay(8);
        }
    }

    // ---------- scripts ----------

    async Task DemoScript()
    {
        await Task.Delay(900);
        await ClickAsync(Find<Button>(b => b.Command == _vm.ScanCommand && IsIn(b, HomeView)));

        await WaitUntil(() => _vm.State == AppState.Results);
        await Task.Delay(1900);

        await ClickAsync(Find<CheckBox>(c => c.DataContext is CategoryVM { Model.Id: "shaders" }));
        await Task.Delay(1300);

        await ClickAsync(Find<RadioButton>(r => (r.Content as string) == S.TabSpaceMap));
        await WaitUntil(() => _vm.SpaceMap.HasData);
        await Task.Delay(1400);
        await ClickAsync(Find<Button>(b => b.DataContext is SpaceRowVM { Name: "Users" }), 0.18);
        await Task.Delay(1500);

        await ClickAsync(Find<RadioButton>(r => (r.Content as string) == S.TabCleanup));
        await Task.Delay(900);
        await ClickAsync(Find<Button>(b => b.Command == _vm.CleanCommand));

        await WaitUntil(() => _vm.State == AppState.Done);
        await Task.Delay(2400);
        await MoveCursorAsync(Find<Button>(b => b.Command == _vm.ShareCommand), 0.5);
        await Task.Delay(1500);
    }

    partial void ExtraScript(string name, ref Func<Task>? run, ref Func<Task>? preroll);

    // ---------- helpers ----------

    async Task ScrollToCategoryAsync(string id)
    {
        var card = Find<CheckBox>(c => c.DataContext is CategoryVM cat && cat.Model.Id == id) as FrameworkElement
                   ?? Find<Button>(b => b.DataContext is CategoryVM cat && cat.Model.Id == id);
        if (card is null || CleanupScroll.Content is not UIElement content) return;
        var y = card.TranslatePoint(new Point(0, 0), content).Y;
        await SmoothScrollAsync(Math.Max(0, y - 40));
    }

    async Task SmoothScrollAsync(double target)
    {
        var from = CleanupScroll.VerticalOffset;
        const int steps = 24;
        for (int i = 1; i <= steps; i++)
        {
            var p = (double)i / steps;
            var e = p < 0.5 ? 4 * p * p * p : 1 - Math.Pow(-2 * p + 2, 3) / 2;
            CleanupScroll.ScrollToVerticalOffset(from + (target - from) * e);
            await Task.Delay(25);
        }
        await Task.Delay(150);
    }

    async Task ClickAsync(FrameworkElement? target, double xFraction = 0.5)
    {
        if (target is null) return;
        await MoveCursorAsync(target, xFraction);
        await Task.Delay(120);
        Ring();
        await Task.Delay(140);
        switch (target)
        {
            case RadioButton rb: rb.IsChecked = true; break;
            case CheckBox cb: cb.IsChecked = cb.IsChecked != true; break;
            case Button b when b.Command?.CanExecute(b.CommandParameter) == true: b.Command.Execute(b.CommandParameter); break;
        }
    }

    async Task MoveCursorAsync(FrameworkElement? target, double xFraction)
    {
        if (target is null) return;
        target.UpdateLayout();
        var p = target.TranslatePoint(new Point(target.ActualWidth * xFraction, target.ActualHeight * 0.55), Root);
        var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };
        var dur = TimeSpan.FromMilliseconds(850);
        FakeCursor.BeginAnimation(Canvas.LeftProperty, new DoubleAnimation(p.X, dur) { EasingFunction = ease });
        FakeCursor.BeginAnimation(Canvas.TopProperty, new DoubleAnimation(p.Y, dur) { EasingFunction = ease });
        await Task.Delay(dur);
    }

    void Ring()
    {
        var x = Canvas.GetLeft(FakeCursor);
        var y = Canvas.GetTop(FakeCursor);
        Canvas.SetLeft(ClickRing, x - ClickRing.Width / 2);
        Canvas.SetTop(ClickRing, y - ClickRing.Height / 2);
        var st = (ScaleTransform)ClickRing.RenderTransform;
        var d = TimeSpan.FromMilliseconds(450);
        st.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.3, 1.2, d));
        st.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.3, 1.2, d));
        ClickRing.BeginAnimation(OpacityProperty, new DoubleAnimation(0.9, 0, d));
    }

    static async Task WaitUntil(Func<bool> cond, int timeoutMs = 30000)
    {
        var sw = Stopwatch.StartNew();
        while (!cond() && sw.ElapsedMilliseconds < timeoutMs) await Task.Delay(30);
    }

    bool IsIn(DependencyObject child, DependencyObject ancestor)
    {
        for (var d = child; d != null; d = VisualTreeHelper.GetParent(d))
            if (d == ancestor) return true;
        return false;
    }

    T? Find<T>(Func<T, bool> match) where T : FrameworkElement
    {
        Root.UpdateLayout();
        var stack = new Stack<DependencyObject>();
        stack.Push(Root);
        while (stack.Count > 0)
        {
            var d = stack.Pop();
            if (d is T t && t.IsVisible && match(t)) return t;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++) stack.Push(VisualTreeHelper.GetChild(d, i));
        }
        return null;
    }
}
