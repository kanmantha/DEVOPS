namespace DevopsMvcApp.Models.DevOps;

public class BranchInfo
{
    public string Name { get; set; } = string.Empty;
    public string Ref { get; set; } = string.Empty;
    public string ObjectId { get; set; } = string.Empty;
    public string? Creator { get; set; }
}
