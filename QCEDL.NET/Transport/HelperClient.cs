using System.Collections.Concurrent;
using QCEDL.NET.Logging;

namespace Qualcomm.EmergencyDownload.Transport;

/// <summary>
/// GUI-side client of the privileged helper channel. Wraps a pair of streams (the helper's
/// stdin and stdout) and exposes the subset of <see cref="QualcommSerial"/> operations that
/// need root. A dedicated reader thread demultiplexes unsolicited <see cref="HelperOpcode.Log"/>
/// frames (forwarded into the shared <see cref="Helpers.Logging"/> sink) from synchronous
/// replies (returned via a blocking queue so call sites stay synchronous, matching
/// <see cref="QualcommSerial"/>'s existing API shape).
/// </summary>
internal sealed class HelperClient : IDisposable
{
    private readonly Stream _input;   // writes to helper stdin
    private readonly Stream _output;  // reads from helper stdout
    private readonly Action? _onDispose;
    private readonly Thread _readerThread;
    private readonly BlockingCollection<Frame> _replies = new(new ConcurrentQueue<Frame>());
    private readonly Lock _writeLock = new();
    private bool _disposed;
    private volatile Exception? _readerFault;

    public CommunicationMode ActiveMode { get; private set; } = CommunicationMode.None;

    public HelperClient(Stream input, Stream output, Action? onDispose = null)
    {
        _input = input;
        _output = output;
        _onDispose = onDispose;
        _readerThread = new Thread(ReaderLoop) { IsBackground = true, Name = "edl-helper-reader" };
        _readerThread.Start();
    }

    public void Open(string devicePath)
    {
        var payload = HelperChannel.EncodeOpenRequest(devicePath);
        var reply = Exchange(HelperOpcode.Open, payload);
        if (reply.Op != HelperOpcode.OpenOk)
        {
            throw TranslateError(reply, $"Helper OPEN for '{devicePath}' failed");
        }
        ActiveMode = (CommunicationMode)HelperChannel.DecodeOpenOk(reply.Payload);
    }

    public void SendData(byte[] data)
    {
        var reply = Exchange(HelperOpcode.SendData, data);
        if (reply.Op != HelperOpcode.Ack)
        {
            throw TranslateError(reply, "Helper SEND failed");
        }
    }

    public void SendLargeData(byte[] data)
    {
        var reply = Exchange(HelperOpcode.SendLargeData, data);
        if (reply.Op != HelperOpcode.Ack)
        {
            throw TranslateError(reply, "Helper SEND_LARGE failed");
        }
    }

    public void SendZeroLengthPacket()
    {
        var reply = Exchange(HelperOpcode.SendZlp, []);
        if (reply.Op != HelperOpcode.Ack)
        {
            throw TranslateError(reply, "Helper SEND_ZLP failed");
        }
    }

    public byte[] Read(int maxLength, int timeoutMs)
    {
        var reply = Exchange(HelperOpcode.Read, HelperChannel.EncodeReadRequest(maxLength, timeoutMs));
        return reply.Op == HelperOpcode.ReadOk
            ? reply.Payload
            : throw TranslateError(reply, "Helper READ failed");
    }

    public void SetTimeout(int timeoutMs)
    {
        var reply = Exchange(HelperOpcode.SetTimeout, HelperChannel.EncodeSetTimeoutRequest(timeoutMs));
        if (reply.Op != HelperOpcode.Ack)
        {
            throw TranslateError(reply, "Helper SET_TIMEOUT failed");
        }
    }

    private Frame Exchange(HelperOpcode op, ReadOnlySpan<byte> payload)
    {
        ObjectDisposedException.ThrowIf(_disposed, typeof(HelperClient));

        lock (_writeLock)
        {
            try
            {
                HelperChannel.WriteFrame(_input, op, payload);
            }
            catch (Exception ex)
            {
                throw new IOException($"Helper channel write failed: {ex.Message}", ex);
            }
        }

        try
        {
            return _replies.Take();
        }
        catch (InvalidOperationException)
        {
            var fault = _readerFault ?? new IOException("Helper channel closed unexpectedly");
            throw fault is IOException ? fault : new IOException(fault.Message, fault);
        }
    }

    private static Exception TranslateError(Frame reply, string fallbackPrefix)
    {
        var msg = HelperChannel.DecodeErrorMessage(reply.Payload);
        var combined = string.IsNullOrEmpty(msg) ? fallbackPrefix : $"{fallbackPrefix}: {msg}";
        return reply.Op switch
        {
            HelperOpcode.TimeoutError => new TimeoutException(combined),
            HelperOpcode.BadMessageError => new BadMessageException(combined),
            HelperOpcode.BadConnectionError => new BadConnectionException(combined),
            HelperOpcode.Error => new IOException(combined),
            HelperOpcode.Open or HelperOpcode.Close or HelperOpcode.SendData
                or HelperOpcode.SendLargeData or HelperOpcode.SendZlp or HelperOpcode.Read
                or HelperOpcode.SetTimeout or HelperOpcode.OpenOk or HelperOpcode.Ack
                or HelperOpcode.ReadOk or HelperOpcode.Log
                or _ => new IOException($"Helper returned unexpected opcode {reply.Op}: {combined}"),
        };
    }

    private void ReaderLoop()
    {
        try
        {
            while (true)
            {
                if (!HelperChannel.TryReadFrame(_output, out var op, out var payload))
                {
                    break;
                }
                if (op == HelperOpcode.Log)
                {
                    try
                    {
                        var (level, message) = HelperChannel.DecodeLog(payload);
                        Helpers.Logging.Log(message, (Helpers.LogLevel)level);
                    }
                    catch (Exception ex)
                    {
                        LibraryLogger.Warning($"Helper LOG frame decode failed: {ex.Message}");
                    }
                    continue;
                }
                _replies.Add(new Frame(op, payload));
            }
        }
        catch (Exception ex)
        {
            _readerFault = ex;
        }
        finally
        {
            _replies.CompleteAdding();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        try
        {
            lock (_writeLock)
            {
                HelperChannel.WriteFrame(_input, HelperOpcode.Close, []);
            }
        }
        catch { /* best effort */ }
        try { _input.Dispose(); } catch { /* best effort */ }
        try { _output.Dispose(); } catch { /* best effort */ }
        try { _ = _readerThread.Join(TimeSpan.FromSeconds(2)); } catch { /* best effort */ }
        try { _onDispose?.Invoke(); } catch { /* best effort */ }
        _replies.Dispose();
    }

    private readonly record struct Frame(HelperOpcode Op, byte[] Payload);
}