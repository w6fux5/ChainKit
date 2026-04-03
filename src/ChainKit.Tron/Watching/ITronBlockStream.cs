using ChainKit.Tron.Models;

namespace ChainKit.Tron.Watching;

public interface ITronBlockStream
{
    IAsyncEnumerable<TronBlock> StreamBlocksAsync(CancellationToken ct = default);
}
