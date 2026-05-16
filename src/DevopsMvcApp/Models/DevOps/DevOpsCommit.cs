namespace DevopsMvcApp.Models.DevOps;

/// <summary>Git commit details returned from the Azure DevOps commits API.</summary>
public class CommitInfo
{
    public string CommitId { get; set; } = string.Empty;
    public string Comment { get; set; } = string.Empty;
    public string AuthorName { get; set; } = string.Empty;
    public string AuthorEmail { get; set; } = string.Empty;
    public DateTime CommitDate { get; set; }
    public string RemoteUrl { get; set; } = string.Empty;
    public List<string> ChangedFiles { get; set; } = new();
}
