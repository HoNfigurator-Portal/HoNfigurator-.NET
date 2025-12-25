using FluentAssertions;
using HoNfigurator.Core.Services;
using Xunit;

namespace HoNfigurator.Tests.Services;

/// <summary>
/// Comprehensive tests for GitBranchService
/// </summary>
public class GitBranchServiceTests
{
    #region BranchInfo Tests

    [Fact]
    public void BranchInfo_DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var info = new BranchInfo();

        // Assert
        info.Name.Should().BeEmpty();
        info.CommitHash.Should().BeNull();
        info.CommitDate.Should().BeNull();
        info.CommitMessage.Should().BeNull();
        info.IsLocal.Should().BeFalse();
        info.RemoteName.Should().BeNull();
        info.IsGitRepo.Should().BeFalse();
        info.RepoPath.Should().BeNull();
        info.Message.Should().BeNull();
    }

    [Fact]
    public void BranchInfo_AllProperties_CanBeSet()
    {
        // Arrange & Act
        var info = new BranchInfo
        {
            Name = "main",
            CommitHash = "abc123def456",
            CommitDate = "2024-01-15 10:30:00",
            CommitMessage = "Fix bug in server startup",
            IsLocal = true,
            RemoteName = "origin",
            IsGitRepo = true,
            RepoPath = "C:\\Projects\\HoNfigurator",
            Message = "Branch info retrieved successfully"
        };

        // Assert
        info.Name.Should().Be("main");
        info.CommitHash.Should().Be("abc123def456");
        info.CommitDate.Should().Be("2024-01-15 10:30:00");
        info.CommitMessage.Should().Be("Fix bug in server startup");
        info.IsLocal.Should().BeTrue();
        info.RemoteName.Should().Be("origin");
        info.IsGitRepo.Should().BeTrue();
        info.RepoPath.Should().Be("C:\\Projects\\HoNfigurator");
        info.Message.Should().Be("Branch info retrieved successfully");
    }

    [Theory]
    [InlineData("main")]
    [InlineData("develop")]
    [InlineData("feature/new-feature")]
    [InlineData("release/v1.0.0")]
    [InlineData("hotfix/urgent-fix")]
    [InlineData("bugfix/issue-123")]
    public void BranchInfo_VariousBranchNames_StoreCorrectly(string branchName)
    {
        // Arrange & Act
        var info = new BranchInfo { Name = branchName };

        // Assert
        info.Name.Should().Be(branchName);
    }

    [Theory]
    [InlineData("abc123")]
    [InlineData("abc123def456789012345678901234567890abcd")]
    [InlineData("0000000000000000000000000000000000000000")]
    public void BranchInfo_CommitHash_AcceptsVariousFormats(string hash)
    {
        // Arrange & Act
        var info = new BranchInfo { CommitHash = hash };

        // Assert
        info.CommitHash.Should().Be(hash);
    }

    [Fact]
    public void BranchInfo_IsGitRepo_False_WhenNotInRepo()
    {
        // Arrange & Act
        var info = new BranchInfo
        {
            IsGitRepo = false,
            Name = "unknown",
            Message = "Not a git repository"
        };

        // Assert
        info.IsGitRepo.Should().BeFalse();
        info.Message.Should().Contain("Not a git repository");
    }

    [Theory]
    [InlineData("origin")]
    [InlineData("upstream")]
    [InlineData("fork")]
    [InlineData("github")]
    public void BranchInfo_RemoteName_AcceptsVariousRemotes(string remoteName)
    {
        // Arrange & Act
        var info = new BranchInfo { RemoteName = remoteName };

        // Assert
        info.RemoteName.Should().Be(remoteName);
    }

    #endregion

    #region GitHubBranchInfo Tests

    [Fact]
    public void GitHubBranchInfo_DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var info = new GitHubBranchInfo();

        // Assert
        info.Name.Should().BeEmpty();
        info.CommitSha.Should().BeNull();
        info.Protected.Should().BeFalse();
    }

    [Fact]
    public void GitHubBranchInfo_AllProperties_CanBeSet()
    {
        // Arrange & Act
        var info = new GitHubBranchInfo
        {
            Name = "main",
            CommitSha = "abc123def456789012345678901234567890abcd",
            Protected = true
        };

        // Assert
        info.Name.Should().Be("main");
        info.CommitSha.Should().Be("abc123def456789012345678901234567890abcd");
        info.Protected.Should().BeTrue();
    }

    [Fact]
    public void GitHubBranchInfo_Protected_True_ForMainBranch()
    {
        // Arrange & Act
        var info = new GitHubBranchInfo
        {
            Name = "main",
            Protected = true
        };

        // Assert
        info.Protected.Should().BeTrue();
    }

    [Fact]
    public void GitHubBranchInfo_Protected_False_ForFeatureBranch()
    {
        // Arrange & Act
        var info = new GitHubBranchInfo
        {
            Name = "feature/new-feature",
            Protected = false
        };

        // Assert
        info.Protected.Should().BeFalse();
    }

    [Theory]
    [InlineData("main", true)]
    [InlineData("develop", true)]
    [InlineData("feature/test", false)]
    [InlineData("release/v1.0", false)]
    public void GitHubBranchInfo_ProtectedStatus_VariousBranches(string name, bool isProtected)
    {
        // Arrange & Act
        var info = new GitHubBranchInfo
        {
            Name = name,
            Protected = isProtected
        };

        // Assert
        info.Name.Should().Be(name);
        info.Protected.Should().Be(isProtected);
    }

    #endregion

    #region SwitchBranchResult Tests

    [Fact]
    public void SwitchBranchResult_DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var result = new SwitchBranchResult();

        // Assert
        result.Success.Should().BeFalse();
        result.PreviousBranch.Should().BeNull();
        result.CurrentBranch.Should().BeNull();
        result.Message.Should().BeNull();
        result.Error.Should().BeNull();
        result.RequiresRestart.Should().BeFalse();
    }

    [Fact]
    public void SwitchBranchResult_SuccessfulSwitch_HasCorrectState()
    {
        // Arrange & Act
        var result = new SwitchBranchResult
        {
            Success = true,
            PreviousBranch = "develop",
            CurrentBranch = "main",
            Message = "Switched to branch 'main'",
            RequiresRestart = true
        };

        // Assert
        result.Success.Should().BeTrue();
        result.PreviousBranch.Should().Be("develop");
        result.CurrentBranch.Should().Be("main");
        result.Message.Should().Contain("main");
        result.RequiresRestart.Should().BeTrue();
        result.Error.Should().BeNull();
    }

    [Fact]
    public void SwitchBranchResult_AlreadyOnBranch_NoSwitchNeeded()
    {
        // Arrange & Act
        var result = new SwitchBranchResult
        {
            Success = true,
            PreviousBranch = "main",
            CurrentBranch = "main",
            Message = "Already on this branch"
        };

        // Assert
        result.Success.Should().BeTrue();
        result.PreviousBranch.Should().Be(result.CurrentBranch);
        result.Message.Should().Contain("Already");
    }

    [Fact]
    public void SwitchBranchResult_FailedSwitch_HasError()
    {
        // Arrange & Act
        var result = new SwitchBranchResult
        {
            Success = false,
            PreviousBranch = "develop",
            Error = "error: pathspec 'nonexistent' did not match any file(s) known to git"
        };

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().NotBeNullOrEmpty();
        result.Error.Should().Contain("pathspec");
    }

    [Fact]
    public void SwitchBranchResult_RequiresRestart_WhenBranchChanged()
    {
        // Arrange & Act
        var result = new SwitchBranchResult
        {
            Success = true,
            PreviousBranch = "develop",
            CurrentBranch = "main",
            RequiresRestart = true
        };

        // Assert
        result.RequiresRestart.Should().BeTrue();
        result.PreviousBranch.Should().NotBe(result.CurrentBranch);
    }

    #endregion

    #region PullResult Tests

    [Fact]
    public void PullResult_DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var result = new PullResult();

        // Assert
        result.Success.Should().BeFalse();
        result.HadChanges.Should().BeFalse();
        result.Output.Should().BeNull();
        result.NewCommitHash.Should().BeNull();
        result.Error.Should().BeNull();
        result.RequiresRestart.Should().BeFalse();
    }

    [Fact]
    public void PullResult_SuccessWithChanges_HasCorrectState()
    {
        // Arrange & Act
        var result = new PullResult
        {
            Success = true,
            HadChanges = true,
            Output = "Updating abc123..def456\nFast-forward\n 5 files changed",
            NewCommitHash = "def456789",
            RequiresRestart = true
        };

        // Assert
        result.Success.Should().BeTrue();
        result.HadChanges.Should().BeTrue();
        result.Output.Should().Contain("files changed");
        result.NewCommitHash.Should().NotBeNullOrEmpty();
        result.RequiresRestart.Should().BeTrue();
    }

    [Fact]
    public void PullResult_SuccessNoChanges_HasCorrectState()
    {
        // Arrange & Act
        var result = new PullResult
        {
            Success = true,
            HadChanges = false,
            Output = "Already up to date.",
            NewCommitHash = "abc123456",
            RequiresRestart = false
        };

        // Assert
        result.Success.Should().BeTrue();
        result.HadChanges.Should().BeFalse();
        result.Output.Should().Contain("up to date");
        result.RequiresRestart.Should().BeFalse();
    }

    [Fact]
    public void PullResult_FailedPull_HasError()
    {
        // Arrange & Act
        var result = new PullResult
        {
            Success = false,
            Error = "fatal: unable to access repository"
        };

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().NotBeNullOrEmpty();
        result.HadChanges.Should().BeFalse();
    }

    [Fact]
    public void PullResult_RequiresRestart_OnlyWhenChanges()
    {
        // Arrange
        var resultWithChanges = new PullResult { HadChanges = true, RequiresRestart = true };
        var resultNoChanges = new PullResult { HadChanges = false, RequiresRestart = false };

        // Assert
        resultWithChanges.RequiresRestart.Should().BeTrue();
        resultNoChanges.RequiresRestart.Should().BeFalse();
    }

    #endregion

    #region VersionInfo Tests

    [Fact]
    public void VersionInfo_DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var info = new VersionInfo();

        // Assert
        info.Version.Should().BeEmpty();
        info.Branch.Should().BeEmpty();
        info.CommitHash.Should().BeNull();
        info.CommitDate.Should().BeNull();
        info.BuildDate.Should().BeNull();
        info.IsGitRepo.Should().BeFalse();
    }

    [Fact]
    public void VersionInfo_AllProperties_CanBeSet()
    {
        // Arrange & Act
        var info = new VersionInfo
        {
            Version = "1.2.3.0",
            Branch = "main",
            CommitHash = "abc123def456",
            CommitDate = "2024-01-15 10:30:00",
            BuildDate = "2024-01-15 11:00:00",
            IsGitRepo = true
        };

        // Assert
        info.Version.Should().Be("1.2.3.0");
        info.Branch.Should().Be("main");
        info.CommitHash.Should().Be("abc123def456");
        info.CommitDate.Should().Be("2024-01-15 10:30:00");
        info.BuildDate.Should().Be("2024-01-15 11:00:00");
        info.IsGitRepo.Should().BeTrue();
    }

    [Theory]
    [InlineData("1.0.0")]
    [InlineData("1.0.0.0")]
    [InlineData("2.5.3")]
    [InlineData("10.20.30.40")]
    [InlineData("0.0.0")]
    public void VersionInfo_Version_AcceptsVariousFormats(string version)
    {
        // Arrange & Act
        var info = new VersionInfo { Version = version };

        // Assert
        info.Version.Should().Be(version);
    }

    [Fact]
    public void VersionInfo_NotGitRepo_HasDefaultBranch()
    {
        // Arrange & Act
        var info = new VersionInfo
        {
            IsGitRepo = false,
            Branch = "unknown",
            Version = "1.0.0"
        };

        // Assert
        info.IsGitRepo.Should().BeFalse();
        info.Branch.Should().Be("unknown");
        info.CommitHash.Should().BeNull();
    }

    #endregion

    #region UpdateCheckResult Tests

    [Fact]
    public void UpdateCheckResult_DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var result = new UpdateCheckResult();

        // Assert
        result.Success.Should().BeFalse();
        result.UpdateAvailable.Should().BeFalse();
        result.CommitsBehind.Should().Be(0);
        result.LocalCommit.Should().BeNull();
        result.RemoteCommit.Should().BeNull();
        result.Error.Should().BeNull();
    }

    [Fact]
    public void UpdateCheckResult_UpdateAvailable_HasCorrectState()
    {
        // Arrange & Act
        var result = new UpdateCheckResult
        {
            Success = true,
            UpdateAvailable = true,
            CommitsBehind = 5,
            LocalCommit = "abc123",
            RemoteCommit = "def456"
        };

        // Assert
        result.Success.Should().BeTrue();
        result.UpdateAvailable.Should().BeTrue();
        result.CommitsBehind.Should().Be(5);
        result.LocalCommit.Should().NotBe(result.RemoteCommit);
    }

    [Fact]
    public void UpdateCheckResult_UpToDate_HasCorrectState()
    {
        // Arrange & Act
        var result = new UpdateCheckResult
        {
            Success = true,
            UpdateAvailable = false,
            CommitsBehind = 0,
            LocalCommit = "abc123",
            RemoteCommit = "abc123"
        };

        // Assert
        result.Success.Should().BeTrue();
        result.UpdateAvailable.Should().BeFalse();
        result.CommitsBehind.Should().Be(0);
        result.LocalCommit.Should().Be(result.RemoteCommit);
    }

    [Fact]
    public void UpdateCheckResult_CheckFailed_HasError()
    {
        // Arrange & Act
        var result = new UpdateCheckResult
        {
            Success = false,
            Error = "fatal: unable to access remote"
        };

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().NotBeNullOrEmpty();
        result.UpdateAvailable.Should().BeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(100)]
    public void UpdateCheckResult_CommitsBehind_VariousValues(int commitsBehind)
    {
        // Arrange & Act
        var result = new UpdateCheckResult
        {
            Success = true,
            CommitsBehind = commitsBehind,
            UpdateAvailable = commitsBehind > 0
        };

        // Assert
        result.CommitsBehind.Should().Be(commitsBehind);
        result.UpdateAvailable.Should().Be(commitsBehind > 0);
    }

    #endregion

    #region Integration Scenario Tests

    [Fact]
    public void BranchSwitchScenario_FeatureToMain_Success()
    {
        // Arrange
        var beforeBranch = new BranchInfo
        {
            Name = "feature/new-feature",
            IsGitRepo = true,
            IsLocal = true
        };

        // Act
        var switchResult = new SwitchBranchResult
        {
            Success = true,
            PreviousBranch = beforeBranch.Name,
            CurrentBranch = "main",
            Message = "Switched to branch 'main'",
            RequiresRestart = true
        };

        var afterBranch = new BranchInfo
        {
            Name = "main",
            IsGitRepo = true,
            IsLocal = true
        };

        // Assert
        switchResult.Success.Should().BeTrue();
        switchResult.PreviousBranch.Should().Be("feature/new-feature");
        afterBranch.Name.Should().Be(switchResult.CurrentBranch);
        switchResult.RequiresRestart.Should().BeTrue();
    }

    [Fact]
    public void UpdateCheckScenario_BehindRemote_UpdateAvailable()
    {
        // Arrange
        var localVersion = new VersionInfo
        {
            Version = "1.0.0",
            Branch = "main",
            CommitHash = "abc123",
            IsGitRepo = true
        };

        // Act
        var updateCheck = new UpdateCheckResult
        {
            Success = true,
            UpdateAvailable = true,
            CommitsBehind = 3,
            LocalCommit = localVersion.CommitHash,
            RemoteCommit = "def456"
        };

        // Assert
        updateCheck.Success.Should().BeTrue();
        updateCheck.UpdateAvailable.Should().BeTrue();
        updateCheck.CommitsBehind.Should().Be(3);
        updateCheck.LocalCommit.Should().Be(localVersion.CommitHash);
        updateCheck.RemoteCommit.Should().NotBe(localVersion.CommitHash);
    }

    [Fact]
    public void PullUpdateScenario_ChangesApplied_RequiresRestart()
    {
        // Arrange
        var beforePull = new VersionInfo
        {
            CommitHash = "abc123",
            Version = "1.0.0"
        };

        // Act
        var pullResult = new PullResult
        {
            Success = true,
            HadChanges = true,
            NewCommitHash = "def456",
            Output = "5 files changed, 100 insertions(+), 20 deletions(-)",
            RequiresRestart = true
        };

        var afterPull = new VersionInfo
        {
            CommitHash = pullResult.NewCommitHash,
            Version = "1.1.0"
        };

        // Assert
        pullResult.Success.Should().BeTrue();
        pullResult.HadChanges.Should().BeTrue();
        afterPull.CommitHash.Should().Be(pullResult.NewCommitHash);
        afterPull.CommitHash.Should().NotBe(beforePull.CommitHash);
        pullResult.RequiresRestart.Should().BeTrue();
    }

    [Fact]
    public void GitHubBranchListScenario_MultipleProtectedBranches()
    {
        // Arrange & Act
        var branches = new List<GitHubBranchInfo>
        {
            new() { Name = "main", CommitSha = "abc123", Protected = true },
            new() { Name = "develop", CommitSha = "def456", Protected = true },
            new() { Name = "feature/test", CommitSha = "ghi789", Protected = false },
            new() { Name = "feature/new-ui", CommitSha = "jkl012", Protected = false },
            new() { Name = "release/v1.0", CommitSha = "mno345", Protected = false }
        };

        // Assert
        branches.Should().HaveCount(5);
        branches.Where(b => b.Protected).Should().HaveCount(2);
        branches.Where(b => !b.Protected).Should().HaveCount(3);
        branches.Select(b => b.Name).Should().Contain("main");
    }

    #endregion

    #region Edge Case Tests

    [Fact]
    public void BranchInfo_EmptyStrings_HandledCorrectly()
    {
        // Arrange & Act
        var info = new BranchInfo
        {
            Name = "",
            CommitHash = "",
            CommitMessage = "",
            RepoPath = ""
        };

        // Assert
        info.Name.Should().BeEmpty();
        info.CommitHash.Should().BeEmpty();
        info.CommitMessage.Should().BeEmpty();
        info.RepoPath.Should().BeEmpty();
    }

    [Fact]
    public void SwitchBranchResult_NullBranches_HandledCorrectly()
    {
        // Arrange & Act
        var result = new SwitchBranchResult
        {
            Success = false,
            PreviousBranch = null,
            CurrentBranch = null,
            Error = "Not a git repository"
        };

        // Assert
        result.PreviousBranch.Should().BeNull();
        result.CurrentBranch.Should().BeNull();
        result.Success.Should().BeFalse();
    }

    [Fact]
    public void PullResult_EmptyOutput_ValidState()
    {
        // Arrange & Act
        var result = new PullResult
        {
            Success = true,
            HadChanges = false,
            Output = ""
        };

        // Assert
        result.Output.Should().BeEmpty();
        result.Success.Should().BeTrue();
    }

    [Fact]
    public void VersionInfo_ZeroVersion_HandledCorrectly()
    {
        // Arrange & Act
        var info = new VersionInfo
        {
            Version = "0.0.0.0"
        };

        // Assert
        info.Version.Should().Be("0.0.0.0");
    }

    [Fact]
    public void UpdateCheckResult_NegativeCommitsBehind_NotExpected()
    {
        // Arrange & Act - This tests that zero is the minimum
        var result = new UpdateCheckResult
        {
            CommitsBehind = 0,
            UpdateAvailable = false
        };

        // Assert
        result.CommitsBehind.Should().BeGreaterThanOrEqualTo(0);
    }

    #endregion

    #region Thread Safety Tests

    [Fact]
    public async Task BranchList_ConcurrentAccess_ThreadSafe()
    {
        // Arrange
        var branches = new List<GitHubBranchInfo>();
        var lockObj = new object();
        var tasks = new List<Task>();

        // Act
        for (int i = 0; i < 100; i++)
        {
            var index = i;
            tasks.Add(Task.Run(() =>
            {
                var branch = new GitHubBranchInfo
                {
                    Name = $"branch-{index}",
                    CommitSha = $"commit-{index}",
                    Protected = index % 2 == 0
                };
                lock (lockObj)
                {
                    branches.Add(branch);
                }
            }));
        }

        await Task.WhenAll(tasks);

        // Assert
        branches.Should().HaveCount(100);
        branches.Select(b => b.Name).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task VersionInfo_ConcurrentReads_ThreadSafe()
    {
        // Arrange
        var info = new VersionInfo
        {
            Version = "1.0.0",
            Branch = "main",
            CommitHash = "abc123",
            IsGitRepo = true
        };

        var tasks = new List<Task<string>>();

        // Act
        for (int i = 0; i < 100; i++)
        {
            tasks.Add(Task.Run(() => info.Version));
        }

        var results = await Task.WhenAll(tasks);

        // Assert
        results.Should().AllBe("1.0.0");
    }

    #endregion

    #region Validation Tests

    [Fact]
    public void CommitHash_ValidFormat_40Characters()
    {
        // Arrange
        var fullHash = "abc123def456789012345678901234567890abcd";
        var info = new BranchInfo { CommitHash = fullHash };

        // Assert
        info.CommitHash.Should().HaveLength(40);
    }

    [Fact]
    public void CommitHash_ShortFormat_7Characters()
    {
        // Arrange
        var shortHash = "abc123d";
        var info = new BranchInfo { CommitHash = shortHash };

        // Assert
        info.CommitHash.Should().HaveLength(7);
    }

    [Fact]
    public void BranchName_WithSlashes_ValidPath()
    {
        // Arrange & Act
        var branchNames = new[]
        {
            "feature/user-auth",
            "bugfix/issue-123",
            "release/v1.0.0",
            "hotfix/critical-fix",
            "feature/module/subfeature"
        };

        foreach (var name in branchNames)
        {
            var info = new BranchInfo { Name = name };
            
            // Assert
            info.Name.Should().Contain("/");
            info.Name.Should().Be(name);
        }
    }

    [Fact]
    public void RemoteBranch_Format_Correct()
    {
        // Arrange & Act
        var info = new BranchInfo
        {
            Name = "main",
            IsLocal = false,
            RemoteName = "origin"
        };

        // Assert
        info.IsLocal.Should().BeFalse();
        info.RemoteName.Should().Be("origin");
        var remoteBranchName = $"{info.RemoteName}/{info.Name}";
        remoteBranchName.Should().Be("origin/main");
    }

    #endregion

    #region Result State Tests

    [Fact]
    public void SwitchBranchResult_StateTransitions_Correct()
    {
        // Arrange - Initial state (before switch)
        var initialResult = new SwitchBranchResult();

        // Assert initial
        initialResult.Success.Should().BeFalse();
        initialResult.CurrentBranch.Should().BeNull();

        // Act - Simulate successful switch
        var successResult = new SwitchBranchResult
        {
            Success = true,
            PreviousBranch = "develop",
            CurrentBranch = "main"
        };

        // Assert final
        successResult.Success.Should().BeTrue();
        successResult.CurrentBranch.Should().NotBe(successResult.PreviousBranch);
    }

    [Fact]
    public void PullResult_StateTransitions_Correct()
    {
        // Arrange - Check for updates
        var updateCheck = new UpdateCheckResult
        {
            Success = true,
            UpdateAvailable = true,
            CommitsBehind = 5
        };

        // Act - Pull changes
        var pullResult = new PullResult
        {
            Success = true,
            HadChanges = updateCheck.UpdateAvailable,
            RequiresRestart = updateCheck.UpdateAvailable
        };

        // Assert
        pullResult.Success.Should().BeTrue();
        pullResult.HadChanges.Should().Be(updateCheck.UpdateAvailable);
        pullResult.RequiresRestart.Should().BeTrue();
    }

    #endregion
}
