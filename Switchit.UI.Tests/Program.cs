using System.Collections;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Switchit.Core;

namespace Switchit.UI.Tests;

internal static class Program
{
    private static readonly List<CheckResult> Results = [];
    private static string _artifacts = "";

    [STAThread]
    public static int Main(string[] args)
    {
        _artifacts = Path.GetFullPath(args.Length > 0 ? args[0] : Path.Combine(AppContext.BaseDirectory, "artifacts"));
        Directory.CreateDirectory(_artifacts);
        // A linked icon resource resolves MainWindow's pack URI in this test entry assembly.
        var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var dataDirectory = Path.Combine(Path.GetTempPath(), "Switchit.UI.Tests-" + Guid.NewGuid().ToString("N"));
        MainWindow? window = null;
        try
        {
            window = new MainWindow(dataDirectory);
            // A never-shown Window has NaN position defaults; give close-time persistence finite coordinates.
            window.Left = 0;
            window.Top = 0;
            var engine = Field<ClockEngine>(window, "_engine");
            engine.Settings.SoundEnabled = false;
            var root = (FrameworkElement)window.Content;
            // An unshown Window retains its initial layout constraint. Detach only its content for
            // independent off-screen layout; all actual MainWindow handlers and fields remain wired.
            window.Content = null;
            var modules = Modules(window);
            var finish = Field<Button>(window, "_finish");
            var pause = Field<Button>(window, "_pause");
            var receiptScroll = Field<ScrollViewer>(window, "_receiptScroll");
            var footprint = Layout(root);

            Check("Widget is borderless, transparent, and never shown", () =>
            {
                Equal(WindowStyle.None, window.WindowStyle);
                True(window.AllowsTransparency, "Transparent background must be enabled.");
                True(!window.IsVisible && !window.IsLoaded, "The integration test must not display a window.");
                Equal(IntPtr.Zero, new WindowInteropHelper(window).Handle);
            });

            Check("Three-task footprint is less than one quarter of the original baseline", () =>
            {
                Equal(3, modules.Count);
                True(footprint.Width * footprint.Height < 810 * 357 / 4.0,
                    $"Widget measured {footprint.Width} x {footprint.Height}; quarter baseline is {810 * 357 / 4.0} square DIPs.");
                True(footprint.Width <= 460 && footprint.Height <= 160, "Unexpected compact widget dimensions.");
            }, new { width = footprint.Width, height = footprint.Height, areaRatio = footprint.Width * footprint.Height / (810 * 357) });

            Check("Five control buttons and add button have accessible names", () =>
            {
                var buttons = Descendants<Button>(root).ToList();
                foreach (var name in new[] { "설정", "일시정지", "최소화", "닫기", "종료 · 영수증 출력", "항목 추가" })
                    Equal(1, buttons.Count(button => AutomationProperties.GetName(button) == name));
                Equal(9, buttons.Count);
                True(!pause.IsEnabled && !finish.IsEnabled, "Pause and finish must start disabled.");
                Equal("종료", finish.Content);
            });

            Check("Main widget has no header or obsolete helper text", () =>
            {
                var visibleText = Descendants<TextBlock>(root).Select(text => text.Text).ToArray();
                True(!visibleText.Any(text => text.Contains("WESSCLOCK", StringComparison.OrdinalIgnoreCase) ||
                    text.Contains("READY") || text.Contains("TOTAL") || text.Contains("버튼을 눌러 하루")),
                    "A removed title or helper text is still in the visual tree.");
                Equal(3, ((Panel)root).Children.Count);
                var timeMethod = typeof(MainWindow).Assembly.GetType("Switchit.Palette")!
                    .GetMethod("Time", BindingFlags.Public | BindingFlags.Static)!;
                Equal("27:08", (string)timeMethod.Invoke(null, [TimeSpan.FromHours(27) + TimeSpan.FromMinutes(8) + TimeSpan.FromSeconds(59)])!);
            });

            engine.Settings.MinimumSeconds = 60;
            Click(modules[1]);
            Thread.Sleep(1100);
            engine.Tick();
            Invoke(window, "UpdateDisplay");
            Check("Task button starts only its own stopwatch through the UI handler", () =>
            {
                Equal(engine.Items[1].Id, engine.ActiveItemId!.Value);
                True(engine.Items[1].Elapsed >= TimeSpan.FromSeconds(1), "The running clock did not advance.");
                Equal(TimeSpan.Zero, engine.Items[0].Elapsed);
                Equal(TimeSpan.Zero, engine.Items[2].Elapsed);
                True(pause.IsEnabled && finish.IsEnabled, "Controls did not reflect the active session.");
                var totalElement = Field<FrameworkElement>(window, "_totalDisplay");
                Equal(engine.TotalElapsed, (TimeSpan)totalElement.GetType().GetProperty("Elapsed")!.GetValue(totalElement)!);
            });
            SaveImage(root, "compact-widget.png");

            Click(modules[2]);
            Check("Minimum focus time blocks UI switching but allows pausing", () =>
            {
                Equal(engine.Items[1].Id, engine.ActiveItemId!.Value);
                Equal(TimeSpan.Zero, engine.Items[2].Elapsed);
                True(!engine.IsPaused && engine.MinimumRemaining > TimeSpan.Zero, "The minimum period was bypassed.");
                True(Field<TextBlock>(window, "_toastText").Text.Contains("최소 사용 시간"), "Blocking feedback was not shown.");
                Equal(footprint, Layout(root));
                var popup = Field<Popup>(window, "_toastPopup");
                True(!popup.IsOpen, "Hidden UI validation must never open a toast window.");
                True(ReferenceEquals(root, popup.PlacementTarget), "Toast is not anchored to the widget.");
                var toast = Field<Border>(window, "_toast");
                True(!toast.IsHitTestVisible, "Toast would intercept widget mouse input.");
                var toastSize = Layout(toast);
                var placement = popup.CustomPopupPlacementCallback!(toastSize, footprint, new Point())[0];
                True(placement.Point.Y + toastSize.Height <= 0, "Toast placement overlaps the widget instead of appearing above it.");
                Click(pause);
                True(engine.IsPaused && engine.ActiveItemId is null, "Pause was blocked during minimum focus time.");
                True(!pause.IsEnabled && finish.IsEnabled, "Paused session controls did not update.");
            });
            Click(modules[1]);
            SaveImage(Field<Border>(window, "_toast"), "toast-preview.png");

            // A nonactive task can be removed while another task is in its minimum period.
            var inactiveToRemove = engine.Items[0].Id;
            var removeMenuItem = (MenuItem)modules[0].ContextMenu.Items[1];
            removeMenuItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Check("Context-menu removal archives a task without disturbing the active timer", () =>
            {
                True(engine.Items.Single(item => item.Id == inactiveToRemove).IsArchived, "The task was not archived.");
                Equal(2, Modules(window).Count);
                Equal(engine.Items[1].Id, engine.ActiveItemId!.Value);
            });

            Click(finish);
            var receipt = engine.LastReceipt!;
            var receiptView = receiptScroll.Content as ReceiptView;
            Check("Finish UI bypasses minimum time and prints and saves the receipt", () =>
            {
                True(engine.HasFinished && engine.IsPaused, "Finish did not stop the active session.");
                True(receipt is not null && receiptView is not null, "No receipt was produced.");
                Equal(Visibility.Visible, receiptScroll.Visibility);
                True(File.Exists(Path.Combine(dataDirectory, "receipts", receipt!.Id + ".txt")), "Text receipt was not saved.");
                True(File.Exists(Path.Combine(dataDirectory, "receipts", receipt.Id + ".json")), "JSON receipt was not saved.");
                Equal(receipt.ToPlainText(), File.ReadAllText(Path.Combine(dataDirectory, "receipts", receipt.Id + ".txt")));
                Equal("다시시작", finish.Content);
                Equal("다시시작", AutomationProperties.GetName(finish));
                True(finish.IsEnabled, "The same finish button must remain available for restarting.");
            });

            var preview = new ReceiptView(receipt);
            Check("Receipt matches the slot at every widget scale while text remains fixed", () =>
            {
                True(receiptScroll.Clip is RectangleGeometry, "Printing must reveal downward from the slot.");
                receiptScroll.Clip = null;
                var originalScale = engine.Settings.Scale;
                try
                {
                    foreach (var scale in new[] { 0.5, 0.8, 1.25 })
                    {
                        engine.Settings.Scale = scale;
                        Invoke(window, "ApplySettings");
                        Layout(root); Invoke(window, "SyncReceiptLayout"); Layout(root);
                        var slot = Field<Border>(window, "_printerSlot");
                        var slotOrigin = slot.TransformToAncestor(root).Transform(new Point(2, 2));
                        var paperOrigin = receiptView!.TransformToAncestor(root).Transform(new Point());
                        True(Math.Abs((paperOrigin.X - slotOrigin.X) * scale) < 1.5, "Receipt left edge does not match slot.");
                        True(Math.Abs((paperOrigin.Y - slotOrigin.Y) * scale) < 1.5, "Receipt does not start inside slot.");
                        var paperEnd = receiptView.TransformToAncestor(root).Transform(new Point(receiptView.ActualWidth, 0));
                        True(Math.Abs((paperEnd.X - paperOrigin.X) * scale - (slot.ActualWidth - 4) * scale) < 1.5, "Receipt width differs from slot opening.");
                        var bodyText = Descendants<TextBlock>(Field<Border>(receiptView, "_paper")).First(text => text.FontSize == 12);
                        var origin = bodyText.TransformToAncestor(root).Transform(new Point());
                        var unit = bodyText.TransformToAncestor(root).Transform(new Point(0, 1));
                        True(Math.Abs((unit.Y - origin.Y) * scale - 1) < 0.001, "Receipt font is being scaled with widget.");
                        Equal(12d, bodyText.FontSize);
                        SaveImage(root, $"receipt-widget-{scale * 100:0}.png");
                    }
                }
                finally { engine.Settings.Scale = originalScale; Invoke(window, "ApplySettings"); Layout(root); }
            });
            var receiptSize = Layout(preview, 324);
            SaveImage(preview, "receipt-normal.png", 324);
            Check("Receipt uses exact TIME RECIPT title and only statement content", () =>
            {
                var paper = Field<Border>(preview, "_paper");
                var text = Descendants<TextBlock>(paper).ToList();
                True(text.Count > 10 && text.All(block => block.FontFamily.Source.Contains("Gulim")), "Receipt text does not consistently use Gulim.");
                Equal("TIME RECIPT", text[0].Text);
                True(text.Any(block => block.Text == ReceiptData.FormatDuration(receipt.DisplayTotalElapsed)), "Receipt total is not displayed to seconds.");
                Equal(0, Descendants<Button>(preview).Count());
                foreach (var removed in new[] { "WESSCLOCK", "당신이 쌓은 시간", "잘 보낸 시간도", "모두 당신의 시간", "새 기록 시작", "우클릭" })
                    True(!text.Any(block => block.Text.Contains(removed)), "Removed permanent receipt text remains: " + removed);
                Equal("TIME RECIPT", receipt.ToPlainText().Split('\n')[0].TrimEnd('\r'));
                True(!receipt.ToPlainText().Contains("수고하셨습니다"), "Removed prose remains in the copied receipt.");
            });

            Invoke(preview, "SetHoverState", true);
            var hoverSize = Layout(preview, 324);
            SaveImage(preview, "receipt-hover.png", 324);
            Check("Hover presents the copy hint locally without changing receipt dimensions", () =>
            {
                Equal(receiptSize, hoverSize);
                Equal(Visibility.Visible, Field<Border>(preview, "_overlay").Visibility);
                True(Field<TextBlock>(preview, "_overlayText").Text.Contains("우클릭"), "Hover copy guidance is absent.");
                True(!Field<Border>(preview, "_overlay").IsHitTestVisible, "Hover overlay would intercept receipt clicks.");
                Invoke(preview, "SetHoverState", false);
                Equal(Visibility.Hidden, Field<Border>(preview, "_overlay").Visibility);
                Equal(receiptSize, Layout(preview, 324));
            });

            preview.ShowCopyFeedback("클립보드에 복사되었습니다.");
            SaveImage(preview, "receipt-copied.png", 324);
            Check("Copy feedback stays inside the receipt and preserves its dimensions", () =>
            {
                Equal(receiptSize, Layout(preview, 324));
                Equal(Visibility.Visible, Field<Border>(preview, "_overlay").Visibility);
                Equal("클립보드에 복사되었습니다.", Field<TextBlock>(preview, "_overlayText").Text);
                var timer = Field<System.Windows.Threading.DispatcherTimer>(preview, "_feedbackTimer");
                True(timer.IsEnabled && timer.Interval > TimeSpan.Zero && timer.Interval <= TimeSpan.FromSeconds(4), "Feedback has no short expiry timer.");
                Invoke(preview, "SetHoverState", false);
                Equal(Visibility.Visible, Field<Border>(preview, "_overlay").Visibility);
                Equal("클립보드에 복사되었습니다.", Field<TextBlock>(preview, "_overlayText").Text);
            });

            Check("Feedback expiry restores hovered guidance or hides the local overlay", () =>
            {
                Invoke(preview, "SetHoverState", true);
                Invoke(preview, "OnFeedbackTimeout", null!, EventArgs.Empty);
                True(Field<Border>(preview, "_overlay").HasAnimatedProperties, "Feedback timeout did not start its fade.");
                True(!Field<System.Windows.Threading.DispatcherTimer>(preview, "_feedbackTimer").IsEnabled, "Feedback expiry timer kept running.");
                Invoke(preview, "ClearFeedback");
                Equal(Visibility.Visible, Field<Border>(preview, "_overlay").Visibility);
                True(Field<TextBlock>(preview, "_overlayText").Text.Contains("우클릭"), "Hovered copy guidance was not restored after feedback expired.");
                True(!Field<bool>(preview, "_feedbackActive"), "Feedback state did not clear.");
                preview.ShowCopyFeedback("클립보드에 복사되었습니다.");
                Invoke(preview, "SetHoverState", false);
                Invoke(preview, "ClearFeedback");
                Equal(Visibility.Hidden, Field<Border>(preview, "_overlay").Visibility);
                True(!Field<bool>(preview, "_feedbackActive"), "Feedback state did not clear after leaving.");
                Equal(receiptSize, Layout(preview, 324));
            });

            Check("Long receipt guidance stays in the visible viewport without growing the paper", () =>
            {
                var longData = new ReceiptData
                {
                    Id = receipt.Id, StartedAt = receipt.StartedAt, FinishedAt = receipt.FinishedAt,
                    Items = Enumerable.Range(1, 30).Select(index => new ReceiptLine
                    {
                        Name = "업무 " + index, Color = "#88A789", Elapsed = TimeSpan.FromMinutes(index)
                    }).ToList()
                };
                using var longView = new ReceiptView(longData);
                var fullSize = Layout(longView, 324);
                True(fullSize.Height > 800, "Test receipt is not long enough to exercise scrolling.");
                Invoke(longView, "SetHoverState", true);
                foreach (var offset in new[] { 0d, 350d })
                {
                    longView.SetViewport(offset, 210);
                    Equal(fullSize, Layout(longView, 324));
                    var overlay = Field<Border>(longView, "_overlay");
                    var top = overlay.TransformToAncestor(longView).Transform(new Point()).Y;
                    True(top >= offset && top + overlay.ActualHeight <= offset + 210,
                        $"Copy guidance is outside the viewport: top={top}, height={overlay.ActualHeight}, offset={offset}.");
                    longView.ShowCopyFeedback();
                    Equal(fullSize, Layout(longView, 324));
                    Equal(Visibility.Visible, overlay.Visibility);
                }
            });

            Check("Real feedback timer and fade automatically hide the overlay", () =>
            {
                Invoke(preview, "SetHoverState", false);
                preview.ShowCopyFeedback();
                var overlay = Field<Border>(preview, "_overlay");
                Equal(Visibility.Visible, overlay.Visibility);
                True(PumpUntil(() => overlay.Visibility == Visibility.Hidden, TimeSpan.FromSeconds(3.5)),
                    "The actual feedback timer and fade did not hide the overlay within 3.5 seconds.");
                True(!Field<bool>(preview, "_feedbackActive"), "Timed expiry left feedback active.");
                Equal(0d, overlay.Opacity);
                Equal(receiptSize, Layout(preview, 324));
            });

            Field<Border>(window, "_toast").Visibility = Visibility.Collapsed;
            Field<TextBlock>(window, "_toastText").Text = "";
            var rootBeforeCopy = Layout(root);
            Check("Receipt right-click copies exact text with local feedback and no global toast", () =>
            {
                var paper = Field<Border>(receiptView!, "_paper");
                var copyRequests = 0;
                receiptView!.CopyRequested += (_, _) => copyRequests++;
                paper.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Right)
                { RoutedEvent = Mouse.MouseUpEvent, Source = paper });
                Equal(1, copyRequests);
                True(PumpUntil(() => Field<bool>(receiptView, "_feedbackActive"), TimeSpan.FromSeconds(1)), "The actual receipt copy handler did not confirm completion.");
                Equal("클립보드에 복사되었습니다.", Field<TextBlock>(receiptView!, "_overlayText").Text);
                // Read back only after the copy handler confirmed our generated receipt was written.
                True(receipt.ToPlainText() == Clipboard.GetText(), "Copied receipt text does not match the generated statement.");
                Equal(Visibility.Visible, Field<Border>(receiptView!, "_overlay").Visibility);
                Equal(Visibility.Collapsed, Field<Border>(window, "_toast").Visibility);
                Equal("", Field<TextBlock>(window, "_toastText").Text);
                Equal(rootBeforeCopy, Layout(root));
                paper.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
                { RoutedEvent = Mouse.MouseUpEvent, Source = paper });
                Equal(1, copyRequests);
            });

            var receiptFile = Path.Combine(dataDirectory, "receipts", receipt.Id + ".txt");
            var receiptFileBeforeRestart = File.ReadAllBytes(receiptFile);
            Click(finish);
            Check("The same finish button restarts, clears the receipt and resets to disabled 종료", () =>
            {
                True(!engine.HasFinished && !engine.HasSession && engine.IsPaused, "New session did not reset the state.");
                Equal(TimeSpan.Zero, engine.TotalElapsed);
                True(engine.Items.All(item => !item.IsArchived), "Archived items remain in the new session.");
                True(engine.Items.All(item => item.Id != inactiveToRemove), "Removed task was restored unexpectedly.");
                Equal(Visibility.Collapsed, receiptScroll.Visibility);
                True(receiptScroll.Content is null, "Old receipt remains attached.");
                Equal(receipt.Id, engine.LastReceipt!.Id);
                True(!finish.IsEnabled && !pause.IsEnabled, "Session controls did not reset.");
                Equal("종료", finish.Content);
                Equal("종료 · 영수증 출력", AutomationProperties.GetName(finish));
                True(ReferenceEquals(finish, Field<Button>(window, "_finish")), "The restart control replaced the existing button.");
                True(receiptFileBeforeRestart.SequenceEqual(File.ReadAllBytes(receiptFile)), "Restart changed the saved receipt.");
                Equal(TimeSpan.Zero, (TimeSpan)Field<FrameworkElement>(window, "_totalDisplay").GetType().GetProperty("Elapsed")!.GetValue(Field<FrameworkElement>(window, "_totalDisplay"))!);
            });
            preview.Dispose();

            Click(Modules(window)[0]);
            Check("Starting a task after restart re-enables the same finish button", () =>
            {
                True(finish.IsEnabled && engine.HasSession && !engine.HasFinished, "The restarted session cannot be finished.");
                Equal("종료", finish.Content);
            });

            for (var i = 0; i < 30; i++) engine.AddItem("추가 항목 " + (i + 1), "#88A789");
            Invoke(window, "RebuildModules");
            Field<Border>(window, "_toast").Visibility = Visibility.Collapsed;
            var expanded = Layout(root);
            Check("Many tasks scroll while all five controls stay available", () =>
            {
                var scroll = Field<ScrollViewer>(window, "_moduleScroll");
                True(scroll.ExtentWidth > scroll.ViewportWidth, "The activity strip did not scroll.");
                var buttons = Descendants<Button>(root).ToList();
                foreach (var name in new[] { "설정", "일시정지", "최소화", "닫기", "종료 · 영수증 출력" })
                {
                    var button = buttons.Single(item => AutomationProperties.GetName(item) == name);
                    var offset = button.TransformToAncestor(root).Transform(new Point());
                    // TransformToAncestor(root) is in the root's unscaled coordinate space.
                    True(offset.X >= scroll.ActualWidth && offset.X + button.ActualWidth <= root.ActualWidth + 1,
                        $"Control {name}: x={offset.X}, width={button.ActualWidth}; scroll width={scroll.ActualWidth}; layout width={expanded.Width}; actual root width={root.ActualWidth}.");
                }
            });

            Check("No integration action ever creates or displays a window", () =>
            {
                True(!window.IsVisible && !window.IsLoaded, "The test unexpectedly displayed the window.");
                Equal(IntPtr.Zero, new WindowInteropHelper(window).Handle);
            });
        }
        catch (Exception exception)
        {
            Results.Add(new CheckResult("Integration harness", false, exception.ToString(), null));
            Console.Error.WriteLine(exception);
        }
        finally
        {
            window?.Close();
            application.Shutdown();
            if (Directory.Exists(dataDirectory)) Directory.Delete(dataDirectory, recursive: true);
        }

        var failed = Results.Count(result => !result.Passed);
        File.WriteAllText(Path.Combine(_artifacts, "ui-test-results.json"), JsonSerializer.Serialize(new
        {
            testMode = "hidden WPF content; no displayed windows; isolated temporary state",
            passed = Results.Count - failed, total = Results.Count, results = Results
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"\n{Results.Count - failed}/{Results.Count} UI checks passed. Artifacts: {_artifacts}");
        return failed == 0 ? 0 : 1;
    }

    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(target)!;
    private static void Invoke(object target, string name, params object[] args) => target.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(target, args);
    private static List<Button> Modules(MainWindow window) => Field<IEnumerable>(window, "_moduleViews").Cast<Button>().ToList();
    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static bool PumpUntil(Func<bool> condition, TimeSpan timeout)
    {
        if (condition()) return true;
        var deadline = System.Diagnostics.Stopwatch.StartNew();
        var frame = new DispatcherFrame();
        var poll = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(20) };
        poll.Tick += (_, _) => { if (condition() || deadline.Elapsed >= timeout) frame.Continue = false; };
        poll.Start();
        try { Dispatcher.PushFrame(frame); }
        finally { poll.Stop(); }
        return condition();
    }
    private static void Check(string name, Action assertion, object? evidence = null)
    {
        try { assertion(); Results.Add(new(name, true, null, evidence)); Console.WriteLine("PASS  " + name); }
        catch (Exception exception) { Results.Add(new(name, false, exception.Message, evidence)); Console.Error.WriteLine("FAIL  " + name + " — " + exception.Message); }
    }
    private static Size Layout(FrameworkElement element, double width = double.PositiveInfinity)
    {
        foreach (var child in Descendants<UIElement>(element).Reverse()) child.InvalidateMeasure();
        element.Measure(new Size(width, double.PositiveInfinity));
        var size = element.DesiredSize;
        element.Arrange(new Rect(new Point(), size));
        element.UpdateLayout();
        return size;
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T item) yield return item;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (var child in Descendants<T>(VisualTreeHelper.GetChild(root, index))) yield return child;
    }
    private static void SaveImage(FrameworkElement visual, string name, double width = double.PositiveInfinity)
    {
        var size = Layout(visual, width);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(size.Width), (int)Math.Ceiling(size.Height), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
        bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
        True(pixels.Where((_, i) => i % 4 == 3).Count(alpha => alpha != 0) > bitmap.PixelWidth * bitmap.PixelHeight / 3,
            "Rendered snapshot was empty or mostly blank: " + name);
        using var stream = File.Create(Path.Combine(_artifacts, name));
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        encoder.Save(stream);
        Console.WriteLine($"IMAGE {name}: {bitmap.PixelWidth} x {bitmap.PixelHeight}");
    }
    private static void True(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException($"Expected [{expected}], actual [{actual}].");
    }
    private sealed record CheckResult(string Name, bool Passed, string? Error, object? Evidence);
}
