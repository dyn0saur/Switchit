using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Switchit.Core;

namespace Switchit;

public sealed class ReceiptView : Border, IDisposable
{
    public event EventHandler? CopyRequested;
    private static readonly Brush Ink = new SolidColorBrush(Color.FromRgb(62, 53, 43));
    private static readonly Brush Paper = new SolidColorBrush(Color.FromRgb(250, 241, 216));
    private static readonly Brush HoverPaper = new SolidColorBrush(Color.FromRgb(255, 247, 228));
    private static readonly FontFamily ReceiptFont = new("Gulim, 굴림");
    private const string CopyHint = "우클릭하여 클립보드에 복사";
    private readonly Border _paper;
    private readonly System.Windows.Shapes.Path _edge;
    private readonly Border _overlay;
    private readonly TextBlock _overlayText;
    private readonly DispatcherTimer _feedbackTimer;
    private readonly List<Grid> _rows = [];
    private readonly FrameworkElement _barcode;
    private bool _isHovered;
    private bool _feedbackActive;
    private bool _disposed;
    private int _feedbackGeneration;
    private double _viewportOffset;
    private double _viewportHeight = double.PositiveInfinity;

    public ReceiptView(ReceiptData data)
    {
        Width = 324;
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
        Background = Brushes.Transparent;
        Cursor = Cursors.Hand;
        var layers = new Grid();
        Child = layers;
        var root = new StackPanel();
        layers.Children.Add(root);

        _paper = new Border { Background = Paper, Padding = new Thickness(24, 24, 24, 18) };
        var content = new StackPanel();
        _paper.Child = content;
        root.Children.Add(_paper);

        content.Children.Add(Text("TIME RECIPT", 21, true, TextAlignment.Center));
        content.Children.Add(Rule());
        content.Children.Add(Pair("날짜", data.StartedAt.ToLocalTime().ToString("yyyy.MM.dd", CultureInfo.InvariantCulture), 11));
        content.Children.Add(Pair("시작", data.StartedAt.ToLocalTime().ToString("MM.dd HH:mm:ss", CultureInfo.InvariantCulture), 11));
        content.Children.Add(Pair("종료", data.FinishedAt.ToLocalTime().ToString("MM.dd HH:mm:ss", CultureInfo.InvariantCulture), 11));
        string receiptId = data.Id.Split('-').Last().ToUpperInvariant();
        content.Children.Add(Pair("기록", receiptId, 10));
        content.Children.Add(Rule());
        content.Children.Add(Pair("항목", "사용 시간", 11, true));
        foreach (var item in data.Items)
        {
            var row = Pair(item.Name, Duration(item.Elapsed), 12);
            row.Margin = new Thickness(0, 6, 0, 6);
            content.Children.Add(row);
        }
        content.Children.Add(Rule());
        content.Children.Add(Pair("합계", Duration(data.DisplayTotalElapsed), 19, true));
        _barcode = Barcode(receiptId);
        content.Children.Add(_barcode);

        _edge = new System.Windows.Shapes.Path { Fill = Paper, Height = 10, Stretch = Stretch.None, Data = Zigzag(324) };
        root.Children.Add(_edge);

        _overlayText = Text(CopyHint, 11, false, TextAlignment.Center);
        _overlayText.Foreground = Paper;
        _overlayText.VerticalAlignment = VerticalAlignment.Center;
        _overlayText.TextWrapping = TextWrapping.NoWrap;
        _overlayText.TextTrimming = TextTrimming.CharacterEllipsis;
        _overlay = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(237, 51, 45, 41)),
            Padding = new Thickness(8, 9, 8, 9), Margin = new Thickness(10, 0, 10, 10),
            Height = 36, ClipToBounds = true,
            VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Stretch,
            IsHitTestVisible = false, Visibility = Visibility.Hidden, Opacity = 0,
            Child = _overlayText
        };
        layers.Children.Add(_overlay);

        _feedbackTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(2) };
        _feedbackTimer.Tick += OnFeedbackTimeout;
        MouseEnter += (_, _) => SetHoverState(true);
        MouseLeave += (_, _) => SetHoverState(false);
        AddHandler(Mouse.MouseUpEvent, new MouseButtonEventHandler((_, e) =>
        {
            if (e.ChangedButton != MouseButton.Right) return;
            e.Handled = true;
            CopyRequested?.Invoke(this, EventArgs.Empty);
        }), true);
        ContextMenuOpening += (_, e) => e.Handled = true;
        SizeChanged += (_, _) => UpdateOverlayPosition();
    }

    /// <summary>Resize the paper, never the font, to the physical opening of the printer.</summary>
    public void SetPaperWidth(double width)
    {
        if (!double.IsFinite(width) || width <= 0 || Math.Abs(Width - width) < 0.01) return;
        Width = width;
        var padding = width < 250 ? 12 : 24;
        _paper.Padding = new Thickness(padding, 24, padding, 18);
        _edge.Data = Zigzag(width);
        _barcode.Width = Math.Max(1, Math.Min(184, width - padding * 2));
        _barcode.ClipToBounds = true;
        var narrow = width < 220;
        foreach (var row in _rows)
        {
            row.ColumnDefinitions.Clear(); row.RowDefinitions.Clear();
            row.ColumnDefinitions.Add(new ColumnDefinition());
            if (narrow) { row.RowDefinitions.Add(new RowDefinition()); row.RowDefinitions.Add(new RowDefinition()); }
            else row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetRow(row.Children[1], narrow ? 1 : 0);
            Grid.SetColumn(row.Children[1], narrow ? 0 : 1);
            ((TextBlock)row.Children[0]).Margin = narrow ? new Thickness(0, 0, 0, 2) : new Thickness(0, 0, 8, 0);
        }
    }

    /// <summary>Keeps the transient overlay inside the visible part of a scrolling receipt.</summary>
    public void SetViewport(double verticalOffset, double viewportHeight)
    {
        if (_disposed) return;
        _viewportOffset = double.IsFinite(verticalOffset) ? Math.Max(0, verticalOffset) : 0;
        _viewportHeight = double.IsFinite(viewportHeight) && viewportHeight > 0 ? viewportHeight : double.PositiveInfinity;
        UpdateOverlayPosition();
    }

    private void UpdateOverlayPosition()
    {
        double unseenBottom = Math.Max(0, ActualHeight - (_viewportOffset + _viewportHeight));
        double maxBottom = Math.Max(10, ActualHeight - _overlay.Height - 10);
        double bottom = Math.Clamp(10 + unseenBottom, 10, maxBottom);
        _overlay.Margin = new Thickness(10, 0, 10, bottom);
    }

    /// <summary>Shows clipboard success or failure on the receipt without changing its layout.</summary>
    public void ShowCopyFeedback(string message = "클립보드에 복사되었습니다.")
    {
        if (_disposed) return;
        _feedbackGeneration++;
        _feedbackActive = true;
        ShowOverlay(message);
        _feedbackTimer.Stop();
        _feedbackTimer.Start();
    }

    private void SetHoverState(bool hovered)
    {
        if (_disposed) return;
        _isHovered = hovered;
        _paper.Background = hovered ? HoverPaper : Paper;
        _edge.Fill = hovered ? HoverPaper : Paper;
        if (_feedbackActive) return;
        if (hovered) ShowOverlay(CopyHint);
        else HideOverlay();
    }

    private void ShowOverlay(string message)
    {
        _overlay.BeginAnimation(OpacityProperty, null);
        _overlayText.Text = message;
        _overlay.Visibility = Visibility.Visible;
        _overlay.Opacity = 1;
    }

    private void HideOverlay()
    {
        _overlay.BeginAnimation(OpacityProperty, null);
        _overlay.Opacity = 0;
        _overlay.Visibility = Visibility.Hidden;
    }

    private void OnFeedbackTimeout(object? sender, EventArgs e)
    {
        _feedbackTimer.Stop();
        if (_disposed || !_feedbackActive) return;
        int generation = _feedbackGeneration;
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(220));
        fade.Completed += (_, _) =>
        {
            if (!_disposed && _feedbackActive && generation == _feedbackGeneration) ClearFeedback();
        };
        _overlay.BeginAnimation(OpacityProperty, fade);
    }

    private void ClearFeedback()
    {
        _feedbackTimer.Stop();
        _feedbackActive = false;
        if (_isHovered && !_disposed) ShowOverlay(CopyHint);
        else HideOverlay();
    }

    /// <summary>Call when removing the receipt to release its transient feedback timer.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _feedbackGeneration++;
        _feedbackTimer.Stop();
        _feedbackTimer.Tick -= OnFeedbackTimeout;
        ClearFeedback();
    }

    private static string Duration(TimeSpan value)
    {
        long seconds = Math.Max(0, (long)value.TotalSeconds);
        return $"{seconds / 3600:00}:{seconds / 60 % 60:00}:{seconds % 60:00}";
    }

    private static TextBlock Text(string value, double size, bool bold = false, TextAlignment align = TextAlignment.Left, Thickness? margin = null) => new()
    {
        Text = value, FontFamily = ReceiptFont, FontSize = size, FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
        Foreground = Ink, TextAlignment = align, Margin = margin ?? new Thickness(0), TextWrapping = TextWrapping.Wrap,
        SnapsToDevicePixels = true
    };

    private Grid Pair(string left, string right, double size, bool bold = false)
    {
        var grid = new Grid { Margin = new Thickness(0, 5, 0, 5) };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var name = Text(left, size, bold);
        name.Margin = new Thickness(0, 0, 8, 0);
        name.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(name);
        var amount = Text(right, size, bold, TextAlignment.Right);
        amount.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(amount, 1);
        grid.Children.Add(amount);
        _rows.Add(grid);
        return grid;
    }

    private static FrameworkElement Rule() => new System.Windows.Shapes.Path
    {
        Data = new LineGeometry(new Point(0, 0), new Point(1, 0)), Stroke = Ink, Stretch = Stretch.Fill,
        StrokeThickness = 1, StrokeDashArray = new DoubleCollection([3, 3]), Height = 1,
        Margin = new Thickness(0, 13, 0, 13), SnapsToDevicePixels = true
    };

    private static Geometry Zigzag(double width)
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(new Point(0, 0), true, true);
            context.LineTo(new Point(width, 0), true, false);
            for (double x = width; x > 0; x -= 12)
            {
                context.LineTo(new Point(x - 6, 9), true, false);
                context.LineTo(new Point(Math.Max(0, x - 12), 0), true, false);
            }
        }
        geometry.Freeze();
        return geometry;
    }

    private static FrameworkElement Barcode(string value)
    {
        var canvas = new Canvas { Height = 25, Width = 184, Margin = new Thickness(0, 20, 0, 0), HorizontalAlignment = HorizontalAlignment.Center };
        int code = 17;
        foreach (char c in value) code = unchecked(code * 31 + c);
        var random = new Random(code);
        double x = 0;
        while (x < 183)
        {
            int width = random.Next(1, 4);
            var bar = new Rectangle { Width = width, Height = 25, Fill = Ink };
            Canvas.SetLeft(bar, x);
            canvas.Children.Add(bar);
            x += width + random.Next(1, 4);
        }
        return canvas;
    }
}
