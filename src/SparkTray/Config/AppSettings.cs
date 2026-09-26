namespace SparkTray.Config;

public sealed class AppSettings
{
    public int SchemaVersion { get; set; } = 1;
    public TargetSettings Target { get; set; } = new();
    public SshSettings Ssh { get; set; } = new();
    public ShutdownSettings Shutdown { get; set; } = new();
    public LoggingSettings Logging { get; set; } = new();
    public bool StartWithWindows { get; set; }

    public static AppSettings CreateDefault() => new();
}

public sealed class TargetSettings
{
    public string Hostname { get; set; } = "";
    public int CheckIntervalSeconds { get; set; } = 30;
    public int CheckTimeoutSeconds { get; set; } = 3;
    public bool EnableIcmpProbe { get; set; } = true;
}

public sealed class SshSettings
{
    public string Host { get; set; } = "";
    public int Port { get; set; } = 22;
    public string Username { get; set; } = "";
    public string PrivateKeyPath { get; set; } = "";
    public int ConnectTimeoutSeconds { get; set; } = 15;
}

public sealed class ShutdownSettings
{
    public string Command { get; set; } = "sudo shutdown -h now";

    // When true (the default), the shutdown dialog asks for the sudo password each time and
    // pipes it to "sudo -S" over the SSH command's stdin, instead of requiring the Spark to
    // be set up with passwordless sudo for the shutdown command - a safer out-of-the-box
    // default than expecting every user to edit /etc/sudoers.d before this feature works at
    // all. The password is never persisted - only ever held in memory for that one attempt.
    public bool PromptForSudoPassword { get; set; } = true;

    // Kernel worker/helper threads (kworker/*, rcu_*, ...) are filtered structurally in
    // ProcessActivityCheck regardless of this list - they're never meaningful "user work"
    // and their names vary too much to enumerate here. This list is for everything else:
    // core system daemons plus, since a real DGX Spark runs a full GNOME desktop session
    // rather than a bare headless server, the desktop stack that comes with that (entries
    // ending in "*" match by prefix, for daemons whose ps name gets truncated/suffixed).
    public List<string> ProcessIgnoreList { get; set; } = new()
    {
        // Core system/service daemons
        "systemd", "cron", "rsyslogd", "sshd", "NetworkManager",
        "unattended-upgrades", "containerd", "dockerd", "packagekitd", "polkitd",
        "accounts-daemon", "udisksd", "bluetoothd", "wpa_supplicant", "agetty",
        "bash", "ps", "sh", "login", "sudo", "cat", "grep", "awk", "sed", "env",
        "fwupd", "systemd-*", "dbus-*", "snapd*",

        // GNOME desktop session (present because the Spark's default image boots to a
        // desktop, not just a shell) - observed directly on a real Spark rather than assumed
        "gnome-*", "gsd-*", "gjs*", "mutter*", "ibus*", "at-spi*",
        "colord", "upowerd", "xdg-*", "pipewire*", "wireplumber", "fusermount3",
    };
}

public sealed class LoggingSettings
{
    public bool Enabled { get; set; } = true;
}
