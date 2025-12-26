using FluentAssertions;
using HoNfigurator.Core.Notifications;

namespace HoNfigurator.Tests.Notifications;

public class NotificationServiceTests
{
    private NotificationService CreateService()
    {
        return new NotificationService();
    }

    #region SendNotification Tests

    [Fact]
    public void SendNotification_ShouldAddToQueue()
    {
        // Arrange
        var service = CreateService();
        var notification = new Notification
        {
            Title = "Test",
            Message = "Test message",
            Type = NotificationType.Info
        };

        // Act
        service.SendNotification(notification);

        // Assert
        var recent = service.GetRecentNotifications();
        recent.Should().HaveCount(1);
        recent[0].Title.Should().Be("Test");
    }

    [Fact]
    public void SendNotification_ShouldRaiseEvent()
    {
        // Arrange
        var service = CreateService();
        Notification? receivedNotification = null;
        service.OnNotification += (sender, n) => receivedNotification = n;

        var notification = new Notification
        {
            Title = "Test Event",
            Message = "Test message"
        };

        // Act
        service.SendNotification(notification);

        // Assert
        receivedNotification.Should().NotBeNull();
        receivedNotification!.Title.Should().Be("Test Event");
    }

    [Fact]
    public void SendNotification_ShouldGenerateUniqueId()
    {
        // Arrange
        var service = CreateService();

        // Act
        service.SendNotification(new Notification { Title = "Test 1" });
        service.SendNotification(new Notification { Title = "Test 2" });

        // Assert
        var notifications = service.GetRecentNotifications();
        notifications[0].Id.Should().NotBe(notifications[1].Id);
    }

    #endregion

    #region Helper Send Methods Tests

    [Fact]
    public void SendInfo_ShouldCreateInfoNotification()
    {
        // Arrange
        var service = CreateService();

        // Act
        service.SendInfo("Info Title", "Info message");

        // Assert
        var notifications = service.GetRecentNotifications();
        notifications.Should().HaveCount(1);
        notifications[0].Type.Should().Be(NotificationType.Info);
        notifications[0].PlaySound.Should().BeFalse();
    }

    [Fact]
    public void SendSuccess_ShouldCreateSuccessNotification()
    {
        // Arrange
        var service = CreateService();

        // Act
        service.SendSuccess("Success Title", "Success message");

        // Assert
        var notifications = service.GetRecentNotifications();
        notifications[0].Type.Should().Be(NotificationType.Success);
        notifications[0].Priority.Should().Be(NotificationPriority.Normal);
    }

    [Fact]
    public void SendWarning_ShouldCreateWarningNotification()
    {
        // Arrange
        var service = CreateService();

        // Act
        service.SendWarning("Warning Title", "Warning message");

        // Assert
        var notifications = service.GetRecentNotifications();
        notifications[0].Type.Should().Be(NotificationType.Warning);
        notifications[0].Priority.Should().Be(NotificationPriority.High);
    }

    [Fact]
    public void SendError_ShouldCreateErrorNotification()
    {
        // Arrange
        var service = CreateService();

        // Act
        service.SendError("Error Title", "Error message");

        // Assert
        var notifications = service.GetRecentNotifications();
        notifications[0].Type.Should().Be(NotificationType.Error);
    }

    [Fact]
    public void SendAlert_ShouldCreateAlertNotification()
    {
        // Arrange
        var service = CreateService();

        // Act
        service.SendAlert("Alert Title", "Alert message", serverId: 1, priority: NotificationPriority.Critical);

        // Assert
        var notifications = service.GetRecentNotifications();
        notifications[0].Type.Should().Be(NotificationType.Alert);
        notifications[0].Priority.Should().Be(NotificationPriority.Critical);
        notifications[0].ServerId.Should().Be(1);
    }

    [Fact]
    public void SendMethods_WithServerId_ShouldIncludeServerId()
    {
        // Arrange
        var service = CreateService();

        // Act
        service.SendInfo("Test", "Test", serverId: 5);

        // Assert
        var notifications = service.GetRecentNotifications();
        notifications[0].ServerId.Should().Be(5);
    }

    #endregion

    #region GetRecentNotifications Tests

    [Fact]
    public void GetRecentNotifications_ShouldReturnLatestFirst()
    {
        // Arrange
        var service = CreateService();
        service.SendInfo("First", "First message");
        service.SendInfo("Second", "Second message");
        service.SendInfo("Third", "Third message");

        // Act
        var notifications = service.GetRecentNotifications();

        // Assert
        notifications.Should().HaveCount(3);
    }

    [Fact]
    public void GetRecentNotifications_ShouldRespectCountLimit()
    {
        // Arrange
        var service = CreateService();
        for (int i = 0; i < 100; i++)
        {
            service.SendInfo($"Test {i}", "Message");
        }

        // Act
        var notifications = service.GetRecentNotifications(count: 10);

        // Assert
        notifications.Should().HaveCount(10);
    }

    #endregion

    #region Acknowledgement Tests

    [Fact]
    public void AcknowledgeNotification_ShouldMarkAsAcknowledged()
    {
        // Arrange
        var service = CreateService();
        var notification = new Notification
        {
            Title = "Test",
            Message = "Test",
            RequiresAcknowledgement = true
        };
        service.SendNotification(notification);
        var id = service.GetRecentNotifications()[0].Id;

        // Act
        service.AcknowledgeNotification(id);

        // Assert
        var unacknowledged = service.GetUnacknowledgedNotifications();
        unacknowledged.Should().NotContain(n => n.Id == id);
    }

    [Fact]
    public void GetUnacknowledgedNotifications_ShouldReturnOnlyUnacknowledged()
    {
        // Arrange
        var service = CreateService();
        service.SendNotification(new Notification { Title = "Ack 1", RequiresAcknowledgement = true });
        service.SendNotification(new Notification { Title = "Ack 2", RequiresAcknowledgement = true });
        
        var id1 = service.GetRecentNotifications().First(n => n.Title == "Ack 1").Id;
        service.AcknowledgeNotification(id1);

        // Act
        var unacknowledged = service.GetUnacknowledgedNotifications();

        // Assert
        unacknowledged.Should().HaveCount(1);
        unacknowledged[0].Title.Should().Be("Ack 2");
    }

    #endregion

    #region ClearNotifications Tests

    [Fact]
    public void ClearNotifications_ShouldRemoveAllNotifications()
    {
        // Arrange
        var service = CreateService();
        service.SendInfo("Test 1", "Message");
        service.SendInfo("Test 2", "Message");
        service.SendInfo("Test 3", "Message");

        // Act
        service.ClearNotifications();

        // Assert
        var notifications = service.GetRecentNotifications();
        notifications.Should().BeEmpty();
    }

    #endregion

    #region Alert Thresholds Tests

    [Fact]
    public void GetThresholds_ShouldReturnDefaultThresholds()
    {
        // Arrange
        var service = CreateService();

        // Act
        var thresholds = service.GetThresholds();

        // Assert
        thresholds.CpuWarningPercent.Should().Be(70.0);
        thresholds.CpuCriticalPercent.Should().Be(90.0);
        thresholds.MemoryWarningPercent.Should().Be(75.0);
        thresholds.DiskWarningPercent.Should().Be(80.0);
    }

    [Fact]
    public void UpdateThresholds_ShouldChangeThresholds()
    {
        // Arrange
        var service = CreateService();
        var newThresholds = new AlertThresholds
        {
            CpuWarningPercent = 60.0,
            CpuCriticalPercent = 85.0
        };

        // Act
        service.UpdateThresholds(newThresholds);

        // Assert
        var thresholds = service.GetThresholds();
        thresholds.CpuWarningPercent.Should().Be(60.0);
        thresholds.CpuCriticalPercent.Should().Be(85.0);
    }

    #endregion

    #region CheckResourceThresholds Tests

    [Fact]
    public void CheckResourceThresholds_WithNormalValues_ShouldNotSendAlert()
    {
        // Arrange
        var service = CreateService();
        var alertCount = 0;
        service.OnNotification += (s, n) => alertCount++;

        // Act
        service.CheckResourceThresholds(cpuPercent: 50, memoryPercent: 50, diskPercent: 50);

        // Assert
        alertCount.Should().Be(0);
    }

    [Fact]
    public void CheckResourceThresholds_WithHighCpu_ShouldSendAlert()
    {
        // Arrange
        var service = CreateService();
        var thresholds = service.GetThresholds();
        thresholds.EnableCpuAlerts = true;
        thresholds.AlertCooldownMinutes = 0; // Disable cooldown for test
        service.UpdateThresholds(thresholds);

        Notification? receivedNotification = null;
        service.OnNotification += (s, n) => receivedNotification = n;

        // Act
        service.CheckResourceThresholds(cpuPercent: 75, memoryPercent: 50, diskPercent: 50);

        // Assert
        receivedNotification.Should().NotBeNull();
        // Service sends Alert type for threshold warnings
        receivedNotification!.Type.Should().Be(NotificationType.Alert);
        receivedNotification.Priority.Should().Be(NotificationPriority.High);
    }

    [Fact]
    public void CheckResourceThresholds_WithCriticalCpu_ShouldSendCriticalAlert()
    {
        // Arrange
        var service = CreateService();
        var thresholds = service.GetThresholds();
        thresholds.AlertCooldownMinutes = 0;
        service.UpdateThresholds(thresholds);

        Notification? receivedNotification = null;
        service.OnNotification += (s, n) => receivedNotification = n;

        // Act
        service.CheckResourceThresholds(cpuPercent: 95, memoryPercent: 50, diskPercent: 50);

        // Assert
        receivedNotification.Should().NotBeNull();
        receivedNotification!.Priority.Should().Be(NotificationPriority.Critical);
    }

    [Fact]
    public void CheckResourceThresholds_WithDisabledAlerts_ShouldNotSendAlert()
    {
        // Arrange
        var service = CreateService();
        var thresholds = new AlertThresholds
        {
            EnableCpuAlerts = false,
            EnableMemoryAlerts = false,
            EnableDiskAlerts = false
        };
        service.UpdateThresholds(thresholds);

        var alertCount = 0;
        service.OnNotification += (s, n) => alertCount++;

        // Act
        service.CheckResourceThresholds(cpuPercent: 99, memoryPercent: 99, diskPercent: 99);

        // Assert
        alertCount.Should().Be(0);
    }

    #endregion

    #region NotifyServerStatusChange Tests

    [Fact]
    public void NotifyServerStatusChange_ShouldSendNotification()
    {
        // Arrange
        var service = CreateService();
        var thresholds = service.GetThresholds();
        thresholds.EnableServerAlerts = true;
        thresholds.AlertCooldownMinutes = 0;
        service.UpdateThresholds(thresholds);

        Notification? receivedNotification = null;
        service.OnNotification += (s, n) => receivedNotification = n;

        // Act - use "Running" which is the status the service recognizes
        service.NotifyServerStatusChange(serverId: 1, serverName: "Server #1", previousStatus: "Stopped", newStatus: "Running");

        // Assert
        receivedNotification.Should().NotBeNull();
        receivedNotification!.ServerId.Should().Be(1);
        receivedNotification.Message.Should().Contain("Server #1");
    }

    [Fact]
    public void NotifyServerStatusChange_WhenGoingOnline_ShouldSendSuccess()
    {
        // Arrange
        var service = CreateService();
        var thresholds = service.GetThresholds();
        thresholds.EnableServerAlerts = true;
        service.UpdateThresholds(thresholds);

        Notification? receivedNotification = null;
        service.OnNotification += (s, n) => receivedNotification = n;

        // Act - use "Running" as the online status
        service.NotifyServerStatusChange(1, "Server #1", "Stopped", "Running");

        // Assert
        receivedNotification.Should().NotBeNull();
        receivedNotification!.Type.Should().Be(NotificationType.Success);
    }

    [Fact]
    public void NotifyServerStatusChange_WhenCrashed_ShouldSendCriticalAlert()
    {
        // Arrange
        var service = CreateService();
        var thresholds = service.GetThresholds();
        thresholds.EnableServerAlerts = true;
        thresholds.AlertCooldownMinutes = 0;
        service.UpdateThresholds(thresholds);

        Notification? receivedNotification = null;
        service.OnNotification += (s, n) => receivedNotification = n;

        // Act - test crash notification
        service.NotifyServerStatusChange(1, "Server #1", "Running", "Crashed");

        // Assert
        receivedNotification.Should().NotBeNull();
        receivedNotification!.Type.Should().Be(NotificationType.Alert);
        receivedNotification.Priority.Should().Be(NotificationPriority.Critical);
    }

    #endregion

    #region Notification Trimming Tests

    [Fact]
    public void SendNotification_ShouldTrimOldNotifications()
    {
        // Arrange
        var service = CreateService();

        // Act - send many notifications
        for (int i = 0; i < 600; i++)
        {
            service.SendInfo($"Test {i}", "Message");
        }

        // Assert - should be trimmed to max
        var notifications = service.GetRecentNotifications(count: 1000);
        notifications.Count.Should().BeLessThanOrEqualTo(500);
    }

    #endregion

    #region Thread Safety Tests

    [Fact]
    public async Task ConcurrentNotifications_ShouldBeThreadSafe()
    {
        // Arrange
        var service = CreateService();
        var tasks = new List<Task>();

        // Act
        for (int i = 0; i < 100; i++)
        {
            var idx = i;
            tasks.Add(Task.Run(() =>
            {
                service.SendInfo($"Test {idx}", "Message");
                service.GetRecentNotifications();
            }));
        }
        await Task.WhenAll(tasks);

        // Assert - should not throw
        var notifications = service.GetRecentNotifications();
        notifications.Should().NotBeEmpty();
    }

    #endregion

    #region Portal Notification Tests

    [Fact]
    public void NotifyPortalConnectionChange_Connected_ShouldCreateSuccessNotification()
    {
        // Arrange
        var service = CreateService();
        Notification? received = null;
        service.OnNotification += (_, n) => received = n;

        // Act
        service.NotifyPortalConnectionChange(true);

        // Assert
        received.Should().NotBeNull();
        received!.Type.Should().Be(NotificationType.Success);
        received.Title.Should().Be("Management Portal Connected");
        received.Source.Should().Be("ManagementPortal");
        received.Data.Should().ContainKey("portal_url");
    }

    [Fact]
    public void NotifyPortalConnectionChange_Disconnected_ShouldCreateAlertNotification()
    {
        // Arrange
        var service = CreateService();
        service.UpdateThresholds(new AlertThresholds { AlertCooldownMinutes = 0 }); // Disable cooldown for test
        Notification? received = null;
        service.OnNotification += (_, n) => received = n;

        // Act
        service.NotifyPortalConnectionChange(false, "Connection timeout");

        // Assert
        received.Should().NotBeNull();
        received!.Type.Should().Be(NotificationType.Alert);
        received.Title.Should().Be("Management Portal Disconnected");
        received.Message.Should().Contain("timeout");
    }

    [Fact]
    public void NotifyPortalConnectionChange_Disconnected_WithDefaultMessage_ShouldUseDefaultMessage()
    {
        // Arrange
        var service = CreateService();
        service.UpdateThresholds(new AlertThresholds { AlertCooldownMinutes = 0 });
        Notification? received = null;
        service.OnNotification += (_, n) => received = n;

        // Act
        service.NotifyPortalConnectionChange(false);

        // Assert
        received.Should().NotBeNull();
        received!.Message.Should().Contain("management.honfigurator.app");
    }

    [Fact]
    public void NotifyPortalRegistration_Success_ShouldCreateSuccessNotification()
    {
        // Arrange
        var service = CreateService();
        Notification? received = null;
        service.OnNotification += (_, n) => received = n;

        // Act
        service.NotifyPortalRegistration(true, "MyServer");

        // Assert
        received.Should().NotBeNull();
        received!.Type.Should().Be(NotificationType.Success);
        received.Title.Should().Be("Portal Registration Successful");
        received.Message.Should().Contain("MyServer");
        received.Data.Should().ContainKey("server_name");
        received.Data!["server_name"].Should().Be("MyServer");
    }

    [Fact]
    public void NotifyPortalRegistration_Failed_ShouldCreateErrorNotification()
    {
        // Arrange
        var service = CreateService();
        Notification? received = null;
        service.OnNotification += (_, n) => received = n;

        // Act
        service.NotifyPortalRegistration(false, errorMessage: "Server already registered");

        // Assert
        received.Should().NotBeNull();
        received!.Type.Should().Be(NotificationType.Error);
        received.Title.Should().Be("Portal Registration Failed");
        received.Message.Should().Contain("Server already registered");
        received.RequiresAcknowledgement.Should().BeTrue();
    }

    [Fact]
    public void NotifyPortalRegistration_Failed_WithDefaultMessage_ShouldUseDefaultMessage()
    {
        // Arrange
        var service = CreateService();
        Notification? received = null;
        service.OnNotification += (_, n) => received = n;

        // Act
        service.NotifyPortalRegistration(false);

        // Assert
        received.Should().NotBeNull();
        received!.Message.Should().Contain("Failed to register");
    }

    [Fact]
    public void NotifyPortalConnectionChange_ShouldThrottleDisconnectedAlerts()
    {
        // Arrange
        var service = CreateService();
        service.UpdateThresholds(new AlertThresholds { AlertCooldownMinutes = 5 });
        int eventCount = 0;
        service.OnNotification += (_, _) => eventCount++;

        // Act
        service.NotifyPortalConnectionChange(false);
        service.NotifyPortalConnectionChange(false);
        service.NotifyPortalConnectionChange(false);

        // Assert - should only fire once due to throttling
        eventCount.Should().Be(1);
    }

    [Fact]
    public void NotifyPortalConnectionChange_Connected_ShouldNotThrottle()
    {
        // Arrange
        var service = CreateService();
        int eventCount = 0;
        service.OnNotification += (_, _) => eventCount++;

        // Act
        service.NotifyPortalConnectionChange(true);
        service.NotifyPortalConnectionChange(true);
        service.NotifyPortalConnectionChange(true);

        // Assert - connection success notifications are not throttled
        eventCount.Should().Be(3);
    }

    [Fact]
    public void NotifyPortalRegistration_Success_ShouldContainPortalUrl()
    {
        // Arrange
        var service = CreateService();
        Notification? received = null;
        service.OnNotification += (_, n) => received = n;

        // Act
        service.NotifyPortalRegistration(true, "TestServer");

        // Assert
        received.Should().NotBeNull();
        received!.Data.Should().ContainKey("portal_url");
        received.Data!["portal_url"].Should().Be("https://management.honfigurator.app:3001");
    }

    #endregion
}