namespace DevopsMvcApp.Models.DevOps;

/// <summary>Represents a deployment environment (Dev/Staging/Prod) with two slots for blue-green deployments.</summary>
public class DeploymentEnvironment
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? AppServiceName { get; set; }
    public string? ResourceGroup { get; set; }
    public List<DeploymentSlot> Slots { get; set; } = new();
    public DateTime LastDeployed { get; set; }
    public string? LastDeployedVersion { get; set; }

    /// <summary>"Live" if any slot is active, otherwise "Idle".</summary>
    public string Status => Slots.Any(s => s.Active) ? "Live" : "Idle";
}

/// <summary>A blue-green slot within a deployment environment (Blue or Green, only one active at a time).</summary>
public class DeploymentSlot
{
    public string Name { get; set; } = string.Empty;
    public string? Label { get; set; }
    /// <summary>Whether this slot is currently serving traffic.</summary>
    public bool Active { get; set; }
    public string? CurrentVersion { get; set; }
    public DateTime? LastDeployed { get; set; }
    public string? DeploymentStatus { get; set; }
    public string Color => Label == "Blue" ? "#2563eb" : "#059669";
    public string BackgroundColor => Label == "Blue" ? "#dbeafe" : "#d1fae5";
}

/// <summary>Records a deployment operation (which version was deployed, when, and by whom).</summary>
public class DeploymentRecord
{
    public int Id { get; set; }
    public string Environment { get; set; } = string.Empty;
    public string Slot { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Status { get; set; } = "Pending";
    public string? TriggeredBy { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? ReleaseId { get; set; }
    public int? BuildId { get; set; }
    public string? Notes { get; set; }
}

/// <summary>An Azure DevOps release definition (classic release pipeline).</summary>
public class ReleaseDefinition
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int Revision { get; set; }
    public string WebUrl { get; set; } = string.Empty;
}

/// <summary>An Azure DevOps release instance created from a release definition.</summary>
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

/// <summary>Status of a single environment within a release.</summary>
public class ReleaseEnvironmentStatus
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime? QueuedOn { get; set; }
    public DateTime? CompletedOn { get; set; }
}

/// <summary>Request payload for swapping slots between blue and green.</summary>
public class SwapSlotRequest
{
    public string Environment { get; set; } = string.Empty;
    public string SourceSlot { get; set; } = string.Empty;
    public string TargetSlot { get; set; } = string.Empty;
}

/// <summary>Request payload for creating a new release definition.</summary>
public class CreateReleaseDefinitionRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
}
