namespace DevopsMvcApp.Models.DevOps;

public class DeploymentEnvironment
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? AppServiceName { get; set; }
    public string? ResourceGroup { get; set; }
    public List<DeploymentSlot> Slots { get; set; } = new();
    public DateTime LastDeployed { get; set; }
    public string? LastDeployedVersion { get; set; }
    public string Status => Slots.Any(s => s.Active) ? "Live" : "Idle";
}

public class DeploymentSlot
{
    public string Name { get; set; } = string.Empty;
    public string? Label { get; set; }    // "Blue" or "Green"
    public bool Active { get; set; }
    public string? CurrentVersion { get; set; }
    public DateTime? LastDeployed { get; set; }
    public string? DeploymentStatus { get; set; }  // Success, Failed, Deploying
    public string Color => Label == "Blue" ? "#2563eb" : "#059669";
    public string BackgroundColor => Label == "Blue" ? "#dbeafe" : "#d1fae5";
}

public class DeploymentRecord
{
    public int Id { get; set; }
    public string Environment { get; set; } = string.Empty;
    public string Slot { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Status { get; set; } = "Pending";  // Pending, Deploying, Success, Failed, RolledBack
    public string? TriggeredBy { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? ReleaseId { get; set; }     // Link to Azure DevOps release
    public int? BuildId { get; set; }
    public string? Notes { get; set; }
}

public class ReleaseDefinition
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int Revision { get; set; }
    public string WebUrl { get; set; } = string.Empty;
}

public class ReleaseInfo
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime CreatedOn { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public string WebUrl { get; set; } = string.Empty;
    public List<ReleaseEnvironmentStatus> Environments { get; set; } = new();
}

public class ReleaseEnvironmentStatus
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime? QueuedOn { get; set; }
    public DateTime? CompletedOn { get; set; }
}

public class SwapSlotRequest
{
    public string Environment { get; set; } = string.Empty;
    public string SourceSlot { get; set; } = string.Empty;
    public string TargetSlot { get; set; } = string.Empty;
}
