using System.Text.Json;
using Switchit.Core;

var tests = new (string Name, Action Run)[]
{
    ("Legacy data migration preserves records and source, then never overwrites current data", () => WithStore((_, time, directory) =>
    {
        var legacy = Path.Combine(directory, "old-data");
        var store = new StateStore(legacy, time);
        var engine = store.Load();
        engine.Activate(engine.Items[1].Id); time.Advance(12.5);
        store.Save(engine);
        File.WriteAllText(Path.Combine(legacy, "widget.json"), "{\"Right\":1234}");
        Directory.CreateDirectory(Path.Combine(legacy, "receipts"));
        File.WriteAllText(Path.Combine(legacy, "receipts", "example.txt"), "기존 영수증");
        File.WriteAllText(Path.Combine(legacy, "unfinished.tmp"), "ignore");
        var current = Path.Combine(directory, "new-data");
        True(DataDirectoryMigration.CopyIfNeeded(legacy, current));
        True(File.Exists(store.StatePath));
        True(!File.Exists(Path.Combine(current, "unfinished.tmp")));
        Equal("기존 영수증", File.ReadAllText(Path.Combine(current, "receipts", "example.txt")));
        Equal("{\"Right\":1234}", File.ReadAllText(Path.Combine(current, "widget.json")));
        var migratedStore = new StateStore(current, time);
        var migrated = migratedStore.Load(resumeSession: true);
        Equal(TimeSpan.FromSeconds(12.5), migrated.TotalElapsed);
        Equal(engine.ActiveItemId!.Value, migrated.ActiveItemId!.Value);
        File.WriteAllText(Path.Combine(current, "keep.txt"), "new");
        True(!DataDirectoryMigration.CopyIfNeeded(legacy, current));
        Equal("new", File.ReadAllText(Path.Combine(current, "keep.txt")));
    })),
    ("One active clock and elapsed conservation across switches", () =>
    {
        var (engine, time) = NewClock();
        var first = engine.Items[1];
        var second = engine.Items[2];
        Success(engine.Activate(first.Id));
        time.Advance(11.25);
        Success(engine.Activate(second.Id));
        time.Advance(6.75);
        engine.Tick();
        Equal(TimeSpan.FromSeconds(11.25), first.Elapsed);
        Equal(TimeSpan.FromSeconds(6.75), second.Elapsed);
        Equal(TimeSpan.FromSeconds(18), engine.TotalElapsed);
        Equal(second.Id, engine.ActiveItemId!.Value);
        Equal(TimeSpan.Zero, engine.Items[0].Elapsed);
    }),
    ("Minimum blocks switch and active removal before exact boundary", () =>
    {
        var (engine, time) = NewClock();
        engine.Settings.MinimumSeconds = 60;
        var first = engine.Items[0];
        Success(engine.Activate(first.Id));
        time.Advance(59.9);
        Failure(engine.Activate(engine.Items[1].Id));
        Failure(engine.RemoveItem(first.Id));
        Equal(TimeSpan.FromSeconds(0.1), engine.MinimumRemaining);
        time.Advance(0.1);
        Success(engine.Activate(engine.Items[1].Id));
        Equal(TimeSpan.FromSeconds(60), engine.MinimumRemaining);
    }),
    ("Clicking the active task never resets its focus period", () =>
    {
        var (engine, time) = NewClock();
        engine.Settings.MinimumSeconds = 10;
        Success(engine.Activate(engine.Items[0].Id));
        time.Advance(6);
        Success(engine.Activate(engine.Items[0].Id));
        Equal(TimeSpan.FromSeconds(4), engine.MinimumRemaining);
        time.Advance(4);
        Success(engine.Activate(engine.Items[1].Id));
    }),
    ("Rest is exempt from minimum while work still blocks early switching", () =>
    {
        var (engine, time) = NewClock();
        engine.Settings.MinimumSeconds = 60;
        var rest = engine.AddItem("휴식", "#88A789");
        Success(engine.Activate(rest.Id));
        time.Advance(0.5);
        Equal(TimeSpan.Zero, engine.MinimumRemaining);
        Success(engine.Activate(engine.Items[1].Id));
        time.Advance(1);
        Success(engine.Activate(rest.Id));
        Failure(engine.Activate(engine.Items[2].Id));
        Success(engine.Pause());
        Success(engine.Activate(rest.Id));
        Success(engine.RemoveItem(rest.Id));
        Equal(TimeSpan.FromSeconds(0.5), rest.Elapsed);
    }),
    ("Explicit update resume preserves elapsed, archived tasks, focus and excludes downtime", () => WithStore((store, time, _) =>
    {
        var engine = store.Load();
        engine.Settings.MinimumSeconds = 60;
        var archived = engine.Items[0];
        Success(engine.Activate(archived.Id));
        time.Advance(0.5);
        Success(engine.RemoveItem(archived.Id));
        var work = engine.Items[1];
        Success(engine.Activate(work.Id));
        time.Advance(12);
        store.Save(engine);
        var started = engine.SessionStartedAt;
        time.Advance(300);
        var resumed = store.Load(resumeSession: true);
        Equal(work.Id, resumed.ActiveItemId!.Value);
        Equal(started!.Value, resumed.SessionStartedAt!.Value);
        Equal(TimeSpan.FromSeconds(48), resumed.MinimumRemaining);
        Equal(TimeSpan.FromSeconds(12.5), resumed.TotalElapsed);
        True(resumed.Items.Single(item => item.Id == archived.Id).IsArchived);
        time.Advance(3);
        resumed.Tick();
        Equal(TimeSpan.FromSeconds(15.5), resumed.TotalElapsed);
        Equal(TimeSpan.FromSeconds(45), resumed.MinimumRemaining);
        resumed.Pause(); store.Save(resumed);
        time.Advance(100);
        var paused = store.Load(resumeSession: true);
        True(paused.IsPaused);
        Equal(resumed.TotalElapsed, paused.TotalElapsed);
        FreshSession(store.Load());
    })),
    ("Pause works before minimum, excludes idle time and preserves the focus period", () =>
    {
        var (engine, time) = NewClock();
        engine.Settings.MinimumSeconds = 60;
        var first = engine.Items[0];
        Success(engine.Activate(first.Id));
        time.Advance(2);
        Success(engine.Pause());
        True(engine.IsPaused);
        time.Advance(600);
        engine.Tick();
        Equal(TimeSpan.FromSeconds(2), first.Elapsed);
        Success(engine.Activate(first.Id));
        Equal(TimeSpan.FromSeconds(58), engine.MinimumRemaining);
    }),
    ("OS suspension bypasses minimum and excludes closed or locked time", () =>
    {
        var (engine, time) = NewClock();
        engine.Settings.MinimumSeconds = 60;
        var task = engine.Items[0];
        Success(engine.Activate(task.Id));
        time.Advance(10);
        engine.Suspend();
        True(engine.IsPaused);
        time.Advance(3600);
        engine.Tick();
        Equal(TimeSpan.FromSeconds(10), task.Elapsed);
        Success(engine.Activate(task.Id));
        Equal(TimeSpan.FromSeconds(50), engine.MinimumRemaining);
    }),
    ("Thirty-minute work blocks survive rests and reset only after another work task", () =>
    {
        var (engine, time) = NewClock();
        engine.Settings.MinimumSeconds = 1800;
        var a = engine.Items[1]; var b = engine.Items[2];
        var c = engine.AddItem("업무 C", "#85A5A4");
        var rest = engine.AddItem("휴식", "#88A789");
        Success(engine.Activate(a.Id));
        time.Advance(600);
        Success(engine.Activate(rest.Id));
        time.Advance(500);
        Failure(engine.Activate(b.Id));
        True(engine.Activate(c.Id).Message.Contains("20분"));
        Success(engine.Activate(a.Id));
        Equal(TimeSpan.FromMinutes(20), engine.MinimumRemaining);
        time.Advance(1200);
        Success(engine.Activate(rest.Id));
        time.Advance(0.1);
        Success(engine.Activate(a.Id));
        Equal(TimeSpan.Zero, engine.MinimumRemaining);
        Success(engine.Activate(b.Id));
        Equal(TimeSpan.FromMinutes(30), engine.MinimumRemaining);
        Success(engine.Pause());
        Failure(engine.Activate(a.Id));
        Success(engine.Activate(b.Id));
        time.Advance(1800);
        Success(engine.Activate(rest.Id));
        time.Advance(0.1);
        Success(engine.Activate(a.Id));
        Equal(TimeSpan.FromMinutes(30), engine.MinimumRemaining);
        Equal(TimeSpan.FromMinutes(30), a.Elapsed);
    }),
    ("Update resume during rest retains the previous work and minimum progress", () => WithStore((store, time, _) =>
    {
        var engine = store.Load();
        engine.Settings.MinimumSeconds = 60;
        var rest = engine.Items[0]; var work = engine.Items[1];
        Success(engine.Activate(work.Id)); time.Advance(10);
        Success(engine.Activate(rest.Id)); time.Advance(30);
        store.Save(engine); time.Advance(300);
        var resumed = store.Load(resumeSession: true);
        Equal(rest.Id, resumed.ActiveItemId!.Value);
        Failure(resumed.Activate(engine.Items[2].Id));
        Success(resumed.Activate(work.Id));
        Equal(TimeSpan.FromSeconds(50), resumed.MinimumRemaining);
        time.Advance(50);
        Success(resumed.Activate(engine.Items[2].Id));
        Equal(TimeSpan.FromSeconds(60), resumed.MinimumRemaining);
    })),
    ("Finish always works during minimum time and freezes all clocks", () =>
    {
        var (engine, time) = NewClock();
        engine.Settings.MinimumSeconds = 300;
        Success(engine.Activate(engine.Items[1].Id));
        time.Advance(4.5);
        var receipt = engine.Finish()!;
        True(engine.HasFinished && engine.IsPaused);
        Equal(TimeSpan.FromSeconds(4.5), receipt.TotalElapsed);
        time.Advance(900);
        engine.Tick();
        Equal(TimeSpan.FromSeconds(4.5), engine.TotalElapsed);
        Equal(receipt.Id, engine.Finish()!.Id);
        Failure(engine.Activate(engine.Items[2].Id));
    }),
    ("Removing a task preserves its time in the final receipt", () =>
    {
        var (engine, time) = NewClock();
        var removed = engine.Items[1];
        Success(engine.Activate(removed.Id));
        time.Advance(35);
        Success(engine.RemoveItem(removed.Id));
        True(removed.IsArchived && engine.IsPaused);
        var added = engine.AddItem("새 업무", "#8DA892");
        Success(engine.Activate(added.Id));
        time.Advance(5);
        var receipt = engine.Finish()!;
        Equal(TimeSpan.FromSeconds(40), receipt.TotalElapsed);
        Equal(TimeSpan.FromSeconds(35), receipt.Items.Single(item => item.Name == removed.Name).Elapsed);
        True(receipt.ToPlainText().Contains("새 업무"));
    }),
    ("Unstarted finish does not invent a session or receipt", () =>
    {
        var (engine, _) = NewClock();
        True(engine.Finish() is null);
        True(!engine.HasSession && !engine.HasFinished);
    }),
    ("New session clears clocks and archives while preserving the last receipt", () =>
    {
        var (engine, time) = NewClock();
        Success(engine.Activate(engine.Items[0].Id));
        time.Advance(10);
        var removedId = engine.Items[1].Id;
        Success(engine.RemoveItem(removedId));
        var receipt = engine.Finish()!;
        engine.StartNewSession();
        True(!engine.HasSession && !engine.HasFinished && engine.IsPaused);
        Equal(TimeSpan.Zero, engine.TotalElapsed);
        True(engine.Items.All(item => item.Id != removedId));
        Equal(receipt.Id, engine.LastReceipt!.Id);
        Success(engine.Activate(engine.Items[0].Id));
        time.Advance(3);
        engine.Tick();
        Equal(TimeSpan.FromSeconds(10), receipt.TotalElapsed);
    }),
    ("New session cannot silently discard an unfinished record", () =>
    {
        var (engine, _) = NewClock();
        Success(engine.Activate(engine.Items[0].Id));
        Throws<InvalidOperationException>(engine.StartNewSession);
    }),
    ("Wall clock jumps never affect elapsed durations", () =>
    {
        var (engine, time) = NewClock();
        Success(engine.Activate(engine.Items[0].Id));
        time.Advance(30);
        time.ShiftWallClock(TimeSpan.FromHours(-7));
        engine.Tick();
        Equal(TimeSpan.FromSeconds(30), engine.TotalElapsed);
        time.ShiftWallClock(TimeSpan.FromDays(2));
        time.Advance(5);
        engine.Tick();
        Equal(TimeSpan.FromSeconds(35), engine.TotalElapsed);
    }),
    ("Long durations never wrap at 24 hours", () =>
    {
        Equal("127:05:09", ReceiptData.FormatDuration(TimeSpan.FromHours(127) + TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(9)));
    }),
    ("Names and colors are validated without damaging the running task", () =>
    {
        var (engine, _) = NewClock();
        Throws<ArgumentException>(() => engine.AddItem("  ", "#FFFFFF"));
        Throws<ArgumentException>(() => engine.AddItem("a\nb", "#FFFFFF"));
        Throws<ArgumentException>(() => engine.AddItem(new string('a', 41), "#FFFFFF"));
        var added = engine.AddItem("  집중  ", "bad-color");
        Equal("집중", added.Name);
        Equal("#CE735A", added.Color);
        Success(engine.Activate(added.Id));
        Failure(engine.RenameItem(added.Id, ""));
        Success(engine.RenameItem(added.Id, "설계"));
        Equal("설계", added.Name);
        Equal(added.Id, engine.ActiveItemId!.Value);
    }),
    ("Reopening a running snapshot restores settings but resets all elapsed time", () => WithStore((store, time, directory) =>
    {
        var engine = store.Load();
        engine.Settings.MinimumSeconds = 120;
        engine.Settings.Scale = 1.25;
        engine.Settings.AlwaysOnTop = false;
        engine.Settings.SoundEnabled = false;
        Success(engine.Activate(engine.Items[2].Id));
        time.Advance(12.3456);
        store.Save(engine);
        time.Advance(86_400);
        var restored = store.Load();
        FreshSession(restored);
        Equal(120, restored.Settings.MinimumSeconds);
        Equal(1.25, restored.Settings.Scale);
        True(!restored.Settings.AlwaysOnTop && !restored.Settings.SoundEnabled);
        restored.Tick();
        Equal(TimeSpan.Zero, restored.TotalElapsed);
        Success(restored.Activate(restored.Items[2].Id));
        Equal(TimeSpan.FromSeconds(120), restored.MinimumRemaining);
        Equal(time.GetLocalNow(), restored.SessionStartedAt!.Value);
        time.Advance(1);
        restored.Tick();
        Equal(TimeSpan.FromSeconds(1), restored.TotalElapsed);
        Equal(TimeSpan.FromSeconds(119), restored.MinimumRemaining);
        Failure(restored.Activate(restored.Items[1].Id));
        True(!Directory.EnumerateFiles(directory, "*.tmp").Any());
    })),
    ("Reopening a completed snapshot starts fresh and leaves receipt files unchanged", () => WithStore((store, time, _) =>
    {
        var engine = store.Load();
        Success(engine.Activate(engine.Items[0].Id));
        time.Advance(6.75);
        var receipt = engine.Finish()!;
        store.SaveReceipt(receipt);
        store.Save(engine);
        var textPath = Path.Combine(store.ReceiptDirectory, receipt.Id + ".txt");
        var jsonPath = Path.Combine(store.ReceiptDirectory, receipt.Id + ".json");
        var textBefore = File.ReadAllBytes(textPath);
        var jsonBefore = File.ReadAllBytes(jsonPath);
        var restored = store.Load();
        FreshSession(restored);
        True(restored.LastReceipt is null);
        True(restored.Finish() is null);
        store.Save(restored);
        FreshSession(store.Load());
        True(textBefore.SequenceEqual(File.ReadAllBytes(textPath)));
        True(jsonBefore.SequenceEqual(File.ReadAllBytes(jsonPath)));
        Equal(receipt.ToPlainText(), File.ReadAllText(textPath));
        Equal(2, Directory.EnumerateFiles(store.ReceiptDirectory).Count());
    })),
    ("Custom names colors ids and order survive additions edits and removals", () => WithStore((store, time, _) =>
    {
        var engine = store.Load();
        var renamed = engine.Items[0];
        Success(engine.RenameItem(renamed.Id, "잠깐 휴식"));
        renamed.Color = "#ABCDEF";
        var removed = engine.Items[1];
        Success(engine.Activate(removed.Id));
        time.Advance(25);
        Success(engine.RemoveItem(removed.Id));
        engine.AddItem("도면 검토", "#654321");
        engine.AddItem("회의", "#A1B2C3");
        var expected = engine.Items.Where(item => !item.IsArchived).Select(item => (item.Id, item.Name, item.Color)).ToArray();
        store.Save(engine);
        var restored = store.Load();
        FreshSession(restored);
        True(expected.SequenceEqual(restored.Items.Select(item => (item.Id, item.Name, item.Color))));
        True(restored.Items.All(item => item.Id != removed.Id));
        Equal(4, restored.Items.Count);
        store.Save(restored);
        True(expected.SequenceEqual(store.Load().Items.Select(item => (item.Id, item.Name, item.Color))));
    })),
    ("An intentionally empty task list stays empty after reopening", () => WithStore((store, _, _) =>
    {
        var engine = store.Load();
        foreach (var id in engine.Items.Select(item => item.Id).ToArray()) Success(engine.RemoveItem(id));
        store.Save(engine);
        var restored = store.Load();
        Equal(0, restored.Items.Count);
        FreshSession(restored);
        store.Save(restored);
        Equal(0, store.Load().Items.Count);
    })),
    ("Missing state creates default tasks once and preserves their saved ids", () => WithStore((store, _, _) =>
    {
        var engine = store.Load();
        True(new[] { "휴식", "업무1", "업무2" }.SequenceEqual(engine.Items.Select(item => item.Name)));
        True(new[] { "#88A789", "#D58364", "#CBA662" }.SequenceEqual(engine.Items.Select(item => item.Color)));
        FreshSession(engine);
        var ids = engine.Items.Select(item => item.Id).ToArray();
        store.Save(engine);
        True(ids.SequenceEqual(store.Load().Items.Select(item => item.Id)));
    })),
    ("Fifty-percent widget scale and all settings survive reopening", () => WithStore((store, _, _) =>
    {
        var engine = store.Load();
        engine.Settings.Scale = 0.5;
        engine.Settings.MinimumSeconds = 37;
        engine.Settings.AlwaysOnTop = false;
        engine.Settings.SoundEnabled = false;
        store.Save(engine);
        var restored = store.Load();
        Equal(0.5, restored.Settings.Scale);
        Equal(37, restored.Settings.MinimumSeconds);
        True(!restored.Settings.AlwaysOnTop && !restored.Settings.SoundEnabled);
        FreshSession(restored);
    })),
    ("Legacy paused snapshots also reopen as fresh unstarted sessions", () => WithStore((store, time, _) =>
    {
        var engine = store.Load();
        Success(engine.Activate(engine.Items[0].Id));
        time.Advance(10);
        Success(engine.Pause());
        store.Save(engine);
        var restored = store.Load();
        FreshSession(restored);
        True(restored.Finish() is null);
        restored.StartNewSession();
        FreshSession(restored);
    })),
    ("Corrupt JSON is preserved separately and defaults recover", () => WithStore((store, _, directory) =>
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(store.StatePath, "{broken");
        var engine = store.Load();
        Equal(3, engine.Items.Count);
        True(store.LastLoadWarning is not null);
        True(!File.Exists(store.StatePath));
        var backup = Directory.EnumerateFiles(directory, "state.corrupt-*.json").Single();
        Equal("{broken", File.ReadAllText(backup));
        store.Save(engine);
        True(File.Exists(store.StatePath));
    })),
    ("Malformed settings and duplicate item ids are normalized", () => WithStore((store, _, directory) =>
    {
        Directory.CreateDirectory(directory);
        var id = Guid.NewGuid();
        File.WriteAllText(store.StatePath, JsonSerializer.Serialize(new
        {
            Version = 1,
            Settings = new { MinimumSeconds = -22, Scale = 999.0 },
            Items = new[]
            {
                new { Id = id, Name = "A", Color = "#123456", ElapsedTicks = -100L },
                new { Id = id, Name = "B", Color = "url(bad)", ElapsedTicks = 10L }
            }
        }));
        var engine = store.Load();
        Equal(0, engine.Settings.MinimumSeconds);
        Equal(2.0, engine.Settings.Scale);
        Equal(2, engine.Items.Select(item => item.Id).Distinct().Count());
        Equal(TimeSpan.Zero, engine.Items[0].Elapsed);
        Equal("#CE735A", engine.Items[1].Color);
    })),
    ("Receipt files round trip as Korean UTF-8 and reject path traversal", () => WithStore((store, time, _) =>
    {
        var engine = store.Load();
        Success(engine.Activate(engine.Items[1].Id));
        time.Advance(27);
        var receipt = engine.Finish()!;
        store.SaveReceipt(receipt);
        Equal(receipt.ToPlainText(), File.ReadAllText(Path.Combine(store.ReceiptDirectory, receipt.Id + ".txt")));
        var stored = JsonSerializer.Deserialize<ReceiptData>(File.ReadAllText(Path.Combine(store.ReceiptDirectory, receipt.Id + ".json")))!;
        Equal(receipt.TotalElapsed, stored.TotalElapsed);
        receipt.Id = "../escape";
        Throws<ArgumentException>(() => store.SaveReceipt(receipt));
    })),
    ("Displayed receipt seconds reconcile after fractional switches", () =>
    {
        var (engine, time) = NewClock();
        Success(engine.Activate(engine.Items[0].Id));
        time.Advance(1.8);
        Success(engine.Activate(engine.Items[1].Id));
        time.Advance(1.8);
        var receipt = engine.Finish()!;
        True(receipt.ToPlainText().Contains("합계  00:00:02"));
        Equal(TimeSpan.FromSeconds(2), receipt.DisplayTotalElapsed);
        Equal(TimeSpan.FromSeconds(3.6), receipt.TotalElapsed);
    })
};

var failures = 0;
foreach (var test in tests)
{
    try { test.Run(); Console.WriteLine($"PASS  {test.Name}"); }
    catch (Exception exception) { failures++; Console.Error.WriteLine($"FAIL  {test.Name}\n{exception}"); }
}
Console.WriteLine($"\n{tests.Length - failures}/{tests.Length} passed.");
return failures == 0 ? 0 : 1;

static (ClockEngine Engine, ManualTimeProvider Time) NewClock()
{
    var time = new ManualTimeProvider();
    var engine = new ClockEngine(time);
    engine.RenameItem(engine.Items[0].Id, "기본 업무");
    return (engine, time);
}

static void WithStore(Action<StateStore, ManualTimeProvider, string> test)
{
    var directory = Path.Combine(Path.GetTempPath(), "Switchit.Tests-" + Guid.NewGuid().ToString("N"));
    try
    {
        var time = new ManualTimeProvider();
        test(new StateStore(directory, time), time, directory);
    }
    finally
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}

static void True(bool condition) { if (!condition) throw new Exception("Expected true."); }
static void FreshSession(ClockEngine engine)
{
    True(engine.IsPaused && !engine.HasSession && !engine.HasFinished);
    True(engine.ActiveItemId is null && engine.SessionStartedAt is null);
    Equal(TimeSpan.Zero, engine.TotalElapsed);
    Equal(TimeSpan.Zero, engine.MinimumRemaining);
    True(engine.Items.All(item => item.Elapsed == TimeSpan.Zero && !item.IsArchived));
}
static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new Exception($"Expected [{expected}], actual [{actual}].");
}
static void Success(OperationResult result) { if (!result.Success) throw new Exception(result.Message); }
static void Failure(OperationResult result) { if (result.Success) throw new Exception("Expected operation to be rejected."); }
static void Throws<T>(Action action) where T : Exception
{
    try { action(); }
    catch (T) { return; }
    throw new Exception($"Expected {typeof(T).Name}.");
}

sealed class ManualTimeProvider : TimeProvider
{
    private long _timestamp;
    private DateTimeOffset _utcNow = new(2026, 10, 21, 3, 0, 0, TimeSpan.Zero);
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override long GetTimestamp() => _timestamp;
    public override DateTimeOffset GetUtcNow() => _utcNow;
    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    public void Advance(double seconds)
    {
        var delta = TimeSpan.FromSeconds(seconds);
        _timestamp += delta.Ticks;
        _utcNow += delta;
    }
    public void ShiftWallClock(TimeSpan delta) => _utcNow += delta;
}
