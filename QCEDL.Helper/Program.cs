using System.Net.Sockets;
using QCEDL.Helper;
using Qualcomm.EmergencyDownload.Helpers;

string? socketPath = null;
for (var i = 0; i < args.Length; i++)
{
    if (string.Equals(args[i], "--socket", StringComparison.Ordinal) && i + 1 < args.Length)
    {
        socketPath = args[i + 1];
        i++;
    }
}

if (string.IsNullOrEmpty(socketPath))
{
    await Console.Error.WriteLineAsync("edl-ng-helper: --socket <path> argument is required.");
    return 64; // EX_USAGE
}

// Helper stdout is captured by osascript; suppress the console sink so logs only go over frames.
Logging.ConsoleSinkEnabled = false;
Logging.CurrentLogLevel = LogLevel.Trace;

try
{
    using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
    socket.Connect(new UnixDomainSocketEndPoint(socketPath));
    using var stream = new NetworkStream(socket, ownsSocket: false);
    using var server = new FrameServer(stream);
    server.Run();
    return 0;
}
catch (Exception ex)
{
    await Console.Error.WriteLineAsync($"edl-ng-helper: fatal error: {ex}");
    return 1;
}