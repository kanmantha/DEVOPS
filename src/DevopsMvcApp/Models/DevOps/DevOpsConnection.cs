using System.ComponentModel.DataAnnotations;

namespace DevopsMvcApp.Models.DevOps;

/// <summary>Azure DevOps connection credentials — stored in session, used to authenticate all API calls.</summary>
public class DevOpsConnection
{
    [Required]
    [Display(Name = "Organization")]
    public string Organization { get; set; } = string.Empty;

    [Required]
    [Display(Name = "Project")]
    public string Project { get; set; } = string.Empty;

    [Required]
    [Display(Name = "Personal Access Token")]
    [DataType(DataType.Password)]
    public string Pat { get; set; } = string.Empty;

    [Display(Name = "Base URL")]
    public string BaseUrl => $"https://dev.azure.com/{Organization}";
}
