# HoNfigurator .NET

**Heroes of Newerth Server Manager** - A comprehensive tool for managing HoN Game Servers

---

## 📋 Table of Contents

1. [System Requirements](#-system-requirements)
2. [Installation](#-installation)
3. [Configuration](#-configuration)
4. [Running the Application](#-running-the-application)
5. [Using the Dashboard](#-using-the-dashboard)
6. [API Reference](#-api-reference)
7. [Troubleshooting](#-troubleshooting)

---

## 💻 System Requirements

- **OS:** Windows 10/11 (64-bit), Linux (64-bit), macOS
- **.NET:** .NET 10.0 SDK or higher
- **HoN:** Heroes of Newerth Game Client installed
- **RAM:** Minimum 4GB (8GB+ recommended)
- **Network:** Required ports open (see Network Ports section)

---

## 📥 Installation

### Windows

```powershell
# Install .NET 10 SDK
winget install Microsoft.DotNet.SDK.10

# Clone and build
git clone <repository-url>
cd HoNfigurator-dotnet
dotnet build
```

### Linux (Ubuntu/Debian)

```bash
# Install .NET 10 SDK
wget https://packages.microsoft.com/config/ubuntu/24.04/packages-microsoft-prod.deb
sudo dpkg -i packages-microsoft-prod.deb
sudo apt-get update
sudo apt-get install -y dotnet-sdk-10.0

# Clone and build
git clone <repository-url>
cd HoNfigurator-dotnet
dotnet build
```

### Linux (Fedora/RHEL)

```bash
sudo dnf install dotnet-sdk-10.0
git clone <repository-url>
cd HoNfigurator-dotnet
dotnet build
```

---

## ⚙ Configuration

Config file location: `HoNfigurator.Api/bin/Debug/net10.0/config/config.json`

### Example config.json with explanations:

```json
{
  "hon_data": {
    // === Installation Paths ===
    // Windows: "C:\\Path\\To\\HoN"
    // Linux: "/home/user/HoN" or "/opt/hon"
    "hon_install_directory": "/path/to/hon",          // HoN Client location
    "hon_home_directory": "",                          // HoN Documents folder (leave empty for default)
    "hon_logs_directory": "",                          // Folder for storing logs

    // === Server Connection ===
    "svr_masterServer": "api.kongor.net",          // Master Server (IP:Port)
    "svr_chatServer": "chat.kongor.net",            // Chat Server (IP:Port)
    "svr_patchServer": "api.kongor.net/patch",           // Patch Server

    // === Server Account ===
    "svr_login": "YOUR_SERVER_LOGIN",                 // Server account name (not player account)
    "svr_password": "YOUR_SERVER_PASSWORD",           // Server account password

    // === Server Identity ===
    "svr_name": "NEWERTH",                                 // Server name (shown in Server List)
    "svr_location": "NEWERTH",                        // Region (NEWERTH/US/EU/SEA/etc)
    "svr_priority": "HIGH",                           // Priority (HIGH/NORMAL/LOW)

    // === Server Instances ===
    "svr_total": 2,                                   // Number of Game Servers to run
    "svr_total_per_core": 1,                          // Servers per CPU Core
    "svr_max_start_at_once": 2,                       // Max servers to start simultaneously
    "svr_startup_timeout": 180,                       // Timeout (seconds) waiting for server start

    // === Network Ports ===
    "svr_starting_gamePort": 10001,                   // Starting port for Game (Server 1=10001, 2=10002, ...)
    "svr_starting_voicePort": 10061,                  // Starting port for Voice
    "svr_managerPort": 11235,                         // Port for Manager to receive status from Game Servers
    "svr_api_port": 5050,                             // Port for Web Dashboard and API
    "auto_ping_resp_port": 10069,                     // Port for Ping Response

    // === Proxy Settings ===
    "man_enableProxy": true,                          // Enable Proxy Mode
    "svr_proxyPort": 1135,                            // Proxy Port
    "svr_proxyLocalAddr": null,                       // Local Address for Proxy
    "svr_proxyRemoteAddr": null,                      // Remote Address for Proxy

    // === Server Options ===
    "svr_noConsole": true,                            // Hide Game Server window
    "svr_enableBotMatch": true,                       // Enable Bot Match
    "svr_start_on_launch": false,                     // Auto-start Servers on application launch
    "svr_restart_between_games": false,               // Restart Server after game ends

    // === Advanced ===
    "man_use_cowmaster": false,                       // Use Cowmaster (Central Management)
    "svr_beta_mode": false,                           // Beta Mode
    "local_ip": "127.0.0.1",                        // Internal IP (leave empty for auto-detect)
    "svr_ip": null,                                   // External IP (leave empty for auto-detect)
    "man_version": "4.10.1",                          // HoN Version

    // === Suffix/State Override ===
    "svr_override_suffix": false,
    "svr_suffix": "auto",
    "svr_override_state": false,
    "svr_state": "auto",
    "svr_override_affinity": false
  },
  "application_data": {
    "timers": {
      "manager": {
        "public_ip_healthcheck": 1800,                // Check Public IP interval (seconds)
        "general_healthcheck": 60,                    // General Health Check interval
        "lag_healthcheck": 120,                       // Lag Check interval
        "check_for_hon_update": 120                   // Update Check interval
      },
      "replay_cleaner": {
        "active": false,                              // Enable/Disable Replay Cleaner
        "interval": 3600,                             // Run interval (seconds)
        "max_age_days": 7                             // Delete replays older than (days)
      }
    }
  }
}
```

---

## 🔌 Network Ports

| Port | Protocol | Description |
|------|----------|-------------|
| 5050 | TCP | Web Dashboard / API |
| 5051 | TCP | HTTPS (Dashboard) |
| 10001-10010 | UDP | Game Ports (based on server count) |
| 10061-10070 | UDP | Voice Ports (based on server count) |
| 11235 | TCP | Manager Port (receives status) |
| 10069 | UDP | Auto Ping Response |

**Note:** If using a Firewall, these ports must be open.

---

## 🚀 Running the Application

### Windows

```powershell
cd HoNfigurator-dotnet
dotnet run --project HoNfigurator.Api

# Or from build output:
cd HoNfigurator.Api/bin/Debug/net10.0
./HoNfigurator.Api.exe
```

### Linux

```bash
cd HoNfigurator-dotnet
dotnet run --project HoNfigurator.Api

# Or from build output:
cd HoNfigurator.Api/bin/Debug/net10.0
./HoNfigurator.Api
```

### Run as Linux Service (systemd)

```bash
# Create service file
sudo nano /etc/systemd/system/honfigurator.service
```

```ini
[Unit]
Description=HoNfigurator Server Manager
After=network.target

[Service]
Type=simple
User=hon
WorkingDirectory=/home/hon/HoNfigurator-dotnet/HoNfigurator.Api/bin/Release/net10.0
ExecStart=/home/hon/HoNfigurator-dotnet/HoNfigurator.Api/bin/Release/net10.0/HoNfigurator.Api
Restart=always
RestartSec=10
Environment=ASPNETCORE_URLS=http://0.0.0.0:5050

[Install]
WantedBy=multi-user.target
```

```bash
# Enable and start
sudo systemctl daemon-reload
sudo systemctl enable honfigurator
sudo systemctl start honfigurator
sudo systemctl status honfigurator
```

### Successful startup screen:

```
╔═══════════════════════════════════════════════════════════╗
║           HoNfigurator - .NET 10 Edition                  ║
╠═══════════════════════════════════════════════════════════╣
║  API Server running on:                                   ║
║    HTTP:  http://localhost:5050                           ║
║    HTTPS: https://localhost:5051                          ║
║                                                           ║
║  Dashboard:   http://localhost:5050                       ║
║  API Docs:    http://localhost:5050/scalar/v1             ║
║  SignalR Hub: /hubs/dashboard                             ║
╚═══════════════════════════════════════════════════════════╝
```

---

## 🖥 Using the Dashboard

### Accessing the Dashboard

Open your browser and navigate to: **http://localhost:5050**

### Main Features:

1. **Server List** - View status of all servers
2. **Start/Stop** - Start or stop individual servers
3. **Logs** - View logs for each server
4. **Configuration** - Modify settings

---

## 📡 API Reference

### API Documentation

Open your browser and navigate to: **http://localhost:5050/scalar/v1**

### Main Endpoints:

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/status` | View system status |
| GET | `/api/servers` | List all servers |
| POST | `/api/servers/{id}/start` | Start a server |
| POST | `/api/servers/{id}/stop` | Stop a server |
| POST | `/api/servers/{id}/restart` | Restart a server |
| POST | `/api/servers/start-all` | Start all servers |
| POST | `/api/servers/stop-all` | Stop all servers |
| GET | `/api/config` | View configuration |
| POST | `/api/config` | Save configuration |
| GET | `/api/health` | Health check |
| GET | `/api/metrics` | View metrics |
| GET | `/api/logs/{serverId}` | View logs |

### API Usage Examples:

```powershell
# View status
curl http://localhost:5050/api/status

# List servers
curl http://localhost:5050/api/servers

# Start Server 1
curl -X POST http://localhost:5050/api/servers/1/start

# Stop all servers
curl -X POST http://localhost:5050/api/servers/stop-all
```

---

## 🔧 Troubleshooting

### ❌ Error: "No such host is known" (ChatServer)

**Cause:** ChatServer address is incorrect

**Solution:** Set `svr_chatServer` in config.json:
```json
"svr_chatServer": "chat.kongor.net"
```

---

### ❌ Error: "hon_x64.exe not found" (Windows) / "hon executable not found" (Linux)

**Cause:** HoN installation path is incorrect

**Solution:** 
- Windows: Verify `hon_install_directory` points to folder containing `hon_x64.exe`
- Linux: Verify path points to folder containing `hon_x64-server` or `hon_x64`

---

### ❌ Error: "wwwroot\index.html not found"

**Cause:** wwwroot was not copied to output

**Solution:**
```powershell
Copy-Item -Path "HoNfigurator.Api\wwwroot" -Destination "HoNfigurator.Api\bin\Debug\net10.0\wwwroot" -Recurse -Force
```

---

### ❌ Server not showing in Server List

**Check the following:**
1. Master Server address is correct
2. Login/Password credentials are correct
3. Firewall has required ports open
4. Check logs for authentication success/failure

---

### ❌ Game Servers not connecting back

**Check the following:**
1. `svr_managerPort` matches what Game Server uses
2. Firewall has port 11235 open
3. Check logs for `GameServerListener started on port 11235`

---

## 📁 Project Structure

```
HoNfigurator-dotnet/
├── HoNfigurator.Api/           # Web API + Dashboard
│   ├── Endpoints/              # API Endpoints
│   ├── Hubs/                   # SignalR Hubs
│   ├── Services/               # Background Services
│   ├── Setup/                  # Setup Wizard
│   └── wwwroot/                # Static Files (Dashboard)
├── HoNfigurator.Core/          # Core Logic
│   ├── Connectors/             # Master/Chat Server Connectors
│   ├── Models/                 # Data Models
│   ├── Services/               # Core Services
│   └── Protocol/               # Packet Parsing
├── HoNfigurator.GameServer/    # Game Server Management
│   └── Services/               # Game Server Services
└── HoNfigurator.Dashboard/     # Windows Forms Dashboard (Optional)
```

---

## 📞 Support

If you encounter issues:
1. Check the Console logs
2. Review API Docs at `/scalar/v1`
3. Open an Issue in the Repository

---

## 📄 License

MIT License
