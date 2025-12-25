using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace HoNfigurator.Core.Services;

/// <summary>
/// Manages GitHub repository branches for remote version switching.
/// Port of Python HoNfigurator-Central branch management.
/// </summary>
public class GitBranchService
{
    private readonly ILogger<GitBranchService> _logger;
    private readonly string _repoPath;
    private readonly HttpClient _httpClient;

    // GitHub API for HoNfigurator repository
    private const string GitHubApiBase = "https://api.github.com/repos";
    private const string DefaultOwner = "HoNfigurator";
    private const string DefaultRepo = "HoNfigurator-Central";

    public GitBranchService(ILogger<GitBranchService> logger, string? repoPath = null)
    {
        _logger = logger;
        _repoPath = repoPath ?? AppContext.BaseDirectory;
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "HoNfigurator-NET");
        _httpClient.DefaultRequestHeaders.Add("Accept", "application/vnd.github.v3+json");
    }

    /// <summary>
    /// Get current branch information
    /// </summary>
    public async Task<BranchInfo?> GetCurrentBranchAsync()
    {
        try
        {
            // Check if this is a git repository
            if (!Directory.Exists(Path.Combine(_repoPath, ".git")))
            {
                return new BranchInfo
                {
                    Name = "unknown",
                    IsGitRepo = false,
                    Message = "Not a git repository"
                };
            }

            var branchName = await RunGitCommandAsync("rev-parse --abbrev-ref HEAD");
            var commitHash = await RunGitCommandAsync("rev-parse --short HEAD");
            var commitDate = await RunGitCommandAsync("log -1 --format=%ci");
            var commitMessage = await RunGitCommandAsync("log -1 --format=%s");

            return new BranchInfo
            {
                Name = branchName?.Trim() ?? "unknown",
                CommitHash = commitHash?.Trim(),
                CommitDate = commitDate?.Trim(),
                CommitMessage = commitMessage?.Trim(),
                IsGitRepo = true,
                RepoPath = _repoPath
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get current branch");
            return new BranchInfo
            {
                Name = "error",
                IsGitRepo = false,
                Message = ex.Message
            };
        }
    }

    /// <summary>
    /// Get all available branches (local and remote)
    /// </summary>
    public async Task<List<BranchInfo>> GetAllBranchesAsync()
    {
        var branches = new List<BranchInfo>();

        try
        {
            // Fetch latest from remote first
            await RunGitCommandAsync("fetch --all --prune");

            // Get local branches
            var localOutput = await RunGitCommandAsync("branch --format='%(refname:short)'");
            if (!string.IsNullOrEmpty(localOutput))
            {
                foreach (var line in localOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    var name = line.Trim().Trim('\'');
                    if (!string.IsNullOrEmpty(name))
                    {
                        branches.Add(new BranchInfo
                        {
                            Name = name,
                            IsLocal = true,
                            IsGitRepo = true
                        });
                    }
                }
            }

            // Get remote branches
            var remoteOutput = await RunGitCommandAsync("branch -r --format='%(refname:short)'");
            if (!string.IsNullOrEmpty(remoteOutput))
            {
                foreach (var line in remoteOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    var name = line.Trim().Trim('\'');
                    if (!string.IsNullOrEmpty(name) && !name.Contains("HEAD"))
                    {
                        // Remove origin/ prefix for display
                        var displayName = name.StartsWith("origin/") ? name[7..] : name;
                        
                        // Don't add if we already have this as a local branch
                        if (!branches.Any(b => b.Name == displayName))
                        {
                            branches.Add(new BranchInfo
                            {
                                Name = displayName,
                                IsLocal = false,
                                RemoteName = name,
                                IsGitRepo = true
                            });
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get branches");
        }

        return branches.OrderBy(b => b.Name).ToList();
    }

    /// <summary>
    /// Get branches from GitHub API (doesn't require local git)
    /// </summary>
    public async Task<List<GitHubBranchInfo>> GetGitHubBranchesAsync(string? owner = null, string? repo = null)
    {
        var branches = new List<GitHubBranchInfo>();
        owner ??= DefaultOwner;
        repo ??= DefaultRepo;

        try
        {
            var url = $"{GitHubApiBase}/{owner}/{repo}/branches";
            var response = await _httpClient.GetAsync(url);
            
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                var jsonBranches = JsonSerializer.Deserialize<List<JsonElement>>(content);
                
                if (jsonBranches != null)
                {
                    foreach (var branch in jsonBranches)
                    {
                        branches.Add(new GitHubBranchInfo
                        {
                            Name = branch.GetProperty("name").GetString() ?? "",
                            CommitSha = branch.GetProperty("commit").GetProperty("sha").GetString(),
                            Protected = branch.TryGetProperty("protected", out var prot) && prot.GetBoolean()
                        });
                    }
                }
            }
            else
            {
                _logger.LogWarning("GitHub API returned {Status}", response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch GitHub branches");
        }

        return branches;
    }

    /// <summary>
    /// Switch to a different branch
    /// </summary>
    public async Task<SwitchBranchResult> SwitchBranchAsync(string branchName, bool force = false)
    {
        try
        {
            var currentBranch = await GetCurrentBranchAsync();
            if (currentBranch?.Name == branchName)
            {
                return new SwitchBranchResult
                {
                    Success = true,
                    PreviousBranch = branchName,
                    CurrentBranch = branchName,
                    Message = "Already on this branch"
                };
            }

            _logger.LogInformation("Switching from {Current} to {Target}", currentBranch?.Name, branchName);

            // Stash any local changes
            var stashResult = await RunGitCommandAsync("stash");
            var hadStash = !stashResult?.Contains("No local changes") ?? false;

            // Fetch latest
            await RunGitCommandAsync("fetch --all");

            // Checkout the branch
            var checkoutArgs = force ? $"checkout -f {branchName}" : $"checkout {branchName}";
            var checkoutResult = await RunGitCommandAsync(checkoutArgs);

            // If it's a remote branch that doesn't exist locally
            if (checkoutResult?.Contains("error") == true)
            {
                checkoutResult = await RunGitCommandAsync($"checkout -b {branchName} origin/{branchName}");
            }

            // Pull latest changes
            var pullResult = await RunGitCommandAsync("pull");

            // Pop stash if we had one
            if (hadStash)
            {
                await RunGitCommandAsync("stash pop");
            }

            var newBranch = await GetCurrentBranchAsync();

            return new SwitchBranchResult
            {
                Success = newBranch?.Name == branchName,
                PreviousBranch = currentBranch?.Name,
                CurrentBranch = newBranch?.Name ?? branchName,
                Message = $"Switched to branch '{branchName}'",
                RequiresRestart = true
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to switch branch to {Branch}", branchName);
            return new SwitchBranchResult
            {
                Success = false,
                Error = ex.Message
            };
        }
    }

    /// <summary>
    /// Pull latest changes from remote
    /// </summary>
    public async Task<PullResult> PullLatestAsync()
    {
        try
        {
            var beforeHash = await RunGitCommandAsync("rev-parse HEAD");
            
            await RunGitCommandAsync("fetch --all");
            var pullOutput = await RunGitCommandAsync("pull");
            
            var afterHash = await RunGitCommandAsync("rev-parse HEAD");
            var hadChanges = beforeHash?.Trim() != afterHash?.Trim();

            return new PullResult
            {
                Success = true,
                HadChanges = hadChanges,
                Output = pullOutput,
                NewCommitHash = afterHash?.Trim(),
                RequiresRestart = hadChanges
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to pull latest");
            return new PullResult
            {
                Success = false,
                Error = ex.Message
            };
        }
    }

    /// <summary>
    /// Get version information
    /// </summary>
    public async Task<VersionInfo> GetVersionInfoAsync()
    {
        var branch = await GetCurrentBranchAsync();
        var assembly = System.Reflection.Assembly.GetExecutingAssembly();
        var version = assembly.GetName().Version;

        return new VersionInfo
        {
            Version = version?.ToString() ?? "0.0.0",
            Branch = branch?.Name ?? "unknown",
            CommitHash = branch?.CommitHash,
            CommitDate = branch?.CommitDate,
            BuildDate = File.GetLastWriteTime(assembly.Location).ToString("yyyy-MM-dd HH:mm:ss"),
            IsGitRepo = branch?.IsGitRepo ?? false
        };
    }

    /// <summary>
    /// Check if updates are available
    /// </summary>
    public async Task<UpdateCheckResult> CheckForUpdatesAsync()
    {
        try
        {
            await RunGitCommandAsync("fetch --all");
            
            var localHash = await RunGitCommandAsync("rev-parse HEAD");
            var remoteHash = await RunGitCommandAsync("rev-parse @{u}");
            var behindCount = await RunGitCommandAsync("rev-list HEAD..@{u} --count");

            var updateAvailable = localHash?.Trim() != remoteHash?.Trim();
            int.TryParse(behindCount?.Trim(), out var commitsBehind);

            return new UpdateCheckResult
            {
                Success = true,
                UpdateAvailable = updateAvailable,
                CommitsBehind = commitsBehind,
                LocalCommit = localHash?.Trim(),
                RemoteCommit = remoteHash?.Trim()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check for updates");
            return new UpdateCheckResult
            {
                Success = false,
                Error = ex.Message
            };
        }
    }

    private async Task<string?> RunGitCommandAsync(string arguments)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = arguments,
                WorkingDirectory = _repoPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process == null) return null;

            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            return string.IsNullOrEmpty(output) ? error : output;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Git command failed: {Args}", arguments);
            return null;
        }
    }
}

#region Models

public class BranchInfo
{
    public string Name { get; set; } = string.Empty;
    public string? CommitHash { get; set; }
    public string? CommitDate { get; set; }
    public string? CommitMessage { get; set; }
    public bool IsLocal { get; set; }
    public string? RemoteName { get; set; }
    public bool IsGitRepo { get; set; }
    public string? RepoPath { get; set; }
    public string? Message { get; set; }
}

public class GitHubBranchInfo
{
    public string Name { get; set; } = string.Empty;
    public string? CommitSha { get; set; }
    public bool Protected { get; set; }
}

public class SwitchBranchResult
{
    public bool Success { get; set; }
    public string? PreviousBranch { get; set; }
    public string? CurrentBranch { get; set; }
    public string? Message { get; set; }
    public string? Error { get; set; }
    public bool RequiresRestart { get; set; }
}

public class PullResult
{
    public bool Success { get; set; }
    public bool HadChanges { get; set; }
    public string? Output { get; set; }
    public string? NewCommitHash { get; set; }
    public string? Error { get; set; }
    public bool RequiresRestart { get; set; }
}

public class VersionInfo
{
    public string Version { get; set; } = string.Empty;
    public string Branch { get; set; } = string.Empty;
    public string? CommitHash { get; set; }
    public string? CommitDate { get; set; }
    public string? BuildDate { get; set; }
    public bool IsGitRepo { get; set; }
}

public class UpdateCheckResult
{
    public bool Success { get; set; }
    public bool UpdateAvailable { get; set; }
    public int CommitsBehind { get; set; }
    public string? LocalCommit { get; set; }
    public string? RemoteCommit { get; set; }
    public string? Error { get; set; }
}

#endregion
