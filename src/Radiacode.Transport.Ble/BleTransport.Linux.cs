#if !WINDOWS
using Linux.Bluetooth;
using Linux.Bluetooth.Extensions;
using Radiacode.Core;

namespace Radiacode.Transport.Ble;

/// <summary>RadiaCode over Bluetooth LE using BlueZ (D-Bus) on Linux.</summary>
public sealed class BleTransport : BleTransportBase
{
    private const string ServiceUuid = "e63215e5-7003-49d8-96b0-b024798fb901";
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(20);
    private static readonly Dictionary<string, object> WriteWithoutResponse = new() { ["type"] = "command" };

    private readonly Device _device;
    private readonly GattCharacteristic _write;

    public override string Description { get; }

    private BleTransport(Device device, GattCharacteristic write, string name)
    {
        _device = device;
        _write = write;
        Description = $"Bluetooth LE ({name})";
    }

    private static async Task<IAdapter1> AdapterAsync()
    {
        if (!OperatingSystem.IsLinux()) throw new BleException("Bluetooth is not supported by this build on this platform.");
        return (await BlueZManager.GetAdaptersAsync()).FirstOrDefault() ?? throw new BleException("No Bluetooth adapter found.");
    }

    /// <summary>RadiaCode devices BlueZ knows about (discovery runs for <paramref name="duration"/>).</summary>
    private static async Task<List<(Device Device, string Address, string Name)>> DiscoverAsync(IAdapter1 adapter, TimeSpan duration,
        Func<string, bool>? stopWhen, CancellationToken ct)
    {
        var result = new List<(Device, string, string)>();
        await adapter.StartDiscoveryAsync();
        try
        {
            var deadline = DateTime.UtcNow + duration;
            while (DateTime.UtcNow < deadline)
            {
                result.Clear();
                foreach (var candidate in await adapter.GetDevicesAsync())
                {
                    try
                    {
                        var props = await candidate.GetAllAsync();
                        if (IsRadiaCodeName(props.Alias)) result.Add((candidate, props.Address, props.Alias));
                    }
                    catch { /* device vanished */ }
                }
                if (stopWhen is not null && result.Any(r => stopWhen(r.Item2))) break;
                await Task.Delay(1000, ct);
            }
        }
        finally
        {
            try { await adapter.StopDiscoveryAsync(); } catch { /* already stopped */ }
        }
        return result;
    }

    /// <summary>Lists RadiaCode devices seen within <paramref name="duration"/>.</summary>
    public static async Task<IReadOnlyList<BleDeviceInfo>> ScanAsync(TimeSpan duration, CancellationToken ct = default) =>
        (await DiscoverAsync(await AdapterAsync(), duration, null, ct)).Select(d => new BleDeviceInfo(d.Address, d.Name)).ToArray();

    /// <summary>Connects to the RadiaCode with <paramref name="id"/> (Bluetooth address), or to the first one BlueZ can see.</summary>
    public static async Task<ITransport> ConnectAsync(TimeSpan scanTimeout, CancellationToken ct = default, string? id = null)
    {
        var adapter = await AdapterAsync();
        var seen = await DiscoverAsync(adapter, scanTimeout, a => id is null || a == id, ct);
        var match = seen.FirstOrDefault(d => id is null || d.Address == id);
        if (match.Device is null)
            throw new BleException(id is null
                ? "No RadiaCode found over USB or Bluetooth. Is it switched on, in range, and not connected to another app or Bluetooth proxy?"
                : "The selected RadiaCode is not visible over Bluetooth (off, out of range, or connected elsewhere).");
        var device = match.Device;
        string name = match.Name;
        await device.ConnectAsync();
        await device.WaitForPropertyValueAsync("Connected", value: true, ConnectTimeout);
        await device.WaitForPropertyValueAsync("ServicesResolved", value: true, ConnectTimeout);

        try
        {
            var service = await device.GetServiceAsync(ServiceUuid)
                          ?? throw new BleException($"{name} does not expose the RadiaCode data service.");
            var write = await service.GetCharacteristicAsync(WriteCharacteristic)
                        ?? throw new BleException($"{name}: write characteristic missing.");
            var notify = await service.GetCharacteristicAsync(NotifyCharacteristic)
                         ?? throw new BleException($"{name}: notify characteristic missing.");

            var transport = new BleTransport(device, write, name);
            notify.Value += (_, e) =>
            {
                transport.OnNotification(e.Value);
                return Task.CompletedTask;
            };
            return transport;
        }
        catch
        {
            try { await device.DisconnectAsync(); } catch { /* best effort */ }
            throw;
        }
    }

    protected override Task WriteChunkAsync(byte[] chunk, CancellationToken ct) =>
        _write.WriteValueAsync(chunk, WriteWithoutResponse);

    public override async ValueTask DisposeAsync()
    {
        try { await _device.DisconnectAsync(); } catch { /* best effort */ }
    }
}
#endif
