using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DevopsMvcApp.Models.DevOps;

namespace DevopsMvcApp.Services;

public class DevOpsService
{
    private readonly HttpClient _http;
    private DevOpsConnection? _connection;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public DevOpsService(HttpClient http)
    {
        _http = http;
    }

    public void SetConnection(DevOpsConnection connection)
    {
        _connection = connection;
        var authBytes = Encoding.ASCII.GetBytes($":{connection.Pat}");
        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Basic", Convert.ToBase64String(authBytes));
    }

    public bool IsConnected => _connection != null;
    public string? Org => _connection?.Organization;
    public string? Project => _connection?.Project;

    private string Api(string path) =>
        $"https://dev.azure.com/{_connection!.Organization}/{_connection.Project}/_apis/{path}?api-version=7.1";

    private string OrgApi(string path) =>
        $"https://dev.azure.com/{_connection!.Organization}/_apis/{path}?api-version=7.1";

    private string VsmApi(string path) =>
        $"https://vsrm.dev.azure.com/{_connection!.Organization}/{_connection.Project}/_apis/release/{path}?api-version=7.1";

    // ────────────────────────────── Release Management (Blue-Green) ───────────

    public async Task<List<ReleaseDefinition>> GetReleaseDefinitionsAsync()
    {
        var json = await GetAsync(VsmApi("definitions"));
        var root = JsonDocument.Parse(json);
        if (!root.RootElement.TryGetProperty("value", out var value))
            return new();
        return value.EnumerateArray().Select(d => new ReleaseDefinition
        {
            Id = d.GetProperty("id").GetInt32(),
            Name = d.GetProperty("name").GetString()!,
            Description = d.TryGetProperty("description", out var desc) ? desc.GetString() : null,
            Revision = d.GetProperty("revision").GetInt32(),
            WebUrl = d.TryGetProperty("_links", out var links) && links.TryGetProperty("web", out var web)
                ? web.GetProperty("href").GetString()! : ""
        }).ToList();
    }

    public async Task<List<ReleaseInfo>> GetReleasesAsync(int top = 10)
    {
        var json = await GetAsync(VsmApi("releases") + $"&$top={top}&$expand=environments");
        var root = JsonDocument.Parse(json);
        if (!root.RootElement.TryGetProperty("value", out var value))
            return new();
        return value.EnumerateArray().Select(r => new ReleaseInfo
        {
            Id = r.GetProperty("id").GetInt32(),
            Name = r.GetProperty("name").GetString()!,
            Status = r.GetProperty("status").GetString()!,
            Description = r.TryGetProperty("description", out var d) ? d.GetString() : null,
            CreatedOn = r.GetProperty("createdOn").GetDateTime(),
            CreatedBy = r.TryGetProperty("createdBy", out var cb) ? cb.GetProperty("displayName").GetString()! : "",
            WebUrl = r.TryGetProperty("_links", out var links) && links.TryGetProperty("web", out var web)
                ? web.GetProperty("href").GetString()! : "",
            Environments = r.TryGetProperty("environments", out var envs)
                ? envs.EnumerateArray().Select(e => new ReleaseEnvironmentStatus
                {
                    Id = e.GetProperty("id").GetInt32(),
                    Name = e.GetProperty("name").GetString()!,
                    Status = e.GetProperty("status").GetString()!,
                    QueuedOn = e.TryGetProperty("queuedOn", out var q) ? q.GetDateTime() : null,
                    CompletedOn = e.TryGetProperty("completedOn", out var co) ? co.GetDateTime() : null
                }).ToList() : new()
        }).ToList();
    }

    public async Task<ReleaseInfo?> CreateReleaseAsync(int definitionId, string? description = null)
    {
        var body = JsonSerializer.Serialize(new
        {
            definitionId,
            description = description ?? $"Release from DevOps Portal",
            isDraft = false,
            artifacts = Array.Empty<object>()
        }, JsonOpts);
        var json = await PostAsync(VsmApi("releases"), body);
        var r = JsonDocument.Parse(json).RootElement;
        return new ReleaseInfo
        {
            Id = r.GetProperty("id").GetInt32(),
            Name = r.GetProperty("name").GetString()!,
            Status = r.GetProperty("status").GetString()!,
            CreatedOn = r.GetProperty("createdOn").GetDateTime(),
            WebUrl = r.TryGetProperty("_links", out var links) && links.TryGetProperty("web", out var web)
                ? web.GetProperty("href").GetString()! : ""
        };
    }

    // ── Simulated environments for Blue-Green demo ──

    public Task<List<DeploymentEnvironment>> GetDeploymentEnvironmentsAsync()
    {
        var envs = new List<DeploymentEnvironment>
        {
            new()
            {
                Name = "Development",
                Description = "Dev team integration environment",
                AppServiceName = "myapp-dev",
                LastDeployed = DateTime.UtcNow.AddHours(-2),
                LastDeployedVersion = "v1.2.3-build.45",
                Slots = new()
                {
                    new() { Name = "dev-blue", Label = "Blue", Active = true, CurrentVersion = "v1.2.3-build.45", LastDeployed = DateTime.UtcNow.AddHours(-2), DeploymentStatus = "Success" },
                    new() { Name = "dev-green", Label = "Green", Active = false, CurrentVersion = "v1.2.2-build.44", LastDeployed = DateTime.UtcNow.AddDays(-1), DeploymentStatus = "Success" }
                }
            },
            new()
            {
                Name = "Staging",
                Description = "Pre-production validation",
                AppServiceName = "myapp-staging",
                LastDeployed = DateTime.UtcNow.AddDays(-1),
                LastDeployedVersion = "v1.2.2-build.44",
                Slots = new()
                {
                    new() { Name = "stg-blue", Label = "Blue", Active = false, CurrentVersion = "v1.2.2-build.44", LastDeployed = DateTime.UtcNow.AddDays(-1), DeploymentStatus = "Success" },
                    new() { Name = "stg-green", Label = "Green", Active = true, CurrentVersion = "v1.2.1-build.43", LastDeployed = DateTime.UtcNow.AddDays(-3), DeploymentStatus = "Success" }
                }
            },
            new()
            {
                Name = "Production",
                Description = "Live production environment",
                AppServiceName = "myapp-prod",
                LastDeployed = DateTime.UtcNow.AddDays(-5),
                LastDeployedVersion = "v1.2.1-build.43",
                Slots = new()
                {
                    new() { Name = "prod-blue", Label = "Blue", Active = true, CurrentVersion = "v1.2.1-build.43", LastDeployed = DateTime.UtcNow.AddDays(-5), DeploymentStatus = "Success" },
                    new() { Name = "prod-green", Label = "Green", Active = false, CurrentVersion = "v1.2.0-build.42", LastDeployed = DateTime.UtcNow.AddDays(-10), DeploymentStatus = "Success" }
                }
            }
        };
        return Task.FromResult(envs);
    }

    public Task<DeploymentEnvironment> SwapSlotsAsync(string envName)
    {
        var envs = GetDeploymentEnvironmentsAsync().Result;
        var env = envs.FirstOrDefault(e =>
            e.Name.Equals(envName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Environment '{envName}' not found");

        var activeSlot = env.Slots.FirstOrDefault(s => s.Active);
        var inactiveSlot = env.Slots.FirstOrDefault(s => !s.Active);
        if (activeSlot == null || inactiveSlot == null)
            throw new InvalidOperationException("Need both Blue and Green slots to swap");

        // Swap: deploy the inactive slot's version to active, then toggle
        var tempVersion = activeSlot.CurrentVersion;
        activeSlot.CurrentVersion = inactiveSlot.CurrentVersion;
        inactiveSlot.CurrentVersion = tempVersion;

        activeSlot.Active = false;
        inactiveSlot.Active = true;

        activeSlot.LastDeployed = DateTime.UtcNow;
        inactiveSlot.LastDeployed = DateTime.UtcNow;
        activeSlot.DeploymentStatus = "Swapped";
        inactiveSlot.DeploymentStatus = "Success";

        env.LastDeployed = DateTime.UtcNow;
        env.LastDeployedVersion = inactiveSlot.CurrentVersion;

        return Task.FromResult(env);
    }

    public Task<DeploymentRecord> DeployToSlotAsync(string envName, string slotLabel, string version)
    {
        var record = new DeploymentRecord
        {
            Id = Random.Shared.Next(1000, 9999),
            Environment = envName,
            Slot = slotLabel,
            Version = version,
            Status = "Deploying",
            TriggeredBy = "Portal User",
            StartedAt = DateTime.UtcNow,
            BuildId = Random.Shared.Next(100, 999)
        };

        // Simulate deployment
        var envs = GetDeploymentEnvironmentsAsync().Result;
        var env = envs.FirstOrDefault(e => e.Name == envName);
        var slot = env?.Slots.FirstOrDefault(s => s.Label == slotLabel);
        if (slot != null)
        {
            slot.CurrentVersion = version;
            slot.LastDeployed = DateTime.UtcNow;
            slot.DeploymentStatus = "Success";
        }
        record.Status = "Success";
        record.CompletedAt = DateTime.UtcNow.AddSeconds(15);

        return Task.FromResult(record);
    }

    // ────────────────────────────── Repositories ──────────────────────────────

    public async Task<List<DevOpsRepository>> GetRepositoriesAsync()
    {
        var json = await GetAsync(Api("git/repositories"));
        var root = JsonDocument.Parse(json);
        return root.RootElement.GetProperty("value").EnumerateArray().Select(r => new DevOpsRepository
        {
            Id = r.GetProperty("id").GetString()!,
            Name = r.GetProperty("name").GetString()!,
            Url = r.GetProperty("url").GetString()!,
            RemoteUrl = r.GetProperty("remoteUrl").GetString()!,
            DefaultBranch = r.TryGetProperty("defaultBranch", out var b) ? b.GetString() ?? "main" : "main",
            WebUrl = r.GetProperty("webUrl").GetString()!
        }).ToList();
    }

    public async Task<DevOpsRepository?> CreateRepositoryAsync(string name)
    {
        var body = JsonSerializer.Serialize(new { name }, JsonOpts);
        var json = await PostAsync(Api("git/repositories"), body);
        var r = JsonDocument.Parse(json).RootElement;
        return new DevOpsRepository
        {
            Id = r.GetProperty("id").GetString()!,
            Name = r.GetProperty("name").GetString()!,
            RemoteUrl = r.GetProperty("remoteUrl").GetString()!,
            WebUrl = r.GetProperty("webUrl").GetString()!
        };
    }

    public async Task<bool> DeleteRepositoryAsync(string repoId)
    {
        await DeleteAsync(Api($"git/repositories/{repoId}"));
        return true;
    }

    // ────────────────────────────── Pipeline files (commit YAML) ──────────────────────────────

    public async Task CommitFileAsync(string repoId, string branch, string path, string content, string comment)
    {
        // Get repo info for push
        var refJson = await PostAsync(Api($"git/repositories/{repoId}/refs"), JsonSerializer.Serialize(new
        {
            refUpdates = new[] { new { name = $"refs/heads/{branch}", oldObjectId = "0000000000000000000000000000000000000000" } },
            commits = new[]
            {
                new
                {
                    comment,
                    changes = new[]
                    {
                        new
                        {
                            changeType = "add",
                            item = new { path },
                            newContent = new { content, contentType = "rawtext" }
                        }
                    }
                }
            }
        }, JsonOpts));
    }

    // ────────────────────────────── Pipelines ──────────────────────────────

    public async Task<List<DevOpsPipeline>> GetPipelinesAsync()
    {
        var json = await GetAsync(Api("pipelines"));
        var root = JsonDocument.Parse(json);
        return root.RootElement.GetProperty("value").EnumerateArray().Select(p => new DevOpsPipeline
        {
            Id = p.GetProperty("id").GetInt32(),
            Name = p.GetProperty("name").GetString()!,
            Folder = p.TryGetProperty("folder", out var f) ? f.GetString() ?? "" : "",
            Revision = p.GetProperty("revision").GetInt32(),
            WebUrl = p.GetProperty("_links").GetProperty("web").GetProperty("href").GetString()!
        }).ToList();
    }

    public async Task<DevOpsPipeline?> CreatePipelineAsync(CreatePipelineRequest req)
    {
        var body = JsonSerializer.Serialize(new
        {
            name = req.Name,
            configuration = new
            {
                type = "yaml",
                path = req.YamlPath,
                repository = new
                {
                    id = req.RepositoryId,
                    type = "azureReposGit",
                    defaultBranch = $"refs/heads/{req.DefaultBranch}"
                }
            }
        }, JsonOpts);
        var json = await PostAsync(Api("pipelines"), body);
        var p = JsonDocument.Parse(json).RootElement;
        return new DevOpsPipeline
        {
            Id = p.GetProperty("id").GetInt32(),
            Name = p.GetProperty("name").GetString()!,
            WebUrl = p.GetProperty("_links").GetProperty("web").GetProperty("href").GetString()!
        };
    }

    public async Task<List<PipelineRun>> GetPipelineRunsAsync(int pipelineId)
    {
        var json = await GetAsync(Api($"pipelines/{pipelineId}/runs"));
        var root = JsonDocument.Parse(json);
        return root.RootElement.GetProperty("value").EnumerateArray().Select(r => new PipelineRun
        {
            Id = r.GetProperty("id").GetInt32(),
            State = r.GetProperty("state").GetString()!,
            Result = r.TryGetProperty("result", out var res) ? res.GetString() ?? "" : "",
            CreatedDate = r.GetProperty("createdDate").GetDateTime(),
            WebUrl = r.GetProperty("_links").GetProperty("web").GetProperty("href").GetString()!
        }).ToList();
    }

    public async Task<PipelineRun?> RunPipelineAsync(int pipelineId)
    {
        var body = JsonSerializer.Serialize(new { }, JsonOpts);
        var json = await PostAsync(Api($"pipelines/{pipelineId}/runs"), body);
        var r = JsonDocument.Parse(json).RootElement;
        return new PipelineRun
        {
            Id = r.GetProperty("id").GetInt32(),
            State = r.GetProperty("state").GetString()!,
            CreatedDate = r.GetProperty("createdDate").GetDateTime(),
            WebUrl = r.GetProperty("_links").GetProperty("web").GetProperty("href").GetString()!
        };
    }

    // ────────────────────────────── Pull Requests ──────────────────────────────

    public async Task<List<DevOpsPullRequest>> GetPullRequestsAsync(string repoId, string? status = "active")
    {
        var url = Api($"git/repositories/{repoId}/pullrequests") + (status != null ? $"&searchCriteria.status={status}" : "");
        var json = await GetAsync(url);
        var root = JsonDocument.Parse(json);
        return root.RootElement.GetProperty("value").EnumerateArray().Select(pr => new DevOpsPullRequest
        {
            PullRequestId = pr.GetProperty("pullRequestId").GetInt32(),
            Title = pr.GetProperty("title").GetString()!,
            Description = pr.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "",
            Status = pr.GetProperty("status").GetString()!,
            SourceRefName = pr.GetProperty("sourceRefName").GetString()!,
            TargetRefName = pr.GetProperty("targetRefName").GetString()!,
            CreatedBy = pr.GetProperty("createdBy").GetProperty("displayName").GetString()!,
            CreationDate = pr.GetProperty("creationDate").GetDateTime(),
            WebUrl = pr.GetProperty("webUrl").GetString()!
        }).ToList();
    }

    public async Task<DevOpsPullRequest?> CreatePullRequestAsync(CreatePullRequestRequest req)
    {
        var body = JsonSerializer.Serialize(new
        {
            sourceRefName = $"refs/heads/{req.SourceBranch}",
            targetRefName = $"refs/heads/{req.TargetBranch}",
            title = req.Title,
            description = req.Description
        }, JsonOpts);
        var json = await PostAsync(Api($"git/repositories/{req.RepositoryId}/pullrequests"), body);
        var pr = JsonDocument.Parse(json).RootElement;
        return new DevOpsPullRequest
        {
            PullRequestId = pr.GetProperty("pullRequestId").GetInt32(),
            Title = pr.GetProperty("title").GetString()!,
            Status = pr.GetProperty("status").GetString()!,
            WebUrl = pr.GetProperty("webUrl").GetString()!
        };
    }

    public async Task<DevOpsPullRequest?> UpdatePullRequestStatusAsync(string repoId, int prId, string status)
    {
        var body = JsonSerializer.Serialize(new
        {
            status,
            lastMergeSourceCommit = new { commitId = (string?)null }
        }, JsonOpts);
        var json = await PatchAsync(Api($"git/repositories/{repoId}/pullrequests/{prId}"), body);
        var pr = JsonDocument.Parse(json).RootElement;
        return new DevOpsPullRequest
        {
            PullRequestId = pr.GetProperty("pullRequestId").GetInt32(),
            Title = pr.GetProperty("title").GetString()!,
            Status = pr.GetProperty("status").GetString()!
        };
    }

    public async Task<List<PullRequestComment>> GetPullRequestCommentsAsync(string repoId, int prId)
    {
        var json = await GetAsync(Api($"git/repositories/{repoId}/pullrequests/{prId}/threads"));
        var root = JsonDocument.Parse(json);
        return root.RootElement.GetProperty("value").EnumerateArray()
            .SelectMany(t => t.GetProperty("comments").EnumerateArray())
            .Select(c => new PullRequestComment
            {
                Id = c.GetProperty("id").GetInt32(),
                Content = c.GetProperty("content").GetString()!,
                Author = c.GetProperty("author").GetProperty("displayName").GetString()!,
                PostedDate = c.GetProperty("postedDate").GetDateTime()
            }).ToList();
    }

    // ────────────────────────────── Builds & Artifacts ──────────────────────────────

    public async Task<List<DevOpsBuild>> GetBuildsAsync(int? pipelineId = null, int top = 10)
    {
        var url = Api("build/builds") + $"&$top={top}";
        if (pipelineId.HasValue) url += $"&definitions={pipelineId}";
        var json = await GetAsync(url);
        var root = JsonDocument.Parse(json);
        return root.RootElement.GetProperty("value").EnumerateArray().Select(b => new DevOpsBuild
        {
            Id = b.GetProperty("id").GetInt32(),
            BuildNumber = b.GetProperty("buildNumber").GetString()!,
            Status = b.GetProperty("status").GetString()!,
            Result = b.TryGetProperty("result", out var r) ? r.GetString() ?? "" : "",
            QueueTime = b.GetProperty("queueTime").GetDateTime(),
            WebUrl = b.GetProperty("_links").GetProperty("web").GetProperty("href").GetString()!
        }).ToList();
    }

    public async Task<List<DevOpsArtifact>> GetArtifactsAsync(int buildId)
    {
        var json = await GetAsync(Api($"build/builds/{buildId}/artifacts"));
        var root = JsonDocument.Parse(json);
        if (!root.RootElement.TryGetProperty("value", out var value))
            return new();
        return value.EnumerateArray().Select(a => new DevOpsArtifact
        {
            Id = a.TryGetProperty("id", out var id) ? id.GetInt32() : 0,
            Name = a.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
            Type = a.TryGetProperty("resource", out var r) && r.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "",
            BuildId = buildId,
            DownloadUrl = a.TryGetProperty("resource", out var r2) && r2.TryGetProperty("downloadUrl", out var d) ? d.GetString() ?? "" : ""
        }).ToList();
    }

    public async Task<Stream> DownloadArtifactAsync(string downloadUrl)
    {
        var resp = await _http.GetAsync(downloadUrl);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsStreamAsync();
    }

    public async Task<DevOpsDashboard> GetDashboardAsync()
    {
        var dashboard = new DevOpsDashboard { Connection = _connection! };
        try
        {
            var tasks = new Task[]
            {
                Task.Run(async () => dashboard.Repositories = await GetRepositoriesAsync()),
                Task.Run(async () => dashboard.Pipelines = await GetPipelinesAsync()),
                Task.Run(async () => dashboard.RecentBuilds = await GetBuildsAsync()),
                Task.Run(async () =>
                {
                    try
                    {
                        var repos = await GetRepositoriesAsync();
                        if (repos.Count > 0)
                        {
                            var prTasks = repos.Take(3).Select(r => GetPullRequestsAsync(r.Id));
                            var prs = await Task.WhenAll(prTasks);
                            dashboard.PullRequests = prs.SelectMany(p => p).ToList();
                        }
                    }
                    catch { /* skip PRs for dashboard */ }
                })
            };
            await Task.WhenAll(tasks);
        }
        catch (Exception ex)
        {
            dashboard.ErrorMessage = ex.Message;
        }
        return dashboard;
    }

    // ────────────────────────────── File Browser ──────────────────────────────

    public async Task<List<RepoFile>> GetRepoFilesAsync(string repoId, string? path = "", string? branch = "main")
    {
        var url = Api($"git/repositories/{repoId}/items?scopePath={Uri.EscapeDataString(path ?? "")}&recursionLevel=OneLevel&includeContentMetadata=true") +
                  $"&versionDescriptor.version={Uri.EscapeDataString(branch ?? "main")}&versionDescriptor.versionType=branch";
        var json = await GetAsync(url);
        var root = JsonDocument.Parse(json);
        return root.RootElement.EnumerateArray().Select(f => new RepoFile
        {
            Name = f.GetProperty("path").GetString()!.Split('/').Last(),
            Path = f.GetProperty("path").GetString()!,
            IsFolder = f.GetProperty("isFolder").GetBoolean(),
            Size = f.TryGetProperty("size", out var s) ? s.GetInt64() : 0,
            ContentType = f.TryGetProperty("contentType", out var ct) ? ct.GetString() : null
        }).OrderByDescending(f => f.IsFolder).ThenBy(f => f.Name).ToList();
    }

    public async Task<FileContent> GetFileContentAsync(string repoId, string path, string? branch = "main")
    {
        var url = Api($"git/repositories/{repoId}/items?path={Uri.EscapeDataString(path)}") +
                  $"&versionDescriptor.version={Uri.EscapeDataString(branch ?? "main")}&versionDescriptor.versionType=branch&includeContent=true";
        var json = await GetAsync(url);
        var f = JsonDocument.Parse(json).RootElement;
        return new FileContent
        {
            Name = f.GetProperty("path").GetString()!.Split('/').Last(),
            Path = f.GetProperty("path").GetString()!,
            Content = f.GetProperty("content").GetString()!,
            Encoding = f.TryGetProperty("encoding", out var enc) ? enc.GetString() ?? "base64" : "base64",
            RepositoryId = repoId,
            Branch = branch ?? "main"
        };
    }

    public async Task CommitFileToBranchAsync(string repoId, string branch, string filePath, string content, string comment)
    {
        var refUrl = Api($"git/repositories/{repoId}/refs");
        var refJson = await PostAsync(refUrl, JsonSerializer.Serialize(new
        {
            refUpdates = new[] { new { name = $"refs/heads/{branch}", oldObjectId = "0000000000000000000000000000000000000000" } },
            commits = new[]
            {
                new
                {
                    comment,
                    changes = new[]
                    {
                        new
                        {
                            changeType = "add",
                            item = new { path = filePath },
                            newContent = new { content, contentType = "rawtext" }
                        }
                    }
                }
            }
        }, JsonOpts));
    }

    // ────────────────────────────── Commits ──────────────────────────────

    public async Task<List<CommitInfo>> GetCommitHistoryAsync(string repoId, int top = 20)
    {
        var json = await GetAsync(Api($"git/repositories/{repoId}/commits?$top={top}"));
        var root = JsonDocument.Parse(json);
        return root.RootElement.GetProperty("value").EnumerateArray().Select(c => new CommitInfo
        {
            CommitId = c.GetProperty("commitId").GetString()!,
            Comment = c.GetProperty("comment").GetString()!,
            AuthorName = c.GetProperty("author").GetProperty("name").GetString()!,
            AuthorEmail = c.GetProperty("author").GetProperty("email").GetString()!,
            CommitDate = c.GetProperty("committer").GetProperty("date").GetDateTime(),
            RemoteUrl = c.GetProperty("remoteUrl").GetString()!
        }).ToList();
    }

    // ────────────────────────────── Branches ──────────────────────────────

    public async Task<List<BranchInfo>> GetBranchesAsync(string repoId)
    {
        var json = await GetAsync(Api($"git/repositories/{repoId}/refs?filter=heads"));
        var root = JsonDocument.Parse(json);
        return root.RootElement.GetProperty("value").EnumerateArray().Select(b => new BranchInfo
        {
            Name = b.GetProperty("name").GetString()!.Replace("refs/heads/", ""),
            Ref = b.GetProperty("name").GetString()!,
            ObjectId = b.GetProperty("objectId").GetString()!,
            Creator = b.TryGetProperty("creator", out var cr) ? cr.GetProperty("displayName").GetString() : null
        }).ToList();
    }

    // ────────────────────────────── Cancel Pipeline Run ──────────────────────────────

    public async Task CancelPipelineRunAsync(int pipelineId, int runId)
    {
        var body = JsonSerializer.Serialize(new { state = "cancelling" }, JsonOpts);
        await PatchAsync(Api($"pipelines/{pipelineId}/runs/{runId}"), body);
    }

    public async Task CancelBuildAsync(int buildId)
    {
        var body = JsonSerializer.Serialize(new { status = "cancelling" }, JsonOpts);
        await PatchAsync(Api($"build/builds/{buildId}"), body);
    }

    // ────────────────────────────── Build Timeline & Logs ──────────────────────────────

    public async Task<List<BuildTimelineRecord>> GetBuildTimelineAsync(int buildId)
    {
        var json = await GetAsync(Api($"build/builds/{buildId}/timeline"));
        var root = JsonDocument.Parse(json);
        var all = root.RootElement.GetProperty("records").EnumerateArray()
            .Select(r => new BuildTimelineRecord
            {
                Id = r.GetProperty("id").GetString()!,
                Name = r.GetProperty("name").GetString()!,
                Type = r.GetProperty("type").GetString()!,
                State = r.GetProperty("state").GetString()!,
                Result = r.TryGetProperty("result", out var res) ? res.GetString() : null,
                LogId = r.TryGetProperty("log", out var log) ? log.GetProperty("id").GetInt32() : null,
                StartTime = r.TryGetProperty("startTime", out var st) ? st.GetDateTime() : null,
                FinishTime = r.TryGetProperty("finishTime", out var ft) ? ft.GetDateTime() : null,
                PercentComplete = r.TryGetProperty("percentComplete", out var pc) ? pc.GetDouble() : null
            }).ToList();

        // Build parent-child hierarchy
        var lookup = all.ToDictionary(r => r.Id);
        var roots = new List<BuildTimelineRecord>();
        foreach (var r in all)
        {
            // Records without parents (or Job/Phase type) are roots
            if (r.Type is "Job" or "Phase" or "Task")
            {
                if (r.Type == "Task") roots.Add(r);
                else roots.Add(r);
            }
        }
        // Return only top-level records (jobs/phases) — nested details not needed for simple view
        return all.Where(r => r.Type is "Job" or "Phase").ToList();
    }

    public async Task<List<BuildLogEntry>> GetBuildLogAsync(int buildId, int logId)
    {
        var url = Api($"build/builds/{buildId}/logs/{logId}");
        var content = await GetAsync(url);
        var lines = content.Split('\n', StringSplitOptions.None);
        return lines.Select((line, i) => new BuildLogEntry { LineNumber = i + 1, Text = line.TrimEnd('\r') }).ToList();
    }

    // ────────────────────────────── HTTP helpers ──────────────────────────────

    private async Task<string> GetAsync(string url)
    {
        var resp = await _http.GetAsync(url);
        var content = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode)
            throw new HttpRequestException($"{(int)resp.StatusCode} {resp.ReasonPhrase} — {Truncate(content, 200)}");
        return content;
    }

    private async Task<string> PostAsync(string url, string body)
    {
        var resp = await _http.PostAsync(url, new StringContent(body, Encoding.UTF8, "application/json"));
        var content = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode)
            throw new HttpRequestException($"{(int)resp.StatusCode} {resp.ReasonPhrase} — {Truncate(content, 200)}");
        return content;
    }

    private async Task<string> PatchAsync(string url, string body)
    {
        var req = new HttpRequestMessage(HttpMethod.Patch, url)
        { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        var resp = await _http.SendAsync(req);
        var content = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode)
            throw new HttpRequestException($"{(int)resp.StatusCode} {resp.ReasonPhrase} — {Truncate(content, 200)}");
        return content;
    }

    private async Task DeleteAsync(string url)
    {
        var resp = await _http.DeleteAsync(url);
        if (!resp.IsSuccessStatusCode)
        {
            var content = await resp.Content.ReadAsStringAsync();
            throw new HttpRequestException($"{(int)resp.StatusCode} {resp.ReasonPhrase} — {Truncate(content, 200)}");
        }
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "...";
}
