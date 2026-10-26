using System.Security.Cryptography;
using System.Text;
using Arqon.Core.Download;

namespace Arqon.Core.Tests.Download;

public sealed class ChecksumTests : IDisposable
{
    private const string A = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";   // SHA-256 of nothing
    private const string B = "2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824";   // of "hello"
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "arqon-sum-" + Guid.NewGuid().ToString("N"));

    public ChecksumTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    [Theory]
    [InlineData("https://x.com/debian.iso#sha256=" + B, B)]
    [InlineData("https://x.com/debian.iso#size=5&SHA-256=" + "2CF24DBA5FB0A30E26E83B2AC5B9E29E1B161E5C1FA7425E73043362938B9824", B)]
    [InlineData("https://x.com/debian.iso#sha256=123", null)]
    [InlineData("https://x.com/debian.iso", null)]
    [InlineData("https://x.com/debian.iso#section", null)]
    public void A_link_can_carry_the_expected_hash(string url, string? expected)
    {
        Assert.Equal(expected, Checksum.FromUrl(url));
    }

    [Fact]
    public void Copied_text_gives_the_hash_for_this_file()
    {
        var sums = $"{A}  debian-12-netinst.iso\n{B} *debian-12-DVD-1.iso\nSHA256 (other.iso) = {A}\n";

        Assert.Equal(B, Checksum.FromText(sums, "debian-12-DVD-1.iso"));
        Assert.Equal(A, Checksum.FromText(sums, "DEBIAN-12-NETINST.ISO"));
        Assert.Equal(A, Checksum.FromText(sums, "other.iso"));
        Assert.Null(Checksum.FromText(sums, "missing.iso"));
    }

    [Theory]
    [InlineData("  " + B + "  ", B)]
    [InlineData(B + "  renamed-by-the-site.iso", B)]
    [InlineData("hello", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void A_bare_hash_or_a_single_line_counts(string? text, string? expected)
    {
        Assert.Equal(expected, Checksum.FromText(text, "file.iso"));
    }

    [Fact]
    public async Task The_hash_of_a_file_is_computed()
    {
        var path = Path.Combine(_dir, "f.bin");
        var bytes = Encoding.UTF8.GetBytes(new string('x', 3_000_000));
        await File.WriteAllBytesAsync(path, bytes);

        Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), await Checksum.ComputeAsync(path));
        Assert.True(Checksum.Same(B.ToUpperInvariant(), " " + B));
    }
}
