using System.Buffers.Binary;
using System.Text;

namespace Qualcomm.EmergencyDownload.Transport;

/// <summary>
/// Opcodes carried between the GUI-side <see cref="HelperClient"/> and the privileged
/// helper process. Every request frame is answered by exactly one reply frame; <see cref="Log"/>
/// is an out-of-band notification the server may emit at any time.
/// </summary>
public enum HelperOpcode : byte
{
    Open = 0x01,
    Close = 0x02,
    SendData = 0x03,
    SendLargeData = 0x04,
    SendZlp = 0x05,
    Read = 0x06,
    SetTimeout = 0x07,

    OpenOk = 0x81,
    Ack = 0x82,
    ReadOk = 0x83,
    Log = 0xA0,

    Error = 0xF0,
    TimeoutError = 0xF1,
    BadMessageError = 0xF2,
    BadConnectionError = 0xF3,
}

/// <summary>
/// Binary frame codec: <c>[u8 opcode][u32 BE length][payload]</c>. Both sides of the
/// privileged-helper channel share this codec; higher-level request/reply semantics
/// live in <see cref="HelperClient"/> and the helper's FrameServer.
/// </summary>
internal static class HelperChannel
{
    public const uint MaxPayloadBytes = 128 * 1024 * 1024;

    public static void WriteFrame(Stream stream, HelperOpcode op, ReadOnlySpan<byte> payload)
    {
        if (payload.Length > MaxPayloadBytes)
        {
            throw new InvalidOperationException($"Helper frame payload too large: {payload.Length} bytes");
        }

        Span<byte> header = stackalloc byte[5];
        header[0] = (byte)op;
        BinaryPrimitives.WriteUInt32BigEndian(header[1..], (uint)payload.Length);
        stream.Write(header);
        if (!payload.IsEmpty)
        {
            stream.Write(payload);
        }
        stream.Flush();
    }

    public static bool TryReadFrame(Stream stream, out HelperOpcode op, out byte[] payload)
    {
        Span<byte> header = stackalloc byte[5];
        var read = 0;
        while (read < 5)
        {
            var n = stream.Read(header[read..]);
            if (n == 0)
            {
                op = default;
                payload = [];
                return false;
            }
            read += n;
        }

        op = (HelperOpcode)header[0];
        var length = BinaryPrimitives.ReadUInt32BigEndian(header[1..]);
        if (length > MaxPayloadBytes)
        {
            throw new InvalidOperationException($"Helper frame payload too large: {length} bytes");
        }

        payload = length == 0 ? [] : new byte[length];
        var done = 0;
        while (done < length)
        {
            var n = stream.Read(payload, done, (int)(length - done));
            if (n == 0)
            {
                throw new EndOfStreamException("Helper channel closed mid-frame");
            }
            done += n;
        }
        return true;
    }

    public static byte[] EncodeOpenRequest(string devicePath)
    {
        var pathBytes = Encoding.UTF8.GetBytes(devicePath);
        var buf = new byte[4 + pathBytes.Length];
        BinaryPrimitives.WriteUInt32BigEndian(buf.AsSpan(0, 4), (uint)pathBytes.Length);
        pathBytes.CopyTo(buf.AsSpan(4));
        return buf;
    }

    public static string DecodeOpenRequest(ReadOnlySpan<byte> payload)
    {
        var len = payload.Length < 4
            ? throw new InvalidDataException("OPEN payload too short")
            : BinaryPrimitives.ReadUInt32BigEndian(payload[..4]);
        return 4 + len > payload.Length
            ? throw new InvalidDataException("OPEN payload truncated")
            : Encoding.UTF8.GetString(payload.Slice(4, (int)len));
    }

    public static byte[] EncodeReadRequest(int maxLength, int timeoutMs)
    {
        var buf = new byte[8];
        BinaryPrimitives.WriteInt32BigEndian(buf.AsSpan(0, 4), maxLength);
        BinaryPrimitives.WriteInt32BigEndian(buf.AsSpan(4, 4), timeoutMs);
        return buf;
    }

    public static (int maxLength, int timeoutMs) DecodeReadRequest(ReadOnlySpan<byte> payload)
    {
        return payload.Length < 8
            ? throw new InvalidDataException("READ payload too short")
            : (BinaryPrimitives.ReadInt32BigEndian(payload[..4]),
               BinaryPrimitives.ReadInt32BigEndian(payload.Slice(4, 4)));
    }

    public static byte[] EncodeSetTimeoutRequest(int timeoutMs)
    {
        var buf = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(buf, timeoutMs);
        return buf;
    }

    public static int DecodeSetTimeoutRequest(ReadOnlySpan<byte> payload)
    {
        return payload.Length < 4
            ? throw new InvalidDataException("SET_TIMEOUT payload too short")
            : BinaryPrimitives.ReadInt32BigEndian(payload[..4]);
    }

    public static byte[] EncodeOpenOk(byte mode)
    {
        return [mode];
    }

    public static byte DecodeOpenOk(ReadOnlySpan<byte> payload)
    {
        return payload.IsEmpty ? throw new InvalidDataException("OPEN_OK payload missing") : payload[0];
    }

    public static byte[] EncodeLog(byte level, string message)
    {
        var msg = Encoding.UTF8.GetBytes(message);
        var buf = new byte[1 + 4 + msg.Length];
        buf[0] = level;
        BinaryPrimitives.WriteUInt32BigEndian(buf.AsSpan(1, 4), (uint)msg.Length);
        msg.CopyTo(buf.AsSpan(5));
        return buf;
    }

    public static (byte level, string message) DecodeLog(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 5)
        {
            throw new InvalidDataException("LOG payload too short");
        }
        var level = payload[0];
        var len = BinaryPrimitives.ReadUInt32BigEndian(payload.Slice(1, 4));
        return 5 + len > payload.Length
            ? throw new InvalidDataException("LOG payload truncated")
            : (level, Encoding.UTF8.GetString(payload.Slice(5, (int)len)));
    }

    public static byte[] EncodeErrorMessage(string message)
    {
        return Encoding.UTF8.GetBytes(message);
    }

    public static string DecodeErrorMessage(ReadOnlySpan<byte> payload)
    {
        return payload.IsEmpty ? string.Empty : Encoding.UTF8.GetString(payload);
    }
}