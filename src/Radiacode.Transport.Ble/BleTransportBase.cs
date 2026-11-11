using Radiacode.Core;

namespace Radiacode.Transport.Ble;

public sealed class BleException(string message) : Exception(message);

/// <summary>A RadiaCode seen advertising over Bluetooth LE. <paramref name="Name"/> looks like "RadiaCode-110#RC-110-001702".</summary>
public sealed record BleDeviceInfo(string Id, string Name)
{
    /// <summary>The device serial embedded in the advertised name, if any.</summary>
    public string? Serial => Name.Split('#') is { Length: 2 } p ? p[1] : null;
}

/// <summary>
/// Shared request/response logic for the RadiaCode BLE link (protocol per cdump/radiacode, MIT):
/// requests are written in 18-byte chunks without response, the reply arrives as notifications.
/// </summary>
public abstract class BleTransportBase : ITransport
{
    public const string WriteCharacteristic = "e63215e6-7003-49d8-96b0-b024798fb901";
    public const string NotifyCharacteristic = "e63215e7-7003-49d8-96b0-b024798fb901";
    protected const int ChunkSize = 18;
    private static readonly TimeSpan ResponseTimeout = TimeSpan.FromSeconds(10);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly NotificationAssembler _assembler = new();
    private readonly object _sync = new();
    private TaskCompletionSource<byte[]>? _pending;

    public abstract string Description { get; }

    /// <summary>Device names look like "RadiaCode-110" or "RC-110-001702".</summary>
    public static bool IsRadiaCodeName(string? name) =>
        !string.IsNullOrEmpty(name) &&
        (name.StartsWith("RadiaCode", StringComparison.OrdinalIgnoreCase) || name.StartsWith("RC-", StringComparison.OrdinalIgnoreCase));

    protected abstract Task WriteChunkAsync(byte[] chunk, CancellationToken ct);

    /// <summary>Platform code calls this for every notification from the device.</summary>
    protected void OnNotification(ReadOnlySpan<byte> data)
    {
        lock (_sync)
        {
            if (_pending is null) return; // unsolicited; nothing is waiting
            try
            {
                if (_assembler.Append(data) is { } payload) _pending.TrySetResult(payload);
            }
            catch (Exception ex)
            {
                _assembler.Reset();
                _pending.TrySetException(ex);
            }
        }
    }

    public async Task<byte[]> ExecuteAsync(byte[] request, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            TaskCompletionSource<byte[]> pending;
            lock (_sync)
            {
                _assembler.Reset();
                pending = _pending = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            for (int pos = 0; pos < request.Length; pos += ChunkSize)
                await WriteChunkAsync(request.AsSpan(pos, Math.Min(ChunkSize, request.Length - pos)).ToArray(), ct);

            return await pending.Task.WaitAsync(ResponseTimeout, ct);
        }
        catch (TimeoutException)
        {
            throw new BleException("No response from the device over Bluetooth (10 s).");
        }
        finally
        {
            lock (_sync) _pending = null;
            _gate.Release();
        }
    }

    public abstract ValueTask DisposeAsync();
}
