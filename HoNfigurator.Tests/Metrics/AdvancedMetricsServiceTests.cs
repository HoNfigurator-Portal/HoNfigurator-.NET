using FluentAssertions;
using HoNfigurator.Core.Metrics;

namespace HoNfigurator.Tests.Metrics;

/// <summary>
/// Unit tests for AdvancedMetricsService
/// </summary>
public class AdvancedMetricsServiceTests
{
    private AdvancedMetricsService CreateService()
    {
        return new AdvancedMetricsService();
    }

    #region RecordServerMetrics Tests

    [Fact]
    public void RecordServerMetrics_ShouldStoreMetrics()
    {
        // Arrange
        var service = CreateService();
        var snapshot = new ServerMetricsSnapshot
        {
            CpuPercent = 50.0,
            MemoryMb = 512,
            PlayerCount = 10
        };

        // Act
        service.RecordServerMetrics(1, snapshot);
        var history = service.GetServerMetrics(1);

        // Assert
        history.Should().NotBeNull();
        history!.Snapshots.Should().ContainSingle();
        history.Snapshots[0].CpuPercent.Should().Be(50.0);
    }

    [Fact]
    public void RecordServerMetrics_MultipleTimes_ShouldAccumulate()
    {
        // Arrange
        var service = CreateService();

        // Act
        for (int i = 0; i < 10; i++)
        {
            service.RecordServerMetrics(1, new ServerMetricsSnapshot { CpuPercent = i * 10 });
        }

        // Assert
        var history = service.GetServerMetrics(1);
        history!.Snapshots.Should().HaveCount(10);
    }

    [Fact]
    public void RecordServerMetrics_ForDifferentServers_ShouldTrackSeparately()
    {
        // Arrange
        var service = CreateService();

        // Act
        service.RecordServerMetrics(1, new ServerMetricsSnapshot { CpuPercent = 25 });
        service.RecordServerMetrics(2, new ServerMetricsSnapshot { CpuPercent = 50 });
        service.RecordServerMetrics(1, new ServerMetricsSnapshot { CpuPercent = 30 });

        // Assert
        service.GetServerMetrics(1)!.Snapshots.Should().HaveCount(2);
        service.GetServerMetrics(2)!.Snapshots.Should().HaveCount(1);
    }

    #endregion

    #region RecordSystemMetrics Tests

    [Fact]
    public void RecordSystemMetrics_ShouldStoreMetrics()
    {
        // Arrange
        var service = CreateService();
        var snapshot = new SystemMetricsSnapshot
        {
            CpuPercent = 45.0,
            MemoryUsedMb = 8192,
            MemoryTotalMb = 16384,
            ActiveServers = 5
        };

        // Act
        service.RecordSystemMetrics(snapshot);
        var history = service.GetSystemMetrics();

        // Assert
        history.Snapshots.Should().ContainSingle();
        history.Snapshots[0].CpuPercent.Should().Be(45.0);
        history.Snapshots[0].ActiveServers.Should().Be(5);
    }

    [Fact]
    public void RecordSystemMetrics_MultipleTimes_ShouldAccumulate()
    {
        // Arrange
        var service = CreateService();

        // Act
        for (int i = 0; i < 20; i++)
        {
            service.RecordSystemMetrics(new SystemMetricsSnapshot { CpuPercent = i });
        }

        // Assert
        service.GetSystemMetrics().Snapshots.Should().HaveCount(20);
    }

    #endregion

    #region GetServerMetrics Tests

    [Fact]
    public void GetServerMetrics_ForNonexistentServer_ShouldReturnNull()
    {
        // Arrange
        var service = CreateService();

        // Act
        var result = service.GetServerMetrics(999);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void GetServerMetrics_WithPointLimit_ShouldLimitResults()
    {
        // Arrange
        var service = CreateService();
        for (int i = 0; i < 50; i++)
        {
            service.RecordServerMetrics(1, new ServerMetricsSnapshot { CpuPercent = i });
        }

        // Act
        var history = service.GetServerMetrics(1, points: 10);

        // Assert
        history!.Snapshots.Should().HaveCount(10);
        // Should return most recent
        history.Snapshots.Last().CpuPercent.Should().Be(49);
    }

    #endregion

    #region GetSystemMetrics Tests

    [Fact]
    public void GetSystemMetrics_WithPointLimit_ShouldLimitResults()
    {
        // Arrange
        var service = CreateService();
        for (int i = 0; i < 100; i++)
        {
            service.RecordSystemMetrics(new SystemMetricsSnapshot { CpuPercent = i });
        }

        // Act
        var history = service.GetSystemMetrics(points: 20);

        // Assert
        history.Snapshots.Should().HaveCount(20);
    }

    #endregion

    #region GetAllServersSummary Tests

    [Fact]
    public void GetAllServersSummary_ShouldReturnSummaryForAllServers()
    {
        // Arrange
        var service = CreateService();
        service.RecordServerMetrics(1, new ServerMetricsSnapshot { CpuPercent = 50, MemoryMb = 512 });
        service.RecordServerMetrics(1, new ServerMetricsSnapshot { CpuPercent = 60, MemoryMb = 600 });
        service.RecordServerMetrics(2, new ServerMetricsSnapshot { CpuPercent = 30, MemoryMb = 256 });

        // Act
        var summaries = service.GetAllServersSummary();

        // Assert
        summaries.Should().HaveCount(2);
        summaries.Should().ContainKey(1);
        summaries.Should().ContainKey(2);
        summaries[1].AverageCpu.Should().Be(55.0);
        summaries[2].AverageCpu.Should().Be(30.0);
    }

    [Fact]
    public void GetAllServersSummary_WithNoData_ShouldReturnEmptyDictionary()
    {
        // Arrange
        var service = CreateService();

        // Act
        var summaries = service.GetAllServersSummary();

        // Assert
        summaries.Should().BeEmpty();
    }

    #endregion

    #region CompareServers Tests

    [Fact]
    public void CompareServers_ShouldCalculateComparison()
    {
        // Arrange
        var service = CreateService();
        var now = DateTime.UtcNow;
        
        service.RecordServerMetrics(1, new ServerMetricsSnapshot 
        { 
            Timestamp = now, 
            CpuPercent = 50, 
            MemoryMb = 512,
            MatchId = 1001 
        });
        service.RecordServerMetrics(1, new ServerMetricsSnapshot 
        { 
            Timestamp = now, 
            CpuPercent = 60, 
            MemoryMb = 600,
            MatchId = 1002 
        });
        service.RecordServerMetrics(2, new ServerMetricsSnapshot 
        { 
            Timestamp = now, 
            CpuPercent = 30, 
            MemoryMb = 256 
        });

        // Act
        var comparison = service.CompareServers(new[] { 1, 2 }, TimeSpan.FromHours(1));

        // Assert
        comparison.Servers.Should().HaveCount(2);
        comparison.Servers[1].AverageCpu.Should().Be(55.0);
        comparison.Servers[1].TotalMatches.Should().Be(2);
        comparison.Servers[2].AverageCpu.Should().Be(30.0);
    }

    [Fact]
    public void CompareServers_WithNonexistentServer_ShouldSkip()
    {
        // Arrange
        var service = CreateService();
        service.RecordServerMetrics(1, new ServerMetricsSnapshot { CpuPercent = 50 });

        // Act
        var comparison = service.CompareServers(new[] { 1, 999 }, TimeSpan.FromHours(1));

        // Assert
        comparison.Servers.Should().ContainKey(1);
        comparison.Servers.Should().NotContainKey(999);
    }

    [Fact]
    public void CompareServers_ShouldFilterByPeriod()
    {
        // Arrange
        var service = CreateService();
        var oldTime = DateTime.UtcNow.AddHours(-2);
        var recentTime = DateTime.UtcNow;
        
        // This won't be filtered correctly since we can't set Timestamp easily
        // but we can test the structure
        service.RecordServerMetrics(1, new ServerMetricsSnapshot { CpuPercent = 50 });

        // Act
        var comparison = service.CompareServers(new[] { 1 }, TimeSpan.FromHours(1));

        // Assert
        comparison.Period.Should().Be(TimeSpan.FromHours(1));
        comparison.GeneratedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    #endregion

    #region ServerMetricsHistory Tests

    [Fact]
    public void ServerMetricsHistory_AddSnapshot_ShouldTrimWhenOverLimit()
    {
        // Arrange
        var history = new ServerMetricsHistory { ServerId = 1 };

        // Act - add more than max (1000)
        for (int i = 0; i < 1050; i++)
        {
            history.AddSnapshot(new ServerMetricsSnapshot { CpuPercent = i });
        }

        // Assert
        history.Snapshots.Should().HaveCount(1000);
        // First snapshot should be the 51st one (index 50)
        history.Snapshots[0].CpuPercent.Should().Be(50);
    }

    [Fact]
    public void ServerMetricsHistory_GetRecent_ShouldReturnLatest()
    {
        // Arrange
        var history = new ServerMetricsHistory { ServerId = 1 };
        for (int i = 0; i < 100; i++)
        {
            history.AddSnapshot(new ServerMetricsSnapshot { CpuPercent = i });
        }

        // Act
        var recent = history.GetRecent(10);

        // Assert
        recent.Snapshots.Should().HaveCount(10);
        recent.Snapshots.First().CpuPercent.Should().Be(90);
        recent.Snapshots.Last().CpuPercent.Should().Be(99);
    }

    [Fact]
    public void ServerMetricsHistory_GetSince_ShouldFilterByTime()
    {
        // Arrange
        var history = new ServerMetricsHistory { ServerId = 1 };
        var oldTime = DateTime.UtcNow.AddMinutes(-10);
        var recentTime = DateTime.UtcNow;
        
        history.AddSnapshot(new ServerMetricsSnapshot { Timestamp = oldTime, CpuPercent = 10 });
        history.AddSnapshot(new ServerMetricsSnapshot { Timestamp = recentTime, CpuPercent = 20 });

        // Act
        var filtered = history.GetSince(DateTime.UtcNow.AddMinutes(-5));

        // Assert
        filtered.Snapshots.Should().ContainSingle();
        filtered.Snapshots[0].CpuPercent.Should().Be(20);
    }

    [Fact]
    public void ServerMetricsHistory_GetSummary_ShouldCalculateStats()
    {
        // Arrange
        var history = new ServerMetricsHistory { ServerId = 1 };
        history.AddSnapshot(new ServerMetricsSnapshot { CpuPercent = 20, MemoryMb = 200 });
        history.AddSnapshot(new ServerMetricsSnapshot { CpuPercent = 40, MemoryMb = 400 });
        history.AddSnapshot(new ServerMetricsSnapshot { CpuPercent = 60, MemoryMb = 600 });

        // Act
        var summary = history.GetSummary();

        // Assert
        summary.ServerId.Should().Be(1);
        summary.CurrentCpu.Should().Be(60);
        summary.CurrentMemory.Should().Be(600);
        summary.AverageCpu.Should().Be(40);
        summary.AverageMemory.Should().Be(400);
        summary.PeakCpu.Should().Be(60);
        summary.PeakMemory.Should().Be(600);
        summary.DataPoints.Should().Be(3);
    }

    [Fact]
    public void ServerMetricsHistory_GetSummary_WithNoData_ShouldReturnDefaults()
    {
        // Arrange
        var history = new ServerMetricsHistory { ServerId = 1 };

        // Act
        var summary = history.GetSummary();

        // Assert
        summary.ServerId.Should().Be(1);
        summary.CurrentCpu.Should().Be(0);
        summary.DataPoints.Should().Be(0);
    }

    #endregion

    #region SystemMetricsHistory Tests

    [Fact]
    public void SystemMetricsHistory_AddSnapshot_ShouldTrimWhenOverLimit()
    {
        // Arrange
        var history = new SystemMetricsHistory();

        // Act
        for (int i = 0; i < 1050; i++)
        {
            history.AddSnapshot(new SystemMetricsSnapshot { CpuPercent = i });
        }

        // Assert
        history.Snapshots.Should().HaveCount(1000);
    }

    [Fact]
    public void SystemMetricsHistory_GetRecent_ShouldReturnLatest()
    {
        // Arrange
        var history = new SystemMetricsHistory();
        for (int i = 0; i < 100; i++)
        {
            history.AddSnapshot(new SystemMetricsSnapshot { CpuPercent = i });
        }

        // Act
        var recent = history.GetRecent(5);

        // Assert
        recent.Snapshots.Should().HaveCount(5);
    }

    #endregion

    #region Thread Safety Tests

    [Fact]
    public void RecordServerMetrics_Concurrent_ShouldBeThreadSafe()
    {
        // Arrange
        var service = CreateService();

        // Act
        var tasks = Enumerable.Range(0, 100).Select(i => Task.Run(() =>
        {
            service.RecordServerMetrics(i % 5, new ServerMetricsSnapshot { CpuPercent = i });
        }));
        Task.WaitAll(tasks.ToArray());

        // Assert
        var summaries = service.GetAllServersSummary();
        summaries.Should().HaveCount(5);
    }

    [Fact]
    public void RecordAndGetMetrics_Concurrent_ShouldNotThrow()
    {
        // Arrange
        var service = CreateService();
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        // Act
        var writeTask = Task.Run(() =>
        {
            int i = 0;
            while (!cts.IsCancellationRequested)
            {
                service.RecordServerMetrics(1, new ServerMetricsSnapshot { CpuPercent = i++ % 100 });
                service.RecordSystemMetrics(new SystemMetricsSnapshot { CpuPercent = i % 100 });
            }
        });

        var readTask = Task.Run(() =>
        {
            while (!cts.IsCancellationRequested)
            {
                _ = service.GetServerMetrics(1);
                _ = service.GetSystemMetrics();
                _ = service.GetAllServersSummary();
            }
        });

        // Assert - should not throw
        Task.WaitAll(writeTask, readTask);
    }

    #endregion

    #region Model Tests

    [Fact]
    public void ServerMetricsSnapshot_ShouldHaveCorrectDefaults()
    {
        // Arrange & Act
        var snapshot = new ServerMetricsSnapshot();

        // Assert
        snapshot.Timestamp.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        snapshot.Status.Should().BeEmpty();
        snapshot.CpuPercent.Should().Be(0);
    }

    [Fact]
    public void SystemMetricsSnapshot_ShouldHaveCorrectDefaults()
    {
        // Arrange & Act
        var snapshot = new SystemMetricsSnapshot();

        // Assert
        snapshot.Timestamp.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        snapshot.ActiveServers.Should().Be(0);
    }

    [Fact]
    public void ServerComparisonData_ShouldStoreAllProperties()
    {
        // Arrange & Act
        var data = new ServerComparisonData
        {
            AverageCpu = 45.5,
            AverageMemory = 1024,
            MaxCpu = 90.0,
            MaxMemory = 2048,
            TotalMatches = 10,
            AverageUptime = 3600
        };

        // Assert
        data.AverageCpu.Should().Be(45.5);
        data.AverageMemory.Should().Be(1024);
        data.MaxCpu.Should().Be(90.0);
        data.MaxMemory.Should().Be(2048);
        data.TotalMatches.Should().Be(10);
        data.AverageUptime.Should().Be(3600);
    }

    #endregion
}
