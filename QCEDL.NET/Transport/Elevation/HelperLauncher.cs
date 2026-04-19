using System.Runtime.InteropServices;
using QCEDL.NET.Logging;

namespace Qualcomm.EmergencyDownload.Transport.Elevation;

/// <summary>
/// Entry point callers use to obtain a helper session on the current platform. Picks the
/// most privileged-friendly launcher available (SMAppService first when we ship a signed
/// bundle, osascript otherwise). Callers only see <see cref="IHelperLauncher.Launch"/>.
/// </summary>
internal static class HelperLauncher
{
    /// <summary>Environment variable used by tests/dev to override the resolved helper path.</summary>
    public const string HelperPathOverrideEnvVar = "EDL_NG_HELPER_PATH";

    public static IHelperLauncher Create()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            throw new ElevationUnsupportedException(
                "Privileged helper is only used on macOS. Linux relies on udev rules; Windows does not need elevation.");
        }

        var helperPath = ResolveHelperPath() ?? throw new ElevationUnsupportedException(
            "edl-ng-helper executable not found next to the GUI. Reinstall the application or set EDL_NG_HELPER_PATH.");

        IHelperLauncher[] candidates = [new MacSmAppServiceLauncher(), new MacOsAscriptLauncher(helperPath)];
        foreach (var candidate in candidates)
        {
            if (candidate.IsSupported())
            {
                LibraryLogger.Info($"Selected privileged-helper launcher: {candidate.Name}.");
                return candidate;
            }
        }
        throw new ElevationUnsupportedException("No privileged-helper launcher is supported on this host.");
    }

    /// <summary>
    /// Search order for the helper: override env var → alongside the GUI executable → a
    /// co-located <c>helper/</c> subdirectory → PATH. Returns null when nothing is found.
    /// </summary>
    public static string? ResolveHelperPath()
    {
        var @override = Environment.GetEnvironmentVariable(HelperPathOverrideEnvVar);
        if (!string.IsNullOrWhiteSpace(@override) && File.Exists(@override))
        {
            return @override;
        }

        var exeName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "edl-ng-helper.exe" : "edl-ng-helper";
        var baseDir = AppContext.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(baseDir, exeName),
            Path.Combine(baseDir, "helper", exeName),
            Path.Combine(baseDir, "..", "helper", exeName),
        };
        foreach (var path in candidates)
        {
            if (File.Exists(path))
            {
                return Path.GetFullPath(path);
            }
        }

        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrEmpty(pathEnv))
        {
            foreach (var dir in pathEnv.Split(Path.PathSeparator))
            {
                if (string.IsNullOrEmpty(dir))
                {
                    continue;
                }
                var probe = Path.Combine(dir, exeName);
                if (File.Exists(probe))
                {
                    return probe;
                }
            }
        }
        return null;
    }
}