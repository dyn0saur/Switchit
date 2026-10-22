using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using Switchit.Core;

namespace Switchit;

public sealed class MainWindow : Window
{
    private readonly ClockEngine _engine;
    private readonly StateStore _store;
    private readonly string _dataDirectory;
    private readonly StackPanel _root = new();
    private readonly StackPanel _modules = new() { Orientation = Orientation.Horizontal };
    private readonly List<ActivityModule> _moduleViews = [];
    private readonly TotalDisplay _totalDisplay = new();
    private readonly TextBlock _toastText = Label("", 12, true);
    private readonly Border _toast = new() { Background = Palette.Ink, Padding = new Thickness(12, 8, 12, 8), Visibility = Visibility.Collapsed, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly Popup _toastPopup = new()
    {
        AllowsTransparency = true, StaysOpen = true, Focusable = false,
        IsHitTestVisible = false, Placement = PlacementMode.Custom, PopupAnimation = PopupAnimation.Fade
    };
    private readonly ScrollViewer _moduleScroll = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };
    private readonly ScrollViewer _receiptScroll = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, HorizontalAlignment = HorizontalAlignment.Left, HorizontalContentAlignment = HorizontalAlignment.Stretch, ClipToBounds = true, Visibility = Visibility.Collapsed };
    private Border _printerBase = null!;
    private Border _printerSlot = null!;
    private readonly PixelButton _pause = new("Ⅱ");
    private readonly PixelButton _finish = new("종료", Palette.Coral);
    private readonly DispatcherTimer _clockTimer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private readonly DispatcherTimer _blinkTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private Guid? _blinkingItemId;
    private bool _colonVisible = true;
    private DateTime _lastSave = DateTime.MinValue;
    private DateTime _toastUntil;
    private bool _loaded, _saveErrorShown, _closing;
    private Point? _savedPosition;
    private MonitorArea.SavedPosition? _savedPixelPosition;

    public MainWindow(string dataDirectory, bool resumeSession = false)
    {
        _dataDirectory = dataDirectory;
        _store = new StateStore(dataDirectory);
        _engine = _store.Load(resumeSession);
        Title = "Switchit!";
        Icon = BitmapFrame.Create(new Uri("pack://application:,,,/Switchit.ico"));
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize; SizeToContent = SizeToContent.WidthAndHeight;
        ShowInTaskbar = true; Topmost = _engine.Settings.AlwaysOnTop;
        FontFamily = new FontFamily("Malgun Gothic"); UseLayoutRounding = true; SnapsToDevicePixels = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        AutomationProperties.SetName(this, "Switchit! 시간 측정 위젯");
        Content = _root;
        BuildChrome();
        RebuildModules();
        ApplySettings();
        LoadPosition();
        Loaded += (_, _) =>
        {
            var work = SystemParameters.WorkArea;
            if (_savedPixelPosition is { } position) MonitorArea.Restore(this, position);
            else
            {
                Left = _savedPosition?.X ?? work.Right - ActualWidth - 36;
                Top = _savedPosition?.Y ?? work.Top + 72;
            }
            ResizeWidget();
            // A monitor change can change DPI and the measured window size. Restore again after layout settles.
            Dispatcher.BeginInvoke(() =>
            {
                UpdateLayout();
                if (_savedPixelPosition is { } finalPosition) MonitorArea.Restore(this, finalPosition);
                _loaded = true;
                if (_savedPixelPosition is null) ClampToScreen();
                UpdateDisplay(); SavePosition();
                if (_store.LastLoadWarning is { } warning) Toast(warning);
                _clockTimer.Start();
            }, DispatcherPriority.ContextIdle);
        };
        _clockTimer.Tick += (_, _) =>
        {
            _engine.Tick(); UpdateDisplay();
            if (DateTime.UtcNow - _lastSave > TimeSpan.FromSeconds(5)) Save();
            if (_toast.Visibility == Visibility.Visible && DateTime.UtcNow >= _toastUntil) HideToast();
        };
        _blinkTimer.Tick += (_, _) => { _colonVisible = !_colonVisible; UpdateDisplay(); };
        LocationChanged += (_, _) => HideToast();
        SizeChanged += OnWidgetSizeChanged;
        StateChanged += (_, _) => HideToast();
        Deactivated += (_, _) => HideToast();
        Closing += OnClosing;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.SessionSwitch += OnSessionSwitch;
        PreviewKeyDown += OnKeyDown;
    }

    private void BuildChrome()
    {
        var machine = new Grid(); machine.ColumnDefinitions.Add(new()); machine.ColumnDefinitions.Add(new() { Width = new GridLength(80) });
        _moduleScroll.Content = _modules; machine.Children.Add(_moduleScroll);
        var controls = new Grid { Width = 80, Height = 114, VerticalAlignment = VerticalAlignment.Top };
        controls.Children.Add(new ControlCase());
        var buttons = new Grid { Margin = new Thickness(7, 48, 9, 7) };
        buttons.ColumnDefinitions.Add(new()); buttons.ColumnDefinitions.Add(new());
        buttons.RowDefinitions.Add(new()); buttons.RowDefinitions.Add(new()); buttons.RowDefinitions.Add(new());
        void Put(PixelButton b, int row, int column, string name)
        {
            b.MinHeight = 18; b.Height = 18; b.FontSize = 10; b.Padding = new Thickness(0); b.Margin = new Thickness(1, 0, 1, 1);
            b.ToolTip = name; AutomationProperties.SetName(b, name); Grid.SetRow(b, row); Grid.SetColumn(b, column); buttons.Children.Add(b);
        }
        var settings = new PixelButton("⚙"); Put(settings, 1, 0, "설정"); AutomationProperties.SetAutomationId(settings, "Settings"); settings.Click += (_, _) => OpenSettings();
        Put(_pause, 1, 1, "일시정지");
        _pause.Click += (_, _) => { var result = _engine.Pause(); if (!result.Success) Toast(result.Message); else Save(); UpdateDisplay(); };
        var minimize = new PixelButton("−"); Put(minimize, 0, 0, "최소화"); minimize.Click += (_, _) => WindowState = WindowState.Minimized;
        var close = new PixelButton("×"); Put(close, 0, 1, "닫기"); close.Click += (_, _) => Close();
        Put(_finish, 2, 0, "종료 · 영수증 출력"); Grid.SetColumnSpan(_finish, 2);
        _finish.Background = Palette.Coral; _finish.Click += (_, _) => FinishSession(); AutomationProperties.SetAutomationId(_finish, "Finish");
        controls.Children.Add(buttons); Grid.SetColumn(controls, 1); machine.Children.Add(controls); _root.Children.Add(machine);

        var basePanel = _printerBase = new Border { Background = Palette.Paper, BorderBrush = Palette.Ink, BorderThickness = new Thickness(2, 2, 3, 3), Margin = new Thickness(0, 1, 0, 0), Padding = new Thickness(6, 3, 6, 3), Cursor = Cursors.SizeAll, ToolTip = "이 부분을 드래그하여 이동합니다." };
        basePanel.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) { DragMove(); ResizeWidget(); ClampToScreen(); SavePosition(); } };
        var baseGrid = new Grid(); baseGrid.ColumnDefinitions.Add(new()); baseGrid.ColumnDefinitions.Add(new() { Width = new GridLength(88) });
        var printer = _printerSlot = new Border { Height = 8, BorderBrush = Palette.Shade, BorderThickness = new Thickness(2, 2, 2, 0), Background = Palette.Ink, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center, ToolTip = "영수증 출력구 · 드래그하여 이동" };
        baseGrid.Children.Add(printer); Grid.SetColumn(_totalDisplay, 1); _totalDisplay.HorizontalAlignment = HorizontalAlignment.Right; baseGrid.Children.Add(_totalDisplay);
        basePanel.Child = baseGrid; _root.Children.Add(basePanel);
        _root.Children.Add(_receiptScroll);
        printer.SizeChanged += (_, _) => SyncReceiptLayout();
        _root.SizeChanged += (_, _) => SyncReceiptLayout();
        _receiptScroll.ScrollChanged += (_, e) =>
        {
            if (_receiptScroll.Content is ReceiptView view) view.SetViewport(e.VerticalOffset, e.ViewportHeight);
        };
        _toastText.Foreground = Palette.Light; _toastText.TextWrapping = TextWrapping.Wrap;
        _toastText.MaxWidth = 400; _toast.Child = _toastText; _toast.IsHitTestVisible = false;
        _toastPopup.Child = _toast; _toastPopup.PlacementTarget = _root;
        _toastPopup.CustomPopupPlacementCallback = (popupSize, targetSize, _) =>
        [
            new CustomPopupPlacement(new Point((targetSize.Width - popupSize.Width) / 2, -popupSize.Height - 6), PopupPrimaryAxis.Horizontal)
        ];
    }

    private void RebuildModules()
    {
        _modules.Children.Clear(); _moduleViews.Clear();
        foreach (var item in _engine.Items.Where(x => !x.IsArchived))
        {
            var view = new ActivityModule(item); view.Click += (_, _) => Activate(item.Id);
            var menu = new ContextMenu { Background = Palette.Paper, Foreground = Palette.Ink };
            var edit = new MenuItem { Header = "이름 / 색상 변경" }; edit.Click += (_, _) => EditItem(item); menu.Items.Add(edit);
            var remove = new MenuItem { Header = "항목 삭제 (측정 시간은 보관)" }; remove.Click += (_, _) => { var result = _engine.RemoveItem(item.Id); if (!result.Success) Toast(result.Message); else { RebuildModules(); Save(); Toast("항목을 제거했습니다. 사용 시간은 영수증에 남습니다."); } }; menu.Items.Add(remove);
            view.ContextMenu = menu; _moduleViews.Add(view); _modules.Children.Add(view);
        }
        var add = new ActivityModule(null); add.Click += (_, _) => AddItem(); _modules.Children.Add(add);
        ResizeWidget(); UpdateDisplay();
    }

    private void ResizeWidget()
    {
        var scale = _engine.Settings.Scale;
        var work = MonitorArea.For(this);
        var available = Math.Max(120, (work.Width - 40) / scale - 80);
        _moduleScroll.Width = Math.Min(_moduleViews.Count * 104 + 50, available);
        _moduleScroll.Height = _moduleViews.Count * 104 + 50 > available ? 132 : 114;
        _receiptScroll.MaxHeight = Math.Max(120, Math.Min(600, work.Height - 170 * scale - 40));
        SyncReceiptLayout();
        if (_loaded) Dispatcher.BeginInvoke(ClampToScreen, DispatcherPriority.Loaded);
    }

    private void Activate(Guid id)
    {
        var changed = _engine.ActiveItemId != id;
        var result = _engine.Activate(id);
        if (!result.Success) Toast(result.Message);
        else { if (changed) PlayClick(); Save(); }
        UpdateDisplay();
    }

    private void AddItem()
    {
        var result = Dialogs.ShowItem(this);
        if (result is null) return;
        try { _engine.AddItem(result.Name, result.Color); RebuildModules(); _moduleScroll.ScrollToRightEnd(); Save(); }
        catch (ArgumentException ex) { Toast(ex.Message); }
    }

    private void EditItem(ClockItem item)
    {
        var edit = Dialogs.ShowItem(this, item.Name, item.Color);
        if (edit is null) return;
        var result = _engine.RenameItem(item.Id, edit.Name);
        if (!result.Success) { Toast(result.Message); return; }
        item.Color = edit.Color;
        RebuildModules(); Save();
    }

    private void OpenSettings()
    {
        var settings = Dialogs.ShowSettings(this, _engine.Settings);
        if (settings is null) return;
        _engine.Settings.MinimumSeconds = settings.MinimumSeconds;
        _engine.Settings.AlwaysOnTop = settings.AlwaysOnTop;
        _engine.Settings.SoundEnabled = settings.SoundEnabled;
        _engine.Settings.Scale = settings.Scale;
        ApplySettings(); Save(); Toast("설정을 저장했습니다.");
    }

    private void ApplySettings()
    {
        Topmost = _engine.Settings.AlwaysOnTop;
        _root.LayoutTransform = new ScaleTransform(_engine.Settings.Scale, _engine.Settings.Scale);
        // Cancel only the receipt's inherited widget scale: paper width follows the slot, text stays fixed.
        _receiptScroll.LayoutTransform = new ScaleTransform(1 / _engine.Settings.Scale, 1 / _engine.Settings.Scale);
        _toast.LayoutTransform = new ScaleTransform(_engine.Settings.Scale, _engine.Settings.Scale);
        ResizeWidget();
    }

    private void FinishSession()
    {
        if (_engine.HasFinished && _engine.LastReceipt is { } existing)
        {
            if (!TrySaveReceipt(existing)) return;
            _engine.StartNewSession();
            if (_receiptScroll.Content is ReceiptView previousView) previousView.Dispose();
            _receiptScroll.Visibility = Visibility.Collapsed; _receiptScroll.Content = null;
            HideToast();
            RebuildModules(); Save();
            return;
        }
        var receipt = _engine.Finish();
        if (receipt is null) { Toast("항목을 눌러 먼저 시간을 기록해 주세요."); return; }
        Save();
        TrySaveReceipt(receipt);
        ShowReceipt(receipt, true); PlayClick(); UpdateDisplay();
    }

    private void ShowReceipt(ReceiptData data, bool animate)
    {
        if (_receiptScroll.Content is ReceiptView previousView) previousView.Dispose();
        var view = new ReceiptView(data);
        view.CopyRequested += (_, _) => CopyReceipt(data, view);
        _receiptScroll.Content = view; _receiptScroll.Visibility = Visibility.Visible;
        _receiptScroll.ScrollToTop();
        SyncReceiptLayout();
        if (animate)
        {
            view.Measure(new Size(_receiptScroll.Width, double.PositiveInfinity));
            var width = _receiptScroll.Width;
            var height = Math.Min(view.DesiredSize.Height, _receiptScroll.MaxHeight);
            var reveal = new RectangleGeometry(new Rect(0, 0, width, 0));
            _receiptScroll.Clip = reveal;
            var feed = new RectAnimation(new Rect(0, 0, width, height), TimeSpan.FromMilliseconds(950));
            feed.Completed += (_, _) => { if (ReferenceEquals(_receiptScroll.Clip, reveal)) _receiptScroll.Clip = null; };
            reveal.BeginAnimation(RectangleGeometry.RectProperty, feed);
        }
        Dispatcher.BeginInvoke(ClampToScreen, DispatcherPriority.Loaded);
    }

    private void SyncReceiptLayout()
    {
        if (_printerSlot is null || _printerSlot.ActualWidth <= 4) return;
        var slot = _printerSlot.TransformToAncestor(_root).Transform(new Point());
        var chassis = _printerBase.TransformToAncestor(_root).Transform(new Point());
        var width = (_printerSlot.ActualWidth - 4) * _engine.Settings.Scale;
        _receiptScroll.Width = width;
        _receiptScroll.Margin = new Thickness(slot.X + 2, slot.Y + 2 - chassis.Y - _printerBase.ActualHeight, 0, 0);
        if (_receiptScroll.Content is ReceiptView view) view.SetPaperWidth(width);
    }

    private bool TrySaveReceipt(ReceiptData data)
    {
        try { _store.SaveReceipt(data); return true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Toast("영수증 저장 실패. 기록을 보존했습니다. 다시시작을 눌러 다시 시도해 주세요. " + ex.Message); return false;
        }
    }

    private async void CopyReceipt(ReceiptData data, ReceiptView view)
    {
        for (var i = 0; i < 4; i++)
        {
            try { Clipboard.SetText(data.ToPlainText()); view.ShowCopyFeedback(); return; }
            catch (System.Runtime.InteropServices.COMException) { await System.Threading.Tasks.Task.Delay(100); }
        }
        view.ShowCopyFeedback("클립보드가 사용 중입니다. 다시 눌러 주세요.");
    }

    private void UpdateDisplay()
    {
        if (_blinkingItemId != _engine.ActiveItemId)
        {
            _blinkTimer.Stop();
            _blinkingItemId = _engine.ActiveItemId;
            _colonVisible = true;
            if (_blinkingItemId is not null) _blinkTimer.Start();
        }
        foreach (var view in _moduleViews)
        {
            view.Active = view.Item!.Id == _engine.ActiveItemId; view.Finished = _engine.HasFinished;
            view.ColonVisible = _colonVisible;
            view.MinimumRemaining = view.Active ? _engine.MinimumRemaining : TimeSpan.Zero;
            view.InvalidateVisual();
        }
        _totalDisplay.Elapsed = _engine.HasFinished && _engine.LastReceipt is { } finalReceipt ? finalReceipt.DisplayTotalElapsed : _engine.TotalElapsed;
        _totalDisplay.InvalidateVisual();
        _pause.IsEnabled = _engine.ActiveItemId is not null;
        _finish.Content = _engine.HasFinished ? "다시시작" : "종료";
        _finish.ToolTip = _engine.HasFinished ? "다시시작" : "종료 · 영수증 출력";
        AutomationProperties.SetName(_finish, _engine.HasFinished ? "다시시작" : "종료 · 영수증 출력");
        _finish.IsEnabled = _engine.HasSession || _engine.HasFinished;
    }

    private void Toast(string message)
    {
        void Show()
        {
            if (_closing) return;
            _toastText.MaxWidth = Math.Clamp(_moduleScroll.Width + 80 - 48, 180, 400);
            _toastText.Text = message; _toast.Visibility = Visibility.Visible;
            _toastUntil = DateTime.UtcNow.AddSeconds(3.5);
            if (_loaded && WindowState != WindowState.Minimized) _toastPopup.IsOpen = true;
        }
        // Let settings-driven resize and screen clamping finish before placing the notification.
        if (_loaded) Dispatcher.BeginInvoke(Show, DispatcherPriority.Loaded);
        else Show();
    }

    private void HideToast()
    {
        _toastPopup.IsOpen = false;
        _toast.Visibility = Visibility.Collapsed;
    }

    private void Save()
    {
        try { _store.Save(_engine); SavePosition(); _lastSave = DateTime.UtcNow; _saveErrorShown = false; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (!_saveErrorShown) { _saveErrorShown = true; Toast("기록을 저장할 수 없습니다: " + ex.Message); }
            _lastSave = DateTime.UtcNow;
        }
    }

    private void ClampToScreen()
    {
        if (!_loaded) return;
        UpdateLayout(); var work = MonitorArea.For(this);
        Left = Math.Clamp(Left, work.Left, Math.Max(work.Left, work.Right - ActualWidth));
        Top = Math.Clamp(Top, work.Top, Math.Max(work.Top, work.Bottom - ActualHeight));
    }

    private void OnWidgetSizeChanged(object sender, SizeChangedEventArgs e)
    {
        HideToast();
        if (!_loaded || _closing || WindowState != WindowState.Normal ||
            e.PreviousSize.Width <= 0 || e.PreviousSize.Height <= 0 ||
            e.NewSize.Width <= 0 || e.NewSize.Height <= 0 ||
            !double.IsFinite(Left) || !double.IsFinite(Top)) return;

        // Keep the bottom-right corner fixed as scale, tasks, or the receipt change the size.
        Left += e.PreviousSize.Width - e.NewSize.Width;
        Top += e.PreviousSize.Height - e.NewSize.Height;
        Dispatcher.BeginInvoke(ClampToScreen, DispatcherPriority.Loaded);
    }

    private void SavePosition()
    {
        if (!_loaded || WindowState != WindowState.Normal) return;
        try
        {
            if (MonitorArea.Capture(this) is not { } position) return;
            Directory.CreateDirectory(_dataDirectory);
            var path = Path.Combine(_dataDirectory, "widget.json");
            File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(position));
            File.Move(path + ".tmp", path, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
    private void LoadPosition()
    {
        try
        {
            var path = Path.Combine(_dataDirectory, "widget.json");
            if (!File.Exists(path)) return;
            var json = File.ReadAllText(path);
            if (json.TrimStart().StartsWith('['))
            {
                var p = JsonSerializer.Deserialize<double[]>(json);
                if (p is { Length: 2 } && double.IsFinite(p[0]) && double.IsFinite(p[1])) _savedPosition = new Point(p[0], p[1]);
            }
            else _savedPixelPosition = JsonSerializer.Deserialize<MonitorArea.SavedPosition>(json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_loaded && _engine.HasSession && !_engine.HasFinished &&
            MessageBox.Show(this, "기록이 진행중입니다. 닫으시겠습니까?", "Switchit!",
                MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
        {
            e.Cancel = true;
            return;
        }
        _closing = true;
        HideToast();
        if (_receiptScroll.Content is ReceiptView view) view.Dispose();
        _blinkTimer.Stop(); _clockTimer.Stop(); Save(); _engine.Suspend(); SavePosition();
        SystemEvents.PowerModeChanged -= OnPowerModeChanged; SystemEvents.SessionSwitch -= OnSessionSwitch;
    }
    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Suspend) Dispatcher.Invoke(() => { _engine.Suspend(); Save(); UpdateDisplay(); });
    }
    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        if (e.Reason == SessionSwitchReason.SessionLock) Dispatcher.Invoke(() => { _engine.Suspend(); Save(); UpdateDisplay(); });
    }
    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _engine.ActiveItemId is not null)
        {
            var result = _engine.Pause(); if (!result.Success) Toast(result.Message); else Save(); UpdateDisplay(); e.Handled = true;
        }
    }
    private void PlayClick()
    {
        if (!_engine.Settings.SoundEnabled) return;
        _ = System.Threading.Tasks.Task.Run(() => { try { Console.Beep(620, 25); } catch { } });
    }
    private static TextBlock Label(string text, double size, bool bold = false)
        => new() { Text = text, FontSize = size, FontWeight = bold ? FontWeights.Bold : FontWeights.Normal, Foreground = Palette.Ink, VerticalAlignment = VerticalAlignment.Center };
}
