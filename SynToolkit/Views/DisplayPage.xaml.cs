#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DispatcherQueueTimer = Microsoft.UI.Dispatching.DispatcherQueueTimer;
using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using SynToolkit.Models.Display;
using SynToolkit.Services.Display;
using SynToolkit.ViewModels;
using Windows.Graphics;
using Windows.System;
using WinRT.Interop;

namespace SynToolkit.Views;

public sealed partial class DisplayPage : Page
{
    private readonly Dictionary<string, Button> _tiles = new(StringComparer.OrdinalIgnoreCase);
    private DisplayLayout? _layout;
    private int _layoutOriginX;
    private int _layoutOriginY;
    private string? _dragKey;
    private double _dragOffsetX;
    private double _dragOffsetY;
    private DisplaySnapshot? _dragSnapshot;
    private DisplaySnapshot? _dragBaseline;
    private IReadOnlyList<DisplayPlacementCandidate?>? _dragAlignedSlots;
    private double _dragMaxDistancePx;
    private double _dragDipScale = 1.0;
    private bool _dragSnapped;
    private int _dragSnappedX;
    private int _dragSnappedY;
    private int _dragPointerX;
    private int _dragPointerY;
    private bool _dragPointerSeen;
    private bool _dragCommitted;
    private readonly List<Window> _identifyWindows = [];

    public DisplayPageViewModel ViewModel { get; }

    public DisplayPage()
    {
        ViewModel = new DisplayPageViewModel();
        InitializeComponent();
        CanvasBorder.Height = DisplayGeometry.CanvasBoxHeight;
        CanvasBorder.Width = DisplayGeometry.CanvasBoxWidthFor(PageLayout.ActualWidth);
        CanvasBorder.BorderThickness = new Thickness(DisplayGeometry.CanvasBorderThickness);
        ButtonRow.MinHeight = DisplayGeometry.ArrangeButtonRowHeight;
        DataContext = ViewModel;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _isLoaded = true;
        ViewModel.AttachDispatcher(DispatcherQueue);
        ViewModel.CanvasInvalidated += OnCanvasInvalidated;
        if (XamlRoot is not null)
            XamlRoot.Changed += OnXamlRootChanged;

        if (!ViewModel.HasLoaded)
            await ViewModel.LoadCommand.ExecuteAsync(null);

        if (!_isLoaded)
            return;

        ViewModel.StartRefreshWatch();
        RenderCanvas();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _isLoaded = false;
        StopResizeDebounce();
        ViewModel.StopRefreshWatch();
        ViewModel.CanvasInvalidated -= OnCanvasInvalidated;
        if (XamlRoot is not null)
            XamlRoot.Changed -= OnXamlRootChanged;
        CloseIdentifyWindows();
        if (DisplayChainPolicy.ShouldReportStage(ViewModel.PendingPositions.Count > 0, ViewModel.IsChainActive, ViewModel.IsApplying, ViewModel.IsSettling))
        {
            ViewModel.DiscardStagedArrangement();
            ViewModel.ShowStatus("You left with an arrangement that was staged but not applied, so it was discarded. Nothing changed.", DisplayNoticeKind.Neutral);
        }
    }

    private void OnXamlRootChanged(XamlRoot sender, XamlRootChangedEventArgs args) => RequestRender();

    private void OnCanvasInvalidated(object? sender, EventArgs e) => RequestRender();

    private bool _isLoaded;
    private bool _renderPending;
    private DispatcherQueueTimer? _resizeDebounceTimer;
    private bool _resizeDirty;
    private const int ResizeDebounceMs = 150;

    private void RequestRender()
    {
        if (_renderPending)
            return;

        _renderPending = true;
        if (!DispatcherQueue.TryEnqueue(() =>
        {
            _renderPending = false;
            if (_dragKey is null)
                RenderCanvas();
        }))
        {
            _renderPending = false;
        }
    }

    private void OnPageLayoutSizeChanged(object sender, SizeChangedEventArgs e)
    {
        CanvasBorder.Width = DisplayGeometry.CanvasBoxWidthFor(PageLayout.ActualWidth);
    }

    private void DisplayCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_dragKey is not null)
            return;

        RestartResizeDebounce();
    }

    private void RestartResizeDebounce()
    {
        _resizeDirty = true;
        if (_resizeDebounceTimer is not null)
        {
            _resizeDebounceTimer.Stop();
            _resizeDebounceTimer.Start();
            return;
        }

        _resizeDebounceTimer = DispatcherQueue.CreateTimer();
        _resizeDebounceTimer.Interval = TimeSpan.FromMilliseconds(ResizeDebounceMs);
        _resizeDebounceTimer.IsRepeating = false;
        _resizeDebounceTimer.Tick += OnResizeDebounceTick;
        _resizeDebounceTimer.Start();
    }

    private void OnResizeDebounceTick(DispatcherQueueTimer sender, object args)
    {
        StopResizeDebounce();
        var dirty = _resizeDirty;
        _resizeDirty = false;
        if (dirty && _dragKey is null)
            RequestRender();
    }

    private void StopResizeDebounce()
    {
        if (_resizeDebounceTimer is null)
            return;

        _resizeDebounceTimer.Tick -= OnResizeDebounceTick;
        _resizeDebounceTimer.Stop();
        _resizeDebounceTimer = null;
    }

    private async void Identify_Click(object sender, RoutedEventArgs e)
    {
        if (DisplayChainPolicy.IsStageActionAllowed(ViewModel.HasPendingArrangement, ViewModel.IsApplying, ViewModel.IsCountdownActive, ViewModel.IsSettling, ViewModel.IsBusy))
        {
            ViewModel.ShowStatus("Apply or cancel the staged arrangement before identifying displays.", DisplayNoticeKind.Refusal);
            ViewModel.Announce("Apply or cancel the staged arrangement before identifying displays.");
            return;
        }

        if (!ViewModel.AreControlsEnabled)
        {
            ViewModel.ShowStatus("Wait for the current change to finish before identifying displays.", DisplayNoticeKind.Refusal);
            ViewModel.Announce("Wait for the current change to finish before identifying displays.");
            return;
        }

        CloseIdentifyWindows();
        var snapshot = ViewModel.GetCanvasSnapshot();
        if (snapshot.Monitors.Count == 0)
            return;

        foreach (var monitor in snapshot.Monitors)
        {
            var window = new Window();
            window.Content = new Border
            {
                Background = FindBrush("AccentFillColorDefaultBrush"),
                Child = new TextBlock
                {
                    Text = monitor.Ordinal.ToString(),
                    FontSize = 72,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = FindBrush("TextFillColorPrimaryBrush")
                }
            };

            var hwnd = WindowNative.GetWindowHandle(window);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
            var appWindow = AppWindow.GetFromWindowId(windowId);
            appWindow.IsShownInSwitchers = false;
            if (appWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.SetBorderAndTitleBar(false, false);
                presenter.IsResizable = false;
                presenter.IsMinimizable = false;
                presenter.IsMaximizable = false;
            }
            var width = Math.Min(220, Math.Max(120, (int)monitor.PixelWidth / 8));
            var height = Math.Min(220, Math.Max(120, (int)monitor.PixelHeight / 8));
            var x = monitor.PositionX + (((int)monitor.PixelWidth - width) / 2);
            var y = monitor.PositionY + (((int)monitor.PixelHeight - height) / 2);
            appWindow.MoveAndResize(new RectInt32(x, y, width, height));
            window.Activate();
            _identifyWindows.Add(window);
        }

        await Task.Delay(2500);
        CloseIdentifyWindows();
    }

    private void CloseIdentifyWindows()
    {
        foreach (var window in _identifyWindows.ToArray())
        {
            try { window.Close(); }
            catch { }
        }

        _identifyWindows.Clear();
    }

    private void RenderCanvas()
    {
        using var trail = DisplayReentrancyTrail.Track("RenderCanvas");
        string? focusedKey = null;
        foreach (var entry in _tiles)
        {
            if (entry.Value.FocusState == FocusState.Keyboard || entry.Value.FocusState == FocusState.Programmatic)
            {
                focusedKey = entry.Key;
                break;
            }
        }

        DisplayCanvas.Children.Clear();
        _tiles.Clear();

        var snapshot = ViewModel.GetCanvasSnapshot();
        if (snapshot.Monitors.Count == 0)
        {
            _layout = null;
            _layoutOriginX = 0;
            _layoutOriginY = 0;
            return;
        }

        var width = DisplayCanvas.ActualWidth;
        var height = DisplayCanvas.ActualHeight;
        if (width <= 0 || height <= 0)
        {
            _layout = null;
            _layoutOriginX = 0;
            _layoutOriginY = 0;
            return;
        }

        _layout = DisplayGeometry.BuildLayout(snapshot, width, height, XamlRoot?.RasterizationScale ?? 1.0);
        _layoutOriginX = snapshot.MinX;
        _layoutOriginY = snapshot.MinY;
        if (_layout.Rects.Count == 0)
            return;

        var staged = ViewModel.HasPendingArrangement;
        var fill = FindBrush("CardBackgroundFillColorDefaultBrush");
        var selectedFill = FindBrush("AccentFillColorDefaultBrush");
        var stroke = FindBrush("CardStrokeColorDefaultBrush");
        var label = FindBrush("TextFillColorPrimaryBrush");
        var selectedLabel = FindBrush("TextOnAccentFillColorPrimaryBrush");

        foreach (var rect in _layout.Rects)
        {
            var monitor = rect.Monitor;
            var selected = ViewModel.SelectedMonitor is not null &&
                           string.Equals(ViewModel.SelectedMonitor.GdiDeviceName, monitor.GdiDeviceName, StringComparison.OrdinalIgnoreCase);
            var tileRadius = (CornerRadius)Application.Current.Resources["OverlayCornerRadius"];

            var tile = new Button
            {
                Width = Math.Max(rect.Width, 36),
                Height = Math.Max(rect.Height, 36),
                Style = (Style)Resources["DisplayTileStyle"],
                Background = selected ? selectedFill : fill,
                BorderBrush = stroke,
                BorderThickness = new Thickness(staged ? 3 : 1),
                CornerRadius = tileRadius,
                Padding = new Thickness(0),
                Tag = monitor.GdiDeviceName,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Stretch,
                Content = new Border
                {
                    BorderBrush = stroke,
                    BorderThickness = new Thickness(1),
                    CornerRadius = tileRadius,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch,
                    Child = new TextBlock
                    {
                        Text = monitor.Ordinal.ToString(),
                        FontSize = 32,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = selected ? selectedLabel : label,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        IsHitTestVisible = false
                    }
                }
            };

            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(tile, monitor.TileAutomationName(selected, staged));

            tile.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(Tile_PointerPressed), true);
            tile.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler(Tile_PointerMoved), true);
            tile.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(Tile_PointerReleased), true);
            tile.AddHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler(Tile_PointerCaptureLost), true);
            tile.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(Tile_KeyDown), true);

            Canvas.SetLeft(tile, rect.X);
            Canvas.SetTop(tile, rect.Y);
            DisplayCanvas.Children.Add(tile);
            _tiles[monitor.GdiDeviceName] = tile;
        }

        if (focusedKey is not null && _tiles.TryGetValue(focusedKey, out var focusedTile))
            focusedTile.Focus(FocusState.Programmatic);
    }

    private void Tile_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (sender is not Button tile || tile.Tag is not string key)
            return;

        if (e.Key != VirtualKey.Enter && e.Key != VirtualKey.Space)
            return;

        e.Handled = true;
        if (!ViewModel.AreControlsEnabled)
        {
            if (ViewModel.DisabledControlsTip is string tip)
                ViewModel.Announce(tip);
            return;
        }

        var monitor = ViewModel.Monitors.FirstOrDefault(item =>
            string.Equals(item.GdiDeviceName, key, StringComparison.OrdinalIgnoreCase));
        if (monitor is not null)
            ViewModel.SelectedMonitor = monitor;
    }

    private void Tile_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not Button tile || tile.Tag is not string key)
            return;

        if (!ViewModel.AreControlsEnabled)
        {
            if (ViewModel.DisabledControlsTip is string tip)
                ViewModel.Announce(tip);
            return;
        }

        var monitor = ViewModel.Monitors.FirstOrDefault(item =>
            string.Equals(item.GdiDeviceName, key, StringComparison.OrdinalIgnoreCase));
        if (monitor is not null)
            ViewModel.SelectedMonitor = monitor;

        if (_layout is null)
            return;

        var pointer = ContentPointer(e);
        _dragKey = key;
        _dragCommitted = false;
        _dragOffsetX = pointer.X - Canvas.GetLeft(tile);
        _dragOffsetY = pointer.Y - Canvas.GetTop(tile);
        _dragDipScale = XamlRoot?.RasterizationScale ?? 1.0;
        _dragSnapshot = ViewModel.GetCanvasSnapshot();
        _dragBaseline = ViewModel.GetCanvasSnapshotWithoutStage();
        _dragAlignedSlots = DisplayPlacementSolver.GetAlignedSlots(_dragSnapshot, key);
        _dragMaxDistancePx = DisplayPlacementSolver.ComputeMaxDistancePx(_dragDipScale, _layout?.Scale ?? 1.0);
        _dragSnapped = false;
        _dragPointerSeen = false;
        tile.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void Tile_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_dragKey is null || sender is not Button tile || _layout is null)
            return;

        if (!e.GetCurrentPoint(DisplayCanvas).Properties.IsLeftButtonPressed)
            return;

        var pointer = ContentPointer(e);
        var pointerLeft = pointer.X - _dragOffsetX;
        var pointerTop = pointer.Y - _dragOffsetY;
        (pointerLeft, pointerTop) = ClampTileToCanvas(tile, pointerLeft, pointerTop);

        var motion = new SnapMotion(false, 0, 0, false);
        if (_dragSnapshot is not null && ViewModel.AreControlsEnabled)
        {
            var desktop = DisplayGeometry.CanvasToDesktop(
                _layout,
                pointerLeft,
                pointerTop,
                _layoutOriginX,
                _layoutOriginY,
                _dragDipScale);
            _dragPointerX = desktop.X;
            _dragPointerY = desktop.Y;
            _dragPointerSeen = true;
            var candidates = _dragAlignedSlots is null
                ? DisplayPlacementSolver.GetCandidates(_dragSnapshot, _dragKey, desktop.X, desktop.Y)
                : DisplayPlacementSolver.MergeSlots(
                    DisplayPlacementSolver.GetPointerSlots(_dragSnapshot, _dragKey, desktop.X, desktop.Y),
                    _dragAlignedSlots);
            motion = DisplayPlacementSolver.ResolveSnapMotion(
                _dragSnapshot,
                _dragKey,
                desktop.X,
                desktop.Y,
                candidates,
                _dragMaxDistancePx,
                _dragSnapped,
                _dragSnappedX,
                _dragSnappedY);
        }

        if (motion.Snapped)
        {
            var canvas = DisplayGeometry.DesktopToCanvas(
                _layout,
                motion.DesktopX,
                motion.DesktopY,
                _layoutOriginX,
                _layoutOriginY,
                _dragDipScale);
            Canvas.SetLeft(tile, canvas.X);
            Canvas.SetTop(tile, canvas.Y);
            _dragSnapped = true;
            _dragSnappedX = motion.DesktopX;
            _dragSnappedY = motion.DesktopY;
        }
        else
        {
            Canvas.SetLeft(tile, pointerLeft);
            Canvas.SetTop(tile, pointerTop);
            _dragSnapped = false;
        }

        if (tile.Content is Border cue)
        {
            if (motion.LandingDiffersFromPointer)
            {
                cue.BorderBrush = FindBrush("AccentFillColorDefaultBrush");
                cue.BorderThickness = new Thickness(3);
            }
            else
            {
                cue.BorderBrush = FindBrush("CardStrokeColorDefaultBrush");
                cue.BorderThickness = new Thickness(1);
            }
        }

        e.Handled = true;
    }

    private (double X, double Y) ClampTileToCanvas(Button tile, double left, double top)
    {
        return DisplayGeometry.ClampTile(left, top, tile.ActualWidth, tile.ActualHeight, DisplayCanvas.ActualWidth, DisplayCanvas.ActualHeight);
    }

    private (double X, double Y) ContentPointer(PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(DisplayCanvas);
        return (point.Position.X, point.Position.Y);
    }

    private void ClearDragState()
    {
        _dragSnapshot = null;
        _dragBaseline = null;
        _dragAlignedSlots = null;
        _dragSnapped = false;
        _dragPointerSeen = false;
        _dragCommitted = false;
    }

    private void Tile_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_dragKey is null)
            return;

        var key = _dragKey;
        _dragKey = null;
        if (sender is Button tile)
        {
            tile.ReleasePointerCapture(e.Pointer);
            if (_layout is not null && !_dragCommitted)
            {
                _dragCommitted = true;
                CommitDrag(tile, key);
            }
        }

        ClearDragState();
        e.Handled = true;
    }

    private void Tile_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        if (_dragKey is null || _dragCommitted)
            return;

        var key = _dragKey;
        _dragKey = null;
        if (sender is Button tile && _layout is not null)
        {
            _dragCommitted = true;
            CommitDrag(tile, key);
        }

        ClearDragState();
    }

    private void CommitDrag(Button tile, string dragKey)
    {
        if (_layout is null || ViewModel.Snapshot is null || _dragSnapshot is null || _dragBaseline is null)
            return;

        if (!ViewModel.AreControlsEnabled)
        {
            RenderCanvas();
            return;
        }

        var working = ViewModel.GetCanvasSnapshotWithoutStage();
        if (!DisplayArrangement.AreEquivalent(working, _dragBaseline))
        {
            RenderCanvas();
            return;
        }

        var dipScale = XamlRoot?.RasterizationScale ?? 1.0;
        var layoutScale = _layout.Scale <= 0 ? 1.0 : _layout.Scale;
        var maxDistancePx = DisplayPlacementSolver.ComputeMaxDistancePx(dipScale, layoutScale);
        int desktopX;
        int desktopY;
        if (_dragPointerSeen)
        {
            desktopX = _dragPointerX;
            desktopY = _dragPointerY;
        }
        else
        {
            var canvasX = Canvas.GetLeft(tile);
            var canvasY = Canvas.GetTop(tile);
            var fallback = DisplayGeometry.CanvasToDesktop(_layout, canvasX, canvasY, _layoutOriginX, _layoutOriginY, dipScale);
            desktopX = fallback.X;
            desktopY = fallback.Y;
        }

        var result = DisplayPlacementSolver.TrySolve(working, dragKey, desktopX, desktopY, maxDistancePx);
        if (result.Arrangement is null)
        {
            RenderCanvas();
            const string overlap = "That layout would stack displays on top of each other, so it can't be used. Drag the displays apart and try again.";
            ViewModel.ShowStatus(overlap, DisplayNoticeKind.Refusal);
            ViewModel.Announce(overlap);
            return;
        }

        if (DisplayArrangement.AreEquivalent(working, result.Arrangement))
        {
            RenderCanvas();
            return;
        }

        if (!ViewModel.StageArrangement(result.Arrangement))
        {
            RenderCanvas();
            const string unplaced = "That spot could not be placed, so nothing changed.";
            ViewModel.ShowStatus(unplaced, DisplayNoticeKind.Failure);
            ViewModel.Announce(unplaced);
            return;
        }

        if (result.Reason != DisplayPlacementReason.Placed)
        {
            const string moved = "Moved it to the nearest spot that fits. Arrangement staged. Nothing applied yet.";
            ViewModel.ShowStatus(moved, DisplayNoticeKind.Staged);
            ViewModel.Announce(moved);
        }
        else if (result.Snapped)
        {
            ShowSnappedStatus(working, dragKey, result);
        }
        else
        {
            const string staged = "Arrangement staged. Nothing applied yet.";
            ViewModel.ShowStatus(staged, DisplayNoticeKind.Staged);
            ViewModel.Announce(staged);
        }

        RenderCanvas();
    }

    private void ShowSnappedStatus(DisplaySnapshot working, string dragKey, DisplayPlacementResult result)
    {
        if (result.AnchorKey is null || result.Side is null)
            return;

        ShowSnappedStatus(working, dragKey, result.AnchorKey, result.Side.Value);
    }

    private void ShowSnappedStatus(DisplaySnapshot working, string dragKey, string anchorKey, DisplayPlacementSide side)
    {
        var moving = working.Monitors.FirstOrDefault(monitor =>
            string.Equals(monitor.GdiDeviceName, dragKey, StringComparison.OrdinalIgnoreCase));
        var anchor = working.Monitors.FirstOrDefault(monitor =>
            string.Equals(monitor.GdiDeviceName, anchorKey, StringComparison.OrdinalIgnoreCase));
        if (moving is null || anchor is null)
            return;

        var relation = side switch
        {
            DisplayPlacementSide.Left => "to the left of",
            DisplayPlacementSide.Right => "to the right of",
            DisplayPlacementSide.Above => "above",
            DisplayPlacementSide.Below => "below",
            _ => "next to"
        };

        var message = $"Snapped display {moving.Ordinal} {relation} display {anchor.Ordinal}. Arrangement staged. Nothing applied yet.";
        ViewModel.ShowStatus(message, DisplayNoticeKind.Staged);
        ViewModel.Announce(message);
    }

    private static Brush? FindBrush(string key) =>
        Application.Current.Resources.TryGetValue(key, out var value) ? value as Brush : null;
}
