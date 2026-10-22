using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Switchit.Core;

namespace Switchit;

internal static class Palette
{
    public static readonly Brush Ink = Brush("#352F29"), Paper = Brush("#EBDFC5"), Light = Brush("#FFF1D4"),
        Shell = Brush("#CCBDA0"), Shade = Brush("#9F927C"), Muted = Brush("#807662"),
        Display = Brush("#3D453B"), Phosphor = Brush("#D6DDA5"), Coral = Brush("#D58364");
    public static SolidColorBrush Brush(string color)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(color)!;
        brush.Freeze(); return brush;
    }
    public static void Box(DrawingContext dc, Brush brush, double x, double y, double w, double h)
        => dc.DrawRectangle(brush, null, new Rect(x, y, w, h));
    public static void Polygon(DrawingContext dc, Brush brush, params Point[] points)
    {
        var g = new StreamGeometry();
        using (var c = g.Open()) { c.BeginFigure(points[0], true, true); c.PolyLineTo(points.Skip(1).ToArray(), true, false); }
        g.Freeze(); dc.DrawGeometry(brush, null, g);
    }
    public static void Text(DrawingContext dc, string value, double x, double y, double size, Brush color,
        double maxWidth = 0, bool bold = false, string font = "Malgun Gothic")
    {
        var text = new FormattedText(value, CultureInfo.GetCultureInfo("ko-KR"), FlowDirection.LeftToRight,
            new Typeface(new FontFamily(font), FontStyles.Normal, bold ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal), size, color, 1);
        if (maxWidth > 0) { text.MaxTextWidth = maxWidth; text.MaxLineCount = 1; text.Trimming = TextTrimming.CharacterEllipsis; }
        dc.DrawText(text, new Point(x, y));
    }
    public static string Time(TimeSpan value) => $"{(int)value.TotalHours:00}:{value.Minutes:00}";
    private static readonly Dictionary<char, string[]> Digits = new()
    {
        ['0'] = ["111", "101", "101", "101", "111"], ['1'] = ["010", "110", "010", "010", "111"],
        ['2'] = ["111", "001", "111", "100", "111"], ['3'] = ["111", "001", "111", "001", "111"],
        ['4'] = ["101", "101", "111", "001", "001"], ['5'] = ["111", "100", "111", "001", "111"],
        ['6'] = ["111", "100", "111", "101", "111"], ['7'] = ["111", "001", "010", "010", "010"],
        ['8'] = ["111", "101", "111", "101", "111"], ['9'] = ["111", "101", "111", "001", "111"],
        [':'] = ["0", "1", "0", "1", "0"]
    };
    public static void DigitalTime(DrawingContext dc, TimeSpan value, double x, double y, Brush color, bool colonVisible = true)
    {
        var str = Time(value); var pixel = str.Length > 5 ? 3 : 4;
        foreach (var ch in str)
        {
            if (!Digits.TryGetValue(ch, out var rows)) continue;
            for (var r = 0; r < rows.Length; r++)
                for (var c = 0; c < rows[r].Length; c++)
                    if (rows[r][c] == '1' && (ch != ':' || colonVisible)) Box(dc, color, x + c * pixel, y + r * pixel, pixel, pixel);
            x += (rows[0].Length + 1) * pixel;
        }
    }
}

internal sealed class ActivityModule : Button
{
    public ClockItem? Item { get; }
    public bool Active { get; set; }
    public bool ColonVisible { get; set; } = true;
    public bool Finished { get; set; }
    public TimeSpan MinimumRemaining { get; set; }
    public ActivityModule(ClockItem? item)
    {
        Item = item; Width = item is null ? 46 : 100; Height = 114;
        Template = new ControlTemplate(typeof(Button)); FocusVisualStyle = null;
        Cursor = Cursors.Hand; Margin = new Thickness(0, 0, 4, 0);
        ToolTip = item is null ? "새 항목 추가" : "클릭: 시간 측정 · 우클릭: 이름 / 색상 / 삭제";
        AutomationProperties.SetName(this, item?.Name ?? "항목 추가");
        AutomationProperties.SetAutomationId(this, item is null ? "AddItem" : "Activity-" + item.Id);
        SnapsToDevicePixels = true; UseLayoutRounding = true;
        MouseEnter += (_, _) => InvalidateVisual(); MouseLeave += (_, _) => InvalidateVisual();
        PreviewMouseLeftButtonDown += (_, _) => Dispatcher.BeginInvoke(InvalidateVisual);
        PreviewMouseLeftButtonUp += (_, _) => Dispatcher.BeginInvoke(InvalidateVisual);
        GotKeyboardFocus += (_, _) => InvalidateVisual(); LostKeyboardFocus += (_, _) => InvalidateVisual();
    }
    protected override void OnRender(DrawingContext dc)
    {
        RenderOptions.SetEdgeMode(this, EdgeMode.Aliased);
        var w = Width; var add = Item is null;
        var dark = Palette.Ink;
        Palette.Polygon(dc, dark, new(0, 42), new(12, 30), new(w - 12, 30), new(w, 42), new(w, 108), new(w - 3, 111), new(3, 111), new(0, 108));
        Palette.Polygon(dc, Palette.Light, new(4, 41), new(14, 33), new(w - 14, 33), new(w - 4, 41));
        Palette.Box(dc, Palette.Shell, 3, 44, w - 6, 64);
        Palette.Box(dc, Palette.Paper, 3, 44, w - 9, 19);
        Palette.Box(dc, Palette.Shade, w - 6, 44, 3, 64);
        Palette.Box(dc, Palette.Ink, 9, 111, 12, 3); Palette.Box(dc, Palette.Ink, w - 21, 111, 12, 3);
        Palette.Box(dc, Palette.Light, 6, 45, w - 15, 2);
        if (add)
        {
            Palette.Box(dc, Palette.Ink, 9, 56, 28, 30);
            Palette.Box(dc, IsMouseOver ? Palette.Light : Palette.Paper, 11, 58, 24, 24);
            Palette.Box(dc, Palette.Shade, 11, 82, 24, 2);
            Palette.Box(dc, Palette.Muted, 21, 62, 4, 16);
            Palette.Box(dc, Palette.Muted, 15, 68, 16, 4);
            return;
        }
        var color = Palette.Brush(Item!.Color);
        var y = (Active || IsPressed) ? 6d : 0d;
        // Stepped, deliberately coarse pixel silhouette: no curves or gradients.
        Palette.Box(dc, dark, 25, 27, 50, 15);
        Palette.Box(dc, Palette.Shade, 22, 36, 56, 5);
        Palette.Box(dc, dark, 31, 0 + y, 38, 3); Palette.Box(dc, dark, 25, 3 + y, 50, 3);
        Palette.Box(dc, dark, 22, 6 + y, 56, 21); Palette.Box(dc, dark, 25, 27 + y, 50, 9);
        Palette.Box(dc, color, 31, 3 + y, 38, 3); Palette.Box(dc, color, 25, 6 + y, 50, 18);
        Palette.Box(dc, color, 28, 24 + y, 44, 9);
        Palette.Box(dc, new SolidColorBrush(Color.FromArgb(64, 255, 241, 212)), 31, 6 + y, 35, 3);
        Palette.Box(dc, new SolidColorBrush(Color.FromArgb(55, 0, 0, 0)), 25, 21 + y, 50, 3);
        Palette.Box(dc, new SolidColorBrush(Color.FromArgb(70, 0, 0, 0)), 28, 24 + y, 44, 9);
        Palette.Box(dc, new SolidColorBrush(Color.FromArgb(40, 255, 240, 220)), 31, 24 + y, 3, 9);
        if (IsMouseOver && !Active) Palette.Box(dc, Palette.Light, 40, 12 + y, 20, 2);
        Palette.Box(dc, Active ? Palette.Phosphor : Palette.Shade, 8, 52, 4, 4);
        Palette.Text(dc, Item.Name, 17, 47, 10, Palette.Ink, 77, true);
        Palette.Box(dc, Palette.Ink, 6, 65, 88, 30);
        Palette.Box(dc, Palette.Display, 8, 67, 84, 26);
        Palette.DigitalTime(dc, Item.Elapsed, 14, 70, Active ? Palette.Phosphor : Palette.Brush("#B8BF9B"), !Active || ColonVisible);
        if (Active) Palette.Box(dc, color, 9, 102, 82, 3);
        if (IsKeyboardFocused) dc.DrawRectangle(null, new Pen(Palette.Coral, 1), new Rect(1, 44, w - 2, 65));
    }
    private static void DrawScrew(DrawingContext dc, double x, double y)
    {
        Palette.Box(dc, Palette.Shade, x, y, 9, 6);
        Palette.Box(dc, Palette.Ink, x + 3, y, 3, 6);
    }
}

internal sealed class ControlCase : FrameworkElement
{
    public ControlCase() { Width = 80; Height = 114; SnapsToDevicePixels = true; }
    protected override void OnRender(DrawingContext dc)
    {
        RenderOptions.SetEdgeMode(this, EdgeMode.Aliased);
        Palette.Polygon(dc, Palette.Ink, new(0, 42), new(12, 30), new(68, 30), new(80, 42), new(80, 108), new(77, 111), new(3, 111), new(0, 108));
        Palette.Polygon(dc, Palette.Light, new(4, 41), new(14, 33), new(66, 33), new(76, 41));
        Palette.Box(dc, Palette.Shell, 3, 44, 74, 64);
        Palette.Box(dc, Palette.Shade, 74, 44, 3, 64);
        Palette.Box(dc, Palette.Light, 6, 45, 65, 2);
        Palette.Box(dc, Palette.Ink, 9, 111, 12, 3);
        Palette.Box(dc, Palette.Ink, 59, 111, 12, 3);
    }
}

internal sealed class TotalDisplay : FrameworkElement
{
    public TimeSpan Elapsed { get; set; }
    public TotalDisplay() { Width = 80; Height = 26; SnapsToDevicePixels = true; ToolTip = "전체 사용 시간 · 시간:분"; }
    protected override void OnRender(DrawingContext dc)
    {
        RenderOptions.SetEdgeMode(this, EdgeMode.Aliased);
        Palette.Box(dc, Palette.Ink, 0, 0, 80, 26);
        Palette.Box(dc, Palette.Display, 2, 2, 76, 22);
        Palette.DigitalTime(dc, Elapsed, 4, 3, Palette.Phosphor);
    }
}

internal sealed class PixelButton : Button
{
    public PixelButton(string label, Brush? background = null)
    {
        Content = label; Background = background ?? Palette.Paper; Foreground = Palette.Ink;
        BorderBrush = Palette.Ink; BorderThickness = new Thickness(2); FontFamily = new FontFamily("Malgun Gothic");
        FontSize = 12; FontWeight = FontWeights.SemiBold; Padding = new Thickness(10, 6, 10, 6);
        Cursor = Cursors.Hand; MinHeight = 32; HorizontalContentAlignment = HorizontalAlignment.Center;
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        border.SetValue(Border.BorderBrushProperty, Palette.Ink); border.SetValue(Border.BorderThicknessProperty, new Thickness(2, 2, 2, 4));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        presenter.SetValue(FrameworkElement.MarginProperty, new Thickness(2, 0, 2, 0));
        border.AppendChild(presenter);
        var template = new ControlTemplate(typeof(Button)) { VisualTree = border };
        var hover = new Trigger { Property = IsMouseOverProperty, Value = true }; hover.Setters.Add(new Setter(OpacityProperty, .82)); template.Triggers.Add(hover);
        var press = new Trigger { Property = IsPressedProperty, Value = true }; press.Setters.Add(new Setter(RenderTransformProperty, new TranslateTransform(0, 2))); template.Triggers.Add(press);
        var disabled = new Trigger { Property = IsEnabledProperty, Value = false }; disabled.Setters.Add(new Setter(OpacityProperty, .45)); template.Triggers.Add(disabled);
        Template = template;
        AutomationProperties.SetName(this, label);
    }
}
