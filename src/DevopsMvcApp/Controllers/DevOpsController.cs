using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using DevopsMvcApp.Models.DevOps;
using DevopsMvcApp.Services;

namespace DevopsMvcApp.Controllers;

[Authorize]
public class DevOpsController : Controller
{
    private readonly DevOpsService _devOps;

    public DevOpsController(DevOpsService devOps)
    {
        _devOps = devOps;
    }

    // ── Connect / Dashboard ──

    [HttpGet]
    public IActionResult Connect()
    {
        return View(new DevOpsConnection());
    }

    [HttpPost]
    public IActionResult Connect(DevOpsConnection connection)
    {
        if (!ModelState.IsValid) return View(connection);

        // Sanitize inputs — strip full URLs, trailing slashes, whitespace
        connection.Organization = SanitizeOrg(connection.Organization);
        connection.Project = connection.Project.Trim().TrimEnd('/');
        connection.Pat = connection.Pat.Trim();

        _devOps.SetConnection(connection);
        HttpContext.Session.SetString("DevOpsOrg", connection.Organization);
        HttpContext.Session.SetString("DevOpsProject", connection.Project);
        HttpContext.Session.SetString("DevOpsPat", connection.Pat);

        // Quick validation — test the connection
        try
        {
            var repos = _devOps.GetRepositoriesAsync().GetAwaiter().GetResult();
            TempData["Success"] = $"Connected to {connection.Organization}/{connection.Project}.";
            return RedirectToAction(nameof(Dashboard));
        }
        catch (Exception ex)
        {
            HttpContext.Session.Remove("DevOpsOrg");
            HttpContext.Session.Remove("DevOpsProject");
            HttpContext.Session.Remove("DevOpsPat");
            ModelState.AddModelError("", $"Connection failed: {ex.Message}");
            return View(connection);
        }
    }

    private static string SanitizeOrg(string org)
    {
        org = org.Trim().TrimEnd('/');
        // Remove full URL prefix if pasted
        foreach (var prefix in new[] { "https://dev.azure.com/", "http://dev.azure.com/", "dev.azure.com/" })
        {
            if (org.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                org = org[prefix.Length..].TrimEnd('/');
                break;
            }
        }
        return org;
    }

    public async Task<IActionResult> Dashboard()
    {
        if (!EnsureConnected()) return RedirectToAction(nameof(Connect));
        var dashboard = await _devOps.GetDashboardAsync();
        return View(dashboard);
    }

    public IActionResult Disconnect()
    {
        HttpContext.Session.Remove("DevOpsOrg");
        HttpContext.Session.Remove("DevOpsProject");
        HttpContext.Session.Remove("DevOpsPat");
        TempData["Success"] = "Disconnected from Azure DevOps.";
        return RedirectToAction(nameof(Connect));
    }

    // ── Repositories ──

    public async Task<IActionResult> Repositories()
    {
        if (!EnsureConnected()) return RedirectToAction(nameof(Connect));
        var repos = await _devOps.GetRepositoriesAsync();
        return View(repos);
    }

    [HttpGet]
    public IActionResult CreateRepository()
    {
        return View(new CreateRepositoryRequest());
    }

    [HttpPost]
    public async Task<IActionResult> CreateRepository(CreateRepositoryRequest req)
    {
        if (!EnsureConnected()) return RedirectToAction(nameof(Connect));
        if (!ModelState.IsValid) return View(req);
        await _devOps.CreateRepositoryAsync(req.Name);
        TempData["Success"] = $"Repository '{req.Name}' created.";
        return RedirectToAction(nameof(Repositories));
    }

    // ── Pipelines ──

    public async Task<IActionResult> Pipelines()
    {
        if (!EnsureConnected()) return RedirectToAction(nameof(Connect));
        var pipelines = await _devOps.GetPipelinesAsync();
        return View(pipelines);
    }

    [HttpGet]
    public async Task<IActionResult> CreatePipeline()
    {
        if (!EnsureConnected()) return RedirectToAction(nameof(Connect));
        var repos = await _devOps.GetRepositoriesAsync();
        ViewBag.Repositories = repos;
        return View(new CreatePipelineRequest());
    }

    [HttpPost]
    public async Task<IActionResult> CreatePipeline(CreatePipelineRequest req)
    {
        if (!EnsureConnected()) return RedirectToAction(nameof(Connect));
        if (!ModelState.IsValid) return View(req);

        // 1. Commit the YAML pipeline file to the repo
        var yaml = GetDotNetPipelineYaml();
        await _devOps.CommitFileAsync(req.RepositoryId, req.DefaultBranch, req.YamlPath, yaml,
            $"Add {req.YamlPath} pipeline definition");

        // 2. Create the pipeline definition
        var pipeline = await _devOps.CreatePipelineAsync(req);
        if (pipeline == null)
        {
            TempData["Error"] = "Failed to create pipeline.";
            return RedirectToAction(nameof(Pipelines));
        }

        // 3. Queue a run
        await _devOps.RunPipelineAsync(pipeline.Id);

        TempData["Success"] = $"Pipeline '{pipeline.Name}' created and queued.";
        return RedirectToAction(nameof(Pipelines));
    }

    public async Task<IActionResult> RunPipeline(int id)
    {
        if (!EnsureConnected()) return RedirectToAction(nameof(Connect));
        var run = await _devOps.RunPipelineAsync(id);
        if (run != null) TempData["Success"] = $"Pipeline run #{run.Id} queued.";
        return RedirectToAction(nameof(Pipelines));
    }

    public async Task<IActionResult> PipelineRuns(int id, string name)
    {
        if (!EnsureConnected()) return RedirectToAction(nameof(Connect));
        var runs = await _devOps.GetPipelineRunsAsync(id);
        ViewBag.PipelineName = name;
        ViewBag.PipelineId = id;
        return View(runs);
    }

    // ── Pull Requests ──

    public async Task<IActionResult> PullRequests(string? repoId)
    {
        if (!EnsureConnected()) return RedirectToAction(nameof(Connect));
        var repos = await _devOps.GetRepositoriesAsync();
        ViewBag.Repositories = repos;

        if (repoId == null && repos.Count > 0) repoId = repos[0].Id;
        var prs = repoId != null ? await _devOps.GetPullRequestsAsync(repoId) : new();
        ViewBag.SelectedRepoId = repoId;
        return View(prs);
    }

    [HttpGet]
    public async Task<IActionResult> CreatePullRequest()
    {
        if (!EnsureConnected()) return RedirectToAction(nameof(Connect));
        var repos = await _devOps.GetRepositoriesAsync();
        ViewBag.Repositories = repos;
        return View(new CreatePullRequestRequest());
    }

    [HttpPost]
    public async Task<IActionResult> CreatePullRequest(CreatePullRequestRequest req)
    {
        if (!EnsureConnected()) return RedirectToAction(nameof(Connect));
        if (!ModelState.IsValid) return View(req);
        var pr = await _devOps.CreatePullRequestAsync(req);
        if (pr != null)
            TempData["Success"] = $"PR #{pr.PullRequestId} created.";
        return RedirectToAction(nameof(PullRequests));
    }

    public async Task<IActionResult> ApprovePullRequest(string repoId, int prId)
    {
        if (!EnsureConnected()) return RedirectToAction(nameof(Connect));
        await _devOps.UpdatePullRequestStatusAsync(repoId, prId, "completed");
        TempData["Success"] = $"PR #{prId} completed.";
        return RedirectToAction(nameof(PullRequests));
    }

    // ── Builds & Artifacts ──

    public async Task<IActionResult> Builds()
    {
        if (!EnsureConnected()) return RedirectToAction(nameof(Connect));
        var builds = await _devOps.GetBuildsAsync();
        return View(builds);
    }

    public async Task<IActionResult> Artifacts(int buildId, string? buildNumber)
    {
        if (!EnsureConnected()) return RedirectToAction(nameof(Connect));
        try
        {
            var artifacts = await _devOps.GetArtifactsAsync(buildId);
            ViewBag.BuildNumber = buildNumber ?? buildId.ToString();
            ViewBag.BuildId = buildId;
            return View(artifacts);
        }
        catch (Exception ex)
        {
            TempData["Error"] = $"Failed to load artifacts: {ex.Message}";
            return RedirectToAction(nameof(Builds));
        }
    }

    public async Task<IActionResult> DownloadArtifact(string downloadUrl)
    {
        if (!EnsureConnected()) return RedirectToAction(nameof(Connect));
        try
        {
            var stream = await _devOps.DownloadArtifactAsync(downloadUrl);
            var fileName = Path.GetFileName(new Uri(downloadUrl).LocalPath) ?? "artifact.zip";
            return File(stream, "application/octet-stream", fileName);
        }
        catch (Exception ex)
        {
            TempData["Error"] = $"Download failed: {ex.Message}";
            return RedirectToAction(nameof(Builds));
        }
    }

    // ── File Browser ──

    public async Task<IActionResult> RepoFiles(string repoId, string? path, string? branch)
    {
        if (!EnsureConnected()) return RedirectToAction(nameof(Connect));
        var files = await _devOps.GetRepoFilesAsync(repoId, path ?? "", branch ?? "main");
        ViewBag.RepoId = repoId;
        ViewBag.CurrentPath = path ?? "";
        ViewBag.Branch = branch ?? "main";
        ViewBag.RepoName = (await _devOps.GetRepositoriesAsync()).FirstOrDefault(r => r.Id == repoId)?.Name ?? repoId;
        return View(files);
    }

    public async Task<IActionResult> RepoFileContent(string repoId, string path, string? branch)
    {
        if (!EnsureConnected()) return RedirectToAction(nameof(Connect));
        var file = await _devOps.GetFileContentAsync(repoId, path, branch ?? "main");
        return View(file);
    }

    [HttpGet]
    public async Task<IActionResult> UploadFile()
    {
        if (!EnsureConnected()) return RedirectToAction(nameof(Connect));
        ViewBag.Repositories = await _devOps.GetRepositoriesAsync();
        return View(new UploadFileRequest());
    }

    [HttpPost]
    public async Task<IActionResult> UploadFile(UploadFileRequest req)
    {
        if (!EnsureConnected()) return RedirectToAction(nameof(Connect));
        if (!ModelState.IsValid) return View(req);
        await _devOps.CommitFileToBranchAsync(req.RepositoryId, req.Branch, req.FilePath, req.Content, req.Comment);
        TempData["Success"] = $"File '{req.FilePath}' committed to {req.Branch}.";
        return RedirectToAction(nameof(RepoFiles), new { repoId = req.RepositoryId, branch = req.Branch });
    }

    // ── Commits ──

    public async Task<IActionResult> RepoCommits(string repoId)
    {
        if (!EnsureConnected()) return RedirectToAction(nameof(Connect));
        var commits = await _devOps.GetCommitHistoryAsync(repoId);
        ViewBag.RepoId = repoId;
        ViewBag.RepoName = (await _devOps.GetRepositoriesAsync()).FirstOrDefault(r => r.Id == repoId)?.Name ?? repoId;
        return View(commits);
    }

    // ── Branches ──

    public async Task<IActionResult> RepoBranches(string repoId)
    {
        if (!EnsureConnected()) return RedirectToAction(nameof(Connect));
        var branches = await _devOps.GetBranchesAsync(repoId);
        ViewBag.RepoId = repoId;
        ViewBag.RepoName = (await _devOps.GetRepositoriesAsync()).FirstOrDefault(r => r.Id == repoId)?.Name ?? repoId;
        return View(branches);
    }

    // ── Delete Repository ──

    [HttpPost]
    public async Task<IActionResult> DeleteRepository(string repoId, string repoName)
    {
        if (!EnsureConnected()) return RedirectToAction(nameof(Connect));
        await _devOps.DeleteRepositoryAsync(repoId);
        TempData["Success"] = $"Repository '{repoName}' deleted.";
        return RedirectToAction(nameof(Repositories));
    }

    // ── Cancel Pipeline Run ──

    public async Task<IActionResult> CancelRun(int pipelineId, int runId)
    {
        if (!EnsureConnected()) return RedirectToAction(nameof(Connect));
        await _devOps.CancelPipelineRunAsync(pipelineId, runId);
        TempData["Success"] = $"Run #{runId} cancelled.";
        return RedirectToAction(nameof(PipelineRuns), new { id = pipelineId });
    }

    public async Task<IActionResult> CancelBuild(int buildId)
    {
        if (!EnsureConnected()) return RedirectToAction(nameof(Connect));
        await _devOps.CancelBuildAsync(buildId);
        TempData["Success"] = $"Build #{buildId} cancelled.";
        return RedirectToAction(nameof(Builds));
    }

    // ── Build Timeline & Logs ──

    public async Task<IActionResult> BuildTimeline(int buildId, string? buildNumber)
    {
        if (!EnsureConnected()) return RedirectToAction(nameof(Connect));
        var timeline = await _devOps.GetBuildTimelineAsync(buildId);
        ViewBag.BuildId = buildId;
        ViewBag.BuildNumber = buildNumber ?? $"#{buildId}";
        return View(timeline);
    }

    public async Task<IActionResult> BuildLog(int buildId, int logId, string? stepName)
    {
        if (!EnsureConnected()) return RedirectToAction(nameof(Connect));
        var log = await _devOps.GetBuildLogAsync(buildId, logId);
        ViewBag.StepName = stepName ?? $"Log #{logId}";
        ViewBag.BuildId = buildId;
        return View(log);
    }

    // ── PR Detail with Comments ──

    public async Task<IActionResult> PullRequestDetail(string repoId, int prId)
    {
        if (!EnsureConnected()) return RedirectToAction(nameof(Connect));
        var prs = await _devOps.GetPullRequestsAsync(repoId, null);
        var pr = prs.FirstOrDefault(p => p.PullRequestId == prId);
        if (pr == null) return NotFound();
        var comments = await _devOps.GetPullRequestCommentsAsync(repoId, prId);
        ViewBag.Comments = comments;
        ViewBag.RepoId = repoId;
        return View(pr);
    }

    // ── Auto PR — creates a PR from a feature branch ──

    [HttpPost]
    public async Task<IActionResult> AutoCreatePr(string repoId, string featureBranch, string title, string description)
    {
        if (!EnsureConnected()) return RedirectToAction(nameof(Connect));
        var req = new CreatePullRequestRequest
        {
            RepositoryId = repoId,
            SourceBranch = featureBranch,
            TargetBranch = "main",
            Title = title,
            Description = description
        };
        var pr = await _devOps.CreatePullRequestAsync(req);
        if (pr != null)
            TempData["Success"] = $"Auto PR #{pr.PullRequestId} created from '{featureBranch}'.";
        else
            TempData["Error"] = "Failed to create auto PR.";
        return RedirectToAction(nameof(PullRequests));
    }

    // ── Blue-Green Deployments ──

    public async Task<IActionResult> Deployments()
    {
        if (!EnsureConnected()) return RedirectToAction(nameof(Connect));
        var envs = await _devOps.GetDeploymentEnvironmentsAsync();
        var releases = await _devOps.GetReleasesAsync(5);
        ViewBag.RecentReleases = releases;
        return View(envs);
    }

    public async Task<IActionResult> Environments()
    {
        if (!EnsureConnected()) return RedirectToAction(nameof(Connect));
        var envs = await _devOps.GetDeploymentEnvironmentsAsync();
        return View(envs);
    }

    public async Task<IActionResult> Releases()
    {
        if (!EnsureConnected()) return RedirectToAction(nameof(Connect));
        var releases = await _devOps.GetReleasesAsync(20);
        var defs = await _devOps.GetReleaseDefinitionsAsync();
        ViewBag.Definitions = defs;
        return View(releases);
    }

    [HttpPost]
    public async Task<IActionResult> CreateRelease(int definitionId, string? description)
    {
        if (!EnsureConnected()) return RedirectToAction(nameof(Connect));
        var release = await _devOps.CreateReleaseAsync(definitionId, description);
        if (release != null)
            TempData["Success"] = $"Release '{release.Name}' created.";
        else
            TempData["Error"] = "Failed to create release.";
        return RedirectToAction(nameof(Releases));
    }

    [HttpPost]
    public async Task<IActionResult> SwapSlots(string envName)
    {
        if (!EnsureConnected()) return RedirectToAction(nameof(Connect));
        try
        {
            var env = await _devOps.SwapSlotsAsync(envName);
            TempData["Success"] = $"Blue-Green swap completed for '{env.Name}'. " +
                $"{env.Slots.First(s => s.Active).Label} is now active.";
        }
        catch (Exception ex)
        {
            TempData["Error"] = $"Swap failed: {ex.Message}";
        }
        return RedirectToAction(nameof(Deployments));
    }

    [HttpPost]
    public async Task<IActionResult> DeployToSlot(string envName, string slotLabel, string version)
    {
        if (!EnsureConnected()) return RedirectToAction(nameof(Connect));
        var record = await _devOps.DeployToSlotAsync(envName, slotLabel, version);
        TempData["Success"] = $"Deployed v{version} to {envName}/{slotLabel} slot.";
        return RedirectToAction(nameof(Deployments));
    }

    // ── Helpers ──

    private bool EnsureConnected()
    {
        var org = HttpContext.Session.GetString("DevOpsOrg");
        var project = HttpContext.Session.GetString("DevOpsProject");
        var pat = HttpContext.Session.GetString("DevOpsPat");
        if (string.IsNullOrEmpty(org) || string.IsNullOrEmpty(project) || string.IsNullOrEmpty(pat))
            return false;
        _devOps.SetConnection(new DevOpsConnection
        {
            Organization = org,
            Project = project,
            Pat = pat
        });
        return true;
    }

    private static string GetDotNetPipelineYaml()
    {
        return @"
trigger:
  branches:
    include:
    - main
    - develop

pool:
  vmImage: 'ubuntu-latest'

variables:
  buildConfiguration: 'Release'

steps:
- task: DotNetCoreCLI@2
  displayName: 'Restore packages'
  inputs:
    command: 'restore'

- task: DotNetCoreCLI@2
  displayName: 'Build'
  inputs:
    command: 'build'
    arguments: '--configuration $(buildConfiguration)'

- task: DotNetCoreCLI@2
  displayName: 'Run tests'
  inputs:
    command: 'test'
    arguments: '--configuration $(buildConfiguration) --verbosity normal'

- task: DotNetCoreCLI@2
  displayName: 'Publish'
  inputs:
    command: 'publish'
    publishWebProjects: true
    arguments: '--configuration $(buildConfiguration) --output $(Build.ArtifactStagingDirectory)'

- task: PublishBuildArtifacts@1
  displayName: 'Publish build artifacts'
  inputs:
    PathtoPublish: '$(Build.ArtifactStagingDirectory)'
    ArtifactName: 'drop'
".TrimStart();
    }
}
