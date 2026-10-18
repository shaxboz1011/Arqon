namespace Arqon.Core.Meter;

public static class FullscreenState
{
    /// <summary>
    /// True for the SHQueryUserNotificationState values that mean a full-screen application owns the screen
    /// (QUNS_BUSY, QUNS_RUNNING_D3D_FULL_SCREEN, QUNS_PRESENTATION_MODE). The taskbar is hidden then, and so must the meter be.
    /// </summary>
    public static bool ShouldHideWidget(int queryUserNotificationState) => queryUserNotificationState is 2 or 3 or 4;
}
