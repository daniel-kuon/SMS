using System.ComponentModel;
using System.Drawing.Design;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Forms.Design;

namespace Sms.Companion;

public enum SourceKind { Udp, Tcp, Serial }

/// <summary>One NMEA input. UDP listens on Port; TCP connects to Host:Port; Serial opens ComPort at Baud.</summary>
public sealed class SourceConfig
{
    public SourceKind Kind { get; set; } = SourceKind.Udp;
    public bool Enabled { get; set; } = true;
    [Description("TCP only: multiplexer or TimeZero PC (TimeZero's TCP output listens as a server).")]
    public string Host { get; set; } = "127.0.0.1";
    [Description("UDP: local port to listen on. TCP: remote port. 10110 is the common NMEA-over-IP port.")]
    public int Port { get; set; } = 10110;
    public string ComPort { get; set; } = "COM3";
    [Description("4800 for classic NMEA 0183 devices, 38400 for AIS/high-speed multiplexers.")]
    public int Baud { get; set; } = 4800;
    public override string ToString() => Kind switch { SourceKind.Udp => $"UDP :{Port}", SourceKind.Tcp => $"TCP {Host}:{Port}", _ => $"{ComPort} @ {Baud}" } + (Enabled ? "" : " (off)");
}

public sealed class WatchFolder
{
    [Editor(typeof(FolderNameEditor), typeof(UITypeEditor))]
    public string Folder { get; set; } = "";
    [Description("Semicolon-separated file patterns. The portal accepts gpx, kml, nmea/log/txt, zip, gz and 7z.")]
    public string Patterns { get; set; } = "*.gpx;*.kml;*.nmea;*.log;*.zip;*.gz;*.7z";
    public override string ToString() => $"{Folder} ({Patterns})";
}

/// <summary>%APPDATA%\SmsCompanion\settings.json; the device token is stored DPAPI-protected (current user).</summary>
public sealed class Settings
{
    [Category("Portal"), DisplayName("Server URL")]
    public string ServerUrl { get; set; } = "https://localhost:7150";

    [Category("Portal"), DisplayName("Device token"), PasswordPropertyText(true), JsonIgnore,
     Description("Create a tracking device for the ship in the portal (Schiff > Tracking > Gerät) and paste its token here.")]
    public string DeviceToken { get; set; } = "";

    [Browsable(false)]
    public string? DeviceTokenProtected { get; set; }

    [Category("Sending"), DisplayName("Send interval (s)"), Description("One position (latest fix + sensor values) per interval; the first fix is sent at once.")]
    public int SendIntervalSeconds { get; set; } = 60;

    [Category("Sending"), DisplayName("Minimum movement (m)"), Description("Skip a position while the boat moved less than this; one position every 15 minutes is sent anyway.")]
    public double MinMovementM { get; set; } = 20;

    [Category("Sources"), DisplayName("NMEA sources")]
    public List<SourceConfig> Sources { get; set; } = [];

    [Category("Sources"), DisplayName("Watch folders"), Description("New or changed files are uploaded once their size is stable.")]
    public List<WatchFolder> WatchFolders { get; set; } = [];

    [Category("Windows"), DisplayName("Start with Windows")]
    public bool Autostart { get; set; }

    public static string DefaultDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SmsCompanion");

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

    public static Settings Load(string dir)
    {
        var path = Path.Combine(dir, "settings.json");
        if (!File.Exists(path)) return new Settings();
        var s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(path), Json) ?? new Settings();
        if (s.DeviceTokenProtected is { Length: > 0 } p)
            try { s.DeviceToken = Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(p), null, DataProtectionScope.CurrentUser)); }
            catch (CryptographicException) { s.DeviceToken = ""; } // settings copied from another user/machine
        return s;
    }

    public void Save(string dir)
    {
        Directory.CreateDirectory(dir);
        DeviceTokenProtected = string.IsNullOrWhiteSpace(DeviceToken) ? null
            : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(DeviceToken.Trim()), null, DataProtectionScope.CurrentUser));
        File.WriteAllText(Path.Combine(dir, "settings.json"), JsonSerializer.Serialize(this, Json));
    }

    public Settings Clone()
    {
        var c = JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(this, Json), Json)!;
        c.DeviceToken = DeviceToken;
        return c;
    }

    /// <summary>TimeZero preset: UDP 10110 input (TimeZero Connection Wizard > Data Output > UDP) and a watch folder for manual GPX/KML exports.</summary>
    public string ApplyTimeZeroPreset(string? folder = null)
    {
        if (!Sources.Any(x => x.Kind == SourceKind.Udp && x.Port == 10110)) Sources.Add(new SourceConfig { Kind = SourceKind.Udp, Port = 10110 });
        folder ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "TimeZero Export");
        Directory.CreateDirectory(folder);
        if (!WatchFolders.Any(w => string.Equals(w.Folder, folder, StringComparison.OrdinalIgnoreCase))) WatchFolders.Add(new WatchFolder { Folder = folder, Patterns = "*.gpx;*.kml;*.kmz" });
        return folder;
    }

    public const string TimeZeroHelp =
        """
        TimeZero (Navigator / Professional) setup:

        1. Position, heading, wind, depth: open TimeZero's Connection Wizard > "Data Output" > UDP > Next,
           choose the network adapter, port 10110, select the sentences RMC, GGA, VTG, HDT, MWV (Both), DPT, MTW, MWD > Finish.
           TimeZero broadcasts them on the LAN; this app listens on UDP 10110 (same PC or any PC on the network).
           TCP works too (TimeZero then listens as a server): add a TCP source with the TimeZero PC's address and that port.
           Advanced settings: NMEA talker "II" is fine; MWV "Both" gives apparent and true wind.

        2. Tracks: TimeZero has no command line, API or automatic export. Export by hand
           (Track list > Action > Export, or the Import/Export wizard, format GPX or KML) into the folder
           Documents\TimeZero Export; the app uploads new files from there automatically.
        """;
}
