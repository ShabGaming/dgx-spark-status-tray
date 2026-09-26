using SparkTray.Monitoring;
using Xunit;

namespace SparkTray.Tests;

public class MdnsResolverTests
{
    // Byte-for-byte the mDNS answer captured directly from a real device replying to an
    // A-record query for "spark-6ab2.local" (44 bytes, matching the real capture exactly):
    // header (qdcount=0, ancount=1) + name "spark-6ab2.local" + TYPE A + CLASS IN|flush +
    // TTL 120 + RDLENGTH 4 + RDATA 192.168.1.187. qdcount=0 is the important part - real
    // mDNS responses conventionally omit the question section (RFC 6762 section 6), which
    // is exactly the shape that broke the original parser: it unconditionally skipped a
    // question section that isn't actually there, misaligning every field after it by 4+ bytes.
    private static byte[] BuildRealSparkAnswer() => new byte[]
    {
        0x00, 0x00, // id
        0x84, 0x00, // flags
        0x00, 0x00, // qdcount = 0
        0x00, 0x01, // ancount = 1
        0x00, 0x00, // nscount
        0x00, 0x00, // arcount
        0x0a, 0x73, 0x70, 0x61, 0x72, 0x6b, 0x2d, 0x36, 0x61, 0x62, 0x32, // len 10, "spark-6ab2"
        0x05, 0x6c, 0x6f, 0x63, 0x61, 0x6c,                               // len 5, "local"
        0x00,       // name terminator
        0x00, 0x01, // TYPE = A
        0x80, 0x01, // CLASS = IN | cache-flush bit
        0x00, 0x00, 0x00, 0x78, // TTL = 120
        0x00, 0x04, // RDLENGTH = 4
        0xc0, 0xa8, 0x01, 0xbb, // RDATA = 192.168.1.187
    };

    [Fact]
    public void TryParseAAnswer_RealDeviceResponseWithNoQuestionSection_ParsesCorrectly()
    {
        var packet = BuildRealSparkAnswer();

        var address = MdnsResolver.TryParseAAnswer(packet, "spark-6ab2.local");

        Assert.NotNull(address);
        Assert.Equal("192.168.1.187", address!.ToString());
    }

    [Fact]
    public void TryParseAAnswer_HostnameDoesNotMatch_ReturnsNull()
    {
        var packet = BuildRealSparkAnswer();

        var address = MdnsResolver.TryParseAAnswer(packet, "some-other-device.local");

        Assert.Null(address);
    }

    [Fact]
    public void TryParseAAnswer_TruncatedPacket_ReturnsNullWithoutThrowing()
    {
        var address = MdnsResolver.TryParseAAnswer(new byte[] { 0, 0, 0, 0 }, "spark-6ab2.local");

        Assert.Null(address);
    }

    [Fact]
    public void TryParseAAnswer_ZeroAnswerCount_ReturnsNull()
    {
        var packet = new byte[]
        {
            0x00, 0x00, // id
            0x84, 0x00, // flags
            0x00, 0x00, // qdcount
            0x00, 0x00, // ancount = 0
            0x00, 0x00, // nscount
            0x00, 0x00, // arcount
        };

        var address = MdnsResolver.TryParseAAnswer(packet, "spark-6ab2.local");

        Assert.Null(address);
    }
}
