using System.Runtime.InteropServices;

namespace Arqon.Core.Steering;

/// <summary>Asks Windows whether a file carries a valid Authenticode signature (the same check the driver loader and SmartScreen use).</summary>
public static class DriverSignature
{
    private static readonly Guid GenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    [StructLayout(LayoutKind.Sequential)]
    private struct WinTrustFileInfo
    {
        public uint StructSize;
        public IntPtr FilePath;
        public IntPtr FileHandle;
        public IntPtr KnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WinTrustData
    {
        public uint StructSize;
        public IntPtr PolicyCallbackData;
        public IntPtr SipClientData;
        public uint UiChoice;          // 2 = none
        public uint RevocationChecks;  // 0 = none: an offline PC must still be able to install
        public uint UnionChoice;       // 1 = file
        public IntPtr FileInfo;
        public uint StateAction;       // 0 = ignore
        public IntPtr StateData;
        public IntPtr UrlReference;
        public uint ProvFlags;
        public uint UiContext;
        public IntPtr SignatureSettings;
    }

    [DllImport("wintrust.dll", CharSet = CharSet.Unicode)]
    private static extern int WinVerifyTrust(IntPtr window, ref Guid action, ref WinTrustData data);

    /// <summary>True when Windows accepts the file's signature. False for unsigned, tampered or unreadable files.</summary>
    public static bool IsValid(string path)
    {
        var pathPointer = Marshal.StringToCoTaskMemUni(path);
        var infoPointer = IntPtr.Zero;
        try
        {
            var info = new WinTrustFileInfo { StructSize = (uint)Marshal.SizeOf<WinTrustFileInfo>(), FilePath = pathPointer };
            infoPointer = Marshal.AllocCoTaskMem(Marshal.SizeOf<WinTrustFileInfo>());
            Marshal.StructureToPtr(info, infoPointer, false);
            var data = new WinTrustData
            {
                StructSize = (uint)Marshal.SizeOf<WinTrustData>(),
                UiChoice = 2,
                RevocationChecks = 0,
                UnionChoice = 1,
                FileInfo = infoPointer,
                ProvFlags = 0x00000010,   // no revocation lookup over the network
            };
            var action = GenericVerifyV2;
            return WinVerifyTrust(IntPtr.Zero, ref action, ref data) == 0;
        }
        finally
        {
            if (infoPointer != IntPtr.Zero) Marshal.FreeCoTaskMem(infoPointer);
            Marshal.FreeCoTaskMem(pathPointer);
        }
    }
}
