using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using DevopsMvcApp.Models.DevOps;
using DevopsMvcApp.Services;

namespace DevopsMvcApp.Controllers;

/// <summary>
/// DevOps Portal controller — requires authentication via [Authorize].
/// All actions act as a proxy to the Azure DevOps REST API using
/// connection credentials stored in the user's session.
/// Every action calls EnsureConnected() first to restore the session.
/// </summary>
[Authorize]
public class DevOpsController : Controller
{
    private readonly DevOpsService _devOps;

    public DevOpsController(DevOpsService devOps)
    {
        _devOps = devOps;
    }

    // ══════════════════════════════════════════
    //  CONNECT / DASHBOARD
    // ══════════════════════════════════════════

    /// <summary>Shows the Azure DevOps connection form.</summary>
    [HttpGet]
    public IActionResult Connect()
    {
        return View(new DevOpsConnection());
    }

    /// <summary>Validates credentials, stores them in session, and tests the connection.</summary>
    [HttpPost]
    public IActionResult Connect(DevOpsConnection connection)
    {
        if (!ModelState.IsValid) return View(connection);

        connection.Organization = SanitizeOrg(connection.Organization);
        connection.Project = connection.Project.Trim().TrimEnd('/');
        connection.Pat = connection.Pat.Trim();

        _devOps.SetConnection(connection);
        HttpContext.Session.SetString("DevOpsOrg", connection.Organization);
        HttpContext.Session.SetString("DevOpsProject", connection.Project);
        HttpContext.Session.SetString("DevOpsPat", connection.Pat);

        // Quick validation: try fetching repos to verify credentials
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

    /// <summary>Strips full Azure DevOps URLs from the org field (user may paste a URL).</summary>
    private static string SanitizeOrg(string org)
    {
        org = org.Trim().TrimEnd('/');
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

    /// <summary>Fetches aggregated dashboard stats (repos, pipelines, builds, PRs, releases, activity).</summary>
    public async Task<IActionResult> Dashboard()
    {
        if (!EnsureConnected()) return RedirectToAction(nameof(Connect));
        var dashboard = await _devOps.GetDashboardAsync();
        return View(dashboard);
    }

    /// <summary>Clears the session and disconnects from Azure DevOps.</summary>
    public IActionResult Disconnect()
    {
        HttpContext.Session.Remove("DevOpsOrg");
        HttpContext.Session.Remove("DevOpsProject");
        HttpContext.Session.Remove("DevOpsPat");
        TempData["Success"] = "Disconnected from Azure DevOps.";
        return RedirectToAction(nameof(Connect));
    }

    /// <summary>Shows the auto-deploy page with status of each step (pipeline → build → release → deploy).</summary>
    [HttpGet]
    public async Task<IActionResult> AutoDeploy()
    {
        if (!EnsureConnected()) return RedirectToAction(nameof(Connect));
        var repos = await _devOps.GetRepositoriesAsync();
        ViewBag.Repositories = repos;
        return View(new AutoDeployResult());
    }

    /// <summary>Orchestrates the full CI/CD flow: commits YAML, creates pipeline, queues build, waits for completion, creates release, deploys.</summary>
    [HttpPost]
    public async Task<IActionResult> AutoDeploy(string repoId, string defaultBranch)
    {
        if (!EnsureConnected()) return RedirectToAction(nameof(Connect));
        var result = new AutoDeployResult();
        var branch = string.IsNullOrWhiteSpace(defaultBranch) ? "main" : defaultBranch.Trim();

        try
        {
            // ── Step 1: Set up branch ──
            var step1 = new DeployStep { Name = "Setting up branch" };
            result.Steps.Add(step1);
            try
            {
                var repo = await _devOps.GetRepoAsync(repoId);
                var actualBranch = repo?.DefaultBranch?.Replace("refs/heads/", "") ?? "master";
                if (!string.Equals(branch, actualBranch, StringComparison.OrdinalIgnoreCase))
                {
                    try { await _devOps.CreateBranchAsync(repoId, branch, actualBranch); }
                    catch { }
                }
                await _devOps.SetRepoDefaultBranchAsync(repoId, branch);
                step1.Completed = true;
                step1.Detail = $"Branch '{branch}' ready";
            }
            catch (Exception ex) { step1.Failed = true; step1.ErrorMessage = ex.Message; }

            // ── Step 2: Commit pipeline YAML ──
            var step2 = new DeployStep { Name = "Committing pipeline YAML" };
            result.Steps.Add(step2);
            try
            {
                var yaml = GetDotNetPipelineYaml();
                await _devOps.CommitFileAsync(repoId, branch, "azure-pipelines.yml", yaml,
                    "Add CI pipeline definition");
                step2.Completed = true;
                step2.Detail = "azure-pipelines.yml committed";
            }
            catch (Exception ex) { step2.Failed = true; step2.ErrorMessage = ex.Message; }

            // ── Step 3: Create pipeline definition ──
            var step3 = new DeployStep { Name = "Creating pipeline definition" };
            result.Steps.Add(step3);
            try
            {
                var pipeline = await _devOps.CreatePipelineAsync(new CreatePipelineRequest
                {
                    Name = "CI-CD Pipeline",
                    RepositoryId = repoId,
                    DefaultBranch = branch,
                    YamlPath = "azure-pipelines.yml"
                });
                if (pipeline != null)
                {
                    step3.Completed = true;
                    step3.Detail = $"Pipeline '{pipeline.Name}' created";
                    result.PipelineUrl = pipeline.WebUrl;

                    // ── Step 4: Queue build ──
                    var step4 = new DeployStep { Name = "Queuing build" };
                    result.Steps.Add(step4);
                    try
                    {
                        var run = await _devOps.RunPipelineAsync(pipeline.Id, branch);
                        if (run != null)
                        {
                            step4.Completed = true;
                            step4.Detail = $"Run #{run.Id} queued";
                            result.BuildUrl = run.WebUrl;

                            // ── Step 5: Wait for build to complete ──
                            var step5 = new DeployStep { Name = "Waiting for build to complete" };
                            result.Steps.Add(step5);
                            try
                            {
                                for (int i = 0; i < 60; i++) // poll up to ~10 min
                                {
                                    await Task.Delay(10000);
                                    var build = await _devOps.GetBuildAsync(run.Id);
                                    if (build == null) continue;
                                    if (build.Status == "completed")
                                    {
                                        step5.Completed = true;
                                        step5.Detail = $"Build #{build.Id} completed: {build.Result}";
                                        result.BuildUrl = build.WebUrl;

                                        if (build.Result == "succeeded")
                                        {
                                            // ── Step 6: Create release definition ──
                                            var step6 = new DeployStep { Name = "Creating release definition" };
                                            result.Steps.Add(step6);
                                            try
                                            {
                                                var defs = await _devOps.GetReleaseDefinitionsAsync();
                                                var existing = defs.FirstOrDefault(d => d.Name == "Auto-Deploy Release");
                                                if (existing != null)
                                                {
                                                    step6.Completed = true;
                                                    step6.Detail = $"Release definition '{existing.Name}' already exists";
                                                }
                                                else
                                                {
                                                    var def = await _devOps.CreateReleaseDefinitionAsync("Auto-Deploy Release",
                                                        "Auto-created by DevOps Portal");
                                                    if (def != null)
                                                    {
                                                        step6.Completed = true;
                                                        step6.Detail = $"Release definition '{def.Name}' created";
                                                    }
                                                    else { step6.Failed = true; step6.ErrorMessage = "Failed to create release definition"; }
                                                }
                                            }
                                            catch (Exception ex) { step6.Failed = true; step6.ErrorMessage = ex.Message; }

                                            // ── Step 7: Deploy ──
                                            var step7 = new DeployStep { Name = "Deploying application" };
                                            result.Steps.Add(step7);
                                            try
                                            {
                                                var envs = await _devOps.GetDeploymentEnvironmentsAsync();
                                                var dev = envs.FirstOrDefault();
                                                if (dev != null)
                                                {
                                                    var slot = dev.Slots.FirstOrDefault(s => !s.Active);
                                                    if (slot != null)
                                                    {
                                                        await _devOps.DeployToSlotAsync(dev.Name, slot.Label!, build.BuildNumber);
                                                        await _devOps.SwapSlotsAsync(dev.Name);
                                                        step7.Completed = true;
                                                        step7.Detail = $"Deployed v{build.BuildNumber} to {dev.Name}";
                                                    }
                                                    else
                                                    {
                                                        await _devOps.DeployToSlotAsync(dev.Name, "Blue", build.BuildNumber);
                                                        step7.Completed = true;
                                                        step7.Detail = $"Deployed v{build.BuildNumber} to {dev.Name}/Blue";
                                                    }
                                                }
                                                else
                                                {
                                                    step7.Completed = true;
                                                    step7.Detail = "No deployment environments configured. Create one from the Deployments tab.";
                                                }
                                            }
                                            catch (Exception ex) { step7.Failed = true; step7.ErrorMessage = ex.Message; }
                                        }
                                        break;
                                    }
                                }
                                if (!step5.Completed)
                                {
                                    step5.Completed = true;
                                    step5.Detail = "Build did not complete within the timeout period. Check Pipelines page.";
                                }
                            }
                            catch (Exception ex) { step5.Failed = true; step5.ErrorMessage = ex.Message; }
                        }
                        else { step4.Failed = true; step4.ErrorMessage = "Run returned null"; }
                    }
                    catch (Exception ex) { step4.Failed = true; step4.ErrorMessage = ex.Message; }
                }
                else { step3.Failed = true; step3.ErrorMessage = "Pipeline creation returned null"; }
            }
            catch (Exception ex) { step3.Failed = true; step3.ErrorMessage = ex.Message; }
        }
        catch (Exception ex)
        {
            result.Steps.Add(new DeployStep { Name = "Auto-deploy failed", Failed = true, ErrorMessage = ex.Message });
        }

        return View(result);
    }

    // ══════════════════════════════════════════
    //  REPOSITORIES
    // ══════════════════════════════════════════

    /// <summary>Lists all Git repositories in the project.</summary>
    public async Task<IActionResult> Repositories()
    {
        if (!EnsureConnected()) return RedirectToAction(nameof(Connect));
        var repos = await _devOps.GetRepositoriesAsync();
        return View(repos);
    }

    /// <summary>Shows the create-repository form.</summary>
    [HttpGet]
    public IActionResult CreateRepository()
    {
        return View(new CreateRepositoryRequest());
    }

    /// <summary>Creates a new repository and redirects to the list.</summary>
    [HttpPost]
    public async Task<IActionResult> CreateRepository(CreateRepositoryRequest req)
    {
        if (!EnsureConnected()) return RedirectToAction(nameof(Connect));
        if (!ModelState.IsValid) return View(req);
        try { await _devOps.CreateRepositoryAsync(req.Name); }
        catch (Exception ex) { TempData["Error"] = $"Failed to create repository: {ex.Message}"; return RedirectToAction(nameof(Repositories)); }
        TempData["Success"] = $"Repository '{req.Name}' created.";
        return RedirectToAction(nameof(Repositories));
    }

    /// <summary>Shows the rename-repository form with current name pre-filled.</summary>
    [HttpGet]
    public async Task<IActionResult> RenameRepository(string repoId)
    {
        if (!EnsureConnected()) return RedirectToAction(nameof(Connect));
        var repos = await _devOps.GetRepositoriesAsync();
        var repo = repos.FirstOrDefault(r => r.Id == repoId);
        if (repo == null) return NotFound();
        return View(repo);
    }

    /// <summary>Renames the repository.</summary>
    [HttpPost]
    public async Task<IActionResult> RenameRepository(string repoId, string newName)
    {
        if (!EnsureConnected()) return RedirectToAction(nameof(Connect));
        if (string.IsNullOrWhiteSpace(newName))
        {
            ModelState.AddModelError("", "Name is required.");
            return View(await _devOps.GetRepositoriesAsync().ContinueWith(t => t.Result.FirstOrDefault(r => r.Id == repoId)));
        }
        try { await _devOps.RenameRepositoryAsync(repoId, newName.Trim()); }
        catch (Exception ex) { TempData["Error"] = $"Failed to rename repository: {ex.Message}"; return RedirectToAction(nameof(Repositories)); }
        TempData["Success"] = $"Repository renamed to '{newName}'.";
        return RedirectToAction(nameof(Repositories));
    }
    
    /// <summary>Deletes a repository after confirmation.</summary>
    [HttpPost]
    public async Task<IActionResult> DeleteRepository(string repoId, string repoName)
    {
        if (!EnsureConnected()) return RedirectToAction(nameof(Connect));
        try { await _devOps.DeleteRepositoryAsync(repoId); }
        catch (Exception ex) { TempData["Error"] = $"Failed to delete repository: {ex.Message}"; return RedirectToAction(nameof(Repositories)); }
        TempData["Success"] = $"Repository '{repoName}' deleted.";
        return RedirectToAction(nameof(Repositories));
    }

    // ══════════════════════════════════════════
    //  PIPELINES
    // ══════════════════════════════════════════

    /// <summary>Lists all YAML pipelines.</summary>
    public async Task<IActionResult> Pipelines()
    {
        if (!EnsureConnected()) return RedirectToAction(nameof(Connect));
        var pipelines = await _devOps.GetPipelinesAsync();
        return View(pipelines);
    }

    /// <summary>Shows the create-pipeline form with a repository dropdown.</summary>
    [HttpGet]
    public async Task<IActionResult> CreatePipeline()
    {
        if (!EnsureConnected()) return RedirectToAction(nameof(Connect));
        var repos = await _devOps.GetRepositoriesAsync();
        ViewBag.Repositories = repos;
        return View(new CreatePipelineRequest());
    }

    /// <summary>Commits a YAML file, creates the pipeline definition, and queues a run.</summary>
    [HttpPost]
    public async Task<IActionResult> CreatePipeline(CreatePipelineRequest req)
    {
        if (!EnsureConnected()) return RedirectToAction(nameof(Connect));
        if (!ModelState.IsValid) return View(req);

        try
        {
            // Get the repo to discover its actual default branch
            var repo = await _devOps.GetRepoAsync(req.RepositoryId);
            var actualBranch = repo?.DefaultBranch?.Replace("refs/heads/", "") ?? "master";

            // If requested branch differs from actual, create it from actual first
            // so it inherits all project files (not orphaned)
            if (!string.Equals(req.DefaultBranch, actualBranch, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    await _devOps.CreateBranchAsync(req.RepositoryId, req.DefaultBranch, actualBranch);
                }
                catch { /* branch may already exist */ }
            }

            // Now set repo's default branch to the pipeline branch
            await _devOps.SetRepoDefaultBranchAsync(req.RepositoryId, req.DefaultBranch);

            // 1. Commit the YAML pipeline file (auto-falls back to "edit" if file exists)
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

            // 3. Queue a run (explicitly pass branch to avoid propagation races)
            var run = await _devOps.RunPipelineAsync(pipeline.Id, req.DefaultBranch);
            if (run == null)
            {
                TempData["Warning"] = $"Pipeline '{pipeline.Name}' created but the first run could not be queued. You can manually queue a run.";
                return RedirectToAction(nameof(Pipelines));
            }

            TempData["Success"] = $"Pipeline '{pipeline.Name}' created and queued.";
        }
        catch (HttpRequestException ex)
        {
            TempData["Error"] = $"Pipeline creation failed: {ex.Message}";
        }
        return RedirectToAction(nameof(Pipelines));
    }

    /// <summary>Queues a new run of a specific pipeline.</summary>
    public async Task<IActionResult> RunPipeline(int id)
    {
        if (!EnsureConnected()) return RedirectToAction(nameof(Connect));
        try
        {
            var run = await _devOps.RunPipelineAsync(id);
            if (run != null) TempData["Success"] = $"Pipeline run #{run.Id} queued.";
        }
        catch (Exception ex) { TempData["Error"] = $"Failed to queue run: {ex.Message}"; }
        return RedirectToAction(nameof(Pipelines));
    }

    /// <summary>Shows run history for a pipeline.</summary>
    public async Task<IActionResult> PipelineRuns(int id, string name)
    {
        if (!EnsureConnected()) return RedirectToAction(nameof(Connect));
        var runs = await _devOps.GetPipelineRunsAsync(id);
        ViewBag.PipelineName = name;
        ViewBag.PipelineId = id;
        return View(runs);
    }

    /// <summary>Deletes a pipeline definition.</summary>
    [HttpPost]
    public async Task<IActionResult> DeletePipeline(int id, string name)
    {
        if (!EnsureConnected()) return RedirectToAction(nameof(Connect));
        try { await _devOps.DeletePipelineAsync(id, name); }
        catch (Exception ex) { TempData["Error"] = $"Failed to delete pipeline: {ex.Message}"; return RedirectToAction(nameof(Pipelines)); }
        TempData["Success"] = $"Pipeline '{name}' deleted.";
        return RedirectToAction(nameof(Pipelines));
    }

    // ══════════════════════════════════════════
    //  PULL REQUESTS
    // ══════════════════════════════════════════

    /// <summary>Lists active PRs for the selected repository.</summary>
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

    /// <summary>Shows the create-PR form.</summary>
    [HttpGet]
    public async Task<IActionResult> CreatePullRequest()
    {
        if (!EnsureConnected()) return RedirectToAction(nameof(Connect));
        var repos = await _devOps.GetRepositoriesAsync();
        ViewBag.Repositories = repos;
        return View(new CreatePullRequestRequest());
    }

    /// <summary>Creates a pull request.</summary>
    [HttpPost]
    public async Task<IActionResult> CreatePullRequest(CreatePullRequestRequest req)
    {
        if (!EnsureConnected()) return RedirectToAction(nameof(Connect));
        if (!ModelState.IsValid) return View(req);
        try
        {
            var pr = await _devOps.CreatePullRequestAsync(req);
            if (pr != null)
                TempData["Success"] = $"PR #{pr.PullRequestId} created.";
            else
                TempData["Error"] = "Failed to create pull request.";
        }
        catch (Exception ex) { TempData["Error"] = $"Failed to create PR: {ex.Message}"; }
        return RedirectToAction(nameof(PullRequests));
    }

    /// <summary>Completes (approves + merges) a pull request.</summary>
    public async Task<IActionResult> ApprovePullRequest(string repoId, int prId)
    {
        if (!EnsureConnected()) return RedirectToAction(nameof(Connect));
        try { await _devOps.UpdatePullRequestStatusAsync(repoId, prId, "completed"); }
        catch (Exception ex) { TempData["Error"] = $"Failed to complete PR: {ex.Message}"; return RedirectToAction(nameof(PullRequests)); }
        TempData["Success"] = $"PR #{prId} completed.";
        return RedirectToAction(nameof(PullRequests));
    }

    /// <summary>Abandons a pull request (marks as abandoned without merging).</summary>
    [HttpPost]
    public async Task<IActionResult> AbandonPullRequest(string repoId, int prId)
    {
        if (!EnsureConnected()) return RedirectToAction(nameof(Connect));
        try { await _devOps.AbandonPullRequestAsync(repoId, prId); }
        catch (Exception ex) { TempData["Error"] = $"Failed to abandon PR: {ex.Message}"; return RedirectToAction(nameof(PullRequests)); }
        TempData["Success"] = $"PR #{prId} abandoned.";
        return RedirectToAction(nameof(PullRequests));
    }

    /// <summary>Posts a new comment thread on a pull request.</summary>
    [HttpPost]
    public async Task<IActionResult> AddComment(string repoId, int prId, string content)
    {
        if (!EnsureConnected()) return RedirectToAction(nameof(Connect));
        if (!string.IsNullOrWhiteSpace(content))
        {
            try { await _devOps.AddPullRequestCommentAsync(repoId, prId, content); }
            catch (Exception ex) { TempData["Error"] = $"Failed to add comment: {ex.Message}"; return RedirectToAction(nameof(PullRequestDetail), new { repoId, prId }); }
            TempData["Success"] = "Comment added.";
        }
        return RedirectToAction(nameof(PullRequestDetail), new { repoId, prId });
    }

    // ══════════════════════════════════════════
    //  BUILDS & ARTIFACTS
    // ══════════════════════════════════════════

    /// <summary>Lists recent builds.</summary>
    public async Task<IActionResult> Builds()
    {
        if (!EnsureConnected()) return RedirectToAction(nameof(Connect));
        var builds = await _devOps.GetBuildsAsync();
        return View(builds);
    }

    /// <summary>Lists artifacts produced by a specific build.</summary>
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

    /// <summary>Proxies artifact download through the server (keeps PAT secure in the header).</summary>
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

    // ══════════════════════════════════════════
    //  FILE BROWSER
    // ══════════════════════════════════════════

    /// <summary>Lists files/folders at a given path in a repository branch.</summary>
    public async Task<IActionResult> RepoFiles(string repoId, string? path, string? branch)
    {
        if (!EnsureConnected()) return RedirectToAction(nameof(Connect));
        var repos = await _devOps.GetRepositoriesAsync();
        var repo = repos.FirstOrDefault(r => r.Id == repoId);
        var defaultBranch = repo?.DefaultBranch?.Replace("refs/heads/", "") ?? "main";
        branch ??= defaultBranch;
        var files = await _devOps.GetRepoFilesAsync(repoId, path ?? "", branch);
        ViewBag.RepoId = repoId;
        ViewBag.CurrentPath = path ?? "";
        ViewBag.Branch = branch;
        ViewBag.RepoName = repo?.Name ?? repoId;
        return View(files);
    }

    /// <summary>Displays the raw content of a file.</summary>
    public async Task<IActionResult> RepoFileContent(string repoId, string path, string? branch)
    {
        if (!EnsureConnected()) return RedirectToAction(nameof(Connect));
        var repos = await _devOps.GetRepositoriesAsync();
        var repo = repos.FirstOrDefault(r => r.Id == repoId);
        var defaultBranch = repo?.DefaultBranch?.Replace("refs/heads/", "") ?? "main";
        branch ??= defaultBranch;
        var file = await _devOps.GetFileContentAsync(repoId, path, branch);
        return View(file);
    }

    /// <summary>Shows the file upload form.</summary>
    [HttpGet]
    public async Task<IActionResult> UploadFile()
    {
        if (!EnsureConnected()) return RedirectToAction(nameof(Connect));
        ViewBag.Repositories = await _devOps.GetRepositoriesAsync();
        return View(new UploadFileRequest());
    }

    /// <summary>Commits a new file to a repository branch.</summary>
    [HttpPost]
    public async Task<IActionResult> UploadFile(UploadFileRequest req)
    {
        if (!EnsureConnected()) return RedirectToAction(nameof(Connect));
        if (!ModelState.IsValid) return View(req);
        try { await _devOps.CommitFileToBranchAsync(req.RepositoryId, req.Branch, req.FilePath, req.Content, req.Comment); }
        catch (Exception ex) { TempData["Error"] = $"Failed to upload file: {ex.Message}"; return RedirectToAction(nameof(RepoFiles), new { repoId = req.RepositoryId, branch = req.Branch }); }
        TempData["Success"] = $"File '{req.FilePath}' committed to {req.Branch}.";
        return RedirectToAction(nameof(RepoFiles), new { repoId = req.RepositoryId, branch = req.Branch });
    }

    /// <summary>Shows the inline file editor pre-filled with current file content.</summary>
    [HttpGet]
    public async Task<IActionResult> EditFile(string repoId, string path, string? branch)
    {
        if (!EnsureConnected()) return RedirectToAction(nameof(Connect));
        var repos = await _devOps.GetRepositoriesAsync();
        var repo = repos.FirstOrDefault(r => r.Id == repoId);
        branch ??= repo?.DefaultBranch?.Replace("refs/heads/", "") ?? "main";
        var file = await _devOps.GetFileContentAsync(repoId, path, branch);
        return View(file);
    }

    /// <summary>Saves edited file content via a push with changeType "edit".</summary>
    [HttpPost]
    public async Task<IActionResult> EditFile(string repoId, string path, string branch, string content, string comment)
    {
        if (!EnsureConnected()) return RedirectToAction(nameof(Connect));
        try { await _devOps.EditFileAsync(repoId, branch, path, content, comment ?? $"Updated {path}"); }
        catch (Exception ex) { TempData["Error"] = $"Failed to edit file: {ex.Message}"; return RedirectToAction(nameof(RepoFiles), new { repoId, branch }); }
        TempData["Success"] = $"File '{path}' updated.";
        return RedirectToAction(nameof(RepoFiles), new { repoId, branch });
    }

    /// <summary>Deletes a file from a repository branch.</summary>
    [HttpPost]
    public async Task<IActionResult> DeleteFile(string repoId, string path, string branch)
    {
        if (!EnsureConnected()) return RedirectToAction(nameof(Connect));
        try { await _devOps.DeleteFileAsync(repoId, branch, path, $"Deleted {path}"); }
        catch (Exception ex) { TempData["Error"] = $"Failed to delete file: {ex.Message}"; return RedirectToAction(nameof(RepoFiles), new { repoId, branch }); }
        TempData["Success"] = $"File '{path}' deleted.";
        return RedirectToAction(nameof(RepoFiles), new { repoId, branch });
    }

    // ══════════════════════════════════════════
    //  COMMITS & BRANCHES
    // ══════════════════════════════════════════

    /// <summary>Shows recent commits for a repository.</summary>
    public async Task<IActionResult> RepoCommits(string repoId)
    {
        if (!EnsureConnected()) return RedirectToAction(nameof(Connect));
        var commits = await _devOps.GetCommitHistoryAsync(repoId);
        ViewBag.RepoId = repoId;
        ViewBag.RepoName = (await _devOps.GetRepositoriesAsync()).FirstOrDefault(r => r.Id == repoId)?.Name ?? repoId;
        return View(commits);
    }

    /// <summary>Shows all branches in a repository with create/delete actions.</summary>
    public async Task<IActionResult> RepoBranches(string repoId)
    {
        if (!EnsureConnected()) return RedirectToAction(nameof(Connect));
        var branches = await _devOps.GetBranchesAsync(repoId);
        ViewBag.RepoId = repoId;
        ViewBag.RepoName = (await _devOps.GetRepositoriesAsync()).FirstOrDefault(r => r.Id == repoId)?.Name ?? repoId;
        return View(branches);
    }

    /// <summary>Creates a new branch from a source branch.</summary>
    [HttpPost]
    public async Task<IActionResult> CreateBranch(string repoId, string branchName, string? sourceBranch)
    {
        if (!EnsureConnected()) return RedirectToAction(nameof(Connect));
        sourceBranch = string.IsNullOrWhiteSpace(sourceBranch) ? null : sourceBranch;
        try { await _devOps.CreateBranchAsync(repoId, branchName.Trim(), sourceBranch); }
        catch (Exception ex) { TempData["Error"] = $"Failed to create branch: {ex.Message}"; return RedirectToAction(nameof(RepoBranches), new { repoId }); }
        TempData["Success"] = $"Branch '{branchName}' created.";
        return RedirectToAction(nameof(RepoBranches), new { repoId });
    }

    /// <summary>Deletes a branch.</summary>
    [HttpPost]
    public async Task<IActionResult> DeleteBranch(string repoId, string branchName)
    {
        if (!EnsureConnected()) return RedirectToAction(nameof(Connect));
        try { await _devOps.DeleteBranchAsync(repoId, branchName); }
        catch (Exception ex) { TempData["Error"] = $"Failed to delete branch: {ex.Message}"; return RedirectToAction(nameof(RepoBranches), new { repoId }); }
        TempData["Success"] = $"Branch '{branchName}' deleted.";
        return RedirectToAction(nameof(RepoBranches), new { repoId });
    }

    // ══════════════════════════════════════════
    //  CANCEL OPERATIONS
    // ══════════════════════════════════════════

    /// <summary>Cancels a pipeline run.</summary>
    public async Task<IActionResult> CancelRun(int pipelineId, int runId)
    {
        if (!EnsureConnected()) return RedirectToAction(nameof(Connect));
        try { await _devOps.CancelPipelineRunAsync(pipelineId, runId); }
        catch (Exception ex) { TempData["Error"] = $"Failed to cancel run: {ex.Message}"; return RedirectToAction(nameof(PipelineRuns), new { id = pipelineId }); }
        TempData["Success"] = $"Run #{runId} cancelled.";
        return RedirectToAction(nameof(PipelineRuns), new { id = pipelineId });
    }

    /// <summary>Cancels a build.</summary>
    public async Task<IActionResult> CancelBuild(int buildId)
    {
        if (!EnsureConnected()) return RedirectToAction(nameof(Connect));
        try { await _devOps.CancelBuildAsync(buildId); }
        catch (Exception ex) { TempData["Error"] = $"Failed to cancel build: {ex.Message}"; return RedirectToAction(nameof(Builds)); }
        TempData["Success"] = $"Build #{buildId} cancelled.";
        return RedirectToAction(nameof(Builds));
    }

    // ══════════════════════════════════════════
    //  BUILD TIMELINE & LOGS
    // ══════════════════════════════════════════

    /// <summary>Shows the step-by-step timeline for a completed build.</summary>
    public async Task<IActionResult> BuildTimeline(int buildId, string? buildNumber)
    {
        if (!EnsureConnected()) return RedirectToAction(nameof(Connect));
        var timeline = await _devOps.GetBuildTimelineAsync(buildId);
        ViewBag.BuildId = buildId;
        ViewBag.BuildNumber = buildNumber ?? $"#{buildId}";
        return View(timeline);
    }

    /// <summary>Shows the raw log text for a specific build step.</summary>
    public async Task<IActionResult> BuildLog(int buildId, int logId, string? stepName)
    {
        if (!EnsureConnected()) return RedirectToAction(nameof(Connect));
        var log = await _devOps.GetBuildLogAsync(buildId, logId);
        ViewBag.StepName = stepName ?? $"Log #{logId}";
        ViewBag.BuildId = buildId;
        return View(log);
    }

    // ══════════════════════════════════════════
    //  PR DETAIL (with comments)
    // ══════════════════════════════════════════

    /// <summary>Displays PR details and associated comment threads.</summary>
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

    // ══════════════════════════════════════════
    //  AUTO PR (from feature branch)
    // ══════════════════════════════════════════

    /// <summary>Auto-creates a PR from a feature branch to main.</summary>
    [HttpPost]
    public async Task<IActionResult> AutoCreatePr(string repoId, string featureBranch, string title, string description)
    {
        if (!EnsureConnected()) return RedirectToAction(nameof(Connect));
        try
        {
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
        }
        catch (Exception ex) { TempData["Error"] = $"Auto PR failed: {ex.Message}"; }
        return RedirectToAction(nameof(PullRequests));
    }

    // ══════════════════════════════════════════
    //  BLUE-GREEN DEPLOYMENTS
    // ══════════════════════════════════════════

    /// <summary>Shows the visual Blue-Green deployment dashboard with slot cards.</summary>
    public async Task<IActionResult> Deployments()
    {
        if (!EnsureConnected()) return RedirectToAction(nameof(Connect));
        var envs = await _devOps.GetDeploymentEnvironmentsAsync();
        var releases = await _devOps.GetReleasesAsync(5);
        ViewBag.RecentReleases = releases;
        return View(envs);
    }

    /// <summary>Shows a tabular view of deployment environments.</summary>
    public async Task<IActionResult> Environments()
    {
        if (!EnsureConnected()) return RedirectToAction(nameof(Connect));
        var envs = await _devOps.GetDeploymentEnvironmentsAsync();
        return View(envs);
    }

    /// <summary>Shows releases and release definitions with CRUD forms.</summary>
    public async Task<IActionResult> Releases()
    {
        if (!EnsureConnected()) return RedirectToAction(nameof(Connect));
        var releases = await _devOps.GetReleasesAsync(20);
        var defs = await _devOps.GetReleaseDefinitionsAsync();
        ViewBag.Definitions = defs;
        return View(releases);
    }

    /// <summary>Creates a new release from a release definition.</summary>
    [HttpPost]
    public async Task<IActionResult> CreateRelease(int definitionId, string? description)
    {
        if (!EnsureConnected()) return RedirectToAction(nameof(Connect));
        try
        {
            var release = await _devOps.CreateReleaseAsync(definitionId, description);
            if (release != null)
                TempData["Success"] = $"Release '{release.Name}' created.";
            else
                TempData["Error"] = "Failed to create release.";
        }
        catch (Exception ex) { TempData["Error"] = $"Failed to create release: {ex.Message}"; }
        return RedirectToAction(nameof(Releases));
    }

    /// <summary>Shows the create-release-definition form.</summary>
    [HttpGet]
    public IActionResult CreateReleaseDefinition()
    {
        return View(new CreateReleaseDefinitionRequest());
    }

    /// <summary>Creates a new release definition.</summary>
    [HttpPost]
    public async Task<IActionResult> CreateReleaseDefinition(string name, string? description)
    {
        if (!EnsureConnected()) return RedirectToAction(nameof(Connect));
        if (string.IsNullOrWhiteSpace(name))
        {
            ModelState.AddModelError("", "Name is required.");
            return View(new CreateReleaseDefinitionRequest());
        }
        try
        {
            var def = await _devOps.CreateReleaseDefinitionAsync(name.Trim(), description?.Trim());
            if (def != null)
                TempData["Success"] = $"Release definition '{def.Name}' created.";
            else
                TempData["Error"] = "Failed to create release definition.";
        }
        catch (Exception ex) { TempData["Error"] = $"Failed to create release definition: {ex.Message}"; }
        return RedirectToAction(nameof(Releases));
    }

    /// <summary>Deletes a release definition.</summary>
    [HttpPost]
    public async Task<IActionResult> DeleteReleaseDefinition(int id, string name)
    {
        if (!EnsureConnected()) return RedirectToAction(nameof(Connect));
        try { await _devOps.DeleteReleaseDefinitionAsync(id); }
        catch (Exception ex) { TempData["Error"] = $"Failed to delete release definition: {ex.Message}"; return RedirectToAction(nameof(Releases)); }
        TempData["Success"] = $"Release definition '{name}' deleted.";
        return RedirectToAction(nameof(Releases));
    }

    /// <summary>Swaps active/standby slots within an environment (simulated).</summary>
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

    /// <summary>Deploys a version to a specific slot (simulated).</summary>
    [HttpPost]
    public async Task<IActionResult> DeployToSlot(string envName, string slotLabel, string version)
    {
        if (!EnsureConnected()) return RedirectToAction(nameof(Connect));
        try { await _devOps.DeployToSlotAsync(envName, slotLabel, version); }
        catch (Exception ex) { TempData["Error"] = $"Deploy failed: {ex.Message}"; return RedirectToAction(nameof(Deployments)); }
        TempData["Success"] = $"Deployed v{version} to {envName}/{slotLabel} slot.";
        return RedirectToAction(nameof(Deployments));
    }

    // ══════════════════════════════════════════
    //  HELPERS
    // ══════════════════════════════════════════

    /// <summary>Restores the DevOps connection from session data.
    /// Returns false if no connection is stored, which triggers a redirect to Connect.</summary>
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

    /// <summary>Returns the default .NET CI pipeline YAML content with self-healing on failure.</summary>
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
  maxAutoFixAttempts: 3

steps:
- task: DotNetCoreCLI@2
  displayName: 'Restore packages'
  inputs:
    command: 'restore'
    projects: 'src/DevopsMvcApp/*.csproj'

- task: DotNetCoreCLI@2
  displayName: 'Build'
  inputs:
    command: 'build'
    projects: 'src/DevopsMvcApp/*.csproj'
    arguments: '--configuration $(buildConfiguration)'

- task: DotNetCoreCLI@2
  displayName: 'Run tests'
  inputs:
    command: 'test'
    projects: 'src/DevopsMvcApp/*.csproj'
    arguments: '--configuration $(buildConfiguration) --verbosity normal'

- task: DotNetCoreCLI@2
  displayName: 'Publish'
  inputs:
    command: 'publish'
    projects: 'src/DevopsMvcApp/*.csproj'
    arguments: '--configuration $(buildConfiguration) --output $(Build.ArtifactStagingDirectory)'

- task: PublishBuildArtifacts@1
  displayName: 'Publish build artifacts'
  inputs:
    PathtoPublish: '$(Build.ArtifactStagingDirectory)'
    ArtifactName: 'drop'

- powershell: |
    # Self-healing step (runs only when previous steps fail)
    $ErrorActionPreference = 'Continue'
    $attempt = [int]::Parse('$(RELEASE_ARTIFACTS_AUTOFIX_COUNTER)')
    $maxAttempts = [int]::Parse('$(maxAutoFixAttempts)')
    Write-Host ""Auto-fix attempt: $attempt / $maxAttempts""

    if ($attempt -ge $maxAttempts) {
        Write-Host '##[error]Max auto-fix attempts reached. Manual intervention required.'
        exit 1
    }

    # Find the .csproj to detect project structure
    $csprojFiles = Get-ChildItem -Path '$(Build.SourcesDirectory)' -Recurse -Filter *.csproj -ErrorAction SilentlyContinue
    if (-not $csprojFiles) {
        Write-Host '##[error]No .csproj files found in repository.'
        exit 1
    }
    $projectDir = Split-Path $csprojFiles[0].FullName -Parent
    $relativeDir = Resolve-Path -Path $projectDir -Relative
    $projGlob = ""$relativeDir/*.csproj"".Replace('\', '/').TrimStart('./')

    # Fix 1: Missing project path in dotnet steps
    $yamlFile = Join-Path '$(Build.SourcesDirectory)' 'azure-pipelines.yml'
    if (Test-Path $yamlFile) {
        $yaml = Get-Content $yamlFile -Raw
        if ($yaml -notmatch 'projects:') {
            Write-Host '##[warning]Adding project path to dotnet steps...'
            $yaml = $yaml -replace ""(command: 'restore')"", ""`$1`n    projects: '$projGlob'""
            $yaml = $yaml -replace ""(command: 'build')"", ""`$1`n    projects: '$projGlob'""
            $yaml = $yaml -replace ""(command: 'test')"", ""`$1`n    projects: '$projGlob'""
            $yaml = $yaml -replace ""(command: 'publish')"", ""`$1`n    projects: '$projGlob'""
            Set-Content -Path $yamlFile -Value $yaml
            git config user.email 'azdo-bot@dev.azure.com'
            git config user.name 'Azure DevOps Auto-Fix'
            git add $yamlFile
            git commit -m 'auto-fix: add project path to dotnet steps'
            git push origin HEAD:'$(Build.SourceBranch)'
            Write-Host '##[warning]Fix committed. Triggering rebuild...'
        }
    }

    # Fix 2: Branch mismatch (repo uses master but pipeline expects main)
    $defaultBranch = git symbolic-ref HEAD 2>$null
    if ($defaultBranch -like '*/master' -and '$(Build.SourceBranch)' -like '*/main') {
        Write-Host '##[warning]Branch mismatch - creating main branch from master...'
        git checkout -b main
        git push origin main
    }

    # Trigger new build via REST API
    $pipeId = '$(System.DefinitionId)'
    $pat = '$(System.AccessToken)'
    $base64 = [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes("":$pat""))
    $body = @{ definition = @{ id = [int]::Parse($pipeId) } } | ConvertTo-Json
    $url = '$(System.TeamFoundationCollectionUri)$(System.TeamProject)/_apis/pipelines/$pipeId/runs?api-version=7.1'
    try {
        Invoke-RestMethod -Uri $url -Method Post -Body $body -ContentType 'application/json' -Headers @{
            Authorization = ""Basic $base64""
        } | Out-Null
        Write-Host ""##[warning]New build triggered (attempt $($attempt + 1)).""
    } catch {
        Write-Host ""##[error]Failed to trigger rebuild: $_""
    }
  displayName: 'Auto-heal on failure'
  condition: failed()
  env:
    SYSTEM_ACCESSTOKEN: $(System.AccessToken)
".TrimStart();
    }
}