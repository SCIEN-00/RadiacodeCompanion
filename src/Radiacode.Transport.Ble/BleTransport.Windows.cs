#if WINDOWS
using Radiacode.Core;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Security.Cryptography;

namespace Radiacode.Transport.Ble;

/// <summary>RadiaCode over Bluetooth LE using the Windows (WinRT) Bluetooth stack.</summary>
public sealed class BleTransport : BleTransportBase
{
    private readonly BluetoothLEDevice _device;
    private readonly GattCharacteristic _write, _notify;
    private readonly List<GattDeviceService> _services;

    public override string Description { get; }

    private BleTransport(BluetoothLEDevice device, List<GattDeviceService> services, GattCharacteristic write, GattCharacteristic notify, string name)
    {
        _device = device;
        _services = services;
        _write = write;
        _notify = notify;
        Description = $"Bluetooth LE ({name})";
        _notify.ValueChanged += OnValueChanged;
    }

    private void OnValueChanged(GattCharacteristic sender, GattValueChangedEventArgs e)
    {
        CryptographicBuffer.CopyToByteArray(e.CharacteristicValue, out byte[] data);
        OnNotification(data);
    }

    /// <summary>Lists RadiaCode devices advertising within <paramref name="duration"/>.</summary>
    public static async Task<IReadOnlyList<BleDeviceInfo>> ScanAsync(TimeSpan duration, CancellationToken ct = default)
    {
        var found = new Dictionary<ulong, string>();
        var watcher = new BluetoothLEAdvertisementWatcher { ScanningMode = BluetoothLEScanningMode.Active };
        watcher.Received += (_, e) =>
        {
            var name = e.Advertisement.LocalName;
            if (IsRadiaCodeName(name)) lock (found) found[e.BluetoothAddress] = name;
        };
        watcher.Start();
        try { await Task.Delay(duration, ct); }
        finally { watcher.Stop(); }
        lock (found) return found.Select(kv => new BleDeviceInfo(kv.Key.ToString("X12"), kv.Value)).ToArray();
    }

    /// <summary>Connects to the RadiaCode with <paramref name="id"/> (from <see cref="ScanAsync"/>), or to the first one that advertises.</summary>
    public static async Task<ITransport> ConnectAsync(TimeSpan scanTimeout, CancellationToken ct = default, string? id = null)
    {
        var found = new TaskCompletionSource<(ulong Address, string Name)>(TaskCreationOptions.RunContinuationsAsynchronously);
        var watcher = new BluetoothLEAdvertisementWatcher { ScanningMode = BluetoothLEScanningMode.Active };
        watcher.Received += (_, e) =>
        {
            var name = e.Advertisement.LocalName;
            if (IsRadiaCodeName(name) && (id is null || e.BluetoothAddress.ToString("X12") == id))
                found.TrySetResult((e.BluetoothAddress, name));
        };
        watcher.Start();
        try
        {
            if (await Task.WhenAny(found.Task, Task.Delay(scanTimeout, ct)) != found.Task)
            {
                ct.ThrowIfCancellationRequested();
                throw new BleException(id is null
                    ? "No RadiaCode found over USB or Bluetooth. Is it switched on, in range, and not connected to another app or Bluetooth proxy?"
                    : "The selected RadiaCode is not advertising over Bluetooth (off, out of range, or connected elsewhere).");
            }
        }
        finally
        {
            watcher.Stop();
        }
        var (address, deviceName) = await found.Task;
        var device = await BluetoothLEDevice.FromBluetoothAddressAsync(address)
                     ?? throw new BleException($"Could not open Bluetooth device {deviceName}.");
        var services = new List<GattDeviceService>();
        try
        {
            var result = await device.GetGattServicesAsync(BluetoothCacheMode.Uncached);
            if (result.Status != GattCommunicationStatus.Success)
                throw new BleException($"Bluetooth connection to {deviceName} failed ({result.Status}).");
            services.AddRange(result.Services);

            GattCharacteristic? write = null, notify = null;
            foreach (var service in services)
            {
                var chars = await service.GetCharacteristicsAsync(BluetoothCacheMode.Uncached);
                if (chars.Status != GattCommunicationStatus.Success) continue;
                foreach (var c in chars.Characteristics)
                {
                    if (c.Uuid == Guid.Parse(WriteCharacteristic)) write = c;
                    else if (c.Uuid == Guid.Parse(NotifyCharacteristic)) notify = c;
                }
            }
            if (write is null || notify is null)
                throw new BleException($"{deviceName} does not expose the RadiaCode data service.");

            var transport = new BleTransport(device, services, write, notify, deviceName);
            var status = await notify.WriteClientCharacteristicConfigurationDescriptorAsync(
                GattClientCharacteristicConfigurationDescriptorValue.Notify);
            if (status != GattCommunicationStatus.Success)
            {
                await transport.DisposeAsync();
                throw new BleException($"Could not enable notifications on {deviceName} ({status}).");
            }
            return transport;
        }
        catch
        {
            foreach (var s in services) s.Dispose();
            device.Dispose();
            throw;
        }
    }

    protected override async Task WriteChunkAsync(byte[] chunk, CancellationToken ct)
    {
        var status = await _write.WriteValueAsync(CryptographicBuffer.CreateFromByteArray(chunk), GattWriteOption.WriteWithoutResponse);
        if (status != GattCommunicationStatus.Success) throw new BleException($"Bluetooth write failed ({status}).");
    }

    public override ValueTask DisposeAsync()
    {
        _notify.ValueChanged -= OnValueChanged;
        foreach (var s in _services) s.Dispose();
        _device.Dispose();
        return ValueTask.CompletedTask;
    }
}
#endif
