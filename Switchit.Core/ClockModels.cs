using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;

namespace Switchit.Core;

public sealed class ClockSettings
{
    public int MinimumSeconds { get; set; }
    public bool AlwaysOnTop { get; set; } = true;
    public double Scale { get; set; } = 0.8;
    public bool SoundEnabled { get; set; } = true;
}

public sealed class ClockItem
{
    public Guid Id { get; internal set; } = Guid.NewGuid();
    public string Name { get; internal set; } = "업무";
    public string Color { get; set; } = "#CE735A";
    public TimeSpan Elapsed { get; internal set; }
    public bool IsArchived { get; internal set; }
}

public sealed record OperationResult(bool Success, string Message)
{
    public static OperationResult Ok(string message = "") => new(true, message);
    public static OperationResult Fail(string message) => new(false, message);
}

public sealed class ReceiptLine
{
    public string Name { get; set; } = "";
    public string Color { get; set; } = "#CE735A";
    public TimeSpan Elapsed { get; set; }
}

public sealed class ReceiptData
{
    public string Id { get; set; } = "";
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset FinishedAt { get; set; }
    public List<ReceiptLine> Items { get; set; } = [];
    [JsonIgnore]
    public TimeSpan TotalElapsed => TimeSpan.FromTicks(Items.Sum(item => item.Elapsed.Ticks));
    [JsonIgnore]
    public TimeSpan DisplayTotalElapsed => TimeSpan.FromSeconds(Items.Sum(item => Math.Floor(item.Elapsed.TotalSeconds)));

    public string ToPlainText()
    {
        var text = new StringBuilder();
        text.AppendLine("TIME RECIPT");
        text.AppendLine("--------------------------------");
        text.AppendLine($"시작  {StartedAt:yyyy.MM.dd HH:mm:ss}");
        text.AppendLine($"종료  {FinishedAt:yyyy.MM.dd HH:mm:ss}");
        text.AppendLine("--------------------------------");
        foreach (var item in Items)
            text.AppendLine($"{item.Name}  {FormatDuration(item.Elapsed)}");
        text.AppendLine("--------------------------------");
        // Total the displayed seconds as well, so the printed statement always reconciles.
        text.AppendLine($"합계  {FormatDuration(DisplayTotalElapsed)}");
        text.AppendLine();
        text.Append($"No. {Id}");
        return text.ToString();
    }

    public static string FormatDuration(TimeSpan elapsed)
    {
        var seconds = Math.Max(0, (long)elapsed.TotalSeconds);
        return string.Create(CultureInfo.InvariantCulture, $"{seconds / 3600:00}:{seconds / 60 % 60:00}:{seconds % 60:00}");
    }
}
