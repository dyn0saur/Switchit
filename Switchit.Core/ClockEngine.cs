using System.Text.RegularExpressions;

namespace Switchit.Core;

/// <summary>All running durations come from the monotonic timestamp source, never the wall clock.</summary>
public sealed class ClockEngine
{
    private readonly List<ClockItem> _items = [];
    private readonly TimeProvider _timeProvider;
    private long _lastTimestamp;
    private TimeSpan _activationElapsed;
    private Guid? _focusItemId;

    public ClockEngine(TimeProvider? timeProvider = null) : this(timeProvider, true) { }

    internal ClockEngine(TimeProvider? timeProvider, bool addDefaults)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _lastTimestamp = _timeProvider.GetTimestamp();
        if (addDefaults)
        {
            AddItem("휴식", "#88A789");
            AddItem("업무1", "#D58364");
            AddItem("업무2", "#CBA662");
        }
    }

    public IReadOnlyList<ClockItem> Items => _items.AsReadOnly();
    public Guid? ActiveItemId { get; private set; }
    public bool IsPaused => ActiveItemId is null;
    public bool HasFinished { get; private set; }
    public DateTimeOffset? SessionStartedAt { get; private set; }
    public ClockSettings Settings { get; private set; } = new();
    public ReceiptData? LastReceipt { get; private set; }
    public TimeSpan TotalElapsed => TimeSpan.FromTicks(_items.Sum(item => item.Elapsed.Ticks));
    public bool HasSession => SessionStartedAt.HasValue;

    public TimeSpan MinimumRemaining
    {
        get
        {
            if (ActiveItemId is null || HasFinished ||
                _items.Any(item => item.Id == ActiveItemId && item.Name == "휴식")) return TimeSpan.Zero;
            return FocusMinimumRemaining;
        }
    }

    private TimeSpan FocusMinimumRemaining
    {
        get
        {
            if (HasFinished || !_items.Any(item => item.Id == _focusItemId && !item.IsArchived && item.Name != "휴식"))
                return TimeSpan.Zero;
            var remaining = TimeSpan.FromSeconds(Math.Clamp(Settings.MinimumSeconds, 0, 86_400)) - _activationElapsed;
            return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
        }
    }

    public ClockItem AddItem(string name, string color)
    {
        var item = new ClockItem { Name = ValidateName(name), Color = NormalizeColor(color) };
        _items.Add(item);
        return item;
    }

    public OperationResult Activate(Guid id)
    {
        Tick();
        if (HasFinished) return OperationResult.Fail("다시시작을 누른 뒤 항목을 선택해 주세요.");
        var item = _items.Find(candidate => candidate.Id == id && !candidate.IsArchived);
        if (item is null) return OperationResult.Fail("이 항목을 찾을 수 없습니다.");
        if (ActiveItemId == id) return OperationResult.Ok();
        if (item.Name != "휴식" && _focusItemId != id)
        {
            if (FocusMinimumRemaining > TimeSpan.Zero) return MinimumBlocked();
            _focusItemId = id;
            _activationElapsed = TimeSpan.Zero;
        }
        SessionStartedAt ??= _timeProvider.GetLocalNow();
        ActiveItemId = id;
        _lastTimestamp = _timeProvider.GetTimestamp();
        return OperationResult.Ok();
    }

    public OperationResult Pause()
    {
        Tick();
        ActiveItemId = null;
        return OperationResult.Ok();
    }

    /// <summary>Force-pause only for window closing, OS suspension, or session locking.</summary>
    public void Suspend()
    {
        Tick();
        ActiveItemId = null;
    }

    public OperationResult RenameItem(Guid id, string name)
    {
        var item = _items.Find(candidate => candidate.Id == id && !candidate.IsArchived);
        if (item is null) return OperationResult.Fail("이 항목을 찾을 수 없습니다.");
        try { item.Name = ValidateName(name); }
        catch (ArgumentException exception) { return OperationResult.Fail(exception.Message); }
        return OperationResult.Ok();
    }

    public OperationResult RemoveItem(Guid id)
    {
        Tick();
        var item = _items.Find(candidate => candidate.Id == id && !candidate.IsArchived);
        if (item is null) return OperationResult.Fail("이 항목을 찾을 수 없습니다.");
        if (_focusItemId == id && FocusMinimumRemaining > TimeSpan.Zero) return MinimumBlocked();
        if (ActiveItemId == id)
        {
            ActiveItemId = null;
        }
        if (_focusItemId == id)
        {
            _focusItemId = null;
            _activationElapsed = TimeSpan.Zero;
        }
        // Archive instead of losing the time already spent on a removed task.
        item.IsArchived = true;
        return OperationResult.Ok();
    }

    public ReceiptData? Finish()
    {
        Tick();
        if (HasFinished) return LastReceipt;
        if (!SessionStartedAt.HasValue) return null;
        var finishedAt = _timeProvider.GetLocalNow();
        LastReceipt = new ReceiptData
        {
            Id = $"{finishedAt:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..6]}",
            StartedAt = SessionStartedAt.Value,
            FinishedAt = finishedAt,
            Items = _items.Where(item => !item.IsArchived || item.Elapsed > TimeSpan.Zero)
                .Select(item => new ReceiptLine { Name = item.Name, Color = item.Color, Elapsed = item.Elapsed }).ToList()
        };
        HasFinished = true;
        ActiveItemId = null;
        _focusItemId = null;
        _activationElapsed = TimeSpan.Zero;
        return LastReceipt;
    }

    public void Tick()
    {
        var timestamp = _timeProvider.GetTimestamp();
        var elapsed = _timeProvider.GetElapsedTime(_lastTimestamp, timestamp);
        _lastTimestamp = timestamp;
        if (ActiveItemId is null || HasFinished || elapsed <= TimeSpan.Zero) return;
        var active = _items.Find(item => item.Id == ActiveItemId.Value && !item.IsArchived);
        if (active is null) { ActiveItemId = null; return; }
        active.Elapsed += elapsed;
        if (active.Id == _focusItemId && active.Name != "휴식") _activationElapsed += elapsed;
    }

    public void StartNewSession()
    {
        // Starting another session must never silently discard an unfinished record.
        if (HasSession && !HasFinished)
            throw new InvalidOperationException("진행 중인 세션을 먼저 종료해 주세요.");
        _items.RemoveAll(item => item.IsArchived);
        foreach (var item in _items) item.Elapsed = TimeSpan.Zero;
        ActiveItemId = null;
        _focusItemId = null;
        HasFinished = false;
        SessionStartedAt = null;
        _activationElapsed = TimeSpan.Zero;
        _lastTimestamp = _timeProvider.GetTimestamp();
    }

    internal EngineSnapshot CreateSnapshot()
    {
        Tick();
        return new EngineSnapshot
        {
            Settings = Settings,
            SessionStartedAt = SessionStartedAt,
            HasFinished = HasFinished,
            LastReceipt = LastReceipt,
            ActiveItemId = ActiveItemId,
            FocusItemId = _focusItemId,
            ActivationElapsedTicks = _activationElapsed.Ticks,
            Items = _items.Select(item => (ItemSnapshot?)new ItemSnapshot
            {
                Id = item.Id, Name = item.Name, Color = item.Color,
                ElapsedTicks = item.Elapsed.Ticks, IsArchived = item.IsArchived
            }).ToList()
        };
    }

    internal static ClockEngine Restore(EngineSnapshot snapshot, TimeProvider? timeProvider = null, bool resumeSession = false)
    {
        var engine = new ClockEngine(timeProvider, false);
        var resume = resumeSession && snapshot.SessionStartedAt.HasValue && !snapshot.HasFinished;
        var settings = snapshot.Settings ?? new ClockSettings();
        settings.MinimumSeconds = Math.Clamp(settings.MinimumSeconds, 0, 86_400);
        settings.Scale = double.IsFinite(settings.Scale) ? Math.Clamp(settings.Scale, 0.5, 2.0) : 1.0;
        engine.Settings = settings;
        var seen = new HashSet<Guid>();
        foreach (var saved in snapshot.Items ?? [])
        {
            if (saved is null || (!resume && saved.IsArchived) || string.IsNullOrWhiteSpace(saved.Name)) continue;
            var name = saved.Name.Trim();
            if (name.Length > 40) name = name[..40];
            if (name.Any(char.IsControl)) continue;
            var id = saved.Id;
            if (id == Guid.Empty || !seen.Add(id)) { id = Guid.NewGuid(); seen.Add(id); }
            engine._items.Add(new ClockItem
            {
                Id = id, Name = name, Color = NormalizeColor(saved.Color),
                Elapsed = resume ? TimeSpan.FromTicks(Math.Max(0, saved.ElapsedTicks)) : TimeSpan.Zero,
                IsArchived = resume && saved.IsArchived
            });
        }
        if (resume)
        {
            engine.SessionStartedAt = snapshot.SessionStartedAt;
            engine.LastReceipt = snapshot.LastReceipt;
            engine.ActiveItemId = engine._items.FirstOrDefault(item => item.Id == snapshot.ActiveItemId && !item.IsArchived)?.Id;
            var focusId = snapshot.FocusItemId ?? snapshot.ActiveItemId;
            engine._focusItemId = engine._items.FirstOrDefault(item => item.Id == focusId && !item.IsArchived && item.Name != "휴식")?.Id;
            engine._activationElapsed = engine._focusItemId is null ? TimeSpan.Zero : TimeSpan.FromTicks(Math.Max(0, snapshot.ActivationElapsedTicks));
            _ = engine.TotalElapsed; // Reject overflowing totals through the store's recovery path.
        }
        // A launch restores only task configuration and settings, even for legacy running or
        // completed snapshots. Every stopwatch and focus period starts fresh and paused.
        // Completed receipts stay in their own files; no old receipt is reopened in the widget.
        return engine;
    }

    private OperationResult MinimumBlocked()
    {
        var seconds = (int)Math.Ceiling(FocusMinimumRemaining.TotalSeconds);
        var remaining = seconds >= 60 ? $"{seconds / 60}분 {seconds % 60}초" : $"{seconds}초";
        return OperationResult.Fail($"최소 사용 시간까지 {remaining} 남았습니다.");
    }

    private static string ValidateName(string name)
    {
        var value = name?.Trim() ?? "";
        if (value.Length == 0) throw new ArgumentException("항목 이름을 입력해 주세요.");
        if (value.Length > 40) throw new ArgumentException("항목 이름은 40자 이내로 입력해 주세요.");
        if (value.Any(char.IsControl)) throw new ArgumentException("항목 이름에는 줄바꿈이나 제어 문자를 사용할 수 없습니다.");
        return value;
    }

    internal static string NormalizeColor(string? color) =>
        color is not null && Regex.IsMatch(color, "^#[0-9a-fA-F]{6}$") ? color.ToUpperInvariant() : "#CE735A";

}
