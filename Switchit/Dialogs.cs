using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Switchit.Core;

namespace Switchit;

public sealed record ItemEditResult(string Name, string Color);

public static class Dialogs
{
    private static readonly Brush Ink = Brush("#332D29");
    private static readonly Brush Paper = Brush("#E8DBB9");
    private static readonly Brush Muted = Brush("#776C5A");
    private static readonly string[] Colors = ["#D77D5B", "#93A981", "#D9B85F", "#85A5A4", "#B89BC1", "#D28D88"];
    private static readonly string[] ColorNames = ["살구", "세이지", "겨자", "청록", "라벤더", "장미"];

    public static ClockSettings? ShowSettings(Window owner, ClockSettings current)
    {
        ClockSettings? result = null;
        var shell = CreateShell(owner, "Switchit! / SETTINGS", "시계 설정", 422);
        var body = shell.Body;

        body.Children.Add(Label("최소 사용 시간", 14, true));
        var inputRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        var minutes = Input((current.MinimumSeconds / 60d).ToString("0.##", CultureInfo.InvariantCulture));
        minutes.Width = 98;
        inputRow.Children.Add(minutes);
        inputRow.Children.Add(new TextBlock { Text = "분", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0), Foreground = Ink });
        body.Children.Add(inputRow);
        body.Children.Add(Note("다른 업무로 바꾸기 전 채워야 하는 업무 시간입니다.\n휴식·일시정지는 언제든 가능하며 업무 시간은 유지됩니다.\n다른 업무로 전환하면 최소 시간을 새로 적용합니다."));

        var topmost = ToggleRow(body, "항상 위에 표시", current.AlwaysOnTop);
        var sound = ToggleRow(body, "버튼 효과음", current.SoundEnabled);

        body.Children.Add(new Border { Height = 1, Background = Brush("#BAAC8C"), Margin = new Thickness(0, 18, 0, 16) });
        body.Children.Add(Label("위젯 크기", 14, true));
        double selectedScale = Math.Clamp(current.Scale, 0.5, 1.25);
        var scales = new UniformGrid { Columns = 5, Margin = new Thickness(0, 9, 0, 2) };
        foreach (var scale in new[] { 0.5, 0.75, 0.8, 1.0, 1.25 })
        {
            var choice = new ToggleButton
            {
                Content = $"{scale * 100:0}%", Style = ToggleStyle(), Margin = new Thickness(0, 0, 5, 0),
                IsChecked = Math.Abs(selectedScale - scale) < 0.01, MinHeight = 36,
                Tag = scale
            };
            choice.Click += (_, _) =>
            {
                selectedScale = scale;
                foreach (ToggleButton other in scales.Children) other.IsChecked = ReferenceEquals(other, choice);
            };
            scales.Children.Add(choice);
        }
        body.Children.Add(scales);

        var error = ErrorLabel();
        body.Children.Add(error);
        AddFooter(shell, "저장", () =>
        {
            if (!double.TryParse(minutes.Text.Trim(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value)
                || value < 0 || value > 180)
            {
                error.Text = "최소 사용 시간을 0~180분 사이로 입력해 주세요.";
                minutes.Focus();
                return;
            }
            result = new ClockSettings
            {
                MinimumSeconds = (int)Math.Round(value * 60), AlwaysOnTop = topmost.IsChecked == true,
                SoundEnabled = sound.IsChecked == true, Scale = selectedScale
            };
            shell.Window.DialogResult = true;
        });
        shell.Window.ShowDialog();
        return result;
    }

    public static ItemEditResult? ShowItem(Window owner, string? currentName = null, string? currentColor = null)
    {
        ItemEditResult? result = null;
        bool editing = currentName is not null;
        var shell = CreateShell(owner, editing ? "Switchit! / EDIT MODULE" : "Switchit! / NEW MODULE", editing ? "항목 수정" : "새 항목 추가", 390);
        var body = shell.Body;
        body.Children.Add(Label("항목 이름", 14, true));
        var name = Input(currentName ?? "");
        name.MaxLength = 20;
        name.Margin = new Thickness(0, 9, 0, 0);
        body.Children.Add(name);
        body.Children.Add(Note("휴식, 프로젝트, 독서… 최대 20자"));
        body.Children.Add(new TextBlock { Text = "버튼 색상", FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Ink, Margin = new Thickness(0, 20, 0, 11) });
        string chosenColor = currentColor ?? Colors[0];
        var swatches = new UniformGrid { Columns = Colors.Length };
        foreach (string color in Colors)
        {
            var choice = new Button
            {
                Width = 42, Height = 42, Background = Brush(color), BorderBrush = Ink,
                BorderThickness = new Thickness(2), Style = FlatButtonStyle(), Margin = new Thickness(0, 0, 6, 0),
                Content = string.Equals(color, chosenColor, StringComparison.OrdinalIgnoreCase) ? "✓" : "",
                FontSize = 20, ToolTip = ColorNames[Array.IndexOf(Colors, color)], Tag = color
            };
            choice.Click += (_, _) =>
            {
                chosenColor = color;
                foreach (Button other in swatches.Children) other.Content = ReferenceEquals(other, choice) ? "✓" : "";
            };
            swatches.Children.Add(choice);
        }
        body.Children.Add(swatches);
        var error = ErrorLabel();
        body.Children.Add(error);
        AddFooter(shell, editing ? "저장" : "항목 추가", () =>
        {
            var value = name.Text.Trim();
            if (string.IsNullOrWhiteSpace(value))
            {
                error.Text = "항목 이름을 입력해 주세요.";
                name.Focus();
                return;
            }
            result = new ItemEditResult(value, chosenColor);
            shell.Window.DialogResult = true;
        });
        shell.Window.Loaded += (_, _) => { name.Focus(); name.SelectAll(); };
        shell.Window.ShowDialog();
        return result;
    }

    private sealed record DialogShell(Window Window, StackPanel Body, StackPanel Root);

    private static DialogShell CreateShell(Window owner, string caption, string title, double width)
    {
        var window = new Window
        {
            Owner = owner, Title = title, Width = width, SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, Topmost = owner.Topmost,
            Background = Paper, Foreground = Ink, FontFamily = new FontFamily("Malgun Gothic"), FontSize = 13,
            UseLayoutRounding = true, SnapsToDevicePixels = true
        };
        var root = new StackPanel();
        var border = new Border { BorderBrush = Ink, BorderThickness = new Thickness(3), Background = Paper, Child = root };
        window.Content = border;
        var header = new Grid { Background = Ink, Height = 35 };
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(35) });
        var text = new TextBlock
        {
            Text = caption, Foreground = Paper, FontFamily = new FontFamily("Consolas"), FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(13, 0, 0, 0)
        };
        header.Children.Add(text);
        var close = new Button { Content = "×", Style = FlatButtonStyle(), FontSize = 20, Foreground = Paper, Background = Ink, BorderThickness = new Thickness(0) };
        Grid.SetColumn(close, 1);
        header.Children.Add(close);
        close.Click += (_, _) => window.DialogResult = false;
        header.MouseLeftButtonDown += (_, e) => { if (e.OriginalSource is not Button) window.DragMove(); };
        root.Children.Add(header);
        var body = new StackPanel { Margin = new Thickness(23, 22, 23, 0) };
        body.Children.Add(new TextBlock { Text = title, FontSize = 22, FontWeight = FontWeights.Bold, Foreground = Ink, Margin = new Thickness(0, 0, 0, 23) });
        root.Children.Add(body);
        window.PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { window.DialogResult = false; e.Handled = true; } };
        window.Loaded += (_, _) =>
        {
            var area = MonitorArea.For(window);
            window.Left = Math.Clamp(window.Left, area.Left, Math.Max(area.Left, area.Right - window.ActualWidth));
            window.Top = Math.Clamp(window.Top, area.Top, Math.Max(area.Top, area.Bottom - window.ActualHeight));
        };
        return new DialogShell(window, body, root);
    }

    private static void AddFooter(DialogShell shell, string saveText, Action save)
    {
        var row = new Grid { Margin = new Thickness(23, 6, 23, 22) };
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition());
        var cancel = new Button { Content = "취소", Style = FlatButtonStyle(), MinHeight = 40, Background = Brush("#D6C7A7"), BorderBrush = Ink, BorderThickness = new Thickness(2), Margin = new Thickness(0, 0, 8, 0) };
        cancel.Click += (_, _) => shell.Window.DialogResult = false;
        row.Children.Add(cancel);
        var apply = new Button { Content = saveText, Style = FlatButtonStyle(), MinHeight = 40, Background = Brush("#D77D5B"), BorderBrush = Ink, BorderThickness = new Thickness(2), FontWeight = FontWeights.Bold, IsDefault = true };
        apply.Click += (_, _) => save();
        Grid.SetColumn(apply, 1);
        row.Children.Add(apply);
        shell.Root.Children.Add(row);
    }

    private static ToggleButton ToggleRow(Panel parent, string name, bool value)
    {
        var row = new Grid { Margin = new Thickness(0, 18, 0, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(82) });
        row.Children.Add(new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center, Foreground = Ink, FontSize = 14 });
        var toggle = new ToggleButton { Content = value ? "켜짐" : "꺼짐", Style = ToggleStyle(), IsChecked = value, MinHeight = 34 };
        toggle.Checked += (_, _) => toggle.Content = "켜짐";
        toggle.Unchecked += (_, _) => toggle.Content = "꺼짐";
        Grid.SetColumn(toggle, 1);
        row.Children.Add(toggle);
        parent.Children.Add(row);
        return toggle;
    }

    private static TextBox Input(string text) => new()
    {
        Text = text, FontSize = 16, Foreground = Ink, Background = Brush("#F7EFDA"),
        BorderBrush = Ink, BorderThickness = new Thickness(2), Padding = new Thickness(10, 8, 10, 8),
        CaretBrush = Ink, SelectionBrush = Brush("#D5AE7D"), VerticalContentAlignment = VerticalAlignment.Center
    };

    private static TextBlock Label(string text, double size, bool bold) => new()
    {
        Text = text, FontSize = size, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, Foreground = Ink
    };
    private static TextBlock Note(string text) => new()
    {
        Text = text, FontSize = 11, LineHeight = 18, Foreground = Muted, Margin = new Thickness(0, 9, 0, 0), TextWrapping = TextWrapping.Wrap
    };
    private static TextBlock ErrorLabel() => new()
    {
        Foreground = Brush("#A54232"), FontSize = 11, MinHeight = 27, Margin = new Thickness(0, 14, 0, 0), TextWrapping = TextWrapping.Wrap
    };
    private static SolidColorBrush Brush(string color) => (SolidColorBrush)new BrushConverter().ConvertFromString(color)!;

    private static Style ToggleStyle()
    {
        var style = FlatButtonStyle(typeof(ToggleButton));
        style.Setters.Add(new Setter(Control.BackgroundProperty, Brush("#D6C7A7")));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, Ink));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(2)));
        var selected = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true };
        selected.Setters.Add(new Setter(Control.BackgroundProperty, Brush("#93A981")));
        selected.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.Bold));
        style.Triggers.Add(selected);
        return style;
    }

    internal static Style FlatButtonStyle(Type? type = null)
    {
        type ??= typeof(Button);
        var style = new Style(type);
        style.Setters.Add(new Setter(Control.ForegroundProperty, Ink));
        style.Setters.Add(new Setter(Control.FontFamilyProperty, new FontFamily("Malgun Gothic")));
        style.Setters.Add(new Setter(Control.FontSizeProperty, 12d));
        style.Setters.Add(new Setter(FrameworkElement.CursorProperty, Cursors.Hand));
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        border.SetBinding(Border.BorderBrushProperty, new System.Windows.Data.Binding("BorderBrush") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        border.SetBinding(Border.BorderThicknessProperty, new System.Windows.Data.Binding("BorderThickness") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        content.SetValue(FrameworkElement.MarginProperty, new Thickness(5));
        border.AppendChild(content);
        style.Setters.Add(new Setter(Control.TemplateProperty, new ControlTemplate(type) { VisualTree = border }));
        var over = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        over.Setters.Add(new Setter(UIElement.OpacityProperty, .86));
        style.Triggers.Add(over);
        var pressed = new Trigger { Property = ButtonBase.IsPressedProperty, Value = true };
        pressed.Setters.Add(new Setter(UIElement.OpacityProperty, .68));
        style.Triggers.Add(pressed);
        return style;
    }
}
