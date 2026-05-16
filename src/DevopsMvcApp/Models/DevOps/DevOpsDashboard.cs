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

    // Enhanced stats
    public DashboardStats Stats { get; set; } = new();
    public List<DashboardActivity> RecentActivity { get; set; } = new();
}

public class DashboardStats
{
    public int TotalRepos { get; set; }
    public int TotalPipelines { get; set; }
    public int ActivePrs { get; set; }
    public int CompletedPrs { get; set; }
    public int AbandonedPrs { get; set; }
    public int TotalBuilds { get; set; }
    public int SucceededBuilds { get; set; }
    public int FailedBuilds { get; set; }
    public int InProgressBuilds { get; set; }
    public int TotalBranches { get; set; }
    public int TotalCommits { get; set; }
    public int TotalReleases { get; set; }
    public int TotalReleaseDefs { get; set; }
    public int ActivePipelineRuns { get; set; }

    public double BuildSuccessRate =>
        TotalBuilds > 0 ? Math.Round((double)SucceededBuilds / TotalBuilds * 100, 1) : 0;
    public double PrCompletionRate =>
        (ActivePrs + CompletedPrs) > 0 ? Math.Round((double)CompletedPrs / (ActivePrs + CompletedPrs) * 100, 1) : 0;
}

public class DashboardActivity
{
    public string Type { get; set; } = ""; // "commit", "build", "pr", "release"
    public string Title { get; set; } = "";
    public string? Subtitle { get; set; }
    public string? Url { get; set; }
    public DateTime Timestamp { get; set; }
    public string? Icon { get; set; }
    public string? Color { get; set; }
}