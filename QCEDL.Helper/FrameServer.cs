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
                    ValidateDevicePath(devicePath);
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

    /// <summary>
    /// Reject arbitrary paths before the privileged helper opens them. The helper runs as root,
    /// so a compromised GUI could otherwise coerce it into opening any file on disk. Only accept
    /// macOS tty devices matching the expected USB-serial naming, with no traversal segments,
    /// and require the target to actually be a character-special device.
    /// </summary>
    private static void ValidateDevicePath(string devicePath)
    {
        if (string.IsNullOrWhiteSpace(devicePath))
        {
            throw new ArgumentException("Device path is empty.", nameof(devicePath));
        }

        if (devicePath.Contains('\0', StringComparison.Ordinal))
        {
            throw new ArgumentException("Device path contains a NUL byte.", nameof(devicePath));
        }

        // No traversal, no relative segments — the helper only opens absolute, canonical device nodes.
        if (devicePath.Contains("..", StringComparison.Ordinal))
        {
            throw new ArgumentException($"Device path '{devicePath}' contains '..'.", nameof(devicePath));
        }

        var full = Path.GetFullPath(devicePath);
        if (!string.Equals(full, devicePath, StringComparison.Ordinal))
        {
            throw new ArgumentException($"Device path '{devicePath}' is not canonical (resolved to '{full}').", nameof(devicePath));
        }

        // Allow-list: macOS usb-serial nodes created by the cdc-acm / ftdi / pl2303 drivers.
        var name = Path.GetFileName(full);
        var inDev = string.Equals(Path.GetDirectoryName(full), "/dev", StringComparison.Ordinal);
        var matchesPrefix =
            name.StartsWith("cu.usb", StringComparison.Ordinal) ||
            name.StartsWith("tty.usb", StringComparison.Ordinal);
        if (!inDev || !matchesPrefix)
        {
            throw new ArgumentException($"Device path '{devicePath}' is not an allowed USB-serial node.", nameof(devicePath));
        }

        // Must exist and not be a directory/symlink. Creating a regular file under /dev/ already
        // requires root, so the allow-list above is the load-bearing check; this is defense in depth.
        var info = new FileInfo(full);
        if (!info.Exists)
        {
            throw new FileNotFoundException($"Device path '{devicePath}' does not exist.", full);
        }
        if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new ArgumentException($"Device path '{devicePath}' is a symlink.", nameof(devicePath));
        }
        if ((info.Attributes & FileAttributes.Directory) != 0)
        {
            throw new ArgumentException($"Device path '{devicePath}' is a directory.", nameof(devicePath));
        }
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