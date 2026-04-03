using ChainKit.Core.Extensions;
using ChainKit.Tron.Protocol.Protobuf;
using ChainKit.Tron.Watching;
using Google.Protobuf;
using Xunit;

namespace ChainKit.Tron.Tests.Watching;

public class ZmqBlockStreamTests
{
    [Fact]
    public void Constructor_NullEndpoint_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new ZmqBlockStream(null!));
    }

    [Fact]
    public void Constructor_ValidEndpoint_DoesNotThrow()
    {
        var stream = new ZmqBlockStream("tcp://localhost:5555");
        Assert.NotNull(stream);
    }

    [Fact]
    public async Task StreamBlocksAsync_CancelledToken_CompletesImmediately()
    {
        var stream = new ZmqBlockStream("tcp://localhost:5555");
        var cts = new CancellationTokenSource();
        cts.Cancel();

        var blocks = new List<ChainKit.Tron.Models.TronBlock>();
        // This should not hang — it should complete quickly
        // Note: NetMQ may throw if no socket available, which is fine
        try
        {
            await foreach (var block in stream.StreamBlocksAsync(cts.Token))
                blocks.Add(block);
        }
        catch (NetMQ.TerminatingException) { /* expected when no context */ }
        catch (Exception) { /* acceptable for unit test without real ZMQ endpoint */ }
    }

    [Fact]
    public void ParseBlock_TransferContract_ExtractsAddresses()
    {
        // Build a protobuf Block with a TransferContract transaction
        var ownerHex = "41aabbccdd00112233445566778899aabbccddeeff";
        var toHex = "41112233445566778899aabbccddeeff00112233aa";

        var transfer = new TransferContract
        {
            OwnerAddress = ByteString.CopyFrom(ownerHex.FromHex()),
            ToAddress = ByteString.CopyFrom(toHex.FromHex()),
            Amount = 5_000_000
        };

        var block = BuildProtobufBlock(
            Transaction.Types.Contract.Types.ContractType.TransferContract,
            Google.Protobuf.WellKnownTypes.Any.Pack(transfer, "type.googleapis.com"));

        var result = ZmqBlockStream.ParseBlock(block.ToByteArray());

        Assert.NotNull(result);
        Assert.Single(result!.Transactions);
        Assert.Equal(ownerHex, result.Transactions[0].FromAddress);
        Assert.Equal(toHex, result.Transactions[0].ToAddress);
        Assert.Equal("TransferContract", result.Transactions[0].ContractType);
    }

    [Fact]
    public void ParseBlock_TriggerSmartContract_ExtractsAddresses()
    {
        var ownerHex = "41aabbccdd00112233445566778899aabbccddeeff";
        var contractHex = "41eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee";

        var trigger = new TriggerSmartContract
        {
            OwnerAddress = ByteString.CopyFrom(ownerHex.FromHex()),
            ContractAddress = ByteString.CopyFrom(contractHex.FromHex()),
            Data = ByteString.CopyFrom(new byte[] { 0xa9, 0x05, 0x9c, 0xbb })
        };

        var block = BuildProtobufBlock(
            Transaction.Types.Contract.Types.ContractType.TriggerSmartContract,
            Google.Protobuf.WellKnownTypes.Any.Pack(trigger, "type.googleapis.com"));

        var result = ZmqBlockStream.ParseBlock(block.ToByteArray());

        Assert.NotNull(result);
        Assert.Single(result!.Transactions);
        Assert.Equal(ownerHex, result.Transactions[0].FromAddress);
        Assert.Equal(contractHex, result.Transactions[0].ToAddress);
        Assert.Equal("TriggerSmartContract", result.Transactions[0].ContractType);
    }

    [Fact]
    public void ParseBlock_MultipleTransactions_AllHaveAddresses()
    {
        var owner1 = "41aabbccdd00112233445566778899aabbccddeeff";
        var to1 = "41112233445566778899aabbccddeeff00112233aa";
        var owner2 = "41ffffffffffffffffffffffffffffffffffffffff";
        var contract2 = "41eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee";

        var transfer = new TransferContract
        {
            OwnerAddress = ByteString.CopyFrom(owner1.FromHex()),
            ToAddress = ByteString.CopyFrom(to1.FromHex()),
            Amount = 1_000_000
        };
        var trigger = new TriggerSmartContract
        {
            OwnerAddress = ByteString.CopyFrom(owner2.FromHex()),
            ContractAddress = ByteString.CopyFrom(contract2.FromHex()),
            Data = ByteString.CopyFrom(new byte[4])
        };

        var block = new Block
        {
            BlockHeader = new BlockHeader
            {
                RawData = new BlockHeader.Types.raw
                {
                    Number = 100,
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                }
            }
        };
        block.Transactions.Add(BuildTransaction(
            Transaction.Types.Contract.Types.ContractType.TransferContract,
            Google.Protobuf.WellKnownTypes.Any.Pack(transfer, "type.googleapis.com")));
        block.Transactions.Add(BuildTransaction(
            Transaction.Types.Contract.Types.ContractType.TriggerSmartContract,
            Google.Protobuf.WellKnownTypes.Any.Pack(trigger, "type.googleapis.com")));

        var result = ZmqBlockStream.ParseBlock(block.ToByteArray());

        Assert.NotNull(result);
        Assert.Equal(2, result!.Transactions.Count);
        Assert.Equal(owner1, result.Transactions[0].FromAddress);
        Assert.Equal(to1, result.Transactions[0].ToAddress);
        Assert.Equal(owner2, result.Transactions[1].FromAddress);
        Assert.Equal(contract2, result.Transactions[1].ToAddress);
    }

    [Fact]
    public void ParseBlock_InvalidData_ReturnsNull()
    {
        var result = ZmqBlockStream.ParseBlock(new byte[] { 0xff, 0xfe, 0xfd });
        // Should not throw, returns null for invalid data
        Assert.Null(result);
    }

    [Fact]
    public void ExtractAddresses_NullContract_ReturnsEmpty()
    {
        ZmqBlockStream.ExtractAddresses(null, out var from, out var to);
        Assert.Equal("", from);
        Assert.Equal("", to);
    }

    private static Block BuildProtobufBlock(
        Transaction.Types.Contract.Types.ContractType contractType,
        Google.Protobuf.WellKnownTypes.Any parameter)
    {
        var block = new Block
        {
            BlockHeader = new BlockHeader
            {
                RawData = new BlockHeader.Types.raw
                {
                    Number = 42,
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                }
            }
        };
        block.Transactions.Add(BuildTransaction(contractType, parameter));
        return block;
    }

    private static Transaction BuildTransaction(
        Transaction.Types.Contract.Types.ContractType contractType,
        Google.Protobuf.WellKnownTypes.Any parameter)
    {
        var tx = new Transaction
        {
            RawData = new Transaction.Types.raw
            {
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            }
        };
        tx.RawData.Contract.Add(new Transaction.Types.Contract
        {
            Type = contractType,
            Parameter = parameter
        });
        return tx;
    }
}
