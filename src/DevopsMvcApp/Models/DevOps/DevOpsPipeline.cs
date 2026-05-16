namespace DevopsMvcApp.Models.DevOps;

/// <summary>YAML pipeline definition returned from the Azure DevOps pipelines API.</summary>
public class DevOpsPipeline
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Folder { get; set; } = string.Empty;
    public int Revision { get; set; }
    public string WebUrl { get; set; } = string.Empty;
}

/// <summary>Form model for creating a new pipeline (repo, branch, YAML path).</summary>
public class CreatePipelineRequest
{
    public string Name { get; set; } = string.Empty;
    public string RepositoryId { get; set; } = string.Empty;
    public string DefaultBranch { get; set; } = "main";
    public string YamlPath { get; set; } = "azure-pipelines.yml";
}

/// <summary>A single run/execution of a pipeline.</summary>
public class PipelineRun
{
    public int Id { get; set; }
    public string State { get; set; } = string.Empty;
    public string Result { get; set; } = string.Empty;
    public DateTime CreatedDate { get; set; }
    public string WebUrl { get; set; } = string.Empty;
}

/// <summary>Result of an automated end-to-end deploy operation.</summary>
public class AutoDeployResult
{
    public List<DeployStep> Steps { get; set; } = new();
    public bool Success => Steps.All(s => !s.Failed);
    public string? PipelineUrl { get; set; }
    public string? BuildUrl { get; set; }
    public string? ReleaseUrl { get; set; }
}

/// <summary>A single step within the auto-deploy flow.</summary>
public class DeployStep
{
    public string Name { get; set; } = "";
    public bool Completed { get; set; }
    public bool Failed { get; set; }
    public string? ErrorMessage { get; set; }
    public string? Detail { get; set; }
}
