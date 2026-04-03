using System.Text.Json;
using ChainKit.Tron.Crypto;
using ChainKit.Tron.Models;
using ChainKit.Tron.Protocol.Protobuf;
using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;

namespace ChainKit.Tron.Providers;

/// <summary>
/// Tron provider that communicates via gRPC using manual method descriptors.
/// Uses Approach B: raw byte marshalling with <see cref="CallInvoker"/> to avoid
/// needing full gRPC service stubs generated from proto files.
/// </summary>
public class TronGrpcProvider : ITronProvider, IDisposable
{
    private readonly GrpcChannel _fullNodeChannel;
    private readonly GrpcChannel? _solidityChannel;
    private readonly CallInvoker _fullNodeInvoker;
    private readonly CallInvoker? _solidityInvoker;

    // Marshallers for raw bytes (used for request/response types not in our trimmed protos)
    private static readonly Marshaller<byte[]> ByteArrayMarshaller = new(
        serializer: bytes => bytes,
        deserializer: bytes => bytes);

    // Marshaller for IMessage types we do have (Transaction, TriggerSmartContract)
    private static Marshaller<T> ProtoMarshaller<T>() where T : IMessage<T>, new()
        => new(
            serializer: msg => msg.ToByteArray(),
            deserializer: bytes =>
            {
                var msg = new T();
                msg.MergeFrom(bytes);
                return msg;
            });

    // --- gRPC Method descriptors for Wallet service ---

    private static readonly Method<byte[], byte[]> GetAccountMethod = new(
        MethodType.Unary, "protocol.Wallet", "GetAccount",
        ByteArrayMarshaller, ByteArrayMarshaller);

    private static readonly Method<byte[], byte[]> GetNowBlockMethod = new(
        MethodType.Unary, "protocol.Wallet", "GetNowBlock",
        ByteArrayMarshaller, ByteArrayMarshaller);

    private static readonly Method<byte[], byte[]> GetBlockByNumMethod = new(
        MethodType.Unary, "protocol.Wallet", "GetBlockByNum",
        ByteArrayMarshaller, ByteArrayMarshaller);

    private static readonly Method<byte[], byte[]> BroadcastTransactionMethod = new(
        MethodType.Unary, "protocol.Wallet", "BroadcastTransaction",
        ByteArrayMarshaller, ByteArrayMarshaller);

    private static readonly Method<byte[], byte[]> GetTransactionByIdMethod = new(
        MethodType.Unary, "protocol.Wallet", "GetTransactionById",
        ByteArrayMarshaller, ByteArrayMarshaller);

    private static readonly Method<byte[], byte[]> TriggerSmartContractMethod = new(
        MethodType.Unary, "protocol.Wallet", "TriggerSmartContract",
        ByteArrayMarshaller, ByteArrayMarshaller);

    private static readonly Method<byte[], byte[]> TriggerConstantContractMethod = new(
        MethodType.Unary, "protocol.Wallet", "TriggerConstantContract",
        ByteArrayMarshaller, ByteArrayMarshaller);

    private static readonly Method<byte[], byte[]> GetAccountResourceMethod = new(
        MethodType.Unary, "protocol.Wallet", "GetAccountResource",
        ByteArrayMarshaller, ByteArrayMarshaller);

    private static readonly Method<byte[], byte[]> EstimateEnergyMethod = new(
        MethodType.Unary, "protocol.Wallet", "EstimateEnergy",
        ByteArrayMarshaller, ByteArrayMarshaller);

    // --- gRPC Method descriptors for WalletSolidity service ---

    private static readonly Method<byte[], byte[]> GetTransactionInfoByIdMethod = new(
        MethodType.Unary, "protocol.WalletSolidity", "GetTransactionInfoById",
        ByteArrayMarshaller, ByteArrayMarshaller);

    /// <summary>
    /// Creates a new TronGrpcProvider connecting to the specified endpoints.
    /// </summary>
    /// <param name="fullNodeEndpoint">gRPC endpoint for the full node (e.g., "grpc.trongrid.io:50051").</param>
    /// <param name="solidityEndpoint">Optional gRPC endpoint for the solidity node. If null, solidity
    /// calls will fall back to the full node's Wallet service.</param>
    public TronGrpcProvider(string fullNodeEndpoint, string? solidityEndpoint = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullNodeEndpoint);

        _fullNodeChannel = CreateChannel(fullNodeEndpoint);
        _fullNodeInvoker = _fullNodeChannel.CreateCallInvoker();

        if (!string.IsNullOrWhiteSpace(solidityEndpoint))
        {
            _solidityChannel = CreateChannel(solidityEndpoint);
            _solidityInvoker = _solidityChannel.CreateCallInvoker();
        }
    }

    /// <summary>
    /// Creates a new TronGrpcProvider from a network configuration.
    /// </summary>
    public TronGrpcProvider(TronNetworkConfig network)
        : this(network.GrpcFullNodeEndpoint, network.GrpcSolidityEndpoint) { }

    /// <summary>
    /// Internal constructor for testing, accepting pre-built call invokers.
    /// </summary>
    internal TronGrpcProvider(CallInvoker fullNodeInvoker, CallInvoker? solidityInvoker = null)
    {
        _fullNodeChannel = null!; // not used in test path
        _fullNodeInvoker = fullNodeInvoker;
        _solidityInvoker = solidityInvoker;
    }

    // --- ITronProvider implementation ---

    public async Task<AccountInfo> GetAccountAsync(string address, CancellationToken ct = default)
    {
        // Tron's GetAccount takes an Account message with just the address field set.
        // Account message: field 1 (bytes) = address
        var hexAddress = NormalizeToHex(address);
        var addressBytes = Convert.FromHexString(hexAddress);
        var request = EncodeField(1, addressBytes);

        var response = await CallFullNodeAsync(GetAccountMethod, request, ct);

        // Parse Account response: address(1), balance(5)
        return ParseAccountInfo(response, hexAddress);
    }

    public async Task<BlockInfo> GetNowBlockAsync(CancellationToken ct = default)
    {
        // EmptyMessage = empty bytes
        var response = await CallFullNodeAsync(GetNowBlockMethod, Array.Empty<byte>(), ct);
        return ParseBlockInfo(response);
    }

    public async Task<BlockInfo> GetBlockByNumAsync(long num, CancellationToken ct = default)
    {
        // NumberMessage: field 1 (int64) = num
        var request = EncodeVarintField(1, num);
        var response = await CallFullNodeAsync(GetBlockByNumMethod, request, ct);
        return ParseBlockInfo(response);
    }

    public async Task<Transaction> CreateTransactionAsync(Transaction transaction, CancellationToken ct = default)
    {
        // The full node doesn't have a direct CreateTransaction gRPC method that matches
        // the HTTP API's behavior. The HTTP endpoint is the canonical way to do this.
        // For gRPC, transactions are typically built client-side and then broadcast.
        throw new NotSupportedException(
            "CreateTransaction is not available via gRPC. " +
            "Build transactions locally using TransactionBuilder, then broadcast with BroadcastTransactionAsync.");
    }

    public async Task<BroadcastResult> BroadcastTransactionAsync(Transaction signedTx, CancellationToken ct = default)
    {
        var request = signedTx.ToByteArray();
        var response = await CallFullNodeAsync(BroadcastTransactionMethod, request, ct);

        // Return message: field 1 (bool) = result, field 2 (enum) = code, field 3 (bytes) = message
        return ParseBroadcastReturn(response);
    }

    public async Task<TransactionInfoDto> GetTransactionByIdAsync(string txId, CancellationToken ct = default)
    {
        // BytesMessage: field 1 (bytes) = value (the tx hash)
        var txHash = Convert.FromHexString(txId);
        var request = EncodeField(1, txHash);

        var response = await CallFullNodeAsync(GetTransactionByIdMethod, request, ct);

        // Response is a Transaction message
        var tx = Transaction.Parser.ParseFrom(response);
        var contractResult = "";
        if (tx.Ret.Count > 0)
            contractResult = tx.Ret[0].ContractRet.ToString();

        return new TransactionInfoDto(txId, 0, 0, contractResult, 0, 0, 0);
    }

    public async Task<TransactionInfoDto> GetTransactionInfoByIdAsync(string txId, CancellationToken ct = default)
    {
        // BytesMessage: field 1 (bytes) = value (the tx hash)
        var txHash = Convert.FromHexString(txId);
        var request = EncodeField(1, txHash);

        var invoker = _solidityInvoker ?? _fullNodeInvoker;
        var response = await CallAsync(invoker, GetTransactionInfoByIdMethod, request, ct);

        // Parse TransactionInfo response
        return ParseTransactionInfo(response, txId);
    }

    public async Task<Transaction> TriggerSmartContractAsync(
        string ownerAddress, string contractAddress,
        string functionSelector, byte[] parameter,
        long feeLimit, long callValue = 0,
        CancellationToken ct = default)
    {
        var triggerContract = BuildTriggerSmartContract(ownerAddress, contractAddress, functionSelector, parameter, callValue);
        var request = triggerContract.ToByteArray();

        var response = await CallFullNodeAsync(TriggerSmartContractMethod, request, ct);

        // TransactionExtention: field 1 (Transaction) = transaction, field 2 (Return) = result
        return ParseTransactionFromExtention(response, feeLimit);
    }

    public async Task<byte[]> TriggerConstantContractAsync(
        string ownerAddress, string contractAddress,
        string functionSelector, byte[] parameter,
        CancellationToken ct = default)
    {
        var triggerContract = BuildTriggerSmartContract(ownerAddress, contractAddress, functionSelector, parameter, 0);
        var request = triggerContract.ToByteArray();

        var response = await CallFullNodeAsync(TriggerConstantContractMethod, request, ct);

        // TransactionExtention: field 4 (repeated bytes) = constant_result
        return ParseConstantResult(response);
    }

    public async Task<AccountResourceInfo> GetAccountResourceAsync(string address, CancellationToken ct = default)
    {
        // GetAccountResource takes an Account message with address
        var hexAddress = NormalizeToHex(address);
        var addressBytes = Convert.FromHexString(hexAddress);
        var request = EncodeField(1, addressBytes);

        var response = await CallFullNodeAsync(GetAccountResourceMethod, request, ct);
        return ParseAccountResourceInfo(response);
    }

    public async Task<long> EstimateEnergyAsync(
        string ownerAddress, string contractAddress,
        string functionSelector, byte[] parameter,
        CancellationToken ct = default)
    {
        var triggerContract = BuildTriggerSmartContract(ownerAddress, contractAddress, functionSelector, parameter, 0);
        var request = triggerContract.ToByteArray();

        var response = await CallFullNodeAsync(EstimateEnergyMethod, request, ct);

        // EstimateEnergyMessage: field 1 (int64) = energy_required
        return ParseVarintField(response, 1);
    }

    // --- Channel & call helpers ---

    private static GrpcChannel CreateChannel(string endpoint)
    {
        // Ensure the endpoint has a scheme; default to http for gRPC
        var uri = endpoint.Contains("://")
            ? endpoint
            : $"http://{endpoint}";

        return GrpcChannel.ForAddress(uri);
    }

    private Task<byte[]> CallFullNodeAsync(Method<byte[], byte[]> method, byte[] request, CancellationToken ct)
        => CallAsync(_fullNodeInvoker, method, request, ct);

    private static async Task<byte[]> CallAsync(
        CallInvoker invoker, Method<byte[], byte[]> method,
        byte[] request, CancellationToken ct)
    {
        var callOptions = new CallOptions(cancellationToken: ct);
        var response = await invoker.AsyncUnaryCall(method, null, callOptions, request);
        return response;
    }

    // --- Protobuf manual encoding helpers ---

    /// <summary>Encode a length-delimited (bytes/string) field.</summary>
    internal static byte[] EncodeField(int fieldNumber, byte[] value)
    {
        // Wire type 2 = length-delimited
        var tag = (fieldNumber << 3) | 2;
        using var ms = new MemoryStream();
        WriteVarint(ms, (ulong)tag);
        WriteVarint(ms, (ulong)value.Length);
        ms.Write(value, 0, value.Length);
        return ms.ToArray();
    }

    /// <summary>Encode a varint (int64) field.</summary>
    internal static byte[] EncodeVarintField(int fieldNumber, long value)
    {
        // Wire type 0 = varint
        var tag = (fieldNumber << 3) | 0;
        using var ms = new MemoryStream();
        WriteVarint(ms, (ulong)tag);
        WriteVarint(ms, (ulong)value);
        return ms.ToArray();
    }

    private static void WriteVarint(Stream stream, ulong value)
    {
        while (value > 0x7F)
        {
            stream.WriteByte((byte)(value | 0x80));
            value >>= 7;
        }
        stream.WriteByte((byte)value);
    }

    // --- Protobuf manual decoding helpers ---

    /// <summary>Read a varint field value by field number from raw protobuf bytes.</summary>
    internal static long ParseVarintField(byte[] data, int targetFieldNumber)
    {
        int offset = 0;
        while (offset < data.Length)
        {
            var (tag, newOffset) = ReadVarint(data, offset);
            offset = newOffset;
            int fieldNumber = (int)(tag >> 3);
            int wireType = (int)(tag & 0x07);

            switch (wireType)
            {
                case 0: // varint
                    var (value, nextOffset) = ReadVarint(data, offset);
                    offset = nextOffset;
                    if (fieldNumber == targetFieldNumber)
                        return (long)value;
                    break;
                case 1: // 64-bit fixed
                    offset += 8;
                    break;
                case 2: // length-delimited
                    var (len, lenOffset) = ReadVarint(data, offset);
                    offset = lenOffset + (int)len;
                    break;
                case 5: // 32-bit fixed
                    offset += 4;
                    break;
                default:
                    return 0; // unknown wire type, bail
            }
        }
        return 0;
    }

    /// <summary>Read a length-delimited field by field number from raw protobuf bytes.</summary>
    internal static byte[] ParseBytesField(byte[] data, int targetFieldNumber)
    {
        int offset = 0;
        while (offset < data.Length)
        {
            var (tag, newOffset) = ReadVarint(data, offset);
            offset = newOffset;
            int fieldNumber = (int)(tag >> 3);
            int wireType = (int)(tag & 0x07);

            switch (wireType)
            {
                case 0: // varint
                    var (_, nextOffset) = ReadVarint(data, offset);
                    offset = nextOffset;
                    break;
                case 1: // 64-bit fixed
                    offset += 8;
                    break;
                case 2: // length-delimited
                    var (len, lenOffset) = ReadVarint(data, offset);
                    offset = lenOffset;
                    var bytes = new byte[(int)len];
                    Array.Copy(data, offset, bytes, 0, (int)len);
                    offset += (int)len;
                    if (fieldNumber == targetFieldNumber)
                        return bytes;
                    break;
                case 5: // 32-bit fixed
                    offset += 4;
                    break;
                default:
                    return Array.Empty<byte>(); // unknown wire type
            }
        }
        return Array.Empty<byte>();
    }

    /// <summary>Read all occurrences of a repeated length-delimited field.</summary>
    internal static List<byte[]> ParseRepeatedBytesField(byte[] data, int targetFieldNumber)
    {
        var results = new List<byte[]>();
        int offset = 0;
        while (offset < data.Length)
        {
            var (tag, newOffset) = ReadVarint(data, offset);
            offset = newOffset;
            int fieldNumber = (int)(tag >> 3);
            int wireType = (int)(tag & 0x07);

            switch (wireType)
            {
                case 0: // varint
                    var (_, nextOffset) = ReadVarint(data, offset);
                    offset = nextOffset;
                    break;
                case 1: // 64-bit fixed
                    offset += 8;
                    break;
                case 2: // length-delimited
                    var (len, lenOffset) = ReadVarint(data, offset);
                    offset = lenOffset;
                    var bytes = new byte[(int)len];
                    Array.Copy(data, offset, bytes, 0, (int)len);
                    offset += (int)len;
                    if (fieldNumber == targetFieldNumber)
                        results.Add(bytes);
                    break;
                case 5: // 32-bit fixed
                    offset += 4;
                    break;
                default:
                    return results;
            }
        }
        return results;
    }

    private static (ulong value, int newOffset) ReadVarint(byte[] data, int offset)
    {
        ulong result = 0;
        int shift = 0;
        while (offset < data.Length)
        {
            byte b = data[offset++];
            result |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0)
                break;
            shift += 7;
        }
        return (result, offset);
    }

    // --- Response parsers ---

    private static AccountInfo ParseAccountInfo(byte[] data, string fallbackAddress)
    {
        if (data.Length == 0)
            return new AccountInfo(fallbackAddress, 0, 0, 0, 0);

        // Account message fields (from tron protocol):
        // 1: bytes address, 5: int64 balance, 8: int64 net_usage
        // 26: AccountResource (sub-message, field 1 = energy_usage)
        // 11: int64 create_time
        var address = ParseBytesField(data, 1);
        var addrHex = address.Length > 0 ? Convert.ToHexString(address).ToLowerInvariant() : fallbackAddress;
        var balance = ParseVarintField(data, 5);
        var netUsage = ParseVarintField(data, 8);
        var createTime = ParseVarintField(data, 11);

        // AccountResource sub-message at field 26
        var accountResourceBytes = ParseBytesField(data, 26);
        long energyUsage = 0;
        if (accountResourceBytes.Length > 0)
            energyUsage = ParseVarintField(accountResourceBytes, 1);

        return new AccountInfo(addrHex, balance, netUsage, energyUsage, createTime);
    }

    private static BlockInfo ParseBlockInfo(byte[] data)
    {
        if (data.Length == 0)
            return new BlockInfo(0, "", 0, 0, Array.Empty<byte>());

        // Block message: field 1 (repeated Transaction), field 2 (BlockHeader)
        var transactions = ParseRepeatedBytesField(data, 1);
        var blockHeaderBytes = ParseBytesField(data, 2);

        long blockNumber = 0;
        long timestamp = 0;
        byte[] rawData = Array.Empty<byte>();
        string blockId = "";

        if (blockHeaderBytes.Length > 0)
        {
            // BlockHeader: field 1 (raw_data), field 2 (witness_signature)
            rawData = ParseBytesField(blockHeaderBytes, 1);
            if (rawData.Length > 0)
            {
                // BlockHeader.raw: field 7 (int64 number), field 1 (int64 timestamp)
                blockNumber = ParseVarintField(rawData, 7);
                timestamp = ParseVarintField(rawData, 1);
            }
        }

        // Block ID is typically the hash of the block header, not directly in the proto.
        // We derive a hex representation from the block number for now.
        blockId = blockNumber.ToString("x016");

        return new BlockInfo(blockNumber, blockId, timestamp, transactions.Count, rawData);
    }

    private static BroadcastResult ParseBroadcastReturn(byte[] data)
    {
        if (data.Length == 0)
            return new BroadcastResult(false, null, "Empty response");

        // Return message: field 1 (bool) = result, field 2 (enum/int) = code, field 3 (bytes) = message
        var result = ParseVarintField(data, 1) != 0;
        var messageBytes = ParseBytesField(data, 3);
        var message = messageBytes.Length > 0
            ? System.Text.Encoding.UTF8.GetString(messageBytes)
            : null;

        return new BroadcastResult(result, null, message);
    }

    private static TransactionInfoDto ParseTransactionInfo(byte[] data, string txId)
    {
        if (data.Length == 0)
            return new TransactionInfoDto(txId, 0, 0, "", 0, 0, 0);

        // TransactionInfo: field 1 (bytes id), field 2 (int64 fee), field 3 (int64 blockNumber),
        //   field 4 (int64 blockTimeStamp), field 8 (ResourceReceipt), field 9 (repeated bytes contractResult)
        var idBytes = ParseBytesField(data, 1);
        var id = idBytes.Length > 0 ? Convert.ToHexString(idBytes).ToLowerInvariant() : txId;
        var fee = ParseVarintField(data, 2);
        var blockNum = ParseVarintField(data, 3);
        var blockTs = ParseVarintField(data, 4);

        // ResourceReceipt at field 8
        var receiptBytes = ParseBytesField(data, 8);
        long energyUsage = 0;
        long netUsage = 0;
        if (receiptBytes.Length > 0)
        {
            energyUsage = ParseVarintField(receiptBytes, 1);
            netUsage = ParseVarintField(receiptBytes, 4);
        }

        var contractResults = ParseRepeatedBytesField(data, 9);
        var contractResult = contractResults.Count > 0
            ? Convert.ToHexString(contractResults[0]).ToLowerInvariant()
            : "";

        return new TransactionInfoDto(id, blockNum, blockTs, contractResult, fee, energyUsage, netUsage);
    }

    private static Transaction ParseTransactionFromExtention(byte[] data, long feeLimit)
    {
        if (data.Length == 0)
            throw new InvalidOperationException("Empty TransactionExtention response");

        // TransactionExtention: field 1 (Transaction), field 2 (Return result)
        var txBytes = ParseBytesField(data, 1);
        if (txBytes.Length == 0)
        {
            // Check result message (field 2 sub-message, field 3 bytes = message)
            var resultBytes = ParseBytesField(data, 2);
            var errorMsg = "Unknown error from TriggerSmartContract";
            if (resultBytes.Length > 0)
            {
                var msgBytes = ParseBytesField(resultBytes, 3);
                if (msgBytes.Length > 0)
                    errorMsg = System.Text.Encoding.UTF8.GetString(msgBytes);
            }
            throw new InvalidOperationException(errorMsg);
        }

        var tx = Transaction.Parser.ParseFrom(txBytes);

        // Apply fee_limit if it wasn't set in the response
        if (tx.RawData != null && tx.RawData.FeeLimit == 0 && feeLimit > 0)
            tx.RawData.FeeLimit = feeLimit;

        return tx;
    }

    private static byte[] ParseConstantResult(byte[] data)
    {
        if (data.Length == 0)
            throw new InvalidOperationException("Empty response from TriggerConstantContract");

        // TransactionExtention: field 4 (repeated bytes) = constant_result
        var results = ParseRepeatedBytesField(data, 4);
        if (results.Count > 0)
            return results[0];

        // Check for error in result (field 2)
        var resultBytes = ParseBytesField(data, 2);
        var errorMsg = "No result from TriggerConstantContract";
        if (resultBytes.Length > 0)
        {
            var msgBytes = ParseBytesField(resultBytes, 3);
            if (msgBytes.Length > 0)
                errorMsg = System.Text.Encoding.UTF8.GetString(msgBytes);
        }
        throw new InvalidOperationException(errorMsg);
    }

    private static AccountResourceInfo ParseAccountResourceInfo(byte[] data)
    {
        if (data.Length == 0)
            return new AccountResourceInfo(0, 0, 0, 0, 0, 0);

        // AccountResourceMessage (from Tron protocol):
        // field 1: int64 freeNetUsed, field 2: int64 freeNetLimit
        // field 3: int64 NetUsed, field 4: int64 NetLimit
        // field 5: int64 EnergyUsed, field 6: int64 EnergyLimit
        var freeNetUsed = ParseVarintField(data, 1);
        var freeNetLimit = ParseVarintField(data, 2);
        var netUsed = ParseVarintField(data, 3);
        var netLimit = ParseVarintField(data, 4);
        var energyUsed = ParseVarintField(data, 5);
        var energyLimit = ParseVarintField(data, 6);

        return new AccountResourceInfo(
            FreeBandwidthLimit: freeNetLimit,
            FreeBandwidthUsed: freeNetUsed,
            EnergyLimit: energyLimit,
            EnergyUsed: energyUsed,
            TotalBandwidthLimit: netLimit,
            TotalBandwidthUsed: netUsed);
    }

    // --- Shared helpers ---

    private static TriggerSmartContract BuildTriggerSmartContract(
        string ownerAddress, string contractAddress,
        string functionSelector, byte[] parameter,
        long callValue)
    {
        var hexOwner = NormalizeToHex(ownerAddress);
        var hexContract = NormalizeToHex(contractAddress);

        // Encode function selector hash (first 4 bytes of keccak256) + parameter
        var selectorBytes = Keccak256.Hash(
            System.Text.Encoding.UTF8.GetBytes(functionSelector));
        var data = new byte[4 + parameter.Length];
        Array.Copy(selectorBytes, 0, data, 0, 4);
        Array.Copy(parameter, 0, data, 4, parameter.Length);

        return new TriggerSmartContract
        {
            OwnerAddress = ByteString.CopyFrom(Convert.FromHexString(hexOwner)),
            ContractAddress = ByteString.CopyFrom(Convert.FromHexString(hexContract)),
            CallValue = callValue,
            Data = ByteString.CopyFrom(data)
        };
    }

    private static string NormalizeToHex(string address)
    {
        if (address.StartsWith('T'))
            return TronAddress.ToHex(address);
        return address;
    }

    // --- IDisposable ---

    public void Dispose()
    {
        _solidityChannel?.Dispose();
        _fullNodeChannel?.Dispose();
        GC.SuppressFinalize(this);
    }
}
