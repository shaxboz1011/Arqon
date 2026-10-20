using System.Runtime.InteropServices;

namespace Arqon.Core.Steering;

/// <summary>
/// WinDivert 2.2 (https://reqrypt.org/windivert.html, LGPLv3). Its binaries are not part of this repository:
/// WinDivert.dll and WinDivert64.sys must sit next to Arqon.exe or in %APPDATA%\Arqon\driver (see <see cref="WinDivertLocator"/>;
/// the in-app setup installs them).
/// </summary>
internal static class WinDivertNative
{
    static WinDivertNative() => WinDivertLocator.RegisterResolver();

    public const int LayerNetwork = 0;
    public const int ShutdownBoth = 3;
    public static readonly IntPtr InvalidHandle = new(-1);

    /// <summary>WINDIVERT_ADDRESS is 80 bytes; only the fields we use are named.</summary>
    [StructLayout(LayoutKind.Sequential, Size = 80)]
    public struct Address
    {
        public long Timestamp;
        public uint Flags;      // Layer:8 Event:8 Sniffed:1 Outbound:1 Loopback:1 Impostor:1 IPv6:1 IPChecksum:1 TCPChecksum:1 UDPChecksum:1
        public uint Reserved2;
        public uint IfIdx;      // network layer data starts here
        public uint SubIfIdx;

        public readonly bool Outbound => (Flags >> 17 & 1) != 0;
    }

    [DllImport("WinDivert.dll", EntryPoint = "WinDivertOpen", CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
    public static extern IntPtr Open([MarshalAs(UnmanagedType.LPStr)] string filter, int layer, short priority, ulong flags);

    [DllImport("WinDivert.dll", EntryPoint = "WinDivertRecv", CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool Recv(IntPtr handle, byte[] packet, uint packetLength, out uint receivedLength, ref Address address);

    [DllImport("WinDivert.dll", EntryPoint = "WinDivertSend", CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool Send(IntPtr handle, byte[] packet, uint packetLength, out uint sentLength, ref Address address);

    [DllImport("WinDivert.dll", EntryPoint = "WinDivertShutdown", CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool Shutdown(IntPtr handle, int how);

    [DllImport("WinDivert.dll", EntryPoint = "WinDivertClose", CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool Close(IntPtr handle);
}
