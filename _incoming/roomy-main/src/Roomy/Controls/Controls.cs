using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Roomy.Core;
using Roomy.ViewModels;

namespace Roomy.Controls;

/// <summary>A rounded progress/usage bar that eases to its target and replays when it becomes visible.</summary>
public sealed class UsageBar : FrameworkElement
{
    public static readonly DependencyProperty TargetProperty = DependencyProperty.Register(
        nameof(Target), typeof(double), typeof(UsageBar),
        new PropertyMetadata(0d, (d, _) => ((UsageBar)d).AnimateTo(false)));

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(UsageBar),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
        nameof(Fill), typeof(Brush), typeof(UsageBar),
        new FrameworkPropertyMetadata(Brushes.LimeGreen, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TrackProperty = DependencyProperty.Register(
        nameof(Track), typeof(Brush), typeof(UsageBar),
        new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Target { get => (double)GetValue(TargetProperty); set => SetValue(TargetProperty, value); }
    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public Brush? Fill { get => (Brush?)GetValue(FillProperty); set => SetValue(FillProperty, value); }
    public Brush? Track { get => (Brush?)GetValue(TrackProperty); set => SetValue(TrackProperty, value); }

    public UsageBar()
    {
        IsVisibleChanged += (_, e) => { if ((bool)e.NewValue) AnimateTo(true); };
    }

    void AnimateTo(bool fromZero)
    {
        var to = Math.Clamp(Target, 0, 1);
        if (!IsVisible)
        {
            BeginAnimation(ValueProperty, null);
            Value = to;
            return;
        }
        var a = new DoubleAnimation
        {
            To = to,
            Duration = TimeSpan.FromMilliseconds(1100),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        if (fromZero) a.From = 0;
        BeginAnimation(ValueProperty, a);
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight, r = h / 2;
        if (w <= 0 || h <= 0) return;
        dc.DrawRoundedRectangle(Track, null, new Rect(0, 0, w, h), r, r);
        var v = Math.Clamp(Value, 0, 1);
        if (v <= 0) return;
        dc.DrawRoundedRectangle(Fill, null, new Rect(0, 0, Math.Max(h, w * v), h), r, r);
    }
}

/// <summary>Shows a byte count that counts up to its value.</summary>
public sealed class AnimatedBytes : TextBlock
{
    public static readonly DependencyProperty BytesProperty = DependencyProperty.Register(
        nameof(Bytes), typeof(long), typeof(AnimatedBytes),
        new PropertyMetadata(0L, (d, _) => ((AnimatedBytes)d).Animate(false)));

    public static readonly DependencyProperty CurrentProperty = DependencyProperty.Register(
        nameof(Current), typeof(double), typeof(AnimatedBytes),
        new PropertyMetadata(0d, (d, e) => ((AnimatedBytes)d).Text = Format.Bytes((long)(double)e.NewValue)));

    public static readonly DependencyProperty DurationProperty = DependencyProperty.Register(
        nameof(Duration), typeof(int), typeof(AnimatedBytes), new PropertyMetadata(900));

    public long Bytes { get => (long)GetValue(BytesProperty); set => SetValue(BytesProperty, value); }
    public double Current { get => (double)GetValue(CurrentProperty); set => SetValue(CurrentProperty, value); }
    public int Duration { get => (int)GetValue(DurationProperty); set => SetValue(DurationProperty, value); }

    public AnimatedBytes()
    {
        Text = Format.Bytes(0);
        IsVisibleChanged += (_, e) => { if ((bool)e.NewValue) Animate(true); };
    }

    void Animate(bool fromZero)
    {
        if (!IsVisible)
        {
            BeginAnimation(CurrentProperty, null);
            Current = Bytes;
            Text = Format.Bytes(Bytes);
            return;
        }
        var a = new DoubleAnimation
        {
            To = Bytes,
            Duration = TimeSpan.FromMilliseconds(Duration),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        if (fromZero) a.From = 0;
        BeginAnimation(CurrentProperty, a);
    }
}

/// <summary>A ring with a spinning gradient arc.</summary>
public sealed class Spinner : FrameworkElement
{
    public static readonly DependencyProperty ThicknessProperty = DependencyProperty.Register(
        nameof(Thickness), typeof(double), typeof(Spinner),
        new FrameworkPropertyMetadata(6d, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Thickness { get => (double)GetValue(ThicknessProperty); set => SetValue(ThicknessProperty, value); }

    readonly RotateTransform _rotate = new();

    public Spinner()
    {
        RenderTransformOrigin = new Point(0.5, 0.5);
        RenderTransform = _rotate;
        IsVisibleChanged += (_, e) =>
        {
            if ((bool)e.NewValue)
                _rotate.BeginAnimation(RotateTransform.AngleProperty,
                    new DoubleAnimation(0, 360, TimeSpan.FromSeconds(1.1)) { RepeatBehavior = RepeatBehavior.Forever });
            else
                _rotate.BeginAnimation(RotateTransform.AngleProperty, null);
        };
    }

    protected override void OnRender(DrawingContext dc)
    {
        double t = Thickness, w = ActualWidth, h = ActualHeight;
        var r = Math.Min(w, h) / 2 - t / 2;
        if (r <= 0) return;
        var c = new Point(w / 2, h / 2);
        dc.DrawEllipse(null, new Pen(Res.Brush("Surface2Brush"), t), c, r, r);

        var angle = 110 * Math.PI / 180;
        var start = new Point(c.X, c.Y - r);
        var end = new Point(c.X + r * Math.Sin(angle), c.Y - r * Math.Cos(angle));
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(start, false, false);
            ctx.ArcTo(end, new Size(r, r), 0, false, SweepDirection.Clockwise, true, false);
        }
        g.Freeze();
        dc.DrawGeometry(null, new Pen(Res.Brush("AccentGradient"), t)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
        }, g);
    }
}

public sealed class VisIf : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class VisIfNot : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
