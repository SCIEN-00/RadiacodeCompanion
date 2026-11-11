using System.Buffers.Binary;

namespace Radiacode.Core;

/// <summary>
/// Reassembles a response that arrives as a series of BLE notifications: the first chunk starts with a
/// little-endian u32 payload length, the payload follows across as many chunks as needed.
/// </summary>
public sealed class NotificationAssembler
{
    private const int MaxPayload = 1 << 20;
    private byte[]? _payload;
    private int _filled;

    /// <summary>Drops any partially received response.</summary>
    public void Reset()
    {
        _payload = null;
        _filled = 0;
    }

    /// <summary>Adds one notification; returns the complete payload (without the length prefix) once it is all there.</summary>
    public byte[]? Append(ReadOnlySpan<byte> chunk)
    {
        if (_payload is null)
        {
            if (chunk.Length < 4) throw new ProtocolException($"first BLE chunk too short ({chunk.Length} bytes)");
            int length = BinaryPrimitives.ReadInt32LittleEndian(chunk);
            if (length < 0 || length > MaxPayload) throw new ProtocolException($"implausible BLE response length {length}");
            _payload = new byte[length];
            _filled = 0;
            chunk = chunk[4..];
        }

        if (chunk.Length > _payload.Length - _filled)
            throw new ProtocolException($"BLE response overrun: {_filled + chunk.Length} of {_payload.Length} bytes");
        chunk.CopyTo(_payload.AsSpan(_filled));
        _filled += chunk.Length;
        if (_filled < _payload.Length) return null;

        var done = _payload;
        Reset();
        return done;
    }
}
