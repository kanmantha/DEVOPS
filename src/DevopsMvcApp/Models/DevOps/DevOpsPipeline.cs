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
