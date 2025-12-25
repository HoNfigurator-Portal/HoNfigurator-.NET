using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace HoNfigurator.Core.Connectors;

/// <summary>
/// Response from master server authentication (Server Manager auth via replay_auth)
/// Matches NEXUS ServerRequesterController.Authentication.cs response format
/// </summary>
public record MasterServerAuthResponse
{
    public bool Success { get; init; }
    public int ServerId { get; init; }
    public string SessionId { get; init; } = string.Empty;
    public string ChatServerHost { get; init; } = string.Empty;
    public int ChatServerPort { get; init; }
    public bool IsOfficial { get; init; } = true;
    public string? CdnUploadHost { get; init; }
    public string? CdnUploadTarget { get; init; }
    public string Error { get; init; } = string.Empty;
}

/// <summary>
/// Response from game server authentication (new_session endpoint)
/// Matches NEXUS HandleServerAuthentication response
/// </summary>
public record GameServerAuthResponse
{
    public bool Success { get; init; }
    public int ServerId { get; init; }
    public string SessionId { get; init; } = string.Empty;
    public string ChatServerHost { get; init; } = string.Empty;
    public int ChatServerPort { get; init; }
    public double LeaverThreshold { get; init; } = 0.05;
    public string Error { get; init; } = string.Empty;
}

/// <summary>
/// Response from replay upload
/// </summary>
public record ReplayUploadResponse
{
    public bool Success { get; init; }
    public string Url { get; init; } = string.Empty;
    public string Error { get; init; } = string.Empty;
}

/// <summary>
/// Manages connection to the HoN Master Server (NEXUS KONGOR.MasterServer) for authentication and replay uploads.
/// Implements server_requester.php endpoints matching NEXUS ServerRequesterController.
/// </summary>
public interface IMasterServerConnector
{
    bool IsAuthenticated { get; }
    int? ServerId { get; }
    string? SessionId { get; }
    string? ChatServerHost { get; }
    int? ChatServerPort { get; }
    
    /// <summary>
    /// Authenticates as a Server Manager using replay_auth endpoint (NEXUS HandleServerManagerAuthentication)
    /// </summary>
    Task<MasterServerAuthResponse> AuthenticateAsync(string username, string password, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Authenticates a Game Server using new_session endpoint (NEXUS HandleServerAuthentication)
    /// </summary>
    Task<GameServerAuthResponse> AuthenticateGameServerAsync(
        string hostAccount, 
        int serverInstance, 
        string password,
        int port,
        string serverName,
        string description,
        string location,
        string ipAddress,
        CancellationToken cancellationToken = default);
    
    Task<ReplayUploadResponse> UploadReplayAsync(int matchId, string filePath, CancellationToken cancellationToken = default);
    Task<bool> ValidateSessionAsync(CancellationToken cancellationToken = default);
    void Disconnect();
}

public class MasterServerConnector : IMasterServerConnector, IDisposable
{
    private readonly ILogger<MasterServerConnector> _logger;
    private readonly HttpClient _httpClient;
    private readonly string _masterServerUrl;
    private readonly string _version;
    private readonly string _userAgent;
    private bool _disposed;

    public bool IsAuthenticated { get; private set; }
    public int? ServerId { get; private set; }
    public string? SessionId { get; private set; }
    public string? ChatServerHost { get; private set; }
    public int? ChatServerPort { get; private set; }

    public event Action? OnAuthenticated;
    public event Action<string>? OnAuthenticationFailed;
    public event Action? OnDisconnected;

    public MasterServerConnector(
        ILogger<MasterServerConnector> logger, 
        string masterServerUrl = "http://api.kongor.net",
        string version = "4.10.1")
    {
        _logger = logger;
        _masterServerUrl = masterServerUrl;
        _version = version;
        
        // User-Agent like Python: "S2 Games/Heroes of Newerth/4.10.1/was/x86_64"
        var platform = OperatingSystem.IsWindows() ? "was" : "las";
        var arch = Environment.Is64BitProcess ? "x86_64" : "x86-biarch";
        _userAgent = $"S2 Games/Heroes of Newerth/{_version}/{platform}/{arch}";

        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", _userAgent);
        _httpClient.DefaultRequestHeaders.Add("Accept", "*/*");
        _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Server-Launcher", "HoNfigurator");
    }

    public async Task<MasterServerAuthResponse> AuthenticateAsync(
        string username, 
        string password, 
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Authenticating with master server as {Username}", username);

            // MD5 hash the password like Python does
            var passwordHash = GetMd5Hash(password);
            
            // Format: "username:" (with colon suffix)
            var loginFormatted = username.EndsWith(":") ? username : username + ":";

            // Use replay_auth endpoint like Python
            var url = $"{_masterServerUrl}/server_requester.php?f=replay_auth";
            
            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("login", loginFormatted),
                new KeyValuePair<string, string>("pass", passwordHash),
            });

            var response = await _httpClient.PostAsync(url, content, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            
            _logger.LogDebug("Master server response ({StatusCode}): {Response}", response.StatusCode, responseBody);

            if (!response.IsSuccessStatusCode)
            {
                var errorMsg = response.StatusCode switch
                {
                    System.Net.HttpStatusCode.Unauthorized => "Credentials incorrect",
                    System.Net.HttpStatusCode.Forbidden => "Credentials correct, but no permissions to host",
                    _ when (int)response.StatusCode >= 500 => "Server side issue, master server is probably down",
                    _ => $"Master server returned {response.StatusCode}"
                };
                
                _logger.LogError("Authentication failed: {Error}", errorMsg);
                OnAuthenticationFailed?.Invoke(errorMsg);
                return new MasterServerAuthResponse { Success = false, Error = errorMsg };
            }

            // Parse PHP serialized response
            var authResult = ParsePhpAuthResponse(responseBody);

            if (authResult.Success)
            {
                IsAuthenticated = true;
                ServerId = authResult.ServerId;
                SessionId = authResult.SessionId;
                ChatServerHost = authResult.ChatServerHost;
                ChatServerPort = authResult.ChatServerPort;

                _logger.LogInformation("Authenticated to MasterServer. Server ID: {ServerId}, Chat: {ChatHost}:{ChatPort}", 
                    ServerId, ChatServerHost, ChatServerPort);
                OnAuthenticated?.Invoke();
            }
            else
            {
                _logger.LogWarning("Authentication failed: {Error}", authResult.Error);
                OnAuthenticationFailed?.Invoke(authResult.Error);
            }

            return authResult;
        }
        catch (Exception ex)
        {
            var errorMsg = $"Authentication error: {ex.Message}";
            _logger.LogError(ex, "Authentication failed");
            OnAuthenticationFailed?.Invoke(errorMsg);
            return new MasterServerAuthResponse { Success = false, Error = errorMsg };
        }
    }

    /// <summary>
    /// Authenticates a Game Server using new_session endpoint.
    /// Matches NEXUS ServerRequesterController.HandleServerAuthentication
    /// </summary>
    public async Task<GameServerAuthResponse> AuthenticateGameServerAsync(
        string hostAccount, 
        int serverInstance, 
        string password,
        int port,
        string serverName,
        string description,
        string location,
        string ipAddress,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Format login as "account:instance" per NEXUS
            var loginFormatted = $"{hostAccount}:{serverInstance}";
            var passwordHash = GetMd5Hash(password);

            _logger.LogInformation("Registering game server {ServerName} as {Login}", serverName, loginFormatted);

            var url = $"{_masterServerUrl}/server_requester.php?f=new_session";
            
            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("login", loginFormatted),
                new KeyValuePair<string, string>("pass", passwordHash),
                new KeyValuePair<string, string>("port", port.ToString()),
                new KeyValuePair<string, string>("name", serverName),
                new KeyValuePair<string, string>("desc", description),
                new KeyValuePair<string, string>("location", location),
                new KeyValuePair<string, string>("ip", ipAddress),
            });

            var response = await _httpClient.PostAsync(url, content, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            
            _logger.LogDebug("Game server auth response ({StatusCode}): {Response}", response.StatusCode, responseBody);

            if (!response.IsSuccessStatusCode)
            {
                var errorMsg = response.StatusCode switch
                {
                    System.Net.HttpStatusCode.Unauthorized => "Account is not a Server Host",
                    System.Net.HttpStatusCode.NotFound => "Account not found",
                    _ when (int)response.StatusCode >= 500 => "Master server error",
                    _ => $"Master server returned {response.StatusCode}"
                };
                
                return new GameServerAuthResponse { Success = false, Error = errorMsg };
            }

            // Parse PHP serialized response - same format but with leaverthreshold
            var session = ExtractPhpValue(responseBody, "session");
            var serverIdStr = ExtractPhpValue(responseBody, "server_id", isInt: true);
            var chatAddress = ExtractPhpValue(responseBody, "chat_address");
            var chatPortStr = ExtractPhpValue(responseBody, "chat_port", isInt: true);
            var leaverStr = ExtractPhpValue(responseBody, "leaverthreshold");

            if (!string.IsNullOrEmpty(session) && !string.IsNullOrEmpty(serverIdStr))
            {
                var result = new GameServerAuthResponse
                {
                    Success = true,
                    ServerId = int.TryParse(serverIdStr, out var sid) ? sid : 0,
                    SessionId = session,
                    ChatServerHost = chatAddress ?? "chat.kongor.net",
                    ChatServerPort = int.TryParse(chatPortStr, out var cp) ? cp : 11032,
                    LeaverThreshold = double.TryParse(leaverStr, out var lt) ? lt : 0.05
                };

                _logger.LogInformation("Game server registered. ID: {ServerId}, Chat: {ChatHost}:{ChatPort}",
                    result.ServerId, result.ChatServerHost, result.ChatServerPort);
                
                return result;
            }

            return new GameServerAuthResponse { Success = false, Error = "Failed to parse response" };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Game server authentication failed");
            return new GameServerAuthResponse { Success = false, Error = ex.Message };
        }
    }

    public void Disconnect()
    {
        IsAuthenticated = false;
        ServerId = null;
        SessionId = null;
        ChatServerHost = null;
        ChatServerPort = null;
        OnDisconnected?.Invoke();
        _logger.LogInformation("Disconnected from MasterServer");
    }

    public async Task<ReplayUploadResponse> UploadReplayAsync(
        int matchId, 
        string filePath, 
        CancellationToken cancellationToken = default)
    {
        if (!IsAuthenticated || string.IsNullOrEmpty(SessionId))
        {
            return new ReplayUploadResponse { Success = false, Error = "Not authenticated" };
        }

        try
        {
            if (!File.Exists(filePath))
            {
                return new ReplayUploadResponse { Success = false, Error = "Replay file not found" };
            }

            _logger.LogInformation("Uploading replay for match {MatchId}", matchId);

            using var content = new MultipartFormDataContent();
            using var fileStream = File.OpenRead(filePath);
            using var fileContent = new StreamContent(fileStream);

            content.Add(new StringContent(SessionId!), "cookie");
            content.Add(new StringContent(matchId.ToString()), "match_id");
            content.Add(fileContent, "file", Path.GetFileName(filePath));

            var response = await _httpClient.PostAsync(
                $"{_masterServerUrl}/server_requester.php?f=sm_upload_request",
                content,
                cancellationToken);

            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogDebug("Replay upload response: {Response}", responseBody);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Replay uploaded successfully for match {MatchId}", matchId);
                return new ReplayUploadResponse { Success = true };
            }

            return new ReplayUploadResponse { Success = false, Error = "Upload failed" };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload replay for match {MatchId}", matchId);
            return new ReplayUploadResponse { Success = false, Error = ex.Message };
        }
    }

    public async Task<bool> ValidateSessionAsync(CancellationToken cancellationToken = default)
    {
        if (!IsAuthenticated || string.IsNullOrEmpty(SessionId))
        {
            return false;
        }

        try
        {
            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("cookie", SessionId!),
            });

            var response = await _httpClient.PostAsync(
                $"{_masterServerUrl}/server_requester.php?f=validate_session",
                content,
                cancellationToken);

            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Session validation failed");
            return false;
        }
    }

    private MasterServerAuthResponse ParsePhpAuthResponse(string response)
    {
        try
        {
            // PHP serialized format example from NEXUS:
            // a:N:{s:9:"server_id";i:123;s:8:"official";i:1;s:7:"session";s:36:"...";s:12:"chat_address";s:15:"...";s:9:"chat_port";i:11031;s:15:"cdn_upload_host";s:10:"kongor.net";s:17:"cdn_upload_target";s:6:"upload";}
            
            var serverIdStr = ExtractPhpValue(response, "server_id", isInt: true);
            var session = ExtractPhpValue(response, "session");
            var chatAddress = ExtractPhpValue(response, "chat_address");
            var chatPortStr = ExtractPhpValue(response, "chat_port", isInt: true);
            var officialStr = ExtractPhpValue(response, "official", isInt: true);
            var cdnUploadHost = ExtractPhpValue(response, "cdn_upload_host");
            var cdnUploadTarget = ExtractPhpValue(response, "cdn_upload_target");

            if (!string.IsNullOrEmpty(serverIdStr) && !string.IsNullOrEmpty(session))
            {
                return new MasterServerAuthResponse
                {
                    Success = true,
                    ServerId = int.TryParse(serverIdStr, out var sid) ? sid : 0,
                    SessionId = session,
                    ChatServerHost = chatAddress ?? "chat.kongor.net",
                    ChatServerPort = int.TryParse(chatPortStr, out var cp) ? cp : 11031,
                    IsOfficial = officialStr == "1",
                    CdnUploadHost = cdnUploadHost,
                    CdnUploadTarget = cdnUploadTarget
                };
            }

            // Check for error message
            if (response.Contains("error") || response.Contains("Invalid") || response.Contains("failed"))
            {
                return new MasterServerAuthResponse { Success = false, Error = response };
            }

            return new MasterServerAuthResponse { Success = false, Error = "Failed to parse response: " + response };
        }
        catch (Exception ex)
        {
            return new MasterServerAuthResponse { Success = false, Error = $"Parse error: {ex.Message}" };
        }
    }

    private static string? ExtractPhpValue(string response, string key, bool isInt = false)
    {
        // For string: s:N:"key";s:N:"value";
        // For int: s:N:"key";i:value;
        
        var keyPattern = $"\"{key}\"";
        var keyIndex = response.IndexOf(keyPattern);
        if (keyIndex == -1) return null;

        var afterKey = response.Substring(keyIndex + keyPattern.Length);
        
        if (isInt)
        {
            // Look for ;i:value;
            var match = Regex.Match(afterKey, @";i:(\d+);");
            return match.Success ? match.Groups[1].Value : null;
        }
        else
        {
            // Look for ;s:N:"value";
            var match = Regex.Match(afterKey, @";s:\d+:""([^""]*)"";");
            return match.Success ? match.Groups[1].Value : null;
        }
    }

    private static string GetMd5Hash(string input)
    {
        var inputBytes = Encoding.UTF8.GetBytes(input);
        var hashBytes = MD5.HashData(inputBytes);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _httpClient.Dispose();
    }
}

