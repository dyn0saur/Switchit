using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Switchit.Core;

public sealed class StateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _dataDirectory;
    private readonly TimeProvider? _timeProvider;

    public StateStore(string dataDirectory, TimeProvider? timeProvider = null)
    {
        if (string.IsNullOrWhiteSpace(dataDirectory)) throw new ArgumentException("저장 위치가 필요합니다.", nameof(dataDirectory));
        _dataDirectory = Path.GetFullPath(dataDirectory);
        _timeProvider = timeProvider;
    }

    public string DataDirectory => _dataDirectory;
    public string StatePath => Path.Combine(_dataDirectory, "state.json");
    public string ReceiptDirectory => Path.Combine(_dataDirectory, "receipts");
    public string? LastLoadWarning { get; private set; }

    /// <summary>Load task configuration and settings into a fresh, unstarted session.</summary>
    public ClockEngine Load(bool resumeSession = false)
    {
        LastLoadWarning = null;
        if (!File.Exists(StatePath)) return new ClockEngine(_timeProvider);
        try
        {
            using var file = File.OpenRead(StatePath);
            if (file.Length > 8 * 1024 * 1024) throw new InvalidDataException("저장 파일이 너무 큽니다.");
            var snapshot = JsonSerializer.Deserialize<EngineSnapshot>(file, JsonOptions)
                ?? throw new InvalidDataException("저장 파일이 비어 있습니다.");
            if (snapshot.Version != 1) throw new InvalidDataException("지원하지 않는 저장 형식입니다.");
            return ClockEngine.Restore(snapshot, _timeProvider, resumeSession);
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException or OverflowException or ArgumentOutOfRangeException)
        {
            var recoveredPath = Path.Combine(_dataDirectory, $"state.corrupt-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.json");
            File.Move(StatePath, recoveredPath);
            LastLoadWarning = "이전 저장 파일을 읽지 못해 따로 보관하고 새로 시작했습니다.";
            return new ClockEngine(_timeProvider);
        }
    }

    public void Save(ClockEngine engine)
    {
        ArgumentNullException.ThrowIfNull(engine);
        Directory.CreateDirectory(_dataDirectory);
        AtomicWrite(StatePath, JsonSerializer.Serialize(engine.CreateSnapshot(), JsonOptions));
    }

    public void SaveReceipt(ReceiptData receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        if (!Regex.IsMatch(receipt.Id, "^[0-9a-zA-Z_-]{1,80}$"))
            throw new ArgumentException("영수증 번호가 올바르지 않습니다.", nameof(receipt));
        Directory.CreateDirectory(ReceiptDirectory);
        AtomicWrite(Path.Combine(ReceiptDirectory, receipt.Id + ".json"), JsonSerializer.Serialize(receipt, JsonOptions));
        AtomicWrite(Path.Combine(ReceiptDirectory, receipt.Id + ".txt"), receipt.ToPlainText());
    }

    private static void AtomicWrite(string destination, string contents)
    {
        var temporary = destination + $".{Guid.NewGuid():N}.tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                writer.Write(contents);
                writer.Flush();
                stream.Flush(true);
            }
            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}

internal sealed class EngineSnapshot
{
    public int Version { get; set; } = 1;
    public ClockSettings? Settings { get; set; }
    public DateTimeOffset? SessionStartedAt { get; set; }
    public bool HasFinished { get; set; }
    public ReceiptData? LastReceipt { get; set; }
    public Guid? ActiveItemId { get; set; }
    public Guid? FocusItemId { get; set; }
    public long ActivationElapsedTicks { get; set; }
    public List<ItemSnapshot?>? Items { get; set; }
}

internal sealed class ItemSnapshot
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string Color { get; set; } = "#CE735A";
    public long ElapsedTicks { get; set; }
    public bool IsArchived { get; set; }
}
