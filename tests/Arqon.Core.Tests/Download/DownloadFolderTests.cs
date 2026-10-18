using Arqon.Core.Download;

namespace Arqon.Core.Tests.Download;

public class DownloadFolderTests
{
    private const string Windows = @"C:\Users\me\Downloads";

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Nothing_chosen_means_the_downloads_folder_of_windows(string? chosen)
    {
        Assert.Equal(Windows, DownloadFolder.Resolve(chosen, Windows));
    }

    [Fact]
    public void A_chosen_folder_wins()
    {
        Assert.Equal(@"D:\Torrents", DownloadFolder.Resolve(@"D:\Torrents", Windows));
    }

    [Fact]
    public void Only_a_chosen_folder_is_called_custom()
    {
        Assert.True(DownloadFolder.IsCustom(@"D:\Torrents"));
        Assert.False(DownloadFolder.IsCustom(null));
        Assert.False(DownloadFolder.IsCustom("  "));
    }

    [Fact]
    public void The_real_downloads_folder_of_this_machine_is_found()
    {
        var folder = KnownFolders.Downloads();

        Assert.False(string.IsNullOrWhiteSpace(folder));
        Assert.True(Path.IsPathRooted(folder));
    }
}
