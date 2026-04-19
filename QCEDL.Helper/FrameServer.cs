using QCEDL.NET.Logging;
using Qualcomm.EmergencyDownload.Transport;
using HelpersLogging = Qualcomm.EmergencyDownload.Helpers.Logging;
using HelpersLogLevel = Qualcomm.EmergencyDownload.Helpers.LogLevel;

namespace QCEDL.Helper;

/// <summary>
/// Server side of the privileged-helper frame protocol. Reads requests from the stream,
/// dispatches to a real <see cref="QualcommSerial"/> instance, and writes back the reply.
/// Shared <see cref="HelpersLogging"/> events are forwarded as <see cref="HelperOpcode.Log"/>
/// frames so the GUI's log view stays unified.
/// </summary>
internal sealed class FrameServer(Stream stream) : IDisposable
{
    private readonly Stream _stream = stream;
    private readonly Lock _writeLock = new();
    private QualcommSerial? _serial;
    private Action<DateTime, HelpersLogLevel, string?>? _logHandler;
    private bool _disposed;

    public void Run()
    {
        LibraryLogger.LogAction = (msg, level, _, _, _) =>
            HelpersLogging.Log(msg, (HelpersLogLevel)(int)level);

        _logHandler = (_, level, message) =>
        {
            try
            {
                var payload = HelperChannel.EncodeLog((byte)level, message ?? string.Empty);
                lock (_writeLock)
                {
                    HelperChannel.WriteFrame(_stream, HelperOpcode.Log, payload);
                }
            }
            catch
            {
                // Never let log forwarding take the helper down.
            }
        };
        HelpersLogging.LogEmitted += _logHandler;

        try
        {
            while (true)
            {
                if (!HelperChannel.TryReadFrame(_stream, out var op, out var payload))
                {
                    break;
                }

                if (op == HelperOpcode.Close)
                {
                    break;
                }

                HandleRequest(op, payload);
            }
        }
        finally
        {
            if (_logHandler != null)
            {
                HelpersLogging.LogEmitted -= _logHandler;
            }
            _serial?.Dispose();
            _serial = null;
        }
    }

    private void HandleRequest(HelperOpcode op, byte[] payload)
    {
        try
        {
            switch (op)
            {
                case HelperOpcode.Open:
                    var devicePath = HelperChannel.DecodeOpenRequest(payload);
                    _serial?.Dispose();
                    _serial = new QualcommSerial(devicePath);
                    WriteFrame(HelperOpcode.OpenOk, HelperChannel.EncodeOpenOk((byte)_serial.ActiveCommunicationMode));
                    break;

                case HelperOpcode.SendData:
                    RequireSerial().SendData(payload);
                    WriteFrame(HelperOpcode.Ack, []);
                    break;

                case HelperOpcode.SendLargeData:
                    RequireSerial().SendLargeRawData(payload);
                    WriteFrame(HelperOpcode.Ack, []);
                    break;

                case HelperOpcode.SendZlp:
                    RequireSerial().SendZeroLengthPacket();
                    WriteFrame(HelperOpcode.Ack, []);
                    break;

                case HelperOpcode.Read:
                    var (maxLen, timeout) = HelperChannel.DecodeReadRequest(payload);
                    var serial = RequireSerial();
                    if (timeout > 0)
                    {
                        serial.SetTimeOut(timeout);
                    }
                    var data = serial.GetResponse(null, maxLen);
                    WriteFrame(HelperOpcode.ReadOk, data);
                    break;

                case HelperOpcode.SetTimeout:
                    RequireSerial().SetTimeOut(HelperChannel.DecodeSetTimeoutRequest(payload));
                    WriteFrame(HelperOpcode.Ack, []);
                    break;

                case HelperOpcode.Close:
                case HelperOpcode.OpenOk:
                case HelperOpcode.Ack:
                case HelperOpcode.ReadOk:
                case HelperOpcode.Log:
                case HelperOpcode.Error:
                case HelperOpcode.TimeoutError:
                case HelperOpcode.BadMessageError:
                case HelperOpcode.BadConnectionError:
                default:
                    WriteFrame(HelperOpcode.Error, HelperChannel.EncodeErrorMessage($"Unsupported opcode {op}"));
                    break;
            }
        }
        catch (TimeoutException ex)
        {
            WriteFrame(HelperOpcode.TimeoutError, HelperChannel.EncodeErrorMessage(ex.Message));
        }
        catch (BadMessageException ex)
        {
            WriteFrame(HelperOpcode.BadMessageError, HelperChannel.EncodeErrorMessage(ex.Message));
        }
        catch (BadConnectionException ex)
        {
            WriteFrame(HelperOpcode.BadConnectionError, HelperChannel.EncodeErrorMessage(ex.Message));
        }
        catch (Exception ex)
        {
            WriteFrame(HelperOpcode.Error, HelperChannel.EncodeErrorMessage(ex.Message));
        }
    }

    private QualcommSerial RequireSerial()
    {
        return _serial ?? throw new InvalidOperationException("Helper received an IO request before OPEN.");
    }

    private void WriteFrame(HelperOpcode op, ReadOnlySpan<byte> payload)
    {
        lock (_writeLock)
        {
            HelperChannel.WriteFrame(_stream, op, payload);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _serial?.Dispose();
        _serial = null;
    }
}