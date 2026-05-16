namespace DevopsMvcApp.Models.DevOps;

/// <summary>Git repository returned from the Azure DevOps repositories API.</summary>
public class DevOpsRepository
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string RemoteUrl { get; set; } = string.Empty;
    public string DefaultBranch { get; set; } = "main";
    public string WebUrl { get; set; } = string.Empty;
}

/// <summary>Form model for creating a new repository.</summary>
public class CreateRepositoryRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
}
