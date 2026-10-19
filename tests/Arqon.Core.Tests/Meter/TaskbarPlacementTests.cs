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

    // Windows 11 keeps things such as the weather widget right next to the notification area. The meter must not sit on top of them.

    [Fact]
    public void A_widget_docked_next_to_the_notification_area_pushes_the_meter_further_left()
    {
        var taskbar = new PixelRect(0, 1040, 1920, 1080);
        var tray = new PixelRect(1500, 1040, 1920, 1080);
        var weather = new PixelRect(1340, 1040, 1496, 1080);   // 4 px left of the tray

        var r = TaskbarPlacement.For(taskbar, tray, 120, 32, 8, [weather]);

        Assert.Equal(1340 - 8 - 120, r.Left);
        Assert.Equal(1340 - 8, r.Right);
    }

    [Fact]
    public void Several_things_in_a_row_next_to_the_tray_are_all_stepped_over()
    {
        var taskbar = new PixelRect(0, 1040, 1920, 1080);
        var tray = new PixelRect(1500, 1040, 1920, 1080);
        var weather = new PixelRect(1380, 1040, 1496, 1080);
        var other = new PixelRect(1300, 1040, 1376, 1080);   // touches the weather widget

        var r = TaskbarPlacement.For(taskbar, tray, 120, 32, 8, [other, weather]);

        Assert.Equal(1300 - 8, r.Right);
    }

    [Fact]
    public void Taskbar_buttons_far_away_from_the_notification_area_are_ignored()
    {
        var taskbar = new PixelRect(0, 1040, 1920, 1080);
        var tray = new PixelRect(1500, 1040, 1920, 1080);
        var centredIcons = new[] { new PixelRect(800, 1040, 850, 1080), new PixelRect(850, 1040, 900, 1080), new PixelRect(900, 1040, 950, 1080) };

        var r = TaskbarPlacement.For(taskbar, tray, 120, 32, 8, centredIcons);

        Assert.Equal(new PixelRect(1372, 1044, 1492, 1076), r);   // exactly as without them
    }

    [Fact]
    public void Things_that_are_not_next_to_the_notification_area_but_overlap_the_meter_do_not_move_it()
    {
        var taskbar = new PixelRect(0, 1040, 1920, 1080);
        var tray = new PixelRect(1500, 1040, 1920, 1080);
        var farLeft = new PixelRect(1200, 1040, 1360, 1080);   // 130 px of free space remain between it and the tray

        var r = TaskbarPlacement.For(taskbar, tray, 120, 32, 8, [farLeft]);

        Assert.Equal(1372, r.Left);
    }

    [Fact]
    public void A_thing_that_belongs_to_the_notification_area_itself_is_not_stepped_over_twice()
    {
        var taskbar = new PixelRect(0, 1040, 1920, 1080);
        var tray = new PixelRect(1500, 1040, 1920, 1080);
        var trayIcon = new PixelRect(1510, 1040, 1550, 1080);   // inside the tray

        var r = TaskbarPlacement.For(taskbar, tray, 120, 32, 8, [trayIcon]);

        Assert.Equal(1372, r.Left);
    }

    [Fact]
    public void Elements_in_another_row_of_the_taskbar_do_not_count()
    {
        var taskbar = new PixelRect(0, 1000, 1920, 1080);
        var tray = new PixelRect(1500, 1000, 1920, 1080);
        var above = new PixelRect(1340, 900, 1496, 990);   // above the taskbar

        var r = TaskbarPlacement.For(taskbar, tray, 120, 32, 8, [above]);

        Assert.Equal(1372, r.Left);
    }

    [Fact]
    public void On_a_vertical_taskbar_things_above_the_notification_area_push_the_meter_further_up()
    {
        var taskbar = new PixelRect(1840, 0, 1920, 1080);
        var tray = new PixelRect(1840, 900, 1920, 1080);
        var widget = new PixelRect(1840, 820, 1920, 896);

        var r = TaskbarPlacement.For(taskbar, tray, 70, 32, 8, [widget]);

        Assert.Equal(820 - 8, r.Bottom);
    }
}
