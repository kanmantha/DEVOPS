namespace DevopsMvcApp.Models.DevOps;

/// <summary>Build artifact produced by a pipeline or build.</summary>
public class DevOpsArtifact
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public int BuildId { get; set; }
    public string DownloadUrl { get; set; } = string.Empty;
    public long Size { get; set; }
    public DateTime CreatedOn { get; set; }
}

/// <summary>Build summary returned by the builds list API.</summary>
public class DevOpsBuild
{
    public int Id { get; set; }
    public string BuildNumber { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Result { get; set; } = string.Empty;
    public DateTime QueueTime { get; set; }
    public string WebUrl { get; set; } = string.Empty;
}
