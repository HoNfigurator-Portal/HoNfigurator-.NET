using System.Buffers.Binary;
using System.Text;

namespace HoNfigurator.Core.Protocol;

/// <summary>
/// Binary buffer writer matching NEXUS ChatBuffer for building chat protocol packets.
/// All multi-byte integers are written in little-endian format.
/// Strings are null-terminated UTF-8.
/// </summary>
public class ChatBuffer
{
    private readonly List<byte> _buffer;

    public ChatBuffer(int initialCapacity = 256)
    {
        _buffer = new List<byte>(initialCapacity);
    }

    /// <summary>
    /// Gets the current buffer as a byte array
    /// </summary>
    public byte[] ToArray() => _buffer.ToArray();

    /// <summary>
    /// Gets the current length of the buffer
    /// </summary>
    public int Length => _buffer.Count;

    /// <summary>
    /// Clears the buffer
    /// </summary>
    public void Clear() => _buffer.Clear();

    /// <summary>
    /// Writes a command/packet type (ushort, little-endian)
    /// </summary>
    public ChatBuffer WriteCommand(ushort command)
    {
        WriteUInt16(command);
        return this;
    }

    /// <summary>
    /// Writes a signed byte
    /// </summary>
    public ChatBuffer WriteInt8(sbyte value)
    {
        _buffer.Add((byte)value);
        return this;
    }

    /// <summary>
    /// Writes an unsigned byte
    /// </summary>
    public ChatBuffer WriteUInt8(byte value)
    {
        _buffer.Add(value);
        return this;
    }

    /// <summary>
    /// Writes a signed 16-bit integer (little-endian)
    /// </summary>
    public ChatBuffer WriteInt16(short value)
    {
        Span<byte> bytes = stackalloc byte[2];
        BinaryPrimitives.WriteInt16LittleEndian(bytes, value);
        _buffer.AddRange(bytes.ToArray());
        return this;
    }

    /// <summary>
    /// Writes an unsigned 16-bit integer (little-endian)
    /// </summary>
    public ChatBuffer WriteUInt16(ushort value)
    {
        Span<byte> bytes = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, value);
        _buffer.AddRange(bytes.ToArray());
        return this;
    }

    /// <summary>
    /// Writes a signed 32-bit integer (little-endian)
    /// </summary>
    public ChatBuffer WriteInt32(int value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        _buffer.AddRange(bytes.ToArray());
        return this;
    }

    /// <summary>
    /// Writes an unsigned 32-bit integer (little-endian)
    /// </summary>
    public ChatBuffer WriteUInt32(uint value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        _buffer.AddRange(bytes.ToArray());
        return this;
    }

    /// <summary>
    /// Writes a signed 64-bit integer (little-endian)
    /// </summary>
    public ChatBuffer WriteInt64(long value)
    {
        Span<byte> bytes = stackalloc byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(bytes, value);
        _buffer.AddRange(bytes.ToArray());
        return this;
    }

    /// <summary>
    /// Writes an unsigned 64-bit integer (little-endian)
    /// </summary>
    public ChatBuffer WriteUInt64(ulong value)
    {
        Span<byte> bytes = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(bytes, value);
        _buffer.AddRange(bytes.ToArray());
        return this;
    }

    /// <summary>
    /// Writes a null-terminated UTF-8 string
    /// </summary>
    public ChatBuffer WriteString(string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            _buffer.AddRange(Encoding.UTF8.GetBytes(value));
        }
        _buffer.Add(0x00); // Null terminator
        return this;
    }

    /// <summary>
    /// Writes raw bytes without modification
    /// </summary>
    public ChatBuffer WriteBytes(byte[] data)
    {
        _buffer.AddRange(data);
        return this;
    }

    /// <summary>
    /// Writes raw bytes without modification
    /// </summary>
    public ChatBuffer WriteBytes(ReadOnlySpan<byte> data)
    {
        _buffer.AddRange(data.ToArray());
        return this;
    }

    /// <summary>
    /// Writes a boolean as a single byte (0 or 1)
    /// </summary>
    public ChatBuffer WriteBool(bool value)
    {
        _buffer.Add(value ? (byte)1 : (byte)0);
        return this;
    }

    /// <summary>
    /// Builds a complete packet with length prefix.
    /// Format: [length:2][packetType:2][data...]
    /// Length includes the packet type (2 bytes) plus data length.
    /// </summary>
    public static byte[] BuildPacket(ushort packetType, byte[] data)
    {
        var buffer = new ChatBuffer(data.Length + 4);
        
        // Write length (packet type + data)
        buffer.WriteUInt16((ushort)(2 + data.Length));
        
        // Write packet type
        buffer.WriteCommand(packetType);
        
        // Write data
        buffer.WriteBytes(data);
        
        return buffer.ToArray();
    }

    /// <summary>
    /// Builds a complete packet from this buffer with length prefix.
    /// Format: [length:2][data...]
    /// </summary>
    public byte[] BuildWithLengthPrefix()
    {
        var result = new byte[2 + _buffer.Count];
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(0, 2), (ushort)_buffer.Count);
        _buffer.CopyTo(result, 2);
        return result;
    }
}

/// <summary>
/// Binary buffer reader for parsing chat protocol packets.
/// All multi-byte integers are read in little-endian format.
/// </summary>
public ref struct ChatBufferReader
{
    private readonly ReadOnlySpan<byte> _data;
    private int _position;

    public ChatBufferReader(ReadOnlySpan<byte> data)
    {
        _data = data;
        _position = 0;
    }

    /// <summary>
    /// Gets the current position in the buffer
    /// </summary>
    public int Position => _position;

    /// <summary>
    /// Gets the remaining bytes in the buffer
    /// </summary>
    public int Remaining => _data.Length - _position;

    /// <summary>
    /// Checks if there are more bytes to read
    /// </summary>
    public bool HasMore => _position < _data.Length;

    /// <summary>
    /// Reads a command/packet type (ushort, little-endian)
    /// </summary>
    public ushort ReadCommand() => ReadUInt16();

    /// <summary>
    /// Reads a signed byte
    /// </summary>
    public sbyte ReadInt8()
    {
        if (_position >= _data.Length)
            throw new InvalidOperationException("Buffer underflow");
        return (sbyte)_data[_position++];
    }

    /// <summary>
    /// Reads an unsigned byte
    /// </summary>
    public byte ReadUInt8()
    {
        if (_position >= _data.Length)
            throw new InvalidOperationException("Buffer underflow");
        return _data[_position++];
    }

    /// <summary>
    /// Reads a signed 16-bit integer (little-endian)
    /// </summary>
    public short ReadInt16()
    {
        if (_position + 2 > _data.Length)
            throw new InvalidOperationException("Buffer underflow");
        var value = BinaryPrimitives.ReadInt16LittleEndian(_data.Slice(_position, 2));
        _position += 2;
        return value;
    }

    /// <summary>
    /// Reads an unsigned 16-bit integer (little-endian)
    /// </summary>
    public ushort ReadUInt16()
    {
        if (_position + 2 > _data.Length)
            throw new InvalidOperationException("Buffer underflow");
        var value = BinaryPrimitives.ReadUInt16LittleEndian(_data.Slice(_position, 2));
        _position += 2;
        return value;
    }

    /// <summary>
    /// Reads a signed 32-bit integer (little-endian)
    /// </summary>
    public int ReadInt32()
    {
        if (_position + 4 > _data.Length)
            throw new InvalidOperationException("Buffer underflow");
        var value = BinaryPrimitives.ReadInt32LittleEndian(_data.Slice(_position, 4));
        _position += 4;
        return value;
    }

    /// <summary>
    /// Reads an unsigned 32-bit integer (little-endian)
    /// </summary>
    public uint ReadUInt32()
    {
        if (_position + 4 > _data.Length)
            throw new InvalidOperationException("Buffer underflow");
        var value = BinaryPrimitives.ReadUInt32LittleEndian(_data.Slice(_position, 4));
        _position += 4;
        return value;
    }

    /// <summary>
    /// Reads a signed 64-bit integer (little-endian)
    /// </summary>
    public long ReadInt64()
    {
        if (_position + 8 > _data.Length)
            throw new InvalidOperationException("Buffer underflow");
        var value = BinaryPrimitives.ReadInt64LittleEndian(_data.Slice(_position, 8));
        _position += 8;
        return value;
    }

    /// <summary>
    /// Reads an unsigned 64-bit integer (little-endian)
    /// </summary>
    public ulong ReadUInt64()
    {
        if (_position + 8 > _data.Length)
            throw new InvalidOperationException("Buffer underflow");
        var value = BinaryPrimitives.ReadUInt64LittleEndian(_data.Slice(_position, 8));
        _position += 8;
        return value;
    }

    /// <summary>
    /// Reads a null-terminated UTF-8 string
    /// </summary>
    public string ReadString()
    {
        var start = _position;
        while (_position < _data.Length && _data[_position] != 0)
        {
            _position++;
        }
        var str = Encoding.UTF8.GetString(_data.Slice(start, _position - start));
        if (_position < _data.Length) _position++; // Skip null terminator
        return str;
    }

    /// <summary>
    /// Reads a specified number of bytes
    /// </summary>
    public ReadOnlySpan<byte> ReadBytes(int count)
    {
        if (_position + count > _data.Length)
            throw new InvalidOperationException("Buffer underflow");
        var bytes = _data.Slice(_position, count);
        _position += count;
        return bytes;
    }

    /// <summary>
    /// Reads a boolean as a single byte
    /// </summary>
    public bool ReadBool() => ReadUInt8() != 0;

    /// <summary>
    /// Skips a specified number of bytes
    /// </summary>
    public void Skip(int count)
    {
        if (_position + count > _data.Length)
            throw new InvalidOperationException("Buffer underflow");
        _position += count;
    }

    /// <summary>
    /// Reads all remaining bytes
    /// </summary>
    public ReadOnlySpan<byte> ReadRemaining()
    {
        var remaining = _data.Slice(_position);
        _position = _data.Length;
        return remaining;
    }
}
