using Arqon.Core.Meter;

namespace Arqon.Core.Tests.Meter;

public class TaskbarPlacementTests
{
    private static readonly PixelRect Bar = new(0, 1040, 1920, 1080);
    private static readonly PixelRect Tray = new(1500, 1040, 1920, 1080);

    [Fact]
    public void On_a_bottom_taskbar_the_widget_sits_just_left_of_the_notification_area_and_is_centred_vertically()
    {
        var r = TaskbarPlacement.For(Bar, Tray, width: 120, height: 32, gap: 8);

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

        Assert.Equal(24, r!.Value.Height);
        Assert.Equal(1056, r.Value.Top);
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
    public void When_the_taskbar_has_no_room_at_all_there_is_no_place()
    {
        var taskbar = new PixelRect(0, 1040, 200, 1080);
        var tray = new PixelRect(100, 1040, 200, 1080);

        Assert.Null(TaskbarPlacement.For(taskbar, tray, 120, 32, 8));
    }

    // Windows 11 keeps things such as the weather widget and the app buttons on the same bar. The meter must not sit on top of them.

    [Fact]
    public void A_widget_docked_next_to_the_notification_area_pushes_the_meter_to_the_free_space_left_of_it()
    {
        var weather = new PixelRect(1340, 1040, 1496, 1080);   // 4 px left of the tray

        var r = TaskbarPlacement.For(Bar, Tray, 120, 32, 8, [weather]);

        Assert.Equal(1340 - 8 - 120, r!.Value.Left);
        Assert.Equal(1340 - 8, r.Value.Right);
    }

    [Fact]
    public void A_widget_that_is_close_but_not_touching_still_blocks_a_meter_that_would_overlap_it()
    {
        // the real case of Windows 11: the widget ends 65 px before the notification area, but the meter is 110+ px wide
        var weather = new PixelRect(1180, 1040, 1435, 1080);

        var r = TaskbarPlacement.For(Bar, Tray, 120, 32, 8, [weather]);

        Assert.True(r is null or { Right: <= 1180 - 8 } or { Left: >= 1435 + 8 }, $"the meter {r} overlaps the widget");
        Assert.Equal(1180 - 8, r!.Value.Right);   // no room right of it (57 px), so it goes to the left of the widget
    }

    [Fact]
    public void Several_things_in_a_row_next_to_the_tray_are_all_stepped_over()
    {
        var weather = new PixelRect(1380, 1040, 1496, 1080);
        var other = new PixelRect(1300, 1040, 1376, 1080);   // touches the weather widget

        var r = TaskbarPlacement.For(Bar, Tray, 120, 32, 8, [other, weather]);

        Assert.Equal(1300 - 8, r!.Value.Right);
    }

    [Fact]
    public void Taskbar_buttons_far_away_from_the_notification_area_are_ignored()
    {
        var centredIcons = new[] { new PixelRect(800, 1040, 850, 1080), new PixelRect(850, 1040, 900, 1080), new PixelRect(900, 1040, 950, 1080) };

        var r = TaskbarPlacement.For(Bar, Tray, 120, 32, 8, centredIcons);

        Assert.Equal(new PixelRect(1372, 1044, 1492, 1076), r);   // exactly as without them
    }

    [Fact]
    public void Taskbar_buttons_that_reach_into_the_meters_place_are_never_covered()
    {
        var icons = Enumerable.Range(0, 12).Select(i => new PixelRect(700 + i * 50, 1040, 750 + i * 50, 1080)).ToList();   // up to 1300... and beyond:
        icons.Add(new PixelRect(1300, 1040, 1380, 1080));                                                                   // ... 1380

        var r = TaskbarPlacement.For(Bar, Tray, 120, 32, 8, icons);

        // 1380..1500 leaves 1388..1492 = 104 px: not enough for a 120 px meter without shrinking, so it must not be placed on the icons
        Assert.True(r is null || r.Value.Left >= 1388 || r.Value.Right <= 700 - 8, $"the meter {r} covers the buttons");
    }

    [Fact]
    public void The_meter_shrinks_to_the_free_space_when_it_may()
    {
        var icons = new[] { new PixelRect(700, 1040, 1380, 1080) };   // free: 1388..1492 = 104 px

        var r = TaskbarPlacement.For(Bar, Tray, 120, 32, 8, icons, minWidth: 90);

        Assert.Equal(new PixelRect(1388, 1044, 1492, 1076), r);
    }

    [Fact]
    public void Too_small_a_space_is_not_used_and_the_next_free_stretch_is_tried()
    {
        var icons = new[] { new PixelRect(700, 1040, 1460, 1080) };   // only 32 px free next to the notification area

        var r = TaskbarPlacement.For(Bar, Tray, 120, 32, 8, icons, minWidth: 90);

        Assert.Equal(700 - 8, r!.Value.Right);   // the stretch left of the buttons
    }

    [Fact]
    public void Things_that_overlap_only_what_is_beyond_the_meter_do_not_move_it()
    {
        var farLeft = new PixelRect(1200, 1040, 1360, 1080);   // 132 px of free space remain between it and the notification area

        var r = TaskbarPlacement.For(Bar, Tray, 120, 32, 8, [farLeft]);

        Assert.Equal(1372, r!.Value.Left);
    }

    [Fact]
    public void A_thing_that_belongs_to_the_notification_area_itself_is_ignored()
    {
        var trayIcon = new PixelRect(1510, 1040, 1550, 1080);   // inside the tray

        var r = TaskbarPlacement.For(Bar, Tray, 120, 32, 8, [trayIcon]);

        Assert.Equal(1372, r!.Value.Left);
    }

    [Fact]
    public void Elements_in_another_row_of_the_taskbar_do_not_count()
    {
        var taskbar = new PixelRect(0, 1000, 1920, 1080);
        var tray = new PixelRect(1500, 1000, 1920, 1080);
        var above = new PixelRect(1340, 900, 1496, 990);   // above the taskbar

        var r = TaskbarPlacement.For(taskbar, tray, 120, 32, 8, [above]);

        Assert.Equal(1372, r!.Value.Left);
    }

    [Fact]
    public void On_a_vertical_taskbar_things_above_the_notification_area_push_the_meter_further_up()
    {
        var taskbar = new PixelRect(1840, 0, 1920, 1080);
        var tray = new PixelRect(1840, 900, 1920, 1080);
        var widget = new PixelRect(1840, 820, 1920, 896);

        var r = TaskbarPlacement.For(taskbar, tray, 70, 32, 8, [widget]);

        Assert.Equal(820 - 8, r!.Value.Bottom);
    }
}
