namespace HoNfigurator.Core.Protocol;

/// <summary>
/// Chat protocol constants matching NEXUS/Project KONGOR's chat server protocol.
/// Based on: https://github.com/shawwn/hon chatserver_protocol.h
/// </summary>
public static class ChatProtocol
{
    /// <summary>
    /// The Version Which Official Game Clients, Match Servers, And Server Managers Expect
    /// </summary>
    public const uint CHAT_PROTOCOL_EXTERNAL_VERSION = 68;

    /// <summary>
    /// The Version Used Internally For Keeping Track Of Custom Changes To The Chat Server
    /// </summary>
    public const uint CHAT_PROTOCOL_INTERNAL_VERSION = 69;

    /// <summary>
    /// General chat commands for channels, whispers, clans, etc.
    /// </summary>
    public static class Command
    {
        public const ushort CHAT_CMD_CHANNEL_MSG = 0x0003;
        public const ushort CHAT_CMD_CHANGED_CHANNEL = 0x0004;
        public const ushort CHAT_CMD_JOINED_CHANNEL = 0x0005;
        public const ushort CHAT_CMD_LEFT_CHANNEL = 0x0006;
        public const ushort CHAT_CMD_DISCONNECTED = 0x0007;
        public const ushort CHAT_CMD_WHISPER = 0x0008;
        public const ushort CHAT_CMD_WHISPER_FAILED = 0x0009;
        public const ushort CHAT_CMD_LAST_KNOWN_GAME_SERVER = 0x000A;
        public const ushort CHAT_CMD_INITIAL_STATUS = 0x000B;
        public const ushort CHAT_CMD_UPDATE_STATUS = 0x000C;
        public const ushort CHAT_CMD_REQUEST_BUDDY_ADD = 0x000D;
        public const ushort CHAT_CMD_NOTIFY_BUDDY_REMOVE = 0x000E;
        public const ushort CHAT_CMD_JOINING_GAME = 0x000F;
        public const ushort CHAT_CMD_JOINED_GAME = 0x0010;
        public const ushort CHAT_CMD_LEFT_GAME = 0x0011;
        public const ushort CHAT_CMD_CLAN_WHISPER = 0x0013;
        public const ushort CHAT_CMD_CLAN_WHISPER_FAILED = 0x0014;
        public const ushort CHAT_CMD_FLOODING = 0x001B;
        public const ushort CHAT_CMD_IM = 0x001C;
        public const ushort CHAT_CMD_IM_FAILED = 0x001D;
        public const ushort CHAT_CMD_JOIN_CHANNEL = 0x001E;
        public const ushort CHAT_CMD_WHISPER_BUDDIES = 0x0020;
        public const ushort CHAT_CMD_MAX_CHANNELS = 0x0021;
        public const ushort CHAT_CMD_LEAVE_CHANNEL = 0x0022;
        public const ushort CHAT_CMD_INVITE_USER_ID = 0x0023;
        public const ushort CHAT_CMD_INVITE_USER_NAME = 0x0024;
        public const ushort CHAT_CMD_INVITED_TO_SERVER = 0x0025;
        public const ushort CHAT_CMD_USER_INFO = 0x002A;
        public const ushort CHAT_CMD_CHANNEL_UPDATE = 0x002F;
        public const ushort CHAT_CMD_CHANNEL_TOPIC = 0x0030;
        public const ushort CHAT_CMD_CHANNEL_KICK = 0x0031;
        public const ushort CHAT_CMD_CHANNEL_BAN = 0x0032;
        public const ushort CHAT_CMD_MESSAGE_ALL = 0x0039;
        public const ushort CHAT_CMD_NAME_CHANGE = 0x005A;
        public const ushort CHAT_CMD_AUTO_MATCH_CONNECT = 0x0062;
        public const ushort CHAT_CMD_CHAT_ROLL = 0x0064;
        public const ushort CHAT_CMD_CHAT_EMOTE = 0x0065;
        public const ushort CHAT_CMD_PLAYER_COUNT = 0x0068;
        public const ushort CHAT_CMD_SERVER_NOT_IDLE = 0x0069;
        public const ushort CHAT_CMD_OPTIONS = 0x00C0;
        public const ushort CHAT_CMD_LOGOUT = 0x00C1;
        public const ushort CHAT_CMD_NEW_MESSAGES = 0x00C2;
    }

    /// <summary>
    /// Bidirectional keepalive commands
    /// </summary>
    public static class Bidirectional
    {
        public const ushort NET_CHAT_PING = 0x2A00;
        public const ushort NET_CHAT_PONG = 0x2A01;
    }

    /// <summary>
    /// Client to Chat Server commands
    /// </summary>
    public static class ClientToChatServer
    {
        public const ushort NET_CHAT_CL_CONNECT = 0x0C00;
        public const ushort NET_CHAT_CL_GET_CHANNEL_LIST = 0x0C01;
        public const ushort NET_CHAT_CL_GET_USER_STATUS = 0x0C05;
        public const ushort NET_CHAT_CL_ADMIN_KICK = 0x0C08;
        public const ushort NET_CHAT_CL_REFRESH_UPGRADES = 0x0C09;
    }

    /// <summary>
    /// Chat Server to Client responses
    /// </summary>
    public static class ChatServerToClient
    {
        public const ushort NET_CHAT_CL_ACCEPT = 0x1C00;
        public const ushort NET_CHAT_CL_REJECT = 0x1C01;
        public const ushort NET_CHAT_CL_CHANNEL_INFO = 0x1C02;
        public const ushort NET_CHAT_CL_USER_STATUS = 0x1C08;
        public const ushort NET_CHAT_CL_GAME_LOBBY_JOINED = 0x1C09;
        public const ushort NET_CHAT_CL_GAME_LOBBY_LEFT = 0x1C0A;
        public const ushort NET_CHAT_CL_GAME_LOBBY_UPDATE = 0x1C0B;
        public const ushort NET_CHAT_CL_GAME_LOBBY_LAUNCH_GAME = 0x1C0F;
    }

    /// <summary>
    /// Game Server (Match Server) to Chat Server commands
    /// </summary>
    public static class GameServerToChatServer
    {
        public const ushort NET_CHAT_GS_CONNECT = 0x0500;
        public const ushort NET_CHAT_GS_DISCONNECT = 0x0501;
        public const ushort NET_CHAT_GS_STATUS = 0x0502;
        public const ushort NET_CHAT_GS_ANNOUNCE_MATCH = 0x0503;
        public const ushort NET_CHAT_GS_ABANDON_MATCH = 0x0504;
        public const ushort NET_CHAT_GS_MATCH_STARTED = 0x0505;
        public const ushort NET_CHAT_GS_REMIND_PLAYER = 0x0506;
        public const ushort NET_CHAT_GS_NOT_IDLE = 0x0508;
        public const ushort NET_CHAT_GS_MATCH_ABORTED = 0x0509;
        public const ushort NET_CHAT_GS_SAVE_DISCONNECT_REASON = 0x0510;
        public const ushort NET_CHAT_GS_REPORT_MISSING_PLAYERS = 0x0511;
        public const ushort NET_CHAT_GS_MATCH_ID_RESULT = 0x0512;
        public const ushort NET_CHAT_GS_CLIENT_AUTH_RESULT = 0x0513;
        public const ushort NET_CHAT_GS_STAT_SUBMISSION_RESULT = 0x0514;
        public const ushort NET_CHAT_GS_MATCH_ENDED = 0x0515;
        public const ushort NET_CHAT_GS_MATCH_ONGOING = 0x0516;
        public const ushort NET_CHAT_GS_PLAYER_BENEFITS = 0x0517;
        public const ushort NET_CHAT_GS_REPORT_LEAVER = 0x0518;
    }

    /// <summary>
    /// Chat Server to Game Server (Match Server) responses
    /// </summary>
    public static class ChatServerToGameServer
    {
        public const ushort NET_CHAT_GS_ACCEPT = 0x1500;
        public const ushort NET_CHAT_GS_REJECT = 0x1501;
        public const ushort NET_CHAT_GS_CREATE_MATCH = 0x1502;
        public const ushort NET_CHAT_GS_END_MATCH = 0x1503;
        public const ushort NET_CHAT_GS_REMOTE_COMMAND = 0x1504;
        public const ushort NET_CHAT_GS_OPTIONS = 0x1505;
        public const ushort NET_CHAT_GS_DYNAMIC_PRODUCTS = 0x1506;
    }

    /// <summary>
    /// Server Manager to Chat Server commands
    /// </summary>
    public static class ServerManagerToChatServer
    {
        public const ushort NET_CHAT_SM_CONNECT = 0x1600;
        public const ushort NET_CHAT_SM_DISCONNECT = 0x1601;
        public const ushort NET_CHAT_SM_STATUS = 0x1602;
        public const ushort NET_CHAT_SM_UPLOAD_UPDATE = 0x1603;
    }

    /// <summary>
    /// Chat Server to Server Manager responses
    /// </summary>
    public static class ChatServerToServerManager
    {
        public const ushort NET_CHAT_SM_ACCEPT = 0x1700;
        public const ushort NET_CHAT_SM_REJECT = 0x1701;
        public const ushort NET_CHAT_SM_REMOTE_COMMAND = 0x1702;
        public const ushort NET_CHAT_SM_OPTIONS = 0x1703;
        public const ushort NET_CHAT_SM_UPLOAD_REQUEST = 0x1704;
    }

    /// <summary>
    /// Master Server to Chat Server commands
    /// </summary>
    public static class MasterServerToChatServer
    {
        public const ushort NET_CHAT_WEB_UPLOAD_REQUEST = 0x1800;
    }

    /// <summary>
    /// Chat Server to Master Server responses
    /// </summary>
    public static class ChatServerToMasterServer
    {
        public const ushort NET_CHAT_WEB_ACCEPT = 0x2800;
        public const ushort NET_CHAT_WEB_REJECT = 0x2801;
    }
}

/// <summary>
/// Server status values matching NEXUS ServerStatus enum
/// </summary>
public enum NexusServerStatus : byte
{
    /// <summary>Server is sleeping/inactive</summary>
    Sleeping = 0,
    /// <summary>Server is idle and ready</summary>
    Idle = 1,
    /// <summary>Server is loading a match</summary>
    Loading = 2,
    /// <summary>Server has an active match</summary>
    Active = 3,
    /// <summary>Server has crashed</summary>
    Crashed = 4,
    /// <summary>Server was killed</summary>
    Killed = 5
}

/// <summary>
/// Arranged match types matching NEXUS ArrangedMatchType enum
/// </summary>
public enum ArrangedMatchType : byte
{
    /// <summary>Public game - anyone can join</summary>
    Public = 0,
    /// <summary>Matchmaking game - TMM arranged</summary>
    Matchmaking = 1,
    /// <summary>Tournament or league match</summary>
    Tournament = 2,
    /// <summary>Bot match - local practice</summary>
    BotMatch = 3,
    /// <summary>Custom match</summary>
    Custom = 4
}

/// <summary>
/// Replay upload status values
/// </summary>
public enum ReplayUploadStatusCode : byte
{
    NotFound = 0x01,
    AlreadyUploaded = 0x02,
    InQueue = 0x03,
    Uploading = 0x04,
    HaveReplay = 0x05,
    UploadingNow = 0x06,
    UploadComplete = 0x07
}
