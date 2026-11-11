using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Radiacode.Core;

namespace Radiacode.Transport.LibUsb;

/// <summary>
/// USB transport through libusb-1.0. On Windows the device must use the WinUSB driver,
/// on Linux a udev rule granting access to 0483:f123 is needed (or run as root).
/// </summary>
public sealed class LibUsbTransport : ITransport
{
    public const ushort VendorId = 0x0483;
    public const ushort ProductId = 0xF123;
    private const byte EpOut = 0x01;
    private const byte EpIn = 0x81;

    private IntPtr _ctx;
    private IntPtr _handle;
    private readonly uint _timeoutMs;
    private readonly object _io = new();

    public string Description => "USB (libusb)";

    private LibUsbTransport(IntPtr ctx, IntPtr handle, uint timeoutMs)
    {
        _ctx = ctx;
        _handle = handle;
        _timeoutMs = timeoutMs;
    }

    /// <summary>All RadiaCode devices on USB. <see cref="UsbDeviceInfo.Key"/> is the USB serial (or bus/address).</summary>
    public static IReadOnlyList<UsbDeviceInfo> List()
    {
        var result = new List<UsbDeviceInfo>();
        Check(LibUsbNative.Init(out var ctx), "libusb_init");
        try
        {
            ForEachRadiaCode(ctx, (dev, key, serial) => { result.Add(new UsbDeviceInfo(key, serial)); return false; });
        }
        finally
        {
            LibUsbNative.Exit(ctx);
        }
        return result;
    }

    /// <summary>Calls <paramref name="visit"/> for each RadiaCode; stops when it returns true.</summary>
    private static unsafe void ForEachRadiaCode(IntPtr ctx, Func<IntPtr, string, string?, bool> visit)
    {
        nint count = LibUsbNative.GetDeviceList(ctx, out var list);
        if (count < 0) throw new IOException($"libusb_get_device_list: {LibUsbNative.ErrorName((int)count)}");
        try
        {
            for (int i = 0; i < count; i++)
            {
                var dev = Marshal.ReadIntPtr(list, i * IntPtr.Size);
                if (LibUsbNative.GetDeviceDescriptor(dev, out var d) != 0 || d.idVendor != VendorId || d.idProduct != ProductId) continue;
                string? serial = null;
                if (d.iSerialNumber != 0 && LibUsbNative.Open(dev, out var h) == 0)
                {
                    var buf = stackalloc byte[128];
                    int n = LibUsbNative.GetStringDescriptorAscii(h, d.iSerialNumber, buf, 128);
                    if (n > 0) serial = System.Text.Encoding.ASCII.GetString(buf, n);
                    LibUsbNative.Close(h);
                }
                string key = serial ?? $"bus{LibUsbNative.GetBusNumber(dev)}-{LibUsbNative.GetDeviceAddress(dev)}";
                if (visit(dev, key, serial)) return;
            }
        }
        finally
        {
            LibUsbNative.FreeDeviceList(list, 1);
        }
    }

    /// <summary>Opens a RadiaCode on USB: the one with <paramref name="key"/> (from <see cref="List"/>), or the first found.</summary>
    public static LibUsbTransport Open(string? key = null, uint timeoutMs = 3000)
    {
        Check(LibUsbNative.Init(out var ctx), "libusb_init");
        IntPtr handle = IntPtr.Zero;
        if (key is null)
            handle = LibUsbNative.OpenDeviceWithVidPid(ctx, VendorId, ProductId);
        else
            ForEachRadiaCode(ctx, (dev, k, _) => k == key && LibUsbNative.Open(dev, out handle) == 0);
        if (handle == IntPtr.Zero)
        {
            LibUsbNative.Exit(ctx);
            throw new DeviceNotFoundException(
                "RadiaCode not found or busy (USB 0483:F123). Close the vendor app; on Windows the WinUSB driver is required.");
        }
        LibUsbNative.SetAutoDetachKernelDriver(handle, 1); // ignored where unsupported
        int rc = LibUsbNative.ClaimInterface(handle, 0);
        if (rc != LibUsbNative.LIBUSB_SUCCESS)
        {
            LibUsbNative.Close(handle);
            LibUsbNative.Exit(ctx);
            throw new IOException($"libusb_claim_interface: {LibUsbNative.ErrorName(rc)} (device in use by another app?)");
        }
        var t = new LibUsbTransport(ctx, handle, timeoutMs);
        t.Drain();
        return t;
    }

    /// <summary>Discards stale data left in the IN endpoint from a previous session.</summary>
    private void Drain()
    {
        var buf = new byte[256];
        while (Read(buf, 100, out _) == LibUsbNative.LIBUSB_SUCCESS) { }
    }

    public Task<byte[]> ExecuteAsync(byte[] request, CancellationToken ct = default) =>
        Task.Run(() => Execute(request), ct);

    private byte[] Execute(byte[] request)
    {
        lock (_io)
        {
            if (_handle == IntPtr.Zero) throw new ObjectDisposedException(nameof(LibUsbTransport));
            Write(request);

            var buf = new byte[256];
            int n = 0;
            for (int trial = 0; trial < 3 && n == 0; trial++)
                Check(Read(buf, _timeoutMs, out n), "bulk read");
            if (n < 4) throw new IOException("Short USB response");

            int length = BinaryPrimitives.ReadInt32LittleEndian(buf);
            var result = new byte[length];
            int have = Math.Min(n - 4, length);
            Array.Copy(buf, 4, result, 0, have);
            while (have < length)
            {
                Check(Read(buf, _timeoutMs, out n), "bulk read");
                int take = Math.Min(n, length - have);
                Array.Copy(buf, 0, result, have, take);
                have += take;
            }
            return result;
        }
    }

    private unsafe void Write(byte[] data)
    {
        fixed (byte* p = data)
        {
            Check(LibUsbNative.BulkTransfer(_handle, EpOut, p, data.Length, out int sent, _timeoutMs), "bulk write");
            if (sent != data.Length) throw new IOException($"USB write sent {sent}/{data.Length} bytes");
        }
    }

    private unsafe int Read(byte[] buf, uint timeoutMs, out int read)
    {
        fixed (byte* p = buf)
            return LibUsbNative.BulkTransfer(_handle, EpIn, p, buf.Length, out read, timeoutMs);
    }

    private static void Check(int rc, string what)
    {
        if (rc != LibUsbNative.LIBUSB_SUCCESS) throw new IOException($"{what}: {LibUsbNative.ErrorName(rc)}");
    }

    public ValueTask DisposeAsync()
    {
        lock (_io)
        {
            if (_handle != IntPtr.Zero)
            {
                LibUsbNative.ReleaseInterface(_handle, 0);
                LibUsbNative.Close(_handle);
                _handle = IntPtr.Zero;
            }
            if (_ctx != IntPtr.Zero)
            {
                LibUsbNative.Exit(_ctx);
                _ctx = IntPtr.Zero;
            }
        }
        return ValueTask.CompletedTask;
    }
}

public sealed class DeviceNotFoundException(string message) : Exception(message);

/// <summary>A RadiaCode seen on USB; <paramref name="UsbSerial"/> is the USB serial-number string, if it has one.</summary>
public sealed record UsbDeviceInfo(string Key, string? UsbSerial);
