using System.Buffers.Binary;
using System.Text;

namespace Radiacode.Core;

/// <summary>Sequential little-endian reader over a protocol payload.</summary>
public sealed class ByteReader
{
    private readonly byte[] _data;
    private int _end;
    private int _pos;

    public ByteReader(byte[] data) : this(data, 0, data.Length) { }

    public ByteReader(byte[] data, int offset, int length)
    {
        _data = data;
        _pos = offset;
        _end = offset + length;
    }

    public int Remaining => _end - _pos;

    public ReadOnlySpan<byte> RemainingSpan => _data.AsSpan(_pos, Remaining);

    /// <summary>Drops the last byte (firmware sometimes appends a trailing 0x00).</summary>
    internal void TrimLastByte() => _end--;

    internal byte LastByte => _data[_end - 1];

    private ReadOnlySpan<byte> Take(int count)
    {
        if (count > Remaining)
            throw new ProtocolException($"Need {count} bytes, only {Remaining} left");
        var s = _data.AsSpan(_pos, count);
        _pos += count;
        return s;
    }

    public byte ReadByte() => Take(1)[0];
    public byte PeekByte(int offset = 0) =>
        offset < Remaining ? _data[_pos + offset] : throw new ProtocolException($"peek past end (+{offset})");
    public sbyte ReadSByte() => unchecked((sbyte)Take(1)[0]);
    public ushort ReadUInt16() => BinaryPrimitives.ReadUInt16LittleEndian(Take(2));
    public short ReadInt16() => BinaryPrimitives.ReadInt16LittleEndian(Take(2));
    public uint ReadUInt32() => BinaryPrimitives.ReadUInt32LittleEndian(Take(4));
    public int ReadInt32() => BinaryPrimitives.ReadInt32LittleEndian(Take(4));
    public float ReadSingle() => BinaryPrimitives.ReadSingleLittleEndian(Take(4));
    public byte[] ReadBytes(int count) => Take(count).ToArray();
    public void Skip(int count) => Take(count);

    /// <summary>Length-prefixed (u8) ASCII string.</summary>
    public string ReadString()
    {
        int len = ReadByte();
        return Encoding.ASCII.GetString(Take(len));
    }

    public byte[] ReadToEnd() => Take(Remaining).ToArray();
}

public sealed class ProtocolException(string message) : Exception(message);
