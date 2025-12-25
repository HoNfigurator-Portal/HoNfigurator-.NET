using FluentAssertions;
using HoNfigurator.Core.Protocol;

namespace HoNfigurator.Tests.Protocol;

public class ChatBufferTests
{
    #region ChatBuffer Write Tests

    [Fact]
    public void ChatBuffer_WriteString_ShouldWriteCorrectly()
    {
        var buffer = new ChatBuffer();
        buffer.WriteString("Hello");
        
        var data = buffer.ToArray();
        
        // String format: null-terminated UTF-8 string
        data.Length.Should().Be(6); // 5 for "Hello" + 1 for null terminator
        data[0].Should().Be((byte)'H');
        data[5].Should().Be(0); // Null terminator
    }

    [Fact]
    public void ChatBuffer_WriteEmptyString_ShouldWriteNullTerminator()
    {
        var buffer = new ChatBuffer();
        buffer.WriteString(string.Empty);
        
        var data = buffer.ToArray();
        
        data.Length.Should().Be(1); // Just null terminator
        data[0].Should().Be(0); // Null terminator
    }

    [Fact]
    public void ChatBuffer_WriteUInt8_ShouldWriteSingleByte()
    {
        var buffer = new ChatBuffer();
        buffer.WriteUInt8(0x42);
        
        var data = buffer.ToArray();
        
        data.Should().HaveCount(1);
        data[0].Should().Be(0x42);
    }

    [Fact]
    public void ChatBuffer_WriteInt16_ShouldBeLittleEndian()
    {
        var buffer = new ChatBuffer();
        buffer.WriteInt16(0x1234);
        
        var data = buffer.ToArray();
        
        data.Should().HaveCount(2);
        data[0].Should().Be(0x34); // Low byte first (little-endian)
        data[1].Should().Be(0x12); // High byte second
    }

    [Fact]
    public void ChatBuffer_WriteInt32_ShouldBeLittleEndian()
    {
        var buffer = new ChatBuffer();
        buffer.WriteInt32(0x12345678);
        
        var data = buffer.ToArray();
        
        data.Should().HaveCount(4);
        data[0].Should().Be(0x78);
        data[1].Should().Be(0x56);
        data[2].Should().Be(0x34);
        data[3].Should().Be(0x12);
    }

    [Fact]
    public void ChatBuffer_WriteMultipleValues_ShouldAppendCorrectly()
    {
        var buffer = new ChatBuffer();
        buffer.WriteUInt8(0x01);
        buffer.WriteInt16(0x0203);
        buffer.WriteInt32(0x04050607);
        
        var data = buffer.ToArray();
        
        data.Should().HaveCount(7);
        data[0].Should().Be(0x01);
        data[1].Should().Be(0x03);
        data[2].Should().Be(0x02);
        data[3].Should().Be(0x07);
        data[4].Should().Be(0x06);
        data[5].Should().Be(0x05);
        data[6].Should().Be(0x04);
    }

    [Fact]
    public void ChatBuffer_Clear_ShouldResetBuffer()
    {
        var buffer = new ChatBuffer();
        buffer.WriteInt32(0x12345678);
        buffer.Clear();
        
        buffer.Length.Should().Be(0);
        buffer.ToArray().Should().BeEmpty();
    }

    [Fact]
    public void ChatBuffer_WriteCommand_ShouldWriteUInt16()
    {
        var buffer = new ChatBuffer();
        buffer.WriteCommand(0x0500); // NET_CHAT_GS_CONNECT
        
        var data = buffer.ToArray();
        
        data.Should().HaveCount(2);
        data[0].Should().Be(0x00); // Low byte
        data[1].Should().Be(0x05); // High byte
    }

    #endregion

    #region ChatBufferReader Read Tests

    [Fact]
    public void ChatBufferReader_ReadUInt8_ShouldReadCorrectly()
    {
        var data = new byte[] { 0x42 };
        var reader = new ChatBufferReader(data);
        
        reader.ReadUInt8().Should().Be(0x42);
    }

    [Fact]
    public void ChatBufferReader_ReadInt16_ShouldBeLittleEndian()
    {
        var data = new byte[] { 0x34, 0x12 };
        var reader = new ChatBufferReader(data);
        
        reader.ReadInt16().Should().Be(0x1234);
    }

    [Fact]
    public void ChatBufferReader_ReadInt32_ShouldBeLittleEndian()
    {
        var data = new byte[] { 0x78, 0x56, 0x34, 0x12 };
        var reader = new ChatBufferReader(data);
        
        reader.ReadInt32().Should().Be(0x12345678);
    }

    [Fact]
    public void ChatBufferReader_ReadString_ShouldReadCorrectly()
    {
        // Null-terminated string
        var data = new byte[] { (byte)'H', (byte)'e', (byte)'l', (byte)'l', (byte)'o', 0x00 };
        var reader = new ChatBufferReader(data);
        
        reader.ReadString().Should().Be("Hello");
    }

    [Fact]
    public void ChatBufferReader_ReadEmptyString_ShouldReturnEmpty()
    {
        var data = new byte[] { 0x00 }; // Just null terminator
        var reader = new ChatBufferReader(data);
        
        reader.ReadString().Should().BeEmpty();
    }

    [Fact]
    public void ChatBufferReader_ReadMultipleValues_ShouldAdvancePosition()
    {
        var data = new byte[] { 0x01, 0x03, 0x02, 0x07, 0x06, 0x05, 0x04 };
        var reader = new ChatBufferReader(data);
        
        reader.ReadUInt8().Should().Be(0x01);
        reader.ReadInt16().Should().Be(0x0203);
        reader.ReadInt32().Should().Be(0x04050607);
    }

    [Fact]
    public void ChatBufferReader_HasMore_ShouldReturnCorrectly()
    {
        var data = new byte[] { 0x01, 0x02, 0x03 };
        var reader = new ChatBufferReader(data);
        
        reader.HasMore.Should().BeTrue();
        reader.ReadUInt8();
        reader.HasMore.Should().BeTrue();
        reader.ReadUInt8();
        reader.HasMore.Should().BeTrue();
        reader.ReadUInt8();
        reader.HasMore.Should().BeFalse();
    }

    [Fact]
    public void ChatBufferReader_Remaining_ShouldTrackCorrectly()
    {
        var data = new byte[] { 0x01, 0x02, 0x03, 0x04 };
        var reader = new ChatBufferReader(data);
        
        reader.Remaining.Should().Be(4);
        reader.ReadUInt8();
        reader.Remaining.Should().Be(3);
        reader.ReadInt16();
        reader.Remaining.Should().Be(1);
    }

    [Fact]
    public void ChatBufferReader_Skip_ShouldAdvancePosition()
    {
        var data = new byte[] { 0x01, 0x02, 0x03, 0x04 };
        var reader = new ChatBufferReader(data);
        
        reader.Skip(2);
        reader.ReadUInt8().Should().Be(0x03);
    }

    [Fact]
    public void ChatBufferReader_ReadBytes_ShouldReturnCorrectData()
    {
        var data = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 };
        var reader = new ChatBufferReader(data);
        
        reader.Skip(1);
        var bytes = reader.ReadBytes(3);
        
        bytes.ToArray().Should().Equal(0x02, 0x03, 0x04);
    }

    #endregion

    #region Round-Trip Tests

    [Fact]
    public void ChatBuffer_RoundTrip_UInt8_ShouldPreserveValue()
    {
        var buffer = new ChatBuffer();
        buffer.WriteUInt8(0xAB);
        
        var reader = new ChatBufferReader(buffer.ToArray());
        reader.ReadUInt8().Should().Be(0xAB);
    }

    [Fact]
    public void ChatBuffer_RoundTrip_Int16_ShouldPreserveValue()
    {
        var buffer = new ChatBuffer();
        buffer.WriteInt16(-1234);
        
        var reader = new ChatBufferReader(buffer.ToArray());
        reader.ReadInt16().Should().Be(-1234);
    }

    [Fact]
    public void ChatBuffer_RoundTrip_Int32_ShouldPreserveValue()
    {
        var buffer = new ChatBuffer();
        buffer.WriteInt32(-123456789);
        
        var reader = new ChatBufferReader(buffer.ToArray());
        reader.ReadInt32().Should().Be(-123456789);
    }

    [Fact]
    public void ChatBuffer_RoundTrip_String_ShouldPreserveValue()
    {
        var original = "Hello, 世界! 🎮";
        var buffer = new ChatBuffer();
        buffer.WriteString(original);
        
        var reader = new ChatBufferReader(buffer.ToArray());
        reader.ReadString().Should().Be(original);
    }

    [Fact]
    public void ChatBuffer_RoundTrip_ComplexPacket_ShouldPreserveAllValues()
    {
        // Simulate a typical packet: command + account_id + name + team
        var buffer = new ChatBuffer();
        buffer.WriteCommand(0x0501); // Command
        buffer.WriteInt32(12345);  // Account ID
        buffer.WriteString("TestPlayer"); // Name
        buffer.WriteUInt8(1); // Team

        var data = buffer.ToArray();
        var reader = new ChatBufferReader(data);
        
        reader.ReadUInt16().Should().Be(0x0501);
        reader.ReadInt32().Should().Be(12345);
        reader.ReadString().Should().Be("TestPlayer");
        reader.ReadUInt8().Should().Be(1);
        reader.HasMore.Should().BeFalse();
    }

    #endregion
}

public class ChatProtocolTests
{
    [Fact]
    public void ChatProtocol_GameServerCommands_ShouldBeInCorrectRange()
    {
        // Game server commands should be in 0x0500 range
        ChatProtocol.GameServerToChatServer.NET_CHAT_GS_CONNECT.Should().BeInRange((ushort)0x0500, (ushort)0x05FF);
        ChatProtocol.GameServerToChatServer.NET_CHAT_GS_DISCONNECT.Should().BeInRange((ushort)0x0500, (ushort)0x05FF);
        ChatProtocol.GameServerToChatServer.NET_CHAT_GS_STATUS.Should().BeInRange((ushort)0x0500, (ushort)0x05FF);
    }

    [Fact]
    public void ChatProtocol_ChatServerResponses_ShouldBeInCorrectRange()
    {
        // Chat server responses should be in 0x1500 range
        ChatProtocol.ChatServerToGameServer.NET_CHAT_GS_ACCEPT.Should().BeInRange((ushort)0x1500, (ushort)0x15FF);
        ChatProtocol.ChatServerToGameServer.NET_CHAT_GS_REJECT.Should().BeInRange((ushort)0x1500, (ushort)0x15FF);
    }

    [Fact]
    public void ChatProtocol_Constants_ShouldHaveExpectedValues()
    {
        // Verify key protocol constants
        ChatProtocol.GameServerToChatServer.NET_CHAT_GS_CONNECT.Should().Be(0x0500);
        ChatProtocol.GameServerToChatServer.NET_CHAT_GS_DISCONNECT.Should().Be(0x0501);
        ChatProtocol.GameServerToChatServer.NET_CHAT_GS_STATUS.Should().Be(0x0502);
        ChatProtocol.ChatServerToGameServer.NET_CHAT_GS_ACCEPT.Should().Be(0x1500);
        ChatProtocol.ChatServerToGameServer.NET_CHAT_GS_REJECT.Should().Be(0x1501);
    }

    [Fact]
    public void ChatProtocol_Version_ShouldMatchNexus()
    {
        ChatProtocol.CHAT_PROTOCOL_EXTERNAL_VERSION.Should().Be(68u);
    }

    [Fact]
    public void NexusServerStatus_ShouldHaveCorrectValues()
    {
        ((byte)NexusServerStatus.Sleeping).Should().Be(0);
        ((byte)NexusServerStatus.Idle).Should().Be(1);
        ((byte)NexusServerStatus.Loading).Should().Be(2);
        ((byte)NexusServerStatus.Active).Should().Be(3);
    }

    [Fact]
    public void ArrangedMatchType_ShouldHaveCorrectValues()
    {
        ((byte)ArrangedMatchType.Public).Should().Be(0);
        ((byte)ArrangedMatchType.Matchmaking).Should().Be(1);
        ((byte)ArrangedMatchType.Tournament).Should().Be(2);
    }
}
