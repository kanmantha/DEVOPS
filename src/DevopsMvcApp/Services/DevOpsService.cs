using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DevopsMvcApp.Models.DevOps;

namespace DevopsMvcApp.Services;

/// <summary>
/// Service class that wraps the Azure DevOps REST API.
/// All communication uses basic auth with a PAT stored in the user's session.
/// Every method in this class calls one of the private HTTP helpers (Get/Post/Patch/Delete)
/// which handle auth headers, error checking, and response parsing.
/// </summary>
public class DevOpsService
{
    private readonly HttpClient _http;
    private DevOpsConnection? _connection;

    // JSON serialization: camelCase naming + skip null properties
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public DevOpsService(HttpClient http)
    {
        _http = http;
    }

    // ── Connection management ──

    /// <summary>Sets the PAT-based Basic auth header on every request.</summary>
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

    // ── URL builders ──
    /// <summary>Builds a project-scoped Azure DevOps REST API URL with api-version=7.1.
    /// Handles paths that already contain query parameters (uses &amp; vs ? accordingly).</summary>
    private string Api(string path)
    {
        var sep = path.Contains('?') ? '&' : '?';
        return $"https://dev.azure.com/{_connection!.Organization}/{_connection.Project}/_apis/{path}{sep}api-version=7.1";
    }
    /// <summary>Builds an org-level Azure DevOps REST API URL (no project segment).</summary>
    private string OrgApi(string path) =>
        $"https://dev.azure.com/{_connection!.Organization}/_apis/{path}?api-version=7.1";
    /// <summary>Builds a Release Management (vsrm) REST API URL with api-version=7.1.</summary>
    private string VsmApi(string path) =>
        $"https://vsrm.dev.azure.com/{_connection!.Organization}/{_connection.Project}/_apis/release/{path}?api-version=7.1";

    // ══════════════════════════════════════════
    //  RELEASE MANAGEMENT (Blue-Green)
    // ══════════════════════════════════════════

    /// <summary>Fetches all release definitions from the release management API.</summary>
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

    /// <summary>Fetches recent releases with environment status expansion.</summary>
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

    /// <summary>Creates a new release from an existing release definition.</summary>
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

    // ── Simulated Blue-Green deployment environments ──
    // These are in-memory demo data — no live Azure App Service calls.
    // SwapSlotsAsync / DeployToSlotAsync mutate the in-memory collection.

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

    /// <summary>Swaps Blue ↔ Green slots by exchanging versions and toggling active flag.</summary>
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

    /// <summary>Simulates deploying a version to a specific slot.</summary>
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

    // ══════════════════════════════════════════
    //  REPOSITORIES
    // ══════════════════════════════════════════

    /// <summary>Lists all Git repositories in the connected project.</summary>
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

    /// <summary>Creates a new empty Git repo, then sets its default branch to "main".</summary>
    public async Task<DevOpsRepository?> CreateRepositoryAsync(string name)
    {
        var body = JsonSerializer.Serialize(new { name }, JsonOpts);
        var json = await PostAsync(Api("git/repositories"), body);
        var r = JsonDocument.Parse(json).RootElement;
        var repoId = r.GetProperty("id").GetString()!;
        // Explicitly set default branch — Azure DevOps may default to "master"
        await PatchAsync(Api($"git/repositories/{repoId}"),
            JsonSerializer.Serialize(new { defaultBranch = "refs/heads/main" }, JsonOpts));
        return new DevOpsRepository
        {
            Id = repoId,
            Name = r.GetProperty("name").GetString()!,
            RemoteUrl = r.GetProperty("remoteUrl").GetString()!,
            DefaultBranch = "main",
            WebUrl = r.GetProperty("webUrl").GetString()!
        };
    }

    /// <summary>Permanently deletes a repository by ID.</summary>
    public async Task<bool> DeleteRepositoryAsync(string repoId)
    {
        await DeleteAsync(Api($"git/repositories/{repoId}"));
        return true;
    }

    /// <summary>Renames an existing repository.</summary>
    public async Task<DevOpsRepository?> RenameRepositoryAsync(string repoId, string newName)
    {
        var body = JsonSerializer.Serialize(new { name = newName }, JsonOpts);
        var json = await PatchAsync(Api($"git/repositories/{repoId}"), body);
        var r = JsonDocument.Parse(json).RootElement;
        return new DevOpsRepository
        {
            Id = r.GetProperty("id").GetString()!,
            Name = r.GetProperty("name").GetString()!,
            RemoteUrl = r.GetProperty("remoteUrl").GetString()!,
            DefaultBranch = r.TryGetProperty("defaultBranch", out var b) ? b.GetString() ?? "main" : "main",
            WebUrl = r.GetProperty("webUrl").GetString()!
        };
    }

    /// <summary>Updates the default branch of a repository.</summary>
    public async Task SetRepoDefaultBranchAsync(string repoId, string branch)
    {
        var body = JsonSerializer.Serialize(new { defaultBranch = $"refs/heads/{branch}" }, JsonOpts);
        await PatchAsync(Api($"git/repositories/{repoId}"), body);
    }

    // ══════════════════════════════════════════
    //  FILE OPERATIONS (commit via push API)
    // ══════════════════════════════════════════

    /// <summary>Pushes via the Azure DevOps Push API, retrying on 409 with a fresh oldObjectId.</summary>
    private async Task PushWithRetryAsync(string repoId, string branch,
        Func<string, object> buildBody)
    {
        var url = Api($"git/repositories/{repoId}/pushes");
        for (int attempt = 0; attempt < 3; attempt++)
        {
            var oldObjectId = await GetBranchTipAsync(repoId, branch);
            var body = JsonSerializer.Serialize(buildBody(oldObjectId), JsonOpts);
            var resp = await _http.PostAsync(url, new StringContent(body, Encoding.UTF8, "application/json"));
            var content = await resp.Content.ReadAsStringAsync();
            if (resp.IsSuccessStatusCode) return;
            if ((int)resp.StatusCode != 409) // non-conflict errors are fatal
                throw new HttpRequestException($"{(int)resp.StatusCode} {resp.ReasonPhrase} — {Truncate(content, 200)}");
        }
        throw new HttpRequestException($"409 Conflict — retries exhausted for push on {repoId}/{branch}");
    }

    /// <summary>Commits a new file to a branch via the Azure DevOps Push API.</summary>
    public async Task CommitFileAsync(string repoId, string branch, string path, string content, string comment)
    {
        var normalizedPath = path.StartsWith("/") ? path : $"/{path}";
        await PushWithRetryAsync(repoId, branch, oid => new
        {
            refUpdates = new[] { new { name = $"refs/heads/{branch}", oldObjectId = oid } },
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
                            item = new { path = normalizedPath },
                            newContent = new { content, contentType = "rawtext" }
                        }
                    }
                }
            }
        });
    }

    /// <summary>Edits existing file content via a push with changeType "edit".</summary>
    public async Task EditFileAsync(string repoId, string branch, string path, string content, string comment)
    {
        var normalizedPath = path.StartsWith("/") ? path : $"/{path}";
        await PushWithRetryAsync(repoId, branch, oid => new
        {
            refUpdates = new[] { new { name = $"refs/heads/{branch}", oldObjectId = oid } },
            commits = new[]
            {
                new
                {
                    comment,
                    changes = new[]
                    {
                        new
                        {
                            changeType = "edit",
                            item = new { path = normalizedPath },
                            newContent = new { content, contentType = "rawtext" }
                        }
                    }
                }
            }
        });
    }

    /// <summary>Deletes a file via a push with changeType "delete".</summary>
    public async Task DeleteFileAsync(string repoId, string branch, string path, string comment)
    {
        var normalizedPath = path.StartsWith("/") ? path : $"/{path}";
        await PushWithRetryAsync(repoId, branch, oid => new
        {
            refUpdates = new[] { new { name = $"refs/heads/{branch}", oldObjectId = oid } },
            commits = new[]
            {
                new
                {
                    comment,
                    changes = new[]
                    {
                        new
                        {
                            changeType = "delete",
                            item = new { path = normalizedPath }
                        }
                    }
                }
            }
        });
    }

    /// <summary>Delegates to CommitFileAsync (backward compatibility).</summary>
    public async Task CommitFileToBranchAsync(string repoId, string branch, string filePath, string content, string comment)
    {
        await CommitFileAsync(repoId, branch, filePath, content, comment);
    }

    /// <summary>Gets the commit ID at the tip of a branch (or all-zeros if new).</summary>
    private async Task<string> GetBranchTipAsync(string repoId, string branch)
    {
        try
        {
            var refsJson = await GetAsync(Api($"git/repositories/{repoId}/refs?filter=heads/{branch}"));
            var refsDoc = JsonDocument.Parse(refsJson);
            if (refsDoc.RootElement.TryGetProperty("value", out var refs) && refs.GetArrayLength() > 0)
                return refs[0].GetProperty("objectId").GetString()!;
        }
        catch { }
        return "0000000000000000000000000000000000000000";
    }

    // ══════════════════════════════════════════
    //  BRANCHES
    // ══════════════════════════════════════════

    /// <summary>Creates a new branch from a source branch or commit.</summary>
    public async Task CreateBranchAsync(string repoId, string branchName, string? sourceBranchOrCommit = null)
    {
        var newRef = $"refs/heads/{branchName}";
        if (!string.IsNullOrEmpty(sourceBranchOrCommit))
        {
            string sourceObjectId;
            try
            {
                var refsJson = await GetAsync(Api($"git/repositories/{repoId}/refs?filter=heads/{sourceBranchOrCommit}"));
                var refsDoc = JsonDocument.Parse(refsJson);
                if (refsDoc.RootElement.TryGetProperty("value", out var refs) && refs.GetArrayLength() > 0)
                    sourceObjectId = refs[0].GetProperty("objectId").GetString()!;
                else
                    sourceObjectId = sourceBranchOrCommit;
            }
            catch
            {
                sourceObjectId = sourceBranchOrCommit;
            }
            var body = JsonSerializer.Serialize(new[]
            {
                new { name = newRef, oldObjectId = "0000000000000000000000000000000000000000", newObjectId = sourceObjectId }
            }, JsonOpts);
            await PostAsync(Api($"git/repositories/{repoId}/refs"), body);
        }
        else
        {
            var defaultTip = await GetBranchTipAsync(repoId, "main");
            var body = JsonSerializer.Serialize(new[]
            {
                new { name = newRef, oldObjectId = "0000000000000000000000000000000000000000", newObjectId = defaultTip }
            }, JsonOpts);
            await PostAsync(Api($"git/repositories/{repoId}/refs"), body);
        }
    }

    /// <summary>Deletes a branch by setting its ref to all-zeros.</summary>
    public async Task DeleteBranchAsync(string repoId, string branchName)
    {
        var oldObjectId = await GetBranchTipAsync(repoId, branchName);
        var body = JsonSerializer.Serialize(new[]
        {
            new { name = $"refs/heads/{branchName}", oldObjectId, newObjectId = "0000000000000000000000000000000000000000" }
        }, JsonOpts);
        await PostAsync(Api($"git/repositories/{repoId}/refs"), body);
    }

    // ══════════════════════════════════════════
    //  PIPELINES (YAML-based)
    // ══════════════════════════════════════════

    /// <summary>Lists all YAML pipelines in the project.</summary>
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

    /// <summary>Creates a pipeline definition from a CreatePipelineRequest (which includes
    /// the repo, YAML path, and default branch).</summary>
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

    /// <summary>Fetches run history for a specific pipeline.</summary>
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

    /// <summary>Queues a new pipeline run.</summary>
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

    /// <summary>Permanently deletes a pipeline definition via the Build Definitions API
    /// (the YAML Pipelines API doesn't support DELETE).</summary>
    public async Task DeletePipelineAsync(int pipelineId, string pipelineName)
    {
        // Fetch all build definitions and find the one matching the pipeline name
        var json = await GetAsync(Api($"build/definitions"));
        var root = JsonDocument.Parse(json);
        var defs = root.RootElement.GetProperty("value").EnumerateArray()
            .FirstOrDefault(d => d.GetProperty("name").GetString() == pipelineName);
        if (defs.ValueKind == JsonValueKind.Undefined)
            throw new HttpRequestException($"No build definition found matching pipeline '{pipelineName}'.");
        var defId = defs.GetProperty("id").GetInt32();
        await DeleteAsync(Api($"build/definitions/{defId}"));
    }

    // ══════════════════════════════════════════
    //  PULL REQUESTS
    // ══════════════════════════════════════════

    /// <summary>Lists PRs for a repo, optionally filtered by status (active/completed/abandoned).</summary>
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

    /// <summary>Creates a new pull request.</summary>
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

    /// <summary>Updates PR status (completed / abandoned).</summary>
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

    /// <summary>Fetches all comment threads for a PR.</summary>
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

    /// <summary>Abandons a PR (delegates to UpdatePullRequestStatusAsync).</summary>
    public async Task AbandonPullRequestAsync(string repoId, int prId)
    {
        await UpdatePullRequestStatusAsync(repoId, prId, "abandoned");
    }

    /// <summary>Posts a new comment thread on a PR.</summary>
    public async Task AddPullRequestCommentAsync(string repoId, int prId, string content)
    {
        var body = JsonSerializer.Serialize(new
        {
            comments = new[] { new { content, commentType = "text" } },
            status = "active"
        }, JsonOpts);
        await PostAsync(Api($"git/repositories/{repoId}/pullrequests/{prId}/threads"), body);
    }

    // ══════════════════════════════════════════
    //  BUILDS & ARTIFACTS
    // ══════════════════════════════════════════

    /// <summary>Lists recent builds, optionally filtered by pipeline.</summary>
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

    /// <summary>Lists artifacts produced by a build.</summary>
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

    /// <summary>Proxies an artifact download through the server (keeps PAT secure).</summary>
    public async Task<Stream> DownloadArtifactAsync(string downloadUrl)
    {
        var resp = await _http.GetAsync(downloadUrl);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsStreamAsync();
    }

    // ══════════════════════════════════════════
    //  DASHBOARD (aggregated stats)
    // ══════════════════════════════════════════

    /// <summary>Fetches all data needed for the DevOps dashboard in parallel.
    /// Returns repository counts, pipeline stats, build stats, PR breakdowns,
    /// branch/commit counts, and a unified recent activity feed.</summary>
    public async Task<DevOpsDashboard> GetDashboardAsync()
    {
        var dashboard = new DevOpsDashboard { Connection = _connection! };
        try
        {
            var reposTask = GetRepositoriesAsync();
            var pipelinesTask = GetPipelinesAsync();
            var buildsTask = GetBuildsAsync(null, 50);
            var releasesTask = GetReleasesAsync(10);
            var releaseDefsTask = GetReleaseDefinitionsAsync();

            await Task.WhenAll(reposTask, pipelinesTask, buildsTask, releasesTask, releaseDefsTask);

            var repos = reposTask.Result;
            var pipelines = pipelinesTask.Result;
            var builds = buildsTask.Result;
            var releases = releasesTask.Result;
            var releaseDefs = releaseDefsTask.Result;

            dashboard.Repositories = repos;
            dashboard.Pipelines = pipelines;
            dashboard.RecentBuilds = builds;

            // PRs across repos (fetch all statuses from first 3 repos)
            var allPrs = new List<DevOpsPullRequest>();
            if (repos.Count > 0)
            {
                var prTasks = repos.Take(3).Select(r => GetPullRequestsAsync(r.Id, null));
                await Task.WhenAll(prTasks);
                allPrs = prTasks.SelectMany(t => t.Result).ToList();
                dashboard.PullRequests = allPrs.Where(p => p.Status == "active").ToList();
            }

            // Branch & commit counts + recent activity entries
            int totalBranches = 0, totalCommits = 0;
            var recentActivity = new List<DashboardActivity>();
            foreach (var repo in repos.Take(3))
            {
                try
                {
                    var branches = await GetBranchesAsync(repo.Id);
                    totalBranches += branches.Count;
                    var commits = await GetCommitHistoryAsync(repo.Id, 5);
                    totalCommits += commits.Count;
                    foreach (var c in commits.Take(3))
                        recentActivity.Add(new DashboardActivity
                        {
                            Type = "commit",
                            Title = c.Comment,
                            Subtitle = $"{repo.Name} · {c.AuthorName}",
                            Url = c.RemoteUrl,
                            Timestamp = c.CommitDate,
                            Icon = "🔄",
                            Color = "#2563eb"
                        });
                }
                catch { }
            }

            // Build stats
            int succeeded = 0, failed = 0, inProgress = 0;
            foreach (var b in builds)
            {
                if (b.Result == "succeeded") succeeded++;
                else if (b.Result == "failed") failed++;
                else inProgress++;
            }
            foreach (var b in builds.Take(5))
                recentActivity.Add(new DashboardActivity
                {
                    Type = "build",
                    Title = b.BuildNumber,
                    Subtitle = b.Result ?? "In Progress",
                    Url = b.WebUrl,
                    Timestamp = b.QueueTime,
                    Icon = b.Result == "succeeded" ? "✅" : b.Result == "failed" ? "❌" : "⏳",
                    Color = b.Result == "succeeded" ? "#059669" : b.Result == "failed" ? "#dc2626" : "#d97706"
                });

            // PR activity
            foreach (var pr in dashboard.PullRequests.Take(5))
                recentActivity.Add(new DashboardActivity
                {
                    Type = "pr",
                    Title = pr.Title,
                    Subtitle = $"#{pr.PullRequestId} by {pr.CreatedBy}",
                    Url = pr.WebUrl,
                    Timestamp = pr.CreationDate,
                    Icon = "🔄",
                    Color = "#7c3aed"
                });

            // Release activity
            foreach (var r in releases.Take(5))
                recentActivity.Add(new DashboardActivity
                {
                    Type = "release",
                    Title = r.Name,
                    Subtitle = r.Status,
                    Url = r.WebUrl,
                    Timestamp = r.CreatedOn,
                    Icon = "🏷️",
                    Color = "#0891b2"
                });

            // Active pipeline runs
            int activeRuns = 0;
            foreach (var p in pipelines.Take(5))
            {
                try
                {
                    var runs = await GetPipelineRunsAsync(p.Id);
                    activeRuns += runs.Count(r => r.State == "inProgress");
                }
                catch { }
            }

            // PR status breakdown
            int completedPrs = allPrs.Count(p => p.Status == "completed");
            int abandonedPrs = allPrs.Count(p => p.Status == "abandoned");
            int activePrs = allPrs.Count(p => p.Status == "active");

            dashboard.Stats = new DashboardStats
            {
                TotalRepos = repos.Count,
                TotalPipelines = pipelines.Count,
                ActivePrs = activePrs,
                CompletedPrs = completedPrs,
                AbandonedPrs = abandonedPrs,
                TotalBuilds = builds.Count,
                SucceededBuilds = succeeded,
                FailedBuilds = failed,
                InProgressBuilds = inProgress,
                TotalBranches = totalBranches,
                TotalCommits = totalCommits,
                TotalReleases = releases.Count,
                TotalReleaseDefs = releaseDefs.Count,
                ActivePipelineRuns = activeRuns
            };

            dashboard.RecentActivity = recentActivity
                .OrderByDescending(a => a.Timestamp).Take(15).ToList();
        }
        catch (Exception ex)
        {
            dashboard.ErrorMessage = ex.Message;
        }
        return dashboard;
    }

    // ══════════════════════════════════════════
    //  FILE BROWSER
    // ══════════════════════════════════════════

    /// <summary>Lists files/folders at a given path in a repository branch (one level deep).</summary>
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

    /// <summary>Gets the full content of a single file, including base64 or raw encoding.</summary>
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

    // ══════════════════════════════════════════
    //  COMMITS
    // ══════════════════════════════════════════

    /// <summary>Fetches recent commit history for a repository.</summary>
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

    // ══════════════════════════════════════════
    //  BRANCHES (list)
    // ══════════════════════════════════════════

    /// <summary>Lists all branches in a repository (refs filter=heads).</summary>
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

    // ══════════════════════════════════════════
    //  CANCEL OPERATIONS
    // ══════════════════════════════════════════

    /// <summary>Cancels a pipeline run by setting state to "cancelling".</summary>
    public async Task CancelPipelineRunAsync(int pipelineId, int runId)
    {
        var body = JsonSerializer.Serialize(new { state = "cancelling" }, JsonOpts);
        await PatchAsync(Api($"pipelines/{pipelineId}/runs/{runId}"), body);
    }

    /// <summary>Cancels a build by setting status to "cancelling".</summary>
    public async Task CancelBuildAsync(int buildId)
    {
        var body = JsonSerializer.Serialize(new { status = "cancelling" }, JsonOpts);
        await PatchAsync(Api($"build/builds/{buildId}"), body);
    }

    // ══════════════════════════════════════════
    //  BUILD TIMELINE & LOGS
    // ══════════════════════════════════════════

    /// <summary>Gets the timeline (step-by-step) for a completed build.</summary>
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

        return all.Where(r => r.Type is "Job" or "Phase").ToList();
    }

    /// <summary>Gets the raw log text for a specific build step.</summary>
    public async Task<List<BuildLogEntry>> GetBuildLogAsync(int buildId, int logId)
    {
        var url = Api($"build/builds/{buildId}/logs/{logId}");
        var content = await GetAsync(url);
        var lines = content.Split('\n', StringSplitOptions.None);
        return lines.Select((line, i) => new BuildLogEntry { LineNumber = i + 1, Text = line.TrimEnd('\r') }).ToList();
    }

    // ══════════════════════════════════════════
    //  RELEASE DEFINITION CRUD
    // ══════════════════════════════════════════

    /// <summary>Creates a minimal release definition with Dev+Production environments.</summary>
    public async Task<ReleaseDefinition?> CreateReleaseDefinitionAsync(string name, string? description = null)
    {
        var body = JsonSerializer.Serialize(new
        {
            name,
            description = description ?? $"Release definition for {name}",
            environments = new object[]
            {
                new
                {
                    name = "Dev",
                    rank = 1,
                    owner = new { displayName = "Portal User" },
                    conditions = new object[]
                    {
                        new { conditionType = "event", name = "ReleaseStarted", value = "" }
                    }
                },
                new
                {
                    name = "Production",
                    rank = 2,
                    owner = new { displayName = "Portal User" },
                    conditions = new object[]
                    {
                        new { conditionType = "event", name = "EnvironmentSucceeded", value = "" }
                    },
                    dependsOn = Array.Empty<object>()
                }
            },
            artifacts = new[]
            {
                new
                {
                    type = "Build",
                    alias = "_ProjectBuild",
                    definitionReference = new
                    {
                        project = new { id = _connection!.Project, name = _connection!.Project },
                        @default = new { isBuild = true, id = "", name = "" },
                        branch = new { id = "main", name = "main" },
                        pipelineTrigger = new { isPipelineTrigger = false }
                    }
                }
            },
            triggers = Array.Empty<object>(),
            variableGroups = Array.Empty<object>(),
            variables = new { }
        }, JsonOpts);
        var json = await PostAsync(VsmApi("definitions"), body);
        var d = JsonDocument.Parse(json).RootElement;
        return new ReleaseDefinition
        {
            Id = d.GetProperty("id").GetInt32(),
            Name = d.GetProperty("name").GetString()!,
            Description = d.TryGetProperty("description", out var desc) ? desc.GetString() : null,
            Revision = d.GetProperty("revision").GetInt32(),
            WebUrl = d.TryGetProperty("_links", out var links) && links.TryGetProperty("web", out var web)
                ? web.GetProperty("href").GetString()! : ""
        };
    }

    /// <summary>Deletes a release definition by ID.</summary>
    public async Task DeleteReleaseDefinitionAsync(int definitionId)
    {
        await DeleteAsync(VsmApi($"definitions/{definitionId}"));
    }

    // ══════════════════════════════════════════
    //  HTTP HELPERS
    //  Every call includes Basic auth + JSON content-type.
    //  Non-2xx responses throw HttpRequestException with the response body.
    // ══════════════════════════════════════════

    /// <summary>Sends an HTTP GET and returns the response body as a string.</summary>
    private async Task<string> GetAsync(string url)
    {
        var resp = await _http.GetAsync(url);
        var content = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode)
            throw new HttpRequestException($"{(int)resp.StatusCode} {resp.ReasonPhrase} — {Truncate(content, 200)}");
        return content;
    }

    /// <summary>Sends an HTTP POST with a JSON body and returns the response body.</summary>
    private async Task<string> PostAsync(string url, string body)
    {
        var resp = await _http.PostAsync(url, new StringContent(body, Encoding.UTF8, "application/json"));
        var content = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode)
            throw new HttpRequestException($"{(int)resp.StatusCode} {resp.ReasonPhrase} — {Truncate(content, 200)}");
        return content;
    }

    /// <summary>Sends an HTTP PATCH with a JSON body and returns the response body.</summary>
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

    /// <summary>Sends an HTTP DELETE and throws on non-success status codes.</summary>
    private async Task DeleteAsync(string url)
    {
        var resp = await _http.DeleteAsync(url);
        if (!resp.IsSuccessStatusCode)
        {
            var content = await resp.Content.ReadAsStringAsync();
            throw new HttpRequestException($"{(int)resp.StatusCode} {resp.ReasonPhrase} — {Truncate(content, 200)}");
        }
    }

    /// <summary>Truncates a string to at most <c>max</c> characters, appending "..." if truncated.</summary>
    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "...";
}