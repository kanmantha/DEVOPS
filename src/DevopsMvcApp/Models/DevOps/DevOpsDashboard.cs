namespace DevopsMvcApp.Models.DevOps;

/// <summary>Aggregated data for the DevOps dashboard view — repos, pipelines, PRs, builds, stats, and activity feed.</summary>
public class DevOpsDashboard
{
    /// <summary>Stored connection used to fetch data.</summary>
    public DevOpsConnection Connection { get; set; } = new();
    public List<DevOpsRepository> Repositories { get; set; } = new();
    public List<DevOpsPipeline> Pipelines { get; set; } = new();
    public List<DevOpsPullRequest> PullRequests { get; set; } = new();
    public List<DevOpsBuild> RecentBuilds { get; set; } = new();

    /// <summary>True when organization and project are configured.</summary>
    public bool IsConnected => !string.IsNullOrEmpty(Connection.Organization);
    public string? ErrorMessage { get; set; }
    public DashboardStats Stats { get; set; } = new();
    public List<DashboardActivity> RecentActivity { get; set; } = new();
}

/// <summary>Computed statistics shown on the dashboard — success rates, counts per resource type.</summary>
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

    /// <summary>Percentage of builds that succeeded.</summary>
    public double BuildSuccessRate =>
        TotalBuilds > 0 ? Math.Round((double)SucceededBuilds / TotalBuilds * 100, 1) : 0;

    /// <summary>Percentage of PRs that were completed (vs active or abandoned).</summary>
    public double PrCompletionRate =>
        (ActivePrs + CompletedPrs) > 0 ? Math.Round((double)CompletedPrs / (ActivePrs + CompletedPrs) * 100, 1) : 0;
}

/// <summary>Single activity entry in the recent-activity feed on the dashboard.</summary>
public class DashboardActivity
{
    /// <summary>Activity type discriminator: "commit", "build", "pr", "release".</summary>
    public string Type { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Subtitle { get; set; }
    public string? Url { get; set; }
    public DateTime Timestamp { get; set; }
    public string? Icon { get; set; }
    public string? Color { get; set; }
}
