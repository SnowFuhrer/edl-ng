using System.Runtime.InteropServices;

namespace Qualcomm.EmergencyDownload.Transport.Elevation;

/// <summary>
/// Preferred macOS launcher backed by <c>SMAppService</c> — installs the helper as a
/// LaunchDaemon so users only authorise once. Registration requires a signed <c>.app</c>
/// bundle with a companion <c>Contents/Library/LaunchDaemons/&lt;label&gt;.plist</c>, which
/// we don't yet produce. Until the bundling work lands, <see cref="IsSupported"/> always
/// returns <c>false</c> so the <see cref="HelperLauncher"/> factory transparently falls
/// back to the osascript path. The seam is kept so we can swap in a real P/Invoke
/// implementation without touching callers.
/// </summary>
internal sealed class MacSmAppServiceLauncher : IHelperLauncher
{
    public string Name => "SMAppService";

    public bool IsSupported()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return false;
        }
        // TODO: detect signed .app bundle and presence of LaunchDaemon plist; currently the
        // dotnet publish output is a bare executable so SMAppService registration will fail.
        var exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath))
        {
            return false;
        }
        // Naive bundle detection: walk up from the executable looking for a `.app/Contents/MacOS` layout.
        var dir = Path.GetDirectoryName(exePath);
        while (!string.IsNullOrEmpty(dir))
        {
            if (dir.EndsWith(".app/Contents/MacOS", StringComparison.Ordinal))
            {
                return false; // Bundle detected but SMAppService P/Invoke not implemented yet.
            }
            dir = Path.GetDirectoryName(dir);
        }
        return false;
    }

    public HelperSession Launch()
    {
        throw new ElevationUnsupportedException(
            "SMAppService launcher is not implemented yet. Expected to be reached only after IsSupported() returned true.");
    }
}