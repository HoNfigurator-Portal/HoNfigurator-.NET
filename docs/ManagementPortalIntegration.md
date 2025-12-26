# Management Portal Integration

This document describes the integration with **management.honfigurator.app** - the central management UI for HoNfigurator servers.

## Architecture Overview

```
┌─────────────────────────────┐      ┌────────────────────────────┐
│   HoNfigurator Server       │      │   Management Portal        │
│   (This Application)        │      │   management.honfigurator  │
│                             │      │   .app:3001                │
│  ┌─────────────────────┐    │      │                            │
│  │ ManagementPortal    │──────────►│  REST API                  │
│  │ BackgroundService   │    │      │  - /api/servers/register   │
│  └─────────────────────┘    │      │  - /api/servers/status     │
│           │                 │      │  - /api/ping               │
│           ▼                 │      └────────────────────────────┘
│  ┌─────────────────────┐    │
│  │ ManagementPortal    │    │      ┌────────────────────────────┐
│  │ Connector           │──────────►│   MQTT Broker              │
│  └─────────────────────┘    │      │   mqtt.honfigurator        │
│                             │      │   .app:8883 (TLS)          │
│  ┌─────────────────────┐    │      │                            │
│  │ MqttHandler         │──────────►│  Topics:                   │
│  │ (Events)            │    │      │  - hon/events/game_start   │
│  └─────────────────────┘    │      │  - hon/events/game_end     │
└─────────────────────────────┘      │  - hon/events/player_*     │
                                     └────────────────────────────┘
```

## Configuration

Add the following to your `config.json` under `application_data`:

```json
{
  "application_data": {
    "management_portal": {
      "enabled": true,
      "portal_url": "https://management.honfigurator.app:3001",
      "mqtt_host": "mqtt.honfigurator.app",
      "mqtt_port": 8883,
      "mqtt_use_tls": true,
      "discord_user_id": "YOUR_DISCORD_ID",
      "api_key": "YOUR_API_KEY",
      "status_report_interval_seconds": 30,
      "auto_register": true,
      "ca_certificate_path": "certs/ca.crt",
      "client_certificate_path": "certs/client.crt",
      "client_key_path": "certs/client.key"
    }
  }
}
```

### Configuration Options

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `enabled` | bool | false | Enable/disable management portal integration |
| `portal_url` | string | https://management.honfigurator.app:3001 | Portal REST API URL |
| `mqtt_host` | string | mqtt.honfigurator.app | MQTT broker hostname |
| `mqtt_port` | int | 8883 | MQTT broker port (TLS) |
| `mqtt_use_tls` | bool | true | Use TLS for MQTT connection |
| `discord_user_id` | string | null | Your Discord user ID for ownership verification |
| `api_key` | string | null | API key from the management portal |
| `status_report_interval_seconds` | int | 30 | How often to report status (seconds) |
| `auto_register` | bool | true | Automatically register server on startup |
| `ca_certificate_path` | string | null | Path to CA certificate (Step CA) |
| `client_certificate_path` | string | null | Path to client certificate |
| `client_key_path` | string | null | Path to client private key |

## TLS Certificates

The management portal uses **Step CA** for certificate management. Contact an admin to get your certificates:

1. `ca.crt` - Certificate Authority certificate
2. `client.crt` - Your client certificate
3. `client.key` - Your client private key

Place these in a `certs/` directory (or anywhere accessible) and set the paths in configuration.

## API Endpoints

The following API endpoints are available for management portal control:

### GET `/api/management/status`
Returns the current management portal connection status.

**Response:**
```json
{
  "enabled": true,
  "registered": true,
  "serverName": "MY_SERVER",
  "serverAddress": "192.168.1.1:5050",
  "portalUrl": "https://management.honfigurator.app:3001",
  "portalReachable": true,
  "lastUpdated": "2025-01-01T12:00:00Z"
}
```

### POST `/api/management/register`
Manually trigger server registration with the management portal.

**Response (Success):**
```json
{
  "success": true,
  "message": "Successfully registered with management portal",
  "serverName": "MY_SERVER",
  "serverAddress": "192.168.1.1:5050"
}
```

### POST `/api/management/report-status`
Manually trigger a status report to the management portal.

**Response:**
```json
{
  "success": true,
  "message": "Status report sent",
  "report": {
    "serverName": "MY_SERVER",
    "serverIp": "192.168.1.1",
    "status": "Online",
    "totalServers": 5,
    "runningServers": 3,
    "playersOnline": 24
  }
}
```

### GET `/api/management/config`
Returns current management portal configuration (sensitive data redacted).

**Response:**
```json
{
  "configured": true,
  "enabled": true,
  "portalUrl": "https://management.honfigurator.app:3001",
  "mqttHost": "mqtt.honfigurator.app",
  "mqttPort": 8883,
  "mqttUseTls": true,
  "discordUserId": "1234***",
  "apiKeyConfigured": true,
  "statusReportIntervalSeconds": 30,
  "autoRegister": true,
  "hasCaCertificate": true,
  "hasClientCertificate": true
}
```

## How It Works

### Automatic Registration

When `auto_register` is enabled, the `ManagementPortalBackgroundService` will:

1. **On Startup**: Attempt to register with the management portal
   - Retries up to 3 times with 5-second delays
   - Sends server name, IP, and API port

2. **Periodic Status Reports**: Every `status_report_interval_seconds`:
   - Reports server health and statistics
   - Includes: server count, player count, version info

3. **Auto-Reconnect**: If connection is lost:
   - Automatically attempts to re-register
   - Backoff delay between attempts

### Event Publishing (MQTT)

When connected to the portal MQTT broker, events are published to topics:

- `hon/events/game_start` - Game started
- `hon/events/game_end` - Game ended
- `hon/events/player_join` - Player joined
- `hon/events/player_leave` - Player left
- `hon/events/server_status` - Server status changed

## Public Endpoints

These endpoints are called BY the management portal to query your server:

| Endpoint | Description |
|----------|-------------|
| `GET /api/public/ping` | Health check |
| `GET /api/public/get_server_info` | Basic server information |
| `GET /api/public/get_honfigurator_version` | HoNfigurator version |
| `GET /api/public/get_hon_version` | HoN game version |
| `GET /api/register` | Registration validation |

## Troubleshooting

### Portal not reachable
- Check firewall allows outbound connections to port 3001 (HTTPS)
- Verify portal URL is correct
- Check TLS certificates are valid

### MQTT connection failed
- Verify certificates are correctly configured
- Check firewall allows outbound port 8883
- Ensure client certificate is issued by the portal CA

### Registration failed
- Ensure `server_ip` is configured and publicly accessible
- Verify API port is open on firewall
- Check Discord user ID matches portal account

### Certificate errors
- Ensure all 3 certificate files exist and are readable
- Verify certificates haven't expired
- Check certificate paths are correct (absolute or relative to working directory)

## Development Notes

### Key Files

- `ManagementPortalConnector.cs` - HTTP client for portal communication
- `ManagementPortalBackgroundService.cs` - Background service for auto-registration
- `MqttHandler.cs` - MQTT event publisher (supports portal MQTT)
- `HoNConfiguration.cs` - Configuration model with `ManagementPortalSettings`
- `ApiEndpoints.cs` - REST API endpoints for management control
- `DashboardHub.cs` - SignalR hub with portal status methods

### SignalR Real-time Updates

The dashboard supports real-time management portal status through SignalR:

**Client Methods:**
- `ReceiveManagementPortalStatus(status)` - Receives portal connection status updates

**Hub Methods:**
- `RequestManagementPortalStatus()` - Request current portal status
- `RegisterWithManagementPortal()` - Trigger manual registration

**Example JavaScript:**
```javascript
// Connect to SignalR
const connection = new signalR.HubConnectionBuilder()
    .withUrl('/hubs/dashboard')
    .build();

// Listen for portal status updates
connection.on('ReceiveManagementPortalStatus', (status) => {
    console.log('Portal status:', status);
    // status.enabled, status.connected, status.registered, etc.
});

// Request status
await connection.invoke('RequestManagementPortalStatus');

// Trigger registration
await connection.invoke('RegisterWithManagementPortal');
```

### Testing

Unit tests are located in:
- `ManagementPortalConnectorTests.cs` - Tests for connector and configuration models
- `ManagementPortalBackgroundServiceTests.cs` - Tests for background service
- `DashboardHubTests.cs` - Tests for SignalR hub including portal methods
