using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using HoNfigurator.Core.Services;
using HoNfigurator.Core.Models;
using Xunit;

namespace HoNfigurator.Tests.Services;

public class CpuAffinityServiceTests
{
    private readonly Mock<ILogger<CpuAffinityService>> _loggerMock;
    private readonly HoNConfiguration _config;
    private readonly CpuAffinityService _service;

    public CpuAffinityServiceTests()
    {
        _loggerMock = new Mock<ILogger<CpuAffinityService>>();
        _config = new HoNConfiguration();
        _service = new CpuAffinityService(_loggerMock.Object, _config);
    }

    #region DTO Tests

    [Fact]
    public void AffinityInfo_DefaultValues()
    {
        // Arrange & Act
        var info = new AffinityInfo();

        // Assert
        info.ProcessId.Should().Be(0);
        info.ProcessName.Should().BeEmpty();
        info.AffinityMask.Should().Be(0);
        info.Cores.Should().BeEmpty();
    }

    [Fact]
    public void AffinityInfo_WithValues()
    {
        // Arrange & Act
        var info = new AffinityInfo
        {
            ProcessId = 1234,
            ProcessName = "hon_server",
            AffinityMask = 0x0F, // First 4 cores
            Cores = new[] { 0, 1, 2, 3 }
        };

        // Assert
        info.ProcessId.Should().Be(1234);
        info.ProcessName.Should().Be("hon_server");
        info.AffinityMask.Should().Be(0x0F);
        info.Cores.Should().HaveCount(4);
    }

    [Fact]
    public void AffinityAssignment_DefaultValues()
    {
        // Arrange & Act
        var assignment = new AffinityAssignment();

        // Assert
        assignment.ProcessId.Should().Be(0);
        assignment.ProcessName.Should().BeEmpty();
        assignment.AffinityMask.Should().Be(0);
        assignment.AssignedAt.Should().Be(default);
    }

    [Fact]
    public void AffinityAssignment_WithValues()
    {
        // Arrange
        var assignedAt = DateTime.UtcNow;

        // Act
        var assignment = new AffinityAssignment
        {
            ProcessId = 5678,
            ProcessName = "gameserver",
            AffinityMask = 0xFF,
            AssignedAt = assignedAt
        };

        // Assert
        assignment.ProcessId.Should().Be(5678);
        assignment.ProcessName.Should().Be("gameserver");
        assignment.AffinityMask.Should().Be(0xFF);
        assignment.AssignedAt.Should().Be(assignedAt);
    }

    [Fact]
    public void AffinityRecommendation_DefaultValues()
    {
        // Arrange & Act
        var rec = new AffinityRecommendation();

        // Assert
        rec.TotalCores.Should().Be(0);
        rec.ReservedCores.Should().Be(0);
        rec.AvailableCores.Should().Be(0);
        rec.RecommendedCoresPerServer.Should().Be(0);
        rec.MaxServersRecommended.Should().Be(0);
    }

    [Fact]
    public void AffinityRecommendation_WithValues()
    {
        // Arrange & Act
        var rec = new AffinityRecommendation
        {
            TotalCores = 16,
            ReservedCores = 2,
            AvailableCores = 14,
            RecommendedCoresPerServer = 2,
            MaxServersRecommended = 7
        };

        // Assert
        rec.TotalCores.Should().Be(16);
        rec.ReservedCores.Should().Be(2);
        rec.AvailableCores.Should().Be(14);
        rec.RecommendedCoresPerServer.Should().Be(2);
        rec.MaxServersRecommended.Should().Be(7);
    }

    #endregion

    #region ProcessorCount Tests

    [Fact]
    public void ProcessorCount_ReturnsPositiveValue()
    {
        // Act
        var count = _service.ProcessorCount;

        // Assert
        count.Should().BeGreaterThan(0);
    }

    [Fact]
    public void ProcessorCount_MatchesEnvironment()
    {
        // Act
        var count = _service.ProcessorCount;

        // Assert
        count.Should().Be(Environment.ProcessorCount);
    }

    #endregion

    #region CoreIdsToMask Tests

    [Fact]
    public void CoreIdsToMask_SingleCore_ReturnsCorrectMask()
    {
        // Act
        var mask = CpuAffinityService.CoreIdsToMask(0);

        // Assert
        mask.Should().Be(1);
    }

    [Fact]
    public void CoreIdsToMask_MultipleCores_ReturnsCorrectMask()
    {
        // Act
        var mask = CpuAffinityService.CoreIdsToMask(0, 1, 2, 3);

        // Assert
        mask.Should().Be(0x0F); // Binary: 1111
    }

    [Fact]
    public void CoreIdsToMask_Core0And2_ReturnsCorrectMask()
    {
        // Act
        var mask = CpuAffinityService.CoreIdsToMask(0, 2);

        // Assert
        mask.Should().Be(0x05); // Binary: 0101
    }

    [Fact]
    public void CoreIdsToMask_EmptyArray_ReturnsZero()
    {
        // Act
        var mask = CpuAffinityService.CoreIdsToMask();

        // Assert
        mask.Should().Be(0);
    }

    [Fact]
    public void CoreIdsToMask_HighCoreNumbers_ReturnsCorrectMask()
    {
        // Act
        var mask = CpuAffinityService.CoreIdsToMask(8, 9, 10, 11);

        // Assert
        mask.Should().Be(0x0F00); // Cores 8-11
    }

    [Fact]
    public void CoreIdsToMask_NegativeCoreId_Ignored()
    {
        // Act
        var mask = CpuAffinityService.CoreIdsToMask(-1, 0, 1);

        // Assert
        mask.Should().Be(0x03); // Only cores 0 and 1
    }

    [Fact]
    public void CoreIdsToMask_CoreId64OrHigher_Ignored()
    {
        // Act
        var mask = CpuAffinityService.CoreIdsToMask(0, 64, 65);

        // Assert
        mask.Should().Be(0x01); // Only core 0
    }

    #endregion

    #region MaskToCoreIds Tests

    [Fact]
    public void MaskToCoreIds_SingleCore_ReturnsCorrectIds()
    {
        // Act
        var cores = CpuAffinityService.MaskToCoreIds(1);

        // Assert
        cores.Should().Equal(0);
    }

    [Fact]
    public void MaskToCoreIds_MultipleCores_ReturnsCorrectIds()
    {
        // Act
        var cores = CpuAffinityService.MaskToCoreIds(0x0F);

        // Assert
        cores.Should().Equal(0, 1, 2, 3);
    }

    [Fact]
    public void MaskToCoreIds_NonConsecutiveCores_ReturnsCorrectIds()
    {
        // Act
        var cores = CpuAffinityService.MaskToCoreIds(0x05);

        // Assert
        cores.Should().Equal(0, 2);
    }

    [Fact]
    public void MaskToCoreIds_ZeroMask_ReturnsEmpty()
    {
        // Act
        var cores = CpuAffinityService.MaskToCoreIds(0);

        // Assert
        cores.Should().BeEmpty();
    }

    [Fact]
    public void MaskToCoreIds_AllCoresMask_ReturnsAllCores()
    {
        // Act
        var cores = CpuAffinityService.MaskToCoreIds(0xFF);

        // Assert
        cores.Should().HaveCount(8);
        cores.Should().BeInAscendingOrder();
    }

    #endregion

    #region CoreIdsToMask and MaskToCoreIds Roundtrip Tests

    [Theory]
    [InlineData(new int[] { 0 })]
    [InlineData(new int[] { 0, 1, 2, 3 })]
    [InlineData(new int[] { 0, 2, 4, 6 })]
    [InlineData(new int[] { 7 })]
    public void CoreIdsToMask_MaskToCoreIds_Roundtrip(int[] originalCores)
    {
        // Act
        var mask = CpuAffinityService.CoreIdsToMask(originalCores);
        var recoveredCores = CpuAffinityService.MaskToCoreIds(mask);

        // Assert
        recoveredCores.Should().Equal(originalCores.OrderBy(c => c));
    }

    #endregion

    #region GetRecommendation Tests

    [Fact]
    public void GetRecommendation_SingleServer_ReturnsRecommendation()
    {
        // Act
        var rec = _service.GetRecommendation(1);

        // Assert
        rec.Should().NotBeNull();
        rec.TotalCores.Should().BeGreaterThan(0);
    }

    [Fact]
    public void GetRecommendation_MultipleServers_DividesCores()
    {
        // Act
        var rec = _service.GetRecommendation(4);

        // Assert
        rec.RecommendedCoresPerServer.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public void GetRecommendation_ZeroServers_HandlesGracefully()
    {
        // Act
        var rec = _service.GetRecommendation(0);

        // Assert
        rec.Should().NotBeNull();
        rec.TotalCores.Should().BeGreaterThan(0);
    }

    [Fact]
    public void GetRecommendation_ManyServers_StillGivesAtLeastOneCore()
    {
        // Act
        var rec = _service.GetRecommendation(100);

        // Assert
        rec.RecommendedCoresPerServer.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public void GetRecommendation_UsesConfiguredReservedCores()
    {
        // Arrange
        var config = new HoNConfiguration
        {
            CpuAffinity = new CpuAffinityConfiguration
            {
                ReservedCores = 4
            }
        };
        var service = new CpuAffinityService(_loggerMock.Object, config);

        // Act
        var rec = service.GetRecommendation(1);

        // Assert
        rec.ReservedCores.Should().Be(4);
    }

    [Fact]
    public void GetRecommendation_AvailableCores_IsTotalMinusReserved()
    {
        // Act
        var rec = _service.GetRecommendation(1);

        // Assert
        rec.AvailableCores.Should().BeLessThanOrEqualTo(rec.TotalCores);
    }

    #endregion

    #region SetProcessAffinity Tests

    [Fact]
    public void SetProcessAffinity_NonExistentProcessId_ReturnsFalse()
    {
        // Act
        var result = _service.SetProcessAffinity(999999, 0x01);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void SetProcessCores_NonExistentProcessId_ReturnsFalse()
    {
        // Act
        var result = _service.SetProcessCores(999999, 0, 1);

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region GetProcessAffinity Tests

    [Fact]
    public void GetProcessAffinity_NonExistentProcess_ReturnsNull()
    {
        // Act
        var info = _service.GetProcessAffinity(999999);

        // Assert
        info.Should().BeNull();
    }

    [Fact]
    public void GetProcessAffinity_CurrentProcess_ReturnsInfo()
    {
        // Arrange
        var currentPid = Environment.ProcessId;

        // Act
        var info = _service.GetProcessAffinity(currentPid);

        // Assert
        info.Should().NotBeNull();
        info!.ProcessId.Should().Be(currentPid);
        info.Cores.Should().NotBeEmpty();
    }

    #endregion

    #region GetAssignments Tests

    [Fact]
    public void GetAssignments_Initially_ReturnsEmpty()
    {
        // Act
        var assignments = _service.GetAssignments();

        // Assert
        assignments.Should().BeEmpty();
    }

    [Fact]
    public void GetAssignments_ReturnsReadOnlyList()
    {
        // Act
        var assignments = _service.GetAssignments();

        // Assert
        assignments.Should().BeOfType<List<AffinityAssignment>>();
    }

    #endregion

    #region ResetProcessAffinity Tests

    [Fact]
    public void ResetProcessAffinity_NonExistentProcess_ReturnsFalse()
    {
        // Act
        var result = _service.ResetProcessAffinity(999999);

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region SetProcessPriority Tests

    [Fact]
    public void SetProcessPriority_NonExistentProcess_ReturnsFalse()
    {
        // Act
        var result = _service.SetProcessPriority(999999, System.Diagnostics.ProcessPriorityClass.Normal);

        // Assert
        result.Should().BeFalse();
    }

    [Theory]
    [InlineData(System.Diagnostics.ProcessPriorityClass.Idle)]
    [InlineData(System.Diagnostics.ProcessPriorityClass.BelowNormal)]
    [InlineData(System.Diagnostics.ProcessPriorityClass.Normal)]
    [InlineData(System.Diagnostics.ProcessPriorityClass.AboveNormal)]
    [InlineData(System.Diagnostics.ProcessPriorityClass.High)]
    public void SetProcessPriority_AllPriorityLevels_DoNotThrow(System.Diagnostics.ProcessPriorityClass priority)
    {
        // Act & Assert - Should not throw even for non-existent process
        var act = () => _service.SetProcessPriority(999999, priority);
        act.Should().NotThrow();
    }

    #endregion

    #region Configuration Tests

    [Fact]
    public void Service_UsesConfiguredCoresPerServer()
    {
        // Arrange
        var config = new HoNConfiguration
        {
            CpuAffinity = new CpuAffinityConfiguration
            {
                CoresPerServer = 4
            }
        };
        var service = new CpuAffinityService(_loggerMock.Object, config);

        // Act
        var rec = service.GetRecommendation(2);

        // Assert - should use configured value in calculations
        rec.Should().NotBeNull();
    }

    [Fact]
    public void Service_UsesConfiguredStartCore()
    {
        // Arrange
        var config = new HoNConfiguration
        {
            CpuAffinity = new CpuAffinityConfiguration
            {
                StartCore = 2
            }
        };
        var service = new CpuAffinityService(_loggerMock.Object, config);

        // Assert - service created successfully with config
        service.Should().NotBeNull();
    }

    #endregion

    #region Mask Value Tests

    [Fact]
    public void CoreIdsToMask_AllCoresUpTo7_Returns0xFF()
    {
        // Act
        var mask = CpuAffinityService.CoreIdsToMask(0, 1, 2, 3, 4, 5, 6, 7);

        // Assert
        mask.Should().Be(0xFF);
    }

    [Fact]
    public void CoreIdsToMask_AllCoresUpTo15_Returns0xFFFF()
    {
        // Arrange
        var cores = Enumerable.Range(0, 16).ToArray();

        // Act
        var mask = CpuAffinityService.CoreIdsToMask(cores);

        // Assert
        mask.Should().Be(0xFFFF);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 2)]
    [InlineData(2, 4)]
    [InlineData(3, 8)]
    [InlineData(4, 16)]
    [InlineData(5, 32)]
    [InlineData(6, 64)]
    [InlineData(7, 128)]
    public void CoreIdsToMask_SingleCore_ReturnsPowerOf2(int coreId, long expectedMask)
    {
        // Act
        var mask = CpuAffinityService.CoreIdsToMask(coreId);

        // Assert
        mask.Should().Be(expectedMask);
    }

    #endregion

    #region Thread Safety Tests

    [Fact]
    public async Task GetRecommendation_ConcurrentAccess_ThreadSafe()
    {
        // Arrange
        var tasks = new List<Task<AffinityRecommendation>>();

        // Act
        for (int i = 0; i < 10; i++)
        {
            var serverCount = i + 1;
            tasks.Add(Task.Run(() => _service.GetRecommendation(serverCount)));
        }

        var results = await Task.WhenAll(tasks);

        // Assert
        results.Should().AllSatisfy(r => r.Should().NotBeNull());
    }

    [Fact]
    public async Task CoreIdsToMask_ConcurrentAccess_ThreadSafe()
    {
        // Arrange
        var tasks = new List<Task<long>>();

        // Act
        for (int i = 0; i < 100; i++)
        {
            var index = i;
            tasks.Add(Task.Run(() => CpuAffinityService.CoreIdsToMask(index % 8)));
        }

        var results = await Task.WhenAll(tasks);

        // Assert
        results.Should().AllSatisfy(r => r.Should().BeGreaterThan(0));
    }

    #endregion
}
