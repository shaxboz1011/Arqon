using Arqon.Core.Meter;

namespace Arqon.Core.Tests.Meter;

public class TaskbarPlacementTests
{
    [Fact]
    public void On_a_bottom_taskbar_the_widget_sits_just_left_of_the_notification_area_and_is_centred_vertically()
    {
        var taskbar = new PixelRect(0, 1040, 1920, 1080);
        var tray = new PixelRect(1500, 1040, 1920, 1080);

        var r = TaskbarPlacement.For(taskbar, tray, width: 120, height: 32, gap: 8);

        Assert.Equal(new PixelRect(1372, 1044, 1492, 1076), r);
    }

    [Fact]
    public void A_top_taskbar_works_the_same_way()
    {
        var taskbar = new PixelRect(0, 0, 1920, 40);
        var tray = new PixelRect(1500, 0, 1920, 40);

        var r = TaskbarPlacement.For(taskbar, tray, 120, 32, 8);

        Assert.Equal(new PixelRect(1372, 4, 1492, 36), r);
    }

    [Fact]
    public void The_widget_is_never_taller_than_the_taskbar()
    {
        var taskbar = new PixelRect(0, 1056, 1920, 1080);
        var tray = new PixelRect(1500, 1056, 1920, 1080);

        var r = TaskbarPlacement.For(taskbar, tray, 120, 40, 8);

        Assert.Equal(24, r.Height);
        Assert.Equal(1056, r.Top);
    }

    [Fact]
    public void On_a_vertical_taskbar_the_widget_goes_above_the_notification_area()
    {
        var taskbar = new PixelRect(1840, 0, 1920, 1080);   // right edge, 80 px wide
        var tray = new PixelRect(1840, 900, 1920, 1080);

        var r = TaskbarPlacement.For(taskbar, tray, 70, 32, 8);

        Assert.Equal(new PixelRect(1845, 860, 1915, 892), r);
    }

    [Fact]
    public void The_widget_stays_inside_the_taskbar_when_there_is_no_room_on_the_left()
    {
        var taskbar = new PixelRect(0, 1040, 200, 1080);
        var tray = new PixelRect(100, 1040, 200, 1080);

        var r = TaskbarPlacement.For(taskbar, tray, 120, 32, 8);

        Assert.Equal(0, r.Left);
    }
}
