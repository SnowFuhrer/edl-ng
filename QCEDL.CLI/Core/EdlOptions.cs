using QCEDL.CLI.Helpers;
using Qualcomm.EmergencyDownload.Layers.APSS.Firehose.Xml.Elements;

namespace QCEDL.CLI.Core;

public enum TransportBackend
{
    Auto,
    LibUsb,
    Serial
}

/// <summary>
/// Plain POCO used by <see cref="EdlManager"/> so the GUI (and any other host) can drive
/// the manager without depending on <c>System.CommandLine</c>.
/// </summary>
public sealed class EdlOptions
{
    public string? LoaderPath { get; set; }
    public int? Vid { get; set; }
    public int? Pid { get; set; }
    public StorageType? MemoryType { get; set; }
    public LogLevel LogLevel { get; set; } = LogLevel.Info;
    public ulong? MaxPayloadSize { get; set; }
    public uint Slot { get; set; }
    public string? HostDevAsTarget { get; set; }
    public string? ImgSize { get; set; }
    public bool RadxaWosPlatform { get; set; }
    public TransportBackend Backend { get; set; } = TransportBackend.Auto;
    public string? SerialDevicePath { get; set; }
}