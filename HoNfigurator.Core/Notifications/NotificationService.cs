using System.Collections.Concurrent;

namespace HoNfigurator.Core.Notifications;

/// <summary>
/// Represents a notification to be sent to clients
/// </summary>
public record Notification
{
    public string Id { get; init; } = Guid.NewGuid().ToString();
    public string Title { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public NotificationType Type { get; init; } = NotificationType.Info;
    public NotificationPriority Priority { get; init; } = NotificationPriority.Normal;
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public string? Source { get; init; }
    public int? ServerId { get; init; }
    public Dictionary<string, object>? Data { get; init; }
    public bool RequiresAcknowledgement { get; init; }
    public bool PlaySound { get; init; } = true;
    public bool ShowBrowserNotification { get; init; } = true;
}

public enum NotificationType
{
    Info,
    Success,
    Warning,
    Error,
    Alert,      // Critical alerts (server crash, etc.)
    System      // System-level notifications
}

public enum NotificationPriority
{
    Low,
    Normal,
    High,
    Critical
}

/// <summary>
/// Alert thresholds configuration
/// </summary>
public record AlertThresholds
{
    public double CpuWarningPercent { get; set; } = 70.0;
    public double CpuCriticalPercent { get; set; } = 90.0;
    public double MemoryWarningPercent { get; set; } = 75.0;
    public double MemoryCriticalPercent { get; set; } = 90.0;
    public double DiskWarningPercent { get; set; } = 80.0;
    public double DiskCriticalPercent { get; set; } = 95.0;
    public int ServerDownAlertDelaySeconds { get; set; } = 30;
    public bool EnableCpuAlerts { get; set; } = true;
    public bool EnableMemoryAlerts { get; set; } = true;
    public bool EnableDiskAlerts { get; set; } = true;
    public bool EnableServerAlerts { get; set; } = true;
    public int AlertCooldownMinutes { get; set; } = 5; // Prevent spam
}

/// <summary>
/// Service for managing notifications and alerts
/// </summary>
public interface INotificationService
{
    event EventHandler<Notification>? OnNotification;
    
    void SendNotification(Notification notification);
    void SendInfo(string title, string message, int? serverId = null);
    void SendSuccess(string title, string message, int? serverId = null);
    void SendWarning(string title, string message, int? serverId = null);
    void SendError(string title, string message, int? serverId = null);
    void SendAlert(string title, string message, int? serverId = null, NotificationPriority priority = NotificationPriority.High);
    
    IReadOnlyList<Notification> GetRecentNotifications(int count = 50);
    IReadOnlyList<Notification> GetUnacknowledgedNotifications();
    void AcknowledgeNotification(string notificationId);
    void ClearNotifications();
    
    AlertThresholds GetThresholds();
    void UpdateThresholds(AlertThresholds thresholds);
    
    void CheckResourceThresholds(double cpuPercent, double memoryPercent, double diskPercent);
    void NotifyServerStatusChange(int serverId, string serverName, string previousStatus, string newStatus);
    
    /// <summary>
    /// Notify when management portal connection state changes
    /// </summary>
    void NotifyPortalConnectionChange(bool isConnected, string? errorMessage = null);
    
    /// <summary>
    /// Notify when server is registered with management portal
    /// </summary>
    void NotifyPortalRegistration(bool success, string? serverName = null, string? errorMessage = null);
}

public class NotificationService : INotificationService
{
    private readonly ConcurrentQueue<Notification> _notifications = new();
    private readonly ConcurrentDictionary<string, bool> _acknowledged = new();
    private readonly ConcurrentDictionary<string, DateTime> _alertCooldowns = new();
    private AlertThresholds _thresholds = new();
    private const int MaxNotifications = 500;

    public event EventHandler<Notification>? OnNotification;

    public void SendNotification(Notification notification)
    {
        // Add to queue
        _notifications.Enqueue(notification);
        
        // Trim old notifications
        while (_notifications.Count > MaxNotifications && _notifications.TryDequeue(out _)) { }
        
        // Raise event
        OnNotification?.Invoke(this, notification);
    }

    public void SendInfo(string title, string message, int? serverId = null)
    {
        SendNotification(new Notification
        {
            Title = title,
            Message = message,
            Type = NotificationType.Info,
            Priority = NotificationPriority.Normal,
            ServerId = serverId,
            PlaySound = false
        });
    }

    public void SendSuccess(string title, string message, int? serverId = null)
    {
        SendNotification(new Notification
        {
            Title = title,
            Message = message,
            Type = NotificationType.Success,
            Priority = NotificationPriority.Normal,
            ServerId = serverId
        });
    }

    public void SendWarning(string title, string message, int? serverId = null)
    {
        SendNotification(new Notification
        {
            Title = title,
            Message = message,
            Type = NotificationType.Warning,
            Priority = NotificationPriority.High,
            ServerId = serverId
        });
    }

    public void SendError(string title, string message, int? serverId = null)
    {
        SendNotification(new Notification
        {
            Title = title,
            Message = message,
            Type = NotificationType.Error,
            Priority = NotificationPriority.High,
            ServerId = serverId,
            RequiresAcknowledgement = true
        });
    }

    public void SendAlert(string title, string message, int? serverId = null, NotificationPriority priority = NotificationPriority.High)
    {
        SendNotification(new Notification
        {
            Title = title,
            Message = message,
            Type = NotificationType.Alert,
            Priority = priority,
            ServerId = serverId,
            RequiresAcknowledgement = priority >= NotificationPriority.High,
            ShowBrowserNotification = true
        });
    }

    public IReadOnlyList<Notification> GetRecentNotifications(int count = 50)
    {
        return _notifications.TakeLast(count).Reverse().ToList();
    }

    public IReadOnlyList<Notification> GetUnacknowledgedNotifications()
    {
        return _notifications
            .Where(n => n.RequiresAcknowledgement && !_acknowledged.ContainsKey(n.Id))
            .ToList();
    }

    public void AcknowledgeNotification(string notificationId)
    {
        _acknowledged.TryAdd(notificationId, true);
    }

    public void ClearNotifications()
    {
        while (_notifications.TryDequeue(out _)) { }
        _acknowledged.Clear();
    }

    public AlertThresholds GetThresholds()
    {
        return _thresholds;
    }

    public void UpdateThresholds(AlertThresholds thresholds)
    {
        _thresholds = thresholds;
    }

    public void CheckResourceThresholds(double cpuPercent, double memoryPercent, double diskPercent)
    {
        // CPU Alerts
        if (_thresholds.EnableCpuAlerts)
        {
            if (cpuPercent >= _thresholds.CpuCriticalPercent)
            {
                SendThrottledAlert("cpu_critical", "Critical CPU Usage", 
                    $"CPU usage is critically high at {cpuPercent:F1}%", 
                    NotificationPriority.Critical);
            }
            else if (cpuPercent >= _thresholds.CpuWarningPercent)
            {
                SendThrottledAlert("cpu_warning", "High CPU Usage", 
                    $"CPU usage is high at {cpuPercent:F1}%", 
                    NotificationPriority.High);
            }
        }

        // Memory Alerts
        if (_thresholds.EnableMemoryAlerts)
        {
            if (memoryPercent >= _thresholds.MemoryCriticalPercent)
            {
                SendThrottledAlert("memory_critical", "Critical Memory Usage", 
                    $"Memory usage is critically high at {memoryPercent:F1}%", 
                    NotificationPriority.Critical);
            }
            else if (memoryPercent >= _thresholds.MemoryWarningPercent)
            {
                SendThrottledAlert("memory_warning", "High Memory Usage", 
                    $"Memory usage is high at {memoryPercent:F1}%", 
                    NotificationPriority.High);
            }
        }

        // Disk Alerts
        if (_thresholds.EnableDiskAlerts)
        {
            if (diskPercent >= _thresholds.DiskCriticalPercent)
            {
                SendThrottledAlert("disk_critical", "Critical Disk Usage", 
                    $"Disk usage is critically high at {diskPercent:F1}%", 
                    NotificationPriority.Critical);
            }
            else if (diskPercent >= _thresholds.DiskWarningPercent)
            {
                SendThrottledAlert("disk_warning", "High Disk Usage", 
                    $"Disk usage is high at {diskPercent:F1}%", 
                    NotificationPriority.High);
            }
        }
    }

    public void NotifyServerStatusChange(int serverId, string serverName, string previousStatus, string newStatus)
    {
        if (!_thresholds.EnableServerAlerts) return;

        var alertKey = $"server_{serverId}_{newStatus}";
        
        if (newStatus == "Crashed" || newStatus == "Error")
        {
            SendThrottledAlert(alertKey, "Server Crashed", 
                $"Server #{serverId} ({serverName}) has crashed!", 
                NotificationPriority.Critical, serverId);
        }
        else if (newStatus == "Stopped" && previousStatus == "Running")
        {
            SendThrottledAlert(alertKey, "Server Stopped Unexpectedly", 
                $"Server #{serverId} ({serverName}) stopped unexpectedly", 
                NotificationPriority.High, serverId);
        }
        else if (newStatus == "Running" && previousStatus != "Running")
        {
            SendSuccess("Server Online", $"Server #{serverId} ({serverName}) is now running", serverId);
        }
        else if (newStatus == "Restarting")
        {
            SendInfo("Server Restarting", $"Server #{serverId} ({serverName}) is restarting...", serverId);
        }
    }

    private void SendThrottledAlert(string alertKey, string title, string message, 
        NotificationPriority priority, int? serverId = null)
    {
        var cooldownMinutes = _thresholds.AlertCooldownMinutes;
        
        if (_alertCooldowns.TryGetValue(alertKey, out var lastAlert))
        {
            if (DateTime.UtcNow - lastAlert < TimeSpan.FromMinutes(cooldownMinutes))
            {
                return; // Still in cooldown
            }
        }
        
        _alertCooldowns[alertKey] = DateTime.UtcNow;
        SendAlert(title, message, serverId, priority);
    }

    public void NotifyPortalConnectionChange(bool isConnected, string? errorMessage = null)
    {
        var alertKey = isConnected ? "portal_connected" : "portal_disconnected";
        
        if (isConnected)
        {
            SendNotification(new Notification
            {
                Title = "Management Portal Connected",
                Message = "Successfully connected to management.honfigurator.app",
                Type = NotificationType.Success,
                Priority = NotificationPriority.Normal,
                Source = "ManagementPortal",
                PlaySound = false,
                Data = new Dictionary<string, object>
                {
                    ["portal_url"] = "https://management.honfigurator.app:3001"
                }
            });
        }
        else
        {
            SendThrottledAlert(alertKey, "Management Portal Disconnected",
                errorMessage ?? "Lost connection to management.honfigurator.app",
                NotificationPriority.High);
        }
    }

    public void NotifyPortalRegistration(bool success, string? serverName = null, string? errorMessage = null)
    {
        if (success)
        {
            SendNotification(new Notification
            {
                Title = "Portal Registration Successful",
                Message = string.Format("Server '{0}' is now registered with the management portal", serverName ?? "Unknown"),
                Type = NotificationType.Success,
                Priority = NotificationPriority.Normal,
                Source = "ManagementPortal",
                Data = new Dictionary<string, object>
                {
                    ["server_name"] = serverName ?? "Unknown",
                    ["portal_url"] = "https://management.honfigurator.app:3001"
                }
            });
        }
        else
        {
            SendNotification(new Notification
            {
                Title = "Portal Registration Failed",
                Message = errorMessage ?? "Failed to register with management portal",
                Type = NotificationType.Error,
                Priority = NotificationPriority.High,
                Source = "ManagementPortal",
                RequiresAcknowledgement = true
            });
        }
    }
}
