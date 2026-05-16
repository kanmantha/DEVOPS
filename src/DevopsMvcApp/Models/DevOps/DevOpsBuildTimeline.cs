namespace DevopsMvcApp.Models.DevOps;

public class BuildTimelineRecord
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string? Result { get; set; }
    public int? LogId { get; set; }
    public DateTime? StartTime { get; set; }
    public DateTime? FinishTime { get; set; }
    public double? PercentComplete { get; set; }
    public List<BuildTimelineRecord> Details { get; set; } = new();
}

public class BuildLogEntry
{
    public int LineNumber { get; set; }
    public string Text { get; set; } = string.Empty;
}
