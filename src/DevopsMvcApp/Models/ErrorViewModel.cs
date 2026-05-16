namespace DevopsMvcApp.Models;

/// <summary>Error page view model — displays a request ID for debugging.</summary>
public class ErrorViewModel
{
    public string? RequestId { get; set; }

    /// <summary>True when RequestId is present and should be shown on the error page.</summary>
    public bool ShowRequestId => !string.IsNullOrEmpty(RequestId);
}
