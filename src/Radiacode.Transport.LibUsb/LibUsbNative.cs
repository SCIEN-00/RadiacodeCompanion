using System.Runtime.InteropServices;

namespace Radiacode.Transport.LibUsb;

internal static partial class LibUsbNative
{
    private const string Lib = "libusb-1.0";

    static LibUsbNative()
    {
        // Linux ships the library as libusb-1.0.so.0; Windows needs libusb-1.0.dll next to the app.
        NativeLibrary.SetDllImportResolver(typeof(LibUsbNative).Assembly, static (name, asm, path) =>
        {
            if (name != Lib) return IntPtr.Zero;
            foreach (var candidate in OperatingSystem.IsWindows()
                         ? new[] { "libusb-1.0.dll" }
                         : OperatingSystem.IsMacOS()
                             ? new[] { "libusb-1.0.0.dylib", "libusb-1.0.dylib" }
                             : new[] { "libusb-1.0.so.0", "libusb-1.0.so" })
            {
                if (NativeLibrary.TryLoad(candidate, asm, path, out var h)) return h;
            }
            return IntPtr.Zero;
        });
    }

    public const int LIBUSB_SUCCESS = 0;
    public const int LIBUSB_ERROR_TIMEOUT = -7;

    [LibraryImport(Lib, EntryPoint = "libusb_init")]
    public static partial int Init(out IntPtr ctx);

    [LibraryImport(Lib, EntryPoint = "libusb_exit")]
    public static partial void Exit(IntPtr ctx);

    [LibraryImport(Lib, EntryPoint = "libusb_open_device_with_vid_pid")]
    public static partial IntPtr OpenDeviceWithVidPid(IntPtr ctx, ushort vid, ushort pid);

    [LibraryImport(Lib, EntryPoint = "libusb_close")]
    public static partial void Close(IntPtr handle);

    [LibraryImport(Lib, EntryPoint = "libusb_set_auto_detach_kernel_driver")]
    public static partial int SetAutoDetachKernelDriver(IntPtr handle, int enable);

    [LibraryImport(Lib, EntryPoint = "libusb_claim_interface")]
    public static partial int ClaimInterface(IntPtr handle, int iface);

    [LibraryImport(Lib, EntryPoint = "libusb_release_interface")]
    public static partial int ReleaseInterface(IntPtr handle, int iface);

    [LibraryImport(Lib, EntryPoint = "libusb_bulk_transfer")]
    public static unsafe partial int BulkTransfer(IntPtr handle, byte endpoint, byte* data, int length, out int transferred, uint timeoutMs);

    [StructLayout(LayoutKind.Sequential)]
    public struct DeviceDescriptor
    {
        public byte bLength, bDescriptorType;
        public ushort bcdUSB;
        public byte bDeviceClass, bDeviceSubClass, bDeviceProtocol, bMaxPacketSize0;
        public ushort idVendor, idProduct, bcdDevice;
        public byte iManufacturer, iProduct, iSerialNumber, bNumConfigurations;
    }

    [LibraryImport(Lib, EntryPoint = "libusb_get_device_list")]
    public static partial nint GetDeviceList(IntPtr ctx, out IntPtr list);

    [LibraryImport(Lib, EntryPoint = "libusb_free_device_list")]
    public static partial void FreeDeviceList(IntPtr list, int unrefDevices);

    [LibraryImport(Lib, EntryPoint = "libusb_get_device_descriptor")]
    public static partial int GetDeviceDescriptor(IntPtr device, out DeviceDescriptor descriptor);

    [LibraryImport(Lib, EntryPoint = "libusb_open")]
    public static partial int Open(IntPtr device, out IntPtr handle);

    [LibraryImport(Lib, EntryPoint = "libusb_get_bus_number")]
    public static partial byte GetBusNumber(IntPtr device);

    [LibraryImport(Lib, EntryPoint = "libusb_get_device_address")]
    public static partial byte GetDeviceAddress(IntPtr device);

    [LibraryImport(Lib, EntryPoint = "libusb_get_string_descriptor_ascii")]
    public static unsafe partial int GetStringDescriptorAscii(IntPtr handle, byte index, byte* data, int length);

    [LibraryImport(Lib, EntryPoint = "libusb_error_name")]
    public static partial IntPtr ErrorNamePtr(int code);

    public static string ErrorName(int code) => Marshal.PtrToStringAnsi(ErrorNamePtr(code)) ?? code.ToString();
}
