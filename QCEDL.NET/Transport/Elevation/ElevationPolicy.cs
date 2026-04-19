using System.Runtime.InteropServices;
using Qualcomm.EmergencyDownload.Core;

namespace Qualcomm.EmergencyDownload.Transport.Elevation;

/// <summary>
/// Policy layer that decides when a session must be routed through the privileged helper.
/// Callers consult <see cref="RequiresHelper"/> before handing a device path to
/// <see cref="QualcommSerial"/> so the GUI can surface a one-time authentication prompt.
/// </summary>
public static class ElevationPolicy
{
    [DllImport("libc", EntryPoint = "geteuid")]
    private static extern uint GetEuid();

    /// <summary>
    /// True on hosts where the caller cannot access EDL USB/tty devices directly and must
    /// spawn a privileged helper instead. Currently macOS-only, and skipped when the process
    /// is already running as root (e.g. <c>sudo edl-ng …</c>). Linux relies on udev rules and
    /// Windows uses COM-port / WinUSB APIs that don't need elevation.
    /// </summary>
    public static bool RequiresHelper()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return false;
        }
        try
        {
            return GetEuid() != 0;
        }
        catch (Exception)
        {
            return true;
        }
    }

    /// <summary>
    /// Classification returned by <see cref="Probe"/> to help the GUI phrase its diagnostics.
    /// </summary>
    public enum ProbeResult
    {
        /// <summary>The caller may open this candidate directly.</summary>
        Granted,
        /// <summary>Helper route is required (macOS, or anywhere the OS blocks user-space USB).</summary>
        NeedsHelper,
        /// <summary>Linux host without a matching udev rule; direct the user to install the package rule.</summary>
        NeedsUdev,
    }

    /// <summary>
    /// Cheap, non-destructive probe that classifies why a candidate might fail to open.
    /// The probe is intentionally conservative — it only returns <see cref="ProbeResult.Granted"/>
    /// when it is confident; anything else is treated as "needs elevation" by the caller.
    /// </summary>
    public static ProbeResult Probe(DeviceCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return ProbeResult.NeedsHelper;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            // Serial paths — accessible when udev uaccess tags grant the seat user an ACL.
            if (candidate.Backend == TransportBackend.Serial && File.Exists(candidate.Id))
            {
                try
                {
                    using var fs = File.Open(candidate.Id, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
                    return ProbeResult.Granted;
                }
                catch (UnauthorizedAccessException)
                {
                    return ProbeResult.NeedsUdev;
                }
                catch (IOException)
                {
                    // Busy or transient — treat as inconclusive/granted; the real open will produce a clearer error.
                    return ProbeResult.Granted;
                }
            }
            // LibUsb — we can't cheaply predict LIBUSB_ERROR_ACCESS without opening. Report "needs udev"
            // whenever the user is not in a group that would typically be granted access; the GUI uses
            // this as a hint, not a gate.
            return ProbeResult.NeedsUdev;
        }

        return ProbeResult.Granted;
    }
}