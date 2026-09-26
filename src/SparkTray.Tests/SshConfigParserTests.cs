using SparkTray.NvidiaSyncImport;
using Xunit;

namespace SparkTray.Tests;

public class SshConfigParserTests
{
    [Fact]
    public void Parse_ReadsSingleHostBlock()
    {
        var path = WriteTempConfig("""
            Host dgx-spark
              ### CreatedBy: NVIDIA Sync
              Hostname spark-abcd.local
              IdentityFile "C:\keys\nvsync.key"
              Port 22
              User someuser
            """);

        try
        {
            var entries = SshConfigParser.Parse(path);

            Assert.Single(entries);
            var entry = entries[0];
            Assert.Equal("dgx-spark", entry.HostAlias);
            Assert.Equal("spark-abcd.local", entry.Hostname);
            Assert.Equal("someuser", entry.User);
            Assert.Equal(22, entry.Port);
            Assert.Equal(@"C:\keys\nvsync.key", entry.IdentityFile);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Parse_HandlesMultipleHostBlocks()
    {
        var path = WriteTempConfig("""
            Host first
              Hostname first.local
              User alice

            Host second
              Hostname second.local
              User bob
              Port 2222
            """);

        try
        {
            var entries = SshConfigParser.Parse(path);

            Assert.Equal(2, entries.Count);
            Assert.Equal("first.local", entries[0].Hostname);
            Assert.Null(entries[0].Port);
            Assert.Equal("second.local", entries[1].Hostname);
            Assert.Equal(2222, entries[1].Port);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Parse_IgnoresCommentsAndBlankLines()
    {
        var path = WriteTempConfig("""
            # top level comment

            Host dgx-spark
              # a comment inside the block
              Hostname spark.local
            """);

        try
        {
            var entries = SshConfigParser.Parse(path);
            Assert.Single(entries);
            Assert.Equal("spark.local", entries[0].Hostname);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Parse_EmptyFile_ReturnsNoEntries()
    {
        var path = WriteTempConfig("");
        try
        {
            var entries = SshConfigParser.Parse(path);
            Assert.Empty(entries);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string WriteTempConfig(string content)
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, content);
        return path;
    }
}
