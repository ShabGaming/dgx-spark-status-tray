namespace SparkTray.NvidiaSyncImport;

public sealed record SshHostEntry(string HostAlias, string? Hostname, string? User, int? Port, string? IdentityFile);

/// <summary>
/// A tiny, deliberately narrow parser for the handful of directives NVIDIA Sync's own
/// ssh_config uses (Host/Hostname/User/Port/IdentityFile). This is not a general OpenSSH
/// config parser - it doesn't need to be, and pulling in a full one would be a needless
/// dependency for reading a half-dozen predictable lines.
/// </summary>
public static class SshConfigParser
{
    public static IReadOnlyList<SshHostEntry> Parse(string filePath)
    {
        var entries = new List<SshHostEntry>();
        string? alias = null, hostname = null, user = null, identityFile = null;
        int? port = null;

        void Flush()
        {
            if (alias is not null)
            {
                entries.Add(new SshHostEntry(alias, hostname, user, port, identityFile));
            }
        }

        foreach (var rawLine in File.ReadLines(filePath))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var spaceIdx = line.IndexOfAny(new[] { ' ', '\t' });
            if (spaceIdx < 0)
            {
                continue;
            }

            var key = line[..spaceIdx];
            var value = line[(spaceIdx + 1)..].Trim().Trim('"');

            switch (key.ToLowerInvariant())
            {
                case "host":
                    Flush();
                    alias = value;
                    hostname = user = identityFile = null;
                    port = null;
                    break;
                case "hostname":
                    hostname = value;
                    break;
                case "user":
                    user = value;
                    break;
                case "port":
                    port = int.TryParse(value, out var p) ? p : null;
                    break;
                case "identityfile":
                    identityFile = value;
                    break;
            }
        }

        Flush();
        return entries;
    }
}
