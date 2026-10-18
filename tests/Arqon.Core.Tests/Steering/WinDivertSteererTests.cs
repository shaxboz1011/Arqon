using System.Net;
using Arqon.Core.Steering;

namespace Arqon.Core.Tests.Steering;

public class WinDivertSteererTests
{
    [Fact]
    public void Reports_the_missing_driver_instead_of_throwing()
    {
        var empty = Path.Combine(Path.GetTempPath(), "arqon-nodriver-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(empty);
        try
        {
            var check = WinDivertSteerer.CheckAvailability(empty, isAdmin: true);

            Assert.Equal(SteererAvailability.DriverMissing, check);
        }
        finally { Directory.Delete(empty, true); }
    }

    [Fact]
    public void Needs_administrator_rights_even_when_the_driver_is_present()
    {
        var dir = Path.Combine(Path.GetTempPath(), "arqon-driver-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllBytes(Path.Combine(dir, "WinDivert.dll"), [0]);
            File.WriteAllBytes(Path.Combine(dir, "WinDivert64.sys"), [0]);

            Assert.Equal(SteererAvailability.NotAdmin, WinDivertSteerer.CheckAvailability(dir, isAdmin: false));
            Assert.Equal(SteererAvailability.Ready, WinDivertSteerer.CheckAvailability(dir, isAdmin: true));
        }
        finally { Directory.Delete(dir, true); }
    }
}
