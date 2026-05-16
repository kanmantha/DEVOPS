namespace DevopsMvcApp.Models.DevOps;

public class RepoFile
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public bool IsFolder { get; set; }
    public long Size { get; set; }
    public string? ContentType { get; set; }
}

public class FileContent
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string Encoding { get; set; } = "base64";
    public string RepositoryId { get; set; } = string.Empty;
    public string Branch { get; set; } = string.Empty;
}

public class UploadFileRequest
{
    public string RepositoryId { get; set; } = string.Empty;
    public string Branch { get; set; } = "main";
    public string FilePath { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string Comment { get; set; } = "File uploaded via DevOps Portal";
}
