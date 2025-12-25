using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using HoNfigurator.Core.Models;

namespace HoNfigurator.Core.Services;

/// <summary>
/// Manages CPU affinity for game server processes.
/// Port of Python HoNfigurator-Central CPU affinity functionality.
/// </summary>
public class CpuAffinityService
{
    private readonly ILogger<CpuAffinityService> _logger;
    private readonly HoNConfiguration _config;
    private readonly Dictionary<int, AffinityAssignment> _assignments = new();
    private readonly object _lock = new();

    public CpuAffinityService(ILogger<CpuAffinityService> logger, HoNConfiguration config)
    {
        _logger = logger;
        _config = config;
    }

    /// <summary>
    /// Get the number of logical processors available
    /// </summary>
    public int ProcessorCount => Environment.ProcessorCount;

    /// <summary>
    /// Set CPU affinity for a process
    /// </summary>
    public bool SetProcessAffinity(int processId, long affinityMask)
    {
        try
        {
            var process = Process.GetProcessById(processId);
            return SetProcessAffinity(process, affinityMask);
        }
        catch (ArgumentException)
        {
            _logger.LogWarning("Process {ProcessId} not found", processId);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set affinity for process {ProcessId}", processId);
            return false;
        }
    }

    /// <summary>
    /// Set CPU affinity for a process
    /// </summary>
    public bool SetProcessAffinity(Process process, long affinityMask)
    {
        try
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) &&
                !RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                _logger.LogWarning("CPU affinity not supported on this platform");
                return false;
            }

            process.ProcessorAffinity = new IntPtr(affinityMask);
            
            _logger.LogInformation(
                "Set CPU affinity for process {ProcessId} ({Name}) to mask 0x{Mask:X}",
                process.Id, process.ProcessName, affinityMask);

            lock (_lock)
            {
                _assignments[process.Id] = new AffinityAssignment
                {
                    ProcessId = process.Id,
                    ProcessName = process.ProcessName,
                    AffinityMask = affinityMask,
                    AssignedAt = DateTime.UtcNow
                };
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set affinity for process {ProcessId}", process.Id);
            return false;
        }
    }

    /// <summary>
    /// Set CPU affinity to specific cores
    /// </summary>
    public bool SetProcessCores(int processId, params int[] coreIds)
    {
        var mask = CoreIdsToMask(coreIds);
        return SetProcessAffinity(processId, mask);
    }

    /// <summary>
    /// Set CPU affinity to specific cores
    /// </summary>
    public bool SetProcessCores(Process process, params int[] coreIds)
    {
        var mask = CoreIdsToMask(coreIds);
        return SetProcessAffinity(process, mask);
    }

    /// <summary>
    /// Assign a server to specific cores based on server index
    /// </summary>
    public bool AssignServerToCore(int serverId, Process process)
    {
        var coresPerServer = _config.CpuAffinity?.CoresPerServer ?? 1;
        var startCore = _config.CpuAffinity?.StartCore ?? 0;
        var maxCores = ProcessorCount;

        // Calculate which cores this server should use
        var firstCore = startCore + (serverId * coresPerServer);
        if (firstCore >= maxCores)
        {
            // Wrap around if we exceed available cores
            firstCore = startCore + ((serverId * coresPerServer) % (maxCores - startCore));
        }

        var cores = Enumerable.Range(firstCore, coresPerServer)
            .Select(c => c % maxCores)
            .ToArray();

        _logger.LogDebug(
            "Assigning server {ServerId} to cores: {Cores}",
            serverId, string.Join(", ", cores));

        return SetProcessCores(process, cores);
    }

    /// <summary>
    /// Auto-assign affinity to all game server processes
    /// </summary>
    public async Task<int> AutoAssignAffinityAsync(string processName = "hon_server")
    {
        var assigned = 0;
        var processes = Process.GetProcessesByName(processName);
        var coresPerServer = _config.CpuAffinity?.CoresPerServer ?? 1;
        var startCore = _config.CpuAffinity?.StartCore ?? 0;

        for (var i = 0; i < processes.Length; i++)
        {
            var process = processes[i];
            try
            {
                var firstCore = startCore + (i * coresPerServer);
                var cores = Enumerable.Range(firstCore, coresPerServer)
                    .Select(c => c % ProcessorCount)
                    .ToArray();

                if (SetProcessCores(process, cores))
                {
                    assigned++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to set affinity for {ProcessName} PID {PID}",
                    processName, process.Id);
            }
        }

        _logger.LogInformation("Auto-assigned CPU affinity to {Count}/{Total} processes",
            assigned, processes.Length);

        return assigned;
    }

    /// <summary>
    /// Get current affinity for a process
    /// </summary>
    public AffinityInfo? GetProcessAffinity(int processId)
    {
        try
        {
            var process = Process.GetProcessById(processId);
            var mask = (long)process.ProcessorAffinity;
            
            return new AffinityInfo
            {
                ProcessId = processId,
                ProcessName = process.ProcessName,
                AffinityMask = mask,
                Cores = MaskToCoreIds(mask)
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get affinity for process {ProcessId}", processId);
            return null;
        }
    }

    /// <summary>
    /// Get all current affinity assignments
    /// </summary>
    public IReadOnlyList<AffinityAssignment> GetAssignments()
    {
        lock (_lock)
        {
            return _assignments.Values.ToList();
        }
    }

    /// <summary>
    /// Reset process affinity to use all cores
    /// </summary>
    public bool ResetProcessAffinity(int processId)
    {
        try
        {
            var process = Process.GetProcessById(processId);
            var allCoresMask = (1L << ProcessorCount) - 1;
            process.ProcessorAffinity = new IntPtr(allCoresMask);

            lock (_lock)
            {
                _assignments.Remove(processId);
            }

            _logger.LogInformation("Reset CPU affinity for process {ProcessId}", processId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reset affinity for process {ProcessId}", processId);
            return false;
        }
    }

    /// <summary>
    /// Get recommended affinity configuration
    /// </summary>
    public AffinityRecommendation GetRecommendation(int serverCount)
    {
        var totalCores = ProcessorCount;
        var reservedCores = _config.CpuAffinity?.ReservedCores ?? 2; // Reserve for OS/other
        var availableCores = totalCores - reservedCores;

        if (availableCores <= 0)
            availableCores = totalCores;

        var coresPerServer = Math.Max(1, availableCores / Math.Max(1, serverCount));

        return new AffinityRecommendation
        {
            TotalCores = totalCores,
            ReservedCores = reservedCores,
            AvailableCores = availableCores,
            RecommendedCoresPerServer = coresPerServer,
            MaxServersRecommended = availableCores / coresPerServer
        };
    }

    /// <summary>
    /// Set process priority
    /// </summary>
    public bool SetProcessPriority(int processId, ProcessPriorityClass priority)
    {
        try
        {
            var process = Process.GetProcessById(processId);
            process.PriorityClass = priority;
            _logger.LogInformation(
                "Set priority for process {ProcessId} to {Priority}",
                processId, priority);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set priority for process {ProcessId}", processId);
            return false;
        }
    }

    /// <summary>
    /// Set process priority for all game servers
    /// </summary>
    public async Task<int> SetAllServersPriorityAsync(
        ProcessPriorityClass priority,
        string processName = "hon_server")
    {
        var updated = 0;
        var processes = Process.GetProcessesByName(processName);

        foreach (var process in processes)
        {
            try
            {
                process.PriorityClass = priority;
                updated++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to set priority for {Name} PID {PID}",
                    processName, process.Id);
            }
        }

        _logger.LogInformation("Set priority to {Priority} for {Count}/{Total} processes",
            priority, updated, processes.Length);

        return updated;
    }

    /// <summary>
    /// Convert core IDs to affinity mask
    /// </summary>
    public static long CoreIdsToMask(params int[] coreIds)
    {
        long mask = 0;
        foreach (var coreId in coreIds)
        {
            if (coreId >= 0 && coreId < 64) // 64-bit limit
            {
                mask |= 1L << coreId;
            }
        }
        return mask;
    }

    /// <summary>
    /// Convert affinity mask to core IDs
    /// </summary>
    public static int[] MaskToCoreIds(long mask)
    {
        var cores = new List<int>();
        for (var i = 0; i < 64; i++)
        {
            if ((mask & (1L << i)) != 0)
            {
                cores.Add(i);
            }
        }
        return cores.ToArray();
    }
}

// DTOs

public class AffinityInfo
{
    public int ProcessId { get; set; }
    public string ProcessName { get; set; } = string.Empty;
    public long AffinityMask { get; set; }
    public int[] Cores { get; set; } = Array.Empty<int>();
}

public class AffinityAssignment
{
    public int ProcessId { get; set; }
    public string ProcessName { get; set; } = string.Empty;
    public long AffinityMask { get; set; }
    public DateTime AssignedAt { get; set; }
}

public class AffinityRecommendation
{
    public int TotalCores { get; set; }
    public int ReservedCores { get; set; }
    public int AvailableCores { get; set; }
    public int RecommendedCoresPerServer { get; set; }
    public int MaxServersRecommended { get; set; }
}
