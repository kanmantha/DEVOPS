namespace DevopsMvcApp.Models.DevOps;

public class DevOpsPipeline
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Folder { get; set; } = string.Empty;
    public int Revision { get; set; }
    public string WebUrl { get; set; } = string.Empty;
}

public class CreatePipelineRequest
{
    public string Name { get; set; } = string.Empty;
    public string RepositoryId { get; set; } = string.Empty;
    public string DefaultBranch { get; set; } = "main";
    public string YamlPath { get; set; } = "azure-pipelines.yml";
}

public class PipelineRun
{
    public int Id { get; set; }
    public string State { get; set; } = string.Empty;
    public string Result { get; set; } = string.Empty;
    public DateTime CreatedDate { get; set; }
    public string WebUrl { get; set; } = string.Empty;
}
