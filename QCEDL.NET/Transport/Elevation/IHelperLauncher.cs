namespace Qualcomm.EmergencyDownload.Transport.Elevation;

/// <summary>
/// Launches the privileged helper process and hands back a bidirectional stream pair that
/// <see cref="HelperClient"/> uses to speak the frame protocol. Each platform implements
/// this differently — on macOS we trigger an admin-auth prompt; on Linux we don't need one
/// because udev rules handle permissions at the kernel level.
/// </summary>
internal interface IHelperLauncher
{
    /// <summary>Human-readable name shown in logs and diagnostics.</summary>
    string Name { get; }

    /// <summary>Probe whether this launcher can actually be used in the current runtime.</summary>
    bool IsSupported();

    /// <summary>
    /// Spawn the helper and return its IO streams. The caller owns both streams and the
    /// disposer callback; disposing the client should tear everything down.
    /// </summary>
    HelperSession Launch();
}

internal sealed record HelperSession(Stream Input, Stream Output, Action Dispose);

/// <summary>Raised when no launcher can service a privilege request on the current OS.</summary>
public sealed class ElevationUnsupportedException(string message) : Exception(message)
{
}