namespace Radiacode.Core;

/// <summary>
/// A byte pipe to the device. <see cref="ExecuteAsync"/> sends a full framed request
/// (u32 length + payload) and returns the response payload without its u32 length prefix.
/// </summary>
public interface ITransport : IAsyncDisposable
{
    string Description { get; }
    Task<byte[]> ExecuteAsync(byte[] request, CancellationToken ct = default);
}
