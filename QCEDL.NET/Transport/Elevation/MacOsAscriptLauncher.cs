using System.Diagnostics;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using QCEDL.NET.Logging;

namespace Qualcomm.EmergencyDownload.Transport.Elevation;

/// <summary>
/// macOS fallback launcher that uses <c>osascript</c> to run the helper under
/// <c>do shell script … with administrator privileges</c>. Produces one Touch ID / admin
/// password prompt per session. Communication flows through a Unix-domain socket because
/// <c>osascript</c> buffers stdout until its child exits, which is incompatible with the
/// streaming frame protocol.
/// </summary>
internal sealed class MacOsAscriptLauncher(string helperPath) : IHelperLauncher
{
    private readonly string _helperPath = helperPath;

    public string Name => "osascript";

    public bool IsSupported()
    {
        return RuntimeInformation.IsOSPlatform(OSPlatform.OSX) && File.Exists(_helperPath);
    }

    public HelperSession Launch()
    {
        var socketPath = Path.Combine(Path.GetTempPath(), $"edl-ng-helper-{Guid.NewGuid():N}.sock");
        if (File.Exists(socketPath))
        {
            File.Delete(socketPath);
        }

        var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        listener.Bind(new UnixDomainSocketEndPoint(socketPath));
        listener.Listen(1);

        var script = BuildAppleScript(_helperPath, socketPath);
        var psi = new ProcessStartInfo
        {
            FileName = "/usr/bin/osascript",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add("-e");
        psi.ArgumentList.Add(script);

        var osascript = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start osascript");

        Socket accepted;
        try
        {
            var accept = listener.AcceptAsync();
            var completed = accept.Wait(TimeSpan.FromMinutes(2));
            if (!completed)
            {
                throw new TimeoutException("Timed out waiting for privileged helper to connect (user may have cancelled the authentication prompt).");
            }
            accepted = accept.Result;
        }
        catch
        {
            TryKill(osascript);
            listener.Dispose();
            TryDelete(socketPath);
            throw;
        }
        finally
        {
            listener.Dispose();
        }

        var stream = new NetworkStream(accepted, ownsSocket: true);

        void Teardown()
        {
            try { stream.Dispose(); } catch { /* best effort */ }
            TryKill(osascript);
            TryDelete(socketPath);
            if (!osascript.HasExited)
            {
                try { _ = osascript.WaitForExit(2000); } catch { /* best effort */ }
            }
            LogProcessOutput(osascript);
            osascript.Dispose();
        }

        LibraryLogger.Info($"Privileged helper connected via {socketPath}.");
        return new HelperSession(stream, stream, Teardown);
    }

    private static string BuildAppleScript(string helperPath, string socketPath)
    {
        var quotedHelper = ShellQuote(helperPath);
        var quotedSocket = ShellQuote(socketPath);
        var shellCommand = $"{quotedHelper} --socket {quotedSocket}";
        var sb = new StringBuilder();
        _ = sb.Append("do shell script \"");
        _ = sb.Append(shellCommand.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal));
        _ = sb.Append("\" with administrator privileges with prompt \"edl-ng needs administrator access to talk to the EDL device.\"");
        return sb.ToString();
    }

    private static string ShellQuote(string value)
    {
        return "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";
    }

    private static void TryKill(Process p)
    {
        try
        {
            if (!p.HasExited)
            {
                p.Kill(entireProcessTree: true);
            }
        }
        catch { /* best effort */ }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { /* best effort */ }
    }

    private static void LogProcessOutput(Process p)
    {
        try
        {
            var err = p.StandardError.ReadToEnd();
            if (!string.IsNullOrWhiteSpace(err))
            {
                LibraryLogger.Warning($"osascript stderr: {err.Trim()}");
            }
        }
        catch { /* best effort */ }
    }
}