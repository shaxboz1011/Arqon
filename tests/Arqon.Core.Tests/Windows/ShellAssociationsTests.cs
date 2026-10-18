using Arqon.Core.Windows;
using Microsoft.Win32;

namespace Arqon.Core.Tests.Windows;

/// <summary>Runs against a throw-away branch of HKCU, never against the real file associations.</summary>
public sealed class ShellAssociationsTests : IDisposable
{
    private readonly string _root = @"Software\ArqonTests\" + Guid.NewGuid().ToString("N");
    private const string Exe = @"C:\Apps\Arqon\Arqon.App.exe";

    private ShellAssociations Make(string exe = Exe) =>
        new(exe, _root + @"\Classes", _root + @"\App", _root + @"\RegisteredApplications");

    public void Dispose()
    {
        try { Registry.CurrentUser.DeleteSubKeyTree(@"Software\ArqonTests", throwOnMissingSubKey: false); } catch { /* best effort */ }
    }

    private string? Read(string subKey, string? name = null)
    {
        using var key = Registry.CurrentUser.OpenSubKey(_root + @"\" + subKey);
        return key?.GetValue(name) as string;
    }

    [Fact]
    public void Nothing_is_registered_to_begin_with()
    {
        Assert.False(Make().IsRegistered());
    }

    [Fact]
    public void Registering_makes_arqon_open_magnet_links_with_the_link_as_its_argument()
    {
        Make().Register();

        Assert.Equal($"\"{Exe}\" \"%1\"", Read(@"Classes\magnet\shell\open\command"));
        Assert.Equal("", Read(@"Classes\magnet", "URL Protocol"));
        Assert.True(Make().IsRegistered());
    }

    [Fact]
    public void Another_programs_magnet_handler_is_not_taken_over_when_registering_but_arqon_is_still_offered_elsewhere()
    {
        using (var key = Registry.CurrentUser.CreateSubKey(_root + @"\Classes\magnet\shell\open\command"))
            key.SetValue(null, "\"C:\\Other\\Zona.exe\" \"%1\"");
        var shell = Make();

        shell.Register();

        Assert.Equal("\"C:\\Other\\Zona.exe\" \"%1\"", Read(@"Classes\magnet\shell\open\command"));   // theirs, untouched
        Assert.Equal("\"C:\\Other\\Zona.exe\" \"%1\"", shell.OtherMagnetHandler());
        Assert.False(shell.OwnsMagnet());
        Assert.True(shell.IsRegistered());                                                              // Arqon is in "Open with" and Default apps
        Assert.Equal("Arqon.Magnet", Read(@"App\Capabilities\UrlAssociations", "magnet"));
    }

    [Fact]
    public void Arqon_owns_the_magnet_handler_when_nobody_else_does()
    {
        var shell = Make();
        Assert.Null(shell.OtherMagnetHandler());

        shell.Register();

        Assert.True(shell.OwnsMagnet());
        Assert.Null(shell.OtherMagnetHandler());
    }

    [Fact]
    public void Registering_adds_arqon_to_the_open_with_list_of_torrent_files_without_taking_the_default_over()
    {
        using (var key = Registry.CurrentUser.CreateSubKey(_root + @"\Classes\.torrent"))
            key.SetValue(null, "torrent_auto_file");   // somebody else's default

        Make().Register();

        Assert.Equal("torrent_auto_file", Read(@"Classes\.torrent"));   // untouched
        using var progIds = Registry.CurrentUser.OpenSubKey(_root + @"\Classes\.torrent\OpenWithProgids");
        Assert.Contains("Arqon.Torrent", progIds!.GetValueNames());
        Assert.Equal($"\"{Exe}\" \"%1\"", Read(@"Classes\Arqon.Torrent\shell\open\command"));
    }

    [Fact]
    public void Arqon_is_listed_in_the_default_apps_of_windows()
    {
        Make().Register();

        Assert.Equal(_root + @"\App\Capabilities", Read("RegisteredApplications", "Arqon"));
        Assert.Equal("Arqon.Torrent", Read(@"App\Capabilities\FileAssociations", ".torrent"));
        Assert.Equal("Arqon.Magnet", Read(@"App\Capabilities\UrlAssociations", "magnet"));
    }

    [Fact]
    public void Unregistering_removes_what_was_added()
    {
        var shell = Make();
        shell.Register();

        shell.Unregister();

        Assert.False(shell.IsRegistered());
        Assert.Null(Read(@"Classes\magnet\shell\open\command"));
        Assert.Null(Read("RegisteredApplications", "Arqon"));
        using var progIds = Registry.CurrentUser.OpenSubKey(_root + @"\Classes\.torrent\OpenWithProgids");
        Assert.True(progIds is null || !progIds.GetValueNames().Contains("Arqon.Torrent"));
    }

    [Fact]
    public void Another_programs_magnet_handler_is_left_alone_when_unregistering()
    {
        using (var key = Registry.CurrentUser.CreateSubKey(_root + @"\Classes\magnet\shell\open\command"))
            key.SetValue(null, "\"C:\\Other\\torrent.exe\" \"%1\"");

        Make().Unregister();

        Assert.Equal("\"C:\\Other\\torrent.exe\" \"%1\"", Read(@"Classes\magnet\shell\open\command"));
    }

    [Fact]
    public void A_registration_that_points_to_a_different_copy_of_arqon_does_not_count_as_ours()
    {
        Make(@"C:\Old\Arqon.App.exe").Register();

        Assert.False(Make().IsRegistered());
    }

    [Fact]
    public void Registering_twice_is_harmless()
    {
        var shell = Make();
        shell.Register();
        shell.Register();

        Assert.True(shell.IsRegistered());
    }
}
