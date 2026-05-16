namespace DevopsMvcApp.Models.DevOps;

public class DevOpsPullRequest
{
    public int PullRequestId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string SourceRefName { get; set; } = string.Empty;
    public string TargetRefName { get; set; } = string.Empty;
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreationDate { get; set; }
    public string WebUrl { get; set; } = string.Empty;
}

public class CreatePullRequestRequest
{
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string SourceBranch { get; set; } = string.Empty;
    public string TargetBranch { get; set; } = "main";
    public string RepositoryId { get; set; } = string.Empty;
}

public class PullRequestComment
{
    public int Id { get; set; }
    public string Content { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public DateTime PostedDate { get; set; }
}
