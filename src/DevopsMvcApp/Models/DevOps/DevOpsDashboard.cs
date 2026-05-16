namespace DevopsMvcApp.Models.DevOps;

public class DevOpsDashboard
{
    public DevOpsConnection Connection { get; set; } = new();
    public List<DevOpsRepository> Repositories { get; set; } = new();
    public List<DevOpsPipeline> Pipelines { get; set; } = new();
    public List<DevOpsPullRequest> PullRequests { get; set; } = new();
    public List<DevOpsBuild> RecentBuilds { get; set; } = new();
    public bool IsConnected => !string.IsNullOrEmpty(Connection.Organization);
    public string? ErrorMessage { get; set; }
}
