using Arqon.Core.Download;

namespace Arqon.Core.Tests.Download;

public class DiskSpaceTests
{
    private const long Gb = 1024L * 1024 * 1024;

    [Fact]
    public void There_is_room_when_the_free_space_covers_the_download_plus_a_reserve()
    {
        Assert.True(DiskSpace.HasRoom(freeBytes: 10 * Gb, neededBytes: 8 * Gb));
    }

    [Fact]
    public void A_download_that_would_fill_the_disk_to_the_brim_has_no_room()
    {
        Assert.False(DiskSpace.HasRoom(freeBytes: 8 * Gb + 1000, neededBytes: 8 * Gb));   // the reserve keeps Windows itself breathing
    }

    [Fact]
    public void Not_enough_space_is_not_enough_space()
    {
        Assert.False(DiskSpace.HasRoom(freeBytes: 1 * Gb, neededBytes: 8 * Gb));
    }

    [Fact]
    public void Nothing_left_to_download_always_has_room()
    {
        Assert.True(DiskSpace.HasRoom(freeBytes: 0, neededBytes: 0));
    }

    [Fact]
    public void The_free_space_of_a_real_folder_can_be_read_and_an_unknown_place_gives_nothing()
    {
        Assert.True(DiskSpace.FreeBytes(Path.GetTempPath()) > 0);
        Assert.Null(DiskSpace.FreeBytes(@"\\no-such-server\share\folder"));
    }
}
