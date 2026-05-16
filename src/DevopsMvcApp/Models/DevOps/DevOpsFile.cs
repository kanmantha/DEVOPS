namespace DevopsMvcApp.Models.DevOps;

/// <summary>Represents a file or folder in a repository returned by the items API.</summary>
public class RepoFile
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public bool IsFolder { get; set; }
    public long Size { get; set; }
    public string? ContentType { get; set; }
}

/// <summary>Decoded content of a single file, used by the file viewer and inline editor.</summary>
public class FileContent
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    /// <summary>Content encoding — "base64" for binary, "rawtext" for text files.</summary>
    public string Encoding { get; set; } = "base64";
    public string RepositoryId { get; set; } = string.Empty;
    public string Branch { get; set; } = string.Empty;
}

/// <summary>Form model for the file upload view.</summary>
public class UploadFileRequest
{
    public string RepositoryId { get; set; } = string.Empty;
    public string Branch { get; set; } = "main";
    public string FilePath { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string Comment { get; set; } = "File uploaded via DevOps Portal";
}
