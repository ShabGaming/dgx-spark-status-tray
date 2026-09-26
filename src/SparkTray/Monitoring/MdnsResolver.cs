using System.Net;
using System.Net.Sockets;
using System.Text;
using SparkTray.Logging;

namespace SparkTray.Monitoring;

/// <summary>
/// One-shot mDNS (RFC 6762) A-record resolver for ".local" hostnames.
///
/// This exists because Windows' normal name resolution (Dns.*, plain Socket/TcpClient
/// connects, even `ping`) does not reliably resolve ".local" hostnames on every network -
/// confirmed by testing directly against a real network where the OS resolver failed but
/// a raw mDNS multicast query got answered by other devices on the LAN. Anything that
/// doesn't end in ".local" is left to the normal system resolver.
/// </summary>
public sealed class MdnsResolver
{
    private static readonly IPEndPoint MulticastEndpoint = new(IPAddress.Parse("224.0.0.251"), 5353);
    private readonly IAppLogger _logger;
    private readonly Dictionary<string, CacheEntry> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _cacheLock = new();

    public MdnsResolver(IAppLogger logger)
    {
        _logger = logger;
    }

    public static bool IsMdnsHostname(string hostname) =>
        hostname.EndsWith(".local", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Resolves an mDNS hostname to an IPv4 address. Returns the last-known-good address
    /// (if any) when a fresh query gets no answer, since one missed mDNS reply on a busy
    /// network is a weaker "it's offline" signal than a failed TCP connect to that address.
    /// </summary>
    public async Task<IPAddress?> ResolveAsync(string hostname, TimeSpan timeout, CancellationToken ct)
    {
        var fresh = await QueryAsync(hostname, timeout, ct).ConfigureAwait(false);
        lock (_cacheLock)
        {
            if (fresh is not null)
            {
                _cache[hostname] = new CacheEntry(fresh, DateTimeOffset.UtcNow);
                _logger.Info($"mDNS: fresh answer for {hostname} -> {fresh}");
                return fresh;
            }

            if (_cache.TryGetValue(hostname, out var cached))
            {
                _logger.Warn($"mDNS: no fresh answer for {hostname}, using cached {cached.Address} (resolved {DateTimeOffset.UtcNow - cached.ResolvedAtUtc} ago).");
                return cached.Address;
            }

            _logger.Warn($"mDNS: no fresh answer for {hostname} and nothing cached yet.");
            return null;
        }
    }

    private async Task<IPAddress?> QueryAsync(string hostname, TimeSpan timeout, CancellationToken ct)
    {
        using var udp = new UdpClient(AddressFamily.InterNetwork);
        try
        {
            udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            udp.Client.Bind(new IPEndPoint(IPAddress.Any, 5353));
            udp.JoinMulticastGroup(MulticastEndpoint.Address);

            var query = BuildQuery(hostname);
            await udp.SendAsync(query, query.Length, MulticastEndpoint).ConfigureAwait(false);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout);

            while (!cts.IsCancellationRequested)
            {
                UdpReceiveResult result;
                try
                {
                    var receiveTask = udp.ReceiveAsync();
                    var completed = await Task.WhenAny(receiveTask, Task.Delay(timeout, cts.Token)).ConfigureAwait(false);
                    if (completed != receiveTask)
                    {
                        return null;
                    }

                    result = receiveTask.Result;
                }
                catch (OperationCanceledException)
                {
                    return null;
                }

                var answer = TryParseAAnswer(result.Buffer, hostname);
                if (answer is not null)
                {
                    return answer;
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.Warn($"mDNS query for {hostname} failed: {ex.Message}");
            return null;
        }
    }

    private static byte[] BuildQuery(string hostname)
    {
        using var stream = new MemoryStream();
        stream.Write(new byte[] { 0, 0 }); // transaction id
        stream.Write(new byte[] { 0, 0 }); // flags (standard query)
        stream.Write(new byte[] { 0, 1 }); // qdcount = 1
        stream.Write(new byte[] { 0, 0 }); // ancount
        stream.Write(new byte[] { 0, 0 }); // nscount
        stream.Write(new byte[] { 0, 0 }); // arcount

        foreach (var label in hostname.Split('.'))
        {
            var bytes = Encoding.ASCII.GetBytes(label);
            stream.WriteByte((byte)bytes.Length);
            stream.Write(bytes);
        }

        stream.WriteByte(0); // end of name
        stream.Write(new byte[] { 0, 1 }); // QTYPE A
        stream.Write(new byte[] { 0, 1 }); // QCLASS IN

        return stream.ToArray();
    }

    /// <summary>
    /// Parses just enough of a raw mDNS response to find an A-record answer whose
    /// owner name matches the hostname we queried. Anything else in the packet
    /// (other records, other services chattering on the same multicast group) is ignored.
    /// </summary>
    internal static IPAddress? TryParseAAnswer(byte[] buffer, string expectedHostname)
    {
        try
        {
            if (buffer.Length < 12)
            {
                return null;
            }

            int qdcount = (buffer[4] << 8) | buffer[5];
            int ancount = (buffer[6] << 8) | buffer[7];
            if (ancount == 0)
            {
                return null;
            }

            // Real mDNS responses conventionally omit the question section entirely
            // (RFC 6762 section 6) - confirmed directly against a real device, whose
            // answer came back with qdcount=0. Skipping a question unconditionally here
            // would misread every real answer by 4+ bytes, so only skip one per qdcount.
            int offset = 12;
            for (var q = 0; q < qdcount; q++)
            {
                offset = SkipName(buffer, offset);
                offset += 4; // qtype + qclass
            }

            for (var i = 0; i < ancount && offset < buffer.Length; i++)
            {
                var (name, nameEnd) = ReadName(buffer, offset);
                offset = nameEnd;
                if (offset + 10 > buffer.Length)
                {
                    return null;
                }

                int type = (buffer[offset] << 8) | buffer[offset + 1];
                offset += 8; // type, class, ttl
                int rdlength = (buffer[offset] << 8) | buffer[offset + 1];
                offset += 2;

                if (type == 1 && rdlength == 4 &&
                    string.Equals(name, expectedHostname, StringComparison.OrdinalIgnoreCase))
                {
                    return new IPAddress(new[] { buffer[offset], buffer[offset + 1], buffer[offset + 2], buffer[offset + 3] });
                }

                offset += rdlength;
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    private static int SkipName(byte[] buffer, int offset) => ReadName(buffer, offset).Item2;

    private static (string Name, int NextOffset) ReadName(byte[] buffer, int offset)
    {
        var labels = new List<string>();
        var visited = 0;

        while (offset < buffer.Length && visited++ < 64)
        {
            int length = buffer[offset];
            if (length == 0)
            {
                offset += 1;
                break;
            }

            if ((length & 0xC0) == 0xC0)
            {
                // Compression pointer - not needed for our narrow use case (only the
                // question name and the matching answer name are inspected), skip it.
                offset += 2;
                break;
            }

            offset += 1;
            if (offset + length > buffer.Length)
            {
                break;
            }

            labels.Add(Encoding.ASCII.GetString(buffer, offset, length));
            offset += length;
        }

        return (string.Join(".", labels), offset);
    }

    private sealed record CacheEntry(IPAddress Address, DateTimeOffset ResolvedAtUtc);
}
