using Arqon.Core.Meter;

namespace Arqon.Core.Tests.Meter;

public class FullscreenStateTests
{
    // values of QUERY_USER_NOTIFICATION_STATE (shellapi.h)
    [Theory]
    [InlineData(2, true)]    // QUNS_BUSY: a full-screen application
    [InlineData(3, true)]    // QUNS_RUNNING_D3D_FULL_SCREEN: a game
    [InlineData(4, true)]    // QUNS_PRESENTATION_MODE
    [InlineData(1, false)]   // QUNS_NOT_PRESENT: screen saver or locked, the taskbar is not visible either way
    [InlineData(5, false)]   // QUNS_ACCEPTS_NOTIFICATIONS: normal desktop
    [InlineData(6, false)]   // QUNS_QUIET_TIME
    [InlineData(7, false)]   // QUNS_APP
    public void Only_full_screen_states_hide_the_widget(int state, bool hide)
    {
        Assert.Equal(hide, FullscreenState.ShouldHideWidget(state));
    }
}
