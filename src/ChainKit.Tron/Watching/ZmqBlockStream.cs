using System.Runtime.CompilerServices;
using ChainKit.Core.Extensions;
using ChainKit.Tron.Models;
using ChainKit.Tron.Protocol.Protobuf;
using Google.Protobuf;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NetMQ;
using NetMQ.Sockets;

namespace ChainKit.Tron.Watching;

public sealed class ZmqBlockStream : ITronBlockStream
{
    private readonly string _endpoint;
    private readonly ILogger _logger;

    public ZmqBlockStream(string zmqEndpoint, ILogger<ZmqBlockStream>? logger = null)
    {
        _endpoint = zmqEndpoint ?? throw new ArgumentNullException(nameof(zmqEndpoint));
        _logger = logger ?? NullLogger<ZmqBlockStream>.Instance;
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
            catch (Exception ex) { _logger.LogDebug(ex, "ZMQ block parse error, continuing"); }

            if (block != null)
                yield return block;
        }

        // Suppress CS1998: async method lacks await operators.
        // The method must be async to satisfy IAsyncEnumerable contract with
        // CancellationToken support via EnumeratorCancellation attribute.
        await Task.CompletedTask;
    }

    internal static TronBlock? ParseBlock(byte[] data)
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

                string fromAddress = "";
                string toAddress = "";
                ExtractAddresses(contract, out fromAddress, out toAddress);

                transactions.Add(new TronBlockTransaction(
                    txId, fromAddress, toAddress, contractType,
                    tx.RawData?.ToByteArray() ?? Array.Empty<byte>()));
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

    /// <summary>
    /// Unpacks the contract parameter from a protobuf Transaction.Contract
    /// to extract owner_address (from) and to_address/contract_address (to).
    /// </summary>
    internal static void ExtractAddresses(
        Transaction.Types.Contract? contract,
        out string fromAddress, out string toAddress)
    {
        fromAddress = "";
        toAddress = "";

        if (contract?.Parameter == null) return;

        try
        {
            switch (contract.Type)
            {
                case Transaction.Types.Contract.Types.ContractType.TransferContract:
                {
                    var transfer = contract.Parameter.Unpack<TransferContract>();
                    fromAddress = transfer.OwnerAddress.ToByteArray().ToHex();
                    toAddress = transfer.ToAddress.ToByteArray().ToHex();
                    break;
                }
                case Transaction.Types.Contract.Types.ContractType.TriggerSmartContract:
                {
                    var trigger = contract.Parameter.Unpack<TriggerSmartContract>();
                    fromAddress = trigger.OwnerAddress.ToByteArray().ToHex();
                    toAddress = trigger.ContractAddress.ToByteArray().ToHex();
                    break;
                }
                default:
                {
                    // For other contract types, try to extract owner_address from raw bytes.
                    // Protobuf field 1 (owner_address) with wire type 2 (length-delimited) = tag 0x0a.
                    var paramBytes = contract.Parameter.Value.ToByteArray();
                    fromAddress = TryExtractProtobufBytesField(paramBytes, fieldNumber: 1);
                    break;
                }
            }
        }
        catch
        {
            // If unpacking fails, leave addresses empty
        }
    }

    /// <summary>
    /// Simple extraction of a length-delimited bytes field from protobuf data
    /// by field number. Returns hex string or empty.
    /// </summary>
    private static string TryExtractProtobufBytesField(byte[] data, int fieldNumber)
    {
        try
        {
            int pos = 0;
            while (pos < data.Length)
            {
                int tag = 0;
                int shift = 0;
                while (pos < data.Length && (data[pos] & 0x80) != 0)
                {
                    tag |= (data[pos++] & 0x7f) << shift;
                    shift += 7;
                }
                if (pos < data.Length)
                    tag |= (data[pos++] & 0x7f) << shift;

                int field = tag >> 3;
                int wireType = tag & 7;

                if (wireType == 2) // length-delimited
                {
                    int length = 0;
                    shift = 0;
                    while (pos < data.Length && (data[pos] & 0x80) != 0)
                    {
                        length |= (data[pos++] & 0x7f) << shift;
                        shift += 7;
                    }
                    if (pos < data.Length)
                        length |= (data[pos++] & 0x7f) << shift;

                    if (field == fieldNumber && pos + length <= data.Length)
                    {
                        var fieldBytes = new byte[length];
                        Buffer.BlockCopy(data, pos, fieldBytes, 0, length);
                        return fieldBytes.ToHex();
                    }
                    pos += length;
                }
                else if (wireType == 0) // varint
                {
                    while (pos < data.Length && (data[pos++] & 0x80) != 0) { }
                }
                else if (wireType == 1) // 64-bit
                {
                    pos += 8;
                }
                else if (wireType == 5) // 32-bit
                {
                    pos += 4;
                }
                else
                {
                    break; // unknown wire type
                }
            }
        }
        catch { /* parsing error */ }
        return "";
    }
}
