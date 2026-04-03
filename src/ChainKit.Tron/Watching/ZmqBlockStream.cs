using System.Runtime.CompilerServices;
using ChainKit.Tron.Models;
using Google.Protobuf;
using NetMQ;
using NetMQ.Sockets;

namespace ChainKit.Tron.Watching;

public class ZmqBlockStream : ITronBlockStream
{
    private readonly string _endpoint;

    public ZmqBlockStream(string zmqEndpoint)
    {
        _endpoint = zmqEndpoint ?? throw new ArgumentNullException(nameof(zmqEndpoint));
    }

    public async IAsyncEnumerable<TronBlock> StreamBlocksAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        using var subscriber = new SubscriberSocket();
        subscriber.Connect(_endpoint);
        subscriber.Subscribe(""); // subscribe to all messages

        while (!ct.IsCancellationRequested)
        {
            TronBlock? block = null;
            try
            {
                // Try to receive with timeout so we can check cancellation
                if (subscriber.TryReceiveFrameBytes(TimeSpan.FromSeconds(1), out var data))
                {
                    block = ParseBlock(data);
                }
            }
            catch (OperationCanceledException) { yield break; }
            catch { /* swallow parse errors, continue */ }

            if (block != null)
                yield return block;
        }

        // Suppress CS1998: async method lacks await operators.
        // The method must be async to satisfy IAsyncEnumerable contract with
        // CancellationToken support via EnumeratorCancellation attribute.
        await Task.CompletedTask;
    }

    private static TronBlock? ParseBlock(byte[] data)
    {
        // Tron ZMQ sends protobuf-encoded Block messages
        try
        {
            var block = Protocol.Protobuf.Block.Parser.ParseFrom(data);
            var header = block.BlockHeader?.RawData;
            if (header == null) return null;

            var blockNum = header.Number;
            var timestamp = header.Timestamp;
            var blockId = BitConverter.ToString(
                System.Security.Cryptography.SHA256.HashData(
                    block.BlockHeader.ToByteArray())).Replace("-", "").ToLowerInvariant();

            var transactions = new List<TronBlockTransaction>();
            foreach (var tx in block.Transactions)
            {
                var txId = BitConverter.ToString(
                    System.Security.Cryptography.SHA256.HashData(
                        tx.RawData.ToByteArray())).Replace("-", "").ToLowerInvariant();

                var contract = tx.RawData?.Contract?.FirstOrDefault();
                var contractType = contract?.Type.ToString() ?? "Unknown";

                // Extract from/to from contract parameter would require full parsing
                // For now, set empty — the watcher can enrich later
                transactions.Add(new TronBlockTransaction(
                    txId, "", "", contractType, tx.RawData?.ToByteArray() ?? Array.Empty<byte>()));
            }

            return new TronBlock(
                blockNum, blockId,
                DateTimeOffset.FromUnixTimeMilliseconds(timestamp),
                transactions);
        }
        catch
        {
            return null;
        }
    }
}
