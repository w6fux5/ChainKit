using ChainKit.Evm.Models;

namespace ChainKit.Evm.Watching;

/// <summary>
/// Provides an async stream of EVM blocks starting from a specified block number.
/// Implementations may use polling (HTTP RPC) or subscriptions (WebSocket).
/// </summary>
public interface IEvmBlockStream : IAsyncDisposable
{
    /// <summary>
    /// Yields blocks sequentially starting from <paramref name="startBlock"/>.
    /// When caught up to the chain tip, the stream waits for new blocks before yielding.
    /// </summary>
    /// <param name="startBlock">The block number to start streaming from.</param>
    /// <param name="ct">Cancellation token to stop streaming.</param>
    IAsyncEnumerable<EvmBlock> GetBlocksAsync(long startBlock, CancellationToken ct = default);
}
