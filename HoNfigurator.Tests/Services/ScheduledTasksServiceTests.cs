using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using HoNfigurator.Core.Services;
using Xunit;

namespace HoNfigurator.Tests.Services;

public class ScheduledTasksServiceTests : IDisposable
{
    private readonly Mock<ILogger<ScheduledTasksService>> _loggerMock;

    public ScheduledTasksServiceTests()
    {
        _loggerMock = new Mock<ILogger<ScheduledTasksService>>();
    }

    public void Dispose()
    {
        // Cleanup if needed
    }

    private ScheduledTasksService CreateService()
    {
        return new ScheduledTasksService(_loggerMock.Object);
    }

    #region DTO Tests

    [Fact]
    public void ScheduledTask_DefaultValues()
    {
        // Arrange & Act
        var task = new ScheduledTask();

        // Assert
        task.Id.Should().NotBeNullOrEmpty();
        task.Name.Should().BeEmpty();
        task.Description.Should().BeEmpty();
        task.Schedule.Should().Be("0 * * * *");
        task.TaskType.Should().Be(ScheduledTaskType.Custom);
        task.IsEnabled.Should().BeTrue();
        task.LastRun.Should().BeNull();
        task.RunCount.Should().Be(0);
        task.Parameters.Should().BeNull();
    }

    [Fact]
    public void ScheduledTask_WithValues()
    {
        // Arrange & Act
        var lastRun = DateTime.UtcNow.AddHours(-1);
        var task = new ScheduledTask
        {
            Id = "custom-task-1",
            Name = "Test Task",
            Description = "A test task for unit testing",
            Schedule = "*/5 * * * *",
            TaskType = ScheduledTaskType.CleanupLogs,
            IsEnabled = false,
            LastRun = lastRun,
            RunCount = 10,
            Parameters = new Dictionary<string, object> { ["daysToKeep"] = 7 }
        };

        // Assert
        task.Id.Should().Be("custom-task-1");
        task.Name.Should().Be("Test Task");
        task.Schedule.Should().Be("*/5 * * * *");
        task.TaskType.Should().Be(ScheduledTaskType.CleanupLogs);
        task.IsEnabled.Should().BeFalse();
        task.LastRun.Should().Be(lastRun);
        task.RunCount.Should().Be(10);
        task.Parameters.Should().ContainKey("daysToKeep");
    }

    [Fact]
    public void ScheduledTaskInfo_DefaultValues()
    {
        // Arrange & Act
        var info = new ScheduledTaskInfo();

        // Assert
        info.Id.Should().BeEmpty();
        info.Name.Should().BeEmpty();
        info.Description.Should().BeEmpty();
        info.Schedule.Should().BeEmpty();
        info.IsEnabled.Should().BeFalse();
        info.LastRun.Should().BeNull();
        info.NextRun.Should().Be(default);
        info.RunCount.Should().Be(0);
        info.TaskType.Should().BeEmpty();
    }

    [Fact]
    public void ScheduledTaskInfo_WithValues()
    {
        // Arrange & Act
        var info = new ScheduledTaskInfo
        {
            Id = "task-1",
            Name = "Cleanup Task",
            Description = "Cleans up old files",
            Schedule = "0 3 * * *",
            IsEnabled = true,
            LastRun = DateTime.UtcNow.AddDays(-1),
            NextRun = DateTime.UtcNow.AddHours(5),
            RunCount = 25,
            TaskType = "CleanupLogs"
        };

        // Assert
        info.Name.Should().Be("Cleanup Task");
        info.RunCount.Should().Be(25);
        info.TaskType.Should().Be("CleanupLogs");
    }

    #endregion

    #region TaskType Enum Tests

    [Theory]
    [InlineData(ScheduledTaskType.Custom, 0)]
    [InlineData(ScheduledTaskType.CleanupLogs, 1)]
    [InlineData(ScheduledTaskType.CleanupReplays, 2)]
    [InlineData(ScheduledTaskType.HealthCheck, 3)]
    [InlineData(ScheduledTaskType.RestartIdle, 4)]
    public void ScheduledTaskType_EnumValues(ScheduledTaskType type, int expectedValue)
    {
        // Assert
        ((int)type).Should().Be(expectedValue);
    }

    [Fact]
    public void ScheduledTaskType_AllValuesDefined()
    {
        // Arrange
        var values = Enum.GetValues<ScheduledTaskType>();

        // Assert
        values.Should().HaveCount(5);
        values.Should().Contain(ScheduledTaskType.Custom);
        values.Should().Contain(ScheduledTaskType.CleanupLogs);
        values.Should().Contain(ScheduledTaskType.CleanupReplays);
        values.Should().Contain(ScheduledTaskType.HealthCheck);
        values.Should().Contain(ScheduledTaskType.RestartIdle);
    }

    #endregion

    #region Default Tasks Tests

    [Fact]
    public void GetDefaultTasks_ReturnsExpectedTasks()
    {
        // Act
        var tasks = ScheduledTasksService.GetDefaultTasks();

        // Assert
        tasks.Should().HaveCount(4);
        tasks.Should().Contain(t => t.Id == "cleanup-logs");
        tasks.Should().Contain(t => t.Id == "cleanup-replays");
        tasks.Should().Contain(t => t.Id == "health-check");
        tasks.Should().Contain(t => t.Id == "restart-idle");
    }

    [Fact]
    public void GetDefaultTasks_CleanupLogs_HasCorrectSchedule()
    {
        // Act
        var tasks = ScheduledTasksService.GetDefaultTasks();
        var cleanupLogs = tasks.First(t => t.Id == "cleanup-logs");

        // Assert
        cleanupLogs.Name.Should().Be("Cleanup Old Logs");
        cleanupLogs.Schedule.Should().Be("0 3 * * *"); // 3 AM daily
        cleanupLogs.TaskType.Should().Be(ScheduledTaskType.CleanupLogs);
        cleanupLogs.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void GetDefaultTasks_CleanupReplays_HasCorrectSchedule()
    {
        // Act
        var tasks = ScheduledTasksService.GetDefaultTasks();
        var cleanupReplays = tasks.First(t => t.Id == "cleanup-replays");

        // Assert
        cleanupReplays.Name.Should().Be("Cleanup Old Replays");
        cleanupReplays.Schedule.Should().Be("0 4 * * 0"); // 4 AM Sundays
        cleanupReplays.TaskType.Should().Be(ScheduledTaskType.CleanupReplays);
        cleanupReplays.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void GetDefaultTasks_HealthCheck_HasCorrectSchedule()
    {
        // Act
        var tasks = ScheduledTasksService.GetDefaultTasks();
        var healthCheck = tasks.First(t => t.Id == "health-check");

        // Assert
        healthCheck.Name.Should().Be("Health Check");
        healthCheck.Schedule.Should().Be("*/5 * * * *"); // Every 5 minutes
        healthCheck.TaskType.Should().Be(ScheduledTaskType.HealthCheck);
        healthCheck.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void GetDefaultTasks_RestartIdle_DisabledByDefault()
    {
        // Act
        var tasks = ScheduledTasksService.GetDefaultTasks();
        var restartIdle = tasks.First(t => t.Id == "restart-idle");

        // Assert
        restartIdle.Name.Should().Be("Restart Idle Servers");
        restartIdle.Schedule.Should().Be("0 */6 * * *"); // Every 6 hours
        restartIdle.TaskType.Should().Be(ScheduledTaskType.RestartIdle);
        restartIdle.IsEnabled.Should().BeFalse(); // Disabled by default
    }

    #endregion

    #region Service Initialization Tests

    [Fact]
    public void Constructor_LoadsDefaultTasks()
    {
        // Arrange & Act
        var service = CreateService();

        // Assert
        service.Tasks.Should().HaveCount(4);
        service.IsRunning.Should().BeFalse();
    }

    [Fact]
    public void Constructor_TasksAreReadOnly()
    {
        // Arrange & Act
        var service = CreateService();

        // Assert
        service.Tasks.Should().BeOfType<System.Collections.ObjectModel.ReadOnlyCollection<ScheduledTask>>();
    }

    #endregion

    #region AddTask Tests

    [Fact]
    public void AddTask_IncreasesTaskCount()
    {
        // Arrange
        var service = CreateService();
        var initialCount = service.Tasks.Count;
        var newTask = new ScheduledTask
        {
            Id = "new-task",
            Name = "New Task",
            Schedule = "0 * * * *"
        };

        // Act
        service.AddTask(newTask);

        // Assert
        service.Tasks.Count.Should().Be(initialCount + 1);
    }

    [Fact]
    public void AddTask_TaskIsAccessible()
    {
        // Arrange
        var service = CreateService();
        var newTask = new ScheduledTask
        {
            Id = "custom-task",
            Name = "Custom Task",
            Schedule = "0 12 * * *"
        };

        // Act
        service.AddTask(newTask);

        // Assert
        service.Tasks.Should().Contain(t => t.Id == "custom-task");
    }

    #endregion

    #region RemoveTask Tests

    [Fact]
    public void RemoveTask_ExistingTask_ReturnsTrue()
    {
        // Arrange
        var service = CreateService();

        // Act
        var result = service.RemoveTask("cleanup-logs");

        // Assert
        result.Should().BeTrue();
        service.Tasks.Should().NotContain(t => t.Id == "cleanup-logs");
    }

    [Fact]
    public void RemoveTask_NonExistingTask_ReturnsFalse()
    {
        // Arrange
        var service = CreateService();

        // Act
        var result = service.RemoveTask("non-existing-task");

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void RemoveTask_DecreasesTaskCount()
    {
        // Arrange
        var service = CreateService();
        var initialCount = service.Tasks.Count;

        // Act
        service.RemoveTask("cleanup-logs");

        // Assert
        service.Tasks.Count.Should().Be(initialCount - 1);
    }

    #endregion

    #region GetTasks Tests

    [Fact]
    public void GetTasks_ReturnsAllTasks()
    {
        // Arrange
        var service = CreateService();

        // Act
        var tasks = service.GetTasks();

        // Assert
        tasks.Should().HaveCount(4);
        tasks.Should().AllSatisfy(t =>
        {
            t.Id.Should().NotBeNullOrEmpty();
            t.Name.Should().NotBeNullOrEmpty();
            t.Schedule.Should().NotBeNullOrEmpty();
            t.TaskType.Should().NotBeNullOrEmpty();
        });
    }

    [Fact]
    public void GetTasks_ReturnsTaskInfo_NotTask()
    {
        // Arrange
        var service = CreateService();

        // Act
        var tasks = service.GetTasks();

        // Assert
        tasks.Should().AllBeOfType<ScheduledTaskInfo>();
    }

    #endregion

    #region Start/Stop Tests

    [Fact]
    public void Start_SetsIsRunning()
    {
        // Arrange
        var service = CreateService();

        // Act
        service.Start();

        // Assert
        service.IsRunning.Should().BeTrue();

        // Cleanup
        service.Stop();
    }

    [Fact]
    public void Stop_ClearsIsRunning()
    {
        // Arrange
        var service = CreateService();
        service.Start();

        // Act
        service.Stop();

        // Assert
        service.IsRunning.Should().BeFalse();
    }

    [Fact]
    public void Start_WhenAlreadyRunning_DoesNothing()
    {
        // Arrange
        var service = CreateService();
        service.Start();

        // Act
        service.Start(); // Second call

        // Assert
        service.IsRunning.Should().BeTrue();

        // Cleanup
        service.Stop();
    }

    [Fact]
    public void Stop_WhenNotRunning_DoesNothing()
    {
        // Arrange
        var service = CreateService();

        // Act
        service.Stop();

        // Assert
        service.IsRunning.Should().BeFalse();
    }

    #endregion

    #region SetTaskEnabled Tests

    [Fact]
    public void SetTaskEnabled_DisablesTask()
    {
        // Arrange
        var service = CreateService();

        // Act
        service.SetTaskEnabled("health-check", false);

        // Assert
        var task = service.Tasks.First(t => t.Id == "health-check");
        task.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public void SetTaskEnabled_EnablesTask()
    {
        // Arrange
        var service = CreateService();
        // restart-idle is disabled by default
        var task = service.Tasks.First(t => t.Id == "restart-idle");
        task.IsEnabled.Should().BeFalse();

        // Act
        service.SetTaskEnabled("restart-idle", true);

        // Assert
        task.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void SetTaskEnabled_NonExistingTask_DoesNothing()
    {
        // Arrange
        var service = CreateService();

        // Act & Assert - Should not throw
        service.SetTaskEnabled("non-existing", true);
    }

    [Fact]
    public void SetTaskEnabled_ByName_Works()
    {
        // Arrange
        var service = CreateService();

        // Act
        service.SetTaskEnabled("Health Check", false);

        // Assert
        var task = service.Tasks.First(t => t.Id == "health-check");
        task.IsEnabled.Should().BeFalse();
    }

    #endregion

    #region RunTaskNowAsync Tests

    [Fact]
    public async Task RunTaskNowAsync_ExistingTask_ReturnsTrue()
    {
        // Arrange
        var service = CreateService();

        // Act
        var result = await service.RunTaskNowAsync("health-check");

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task RunTaskNowAsync_NonExistingTask_ReturnsFalse()
    {
        // Arrange
        var service = CreateService();

        // Act
        var result = await service.RunTaskNowAsync("non-existing");

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task RunTaskNowAsync_UpdatesLastRunAndRunCount()
    {
        // Arrange
        var service = CreateService();
        var task = service.Tasks.First(t => t.Id == "health-check");
        var initialRunCount = task.RunCount;

        // Act
        await service.RunTaskNowAsync("health-check");

        // Assert
        task.RunCount.Should().Be(initialRunCount + 1);
        task.LastRun.Should().NotBeNull();
        task.LastRun!.Value.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task RunTaskNowAsync_ByName_Works()
    {
        // Arrange
        var service = CreateService();

        // Act
        var result = await service.RunTaskNowAsync("Health Check");

        // Assert
        result.Should().BeTrue();
    }

    #endregion

    #region IHostedService Tests

    [Fact]
    public async Task StartAsync_StartsService()
    {
        // Arrange
        var service = CreateService();

        // Act
        await service.StartAsync(CancellationToken.None);

        // Assert
        service.IsRunning.Should().BeTrue();

        // Cleanup
        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StopAsync_StopsService()
    {
        // Arrange
        var service = CreateService();
        await service.StartAsync(CancellationToken.None);

        // Act
        await service.StopAsync(CancellationToken.None);

        // Assert
        service.IsRunning.Should().BeFalse();
    }

    #endregion

    #region Dispose Tests

    [Fact]
    public void Dispose_StopsService()
    {
        // Arrange
        var service = CreateService();
        service.Start();

        // Act
        service.Dispose();

        // Assert
        service.IsRunning.Should().BeFalse();
    }

    [Fact]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        // Arrange
        var service = CreateService();

        // Act & Assert - Should not throw
        service.Dispose();
        service.Dispose();
    }

    #endregion

    #region Schedule Parsing Tests

    [Fact]
    public void Task_EveryFiveMinutes_Schedule()
    {
        // Arrange
        var task = new ScheduledTask
        {
            Schedule = "*/5 * * * *"
        };

        // Assert - Verify schedule format
        task.Schedule.Should().StartWith("*/");
        task.Schedule.Split(' ').Should().HaveCount(5);
    }

    [Fact]
    public void Task_DailyAtThreeAM_Schedule()
    {
        // Arrange
        var task = new ScheduledTask
        {
            Schedule = "0 3 * * *"
        };

        // Assert
        var parts = task.Schedule.Split(' ');
        parts[0].Should().Be("0"); // Minute 0
        parts[1].Should().Be("3"); // Hour 3
    }

    [Fact]
    public void Task_WeeklyOnSunday_Schedule()
    {
        // Arrange
        var task = new ScheduledTask
        {
            Schedule = "0 4 * * 0"
        };

        // Assert
        var parts = task.Schedule.Split(' ');
        parts[4].Should().Be("0"); // Sunday
    }

    #endregion

    #region Parameters Tests

    [Fact]
    public void ScheduledTask_Parameters_CanStoreValues()
    {
        // Arrange
        var task = new ScheduledTask
        {
            Parameters = new Dictionary<string, object>
            {
                ["daysToKeep"] = 7,
                ["path"] = "/logs",
                ["enabled"] = true
            }
        };

        // Assert
        task.Parameters!["daysToKeep"].Should().Be(7);
        task.Parameters["path"].Should().Be("/logs");
        task.Parameters["enabled"].Should().Be(true);
    }

    [Fact]
    public void ScheduledTask_Parameters_NullByDefault()
    {
        // Arrange
        var task = new ScheduledTask();

        // Assert
        task.Parameters.Should().BeNull();
    }

    #endregion

    #region Task Collection Operations

    [Fact]
    public void Tasks_CanFilterByEnabled()
    {
        // Arrange
        var service = CreateService();

        // Act
        var enabledTasks = service.Tasks.Where(t => t.IsEnabled).ToList();
        var disabledTasks = service.Tasks.Where(t => !t.IsEnabled).ToList();

        // Assert
        enabledTasks.Should().HaveCount(3); // cleanup-logs, cleanup-replays, health-check
        disabledTasks.Should().HaveCount(1); // restart-idle
    }

    [Fact]
    public void Tasks_CanGroupByType()
    {
        // Arrange
        var service = CreateService();

        // Act
        var grouped = service.Tasks.GroupBy(t => t.TaskType).ToList();

        // Assert
        grouped.Should().HaveCount(4); // Each task has different type
    }

    #endregion
}
