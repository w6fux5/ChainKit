using System.Text.Json;
using ChainKit.Core.Crypto;
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

    private static readonly Method<byte[], byte[]> GetDelegatedResourceAccountIndexV2Method = new(
        MethodType.Unary, "protocol.Wallet", "GetDelegatedResourceAccountIndexV2",
        ByteArrayMarshaller, ByteArrayMarshaller);

    private static readonly Method<byte[], byte[]> GetDelegatedResourceV2Method = new(
        MethodType.Unary, "protocol.Wallet", "GetDelegatedResourceV2",
        ByteArrayMarshaller, ByteArrayMarshaller);

    private static readonly Method<byte[], byte[]> GetContractMethod = new(
        MethodType.Unary, "protocol.Wallet", "GetContract",
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

        // Parse contract details from the transaction
        string contractType = "";
        string ownerAddress = "";
        string toAddress = "";
        long amountSun = 0;
        string? contractAddress = null;
        string? contractData = null;

        if (tx.RawData?.Contract.Count > 0)
        {
            var contract = tx.RawData.Contract[0];
            contractType = contract.Type.ToString();

            // The parameter is a google.protobuf.Any wrapping the specific contract message.
            // We parse the inner bytes based on the contract type.
            if (contract.Parameter?.Value != null)
            {
                var paramBytes = contract.Parameter.Value.ToByteArray();
                switch (contract.Type)
                {
                    case Transaction.Types.Contract.Types.ContractType.TransferContract:
                        // TransferContract: field 1 (bytes owner_address), field 2 (bytes to_address), field 3 (int64 amount)
                        var ownerBytes = ParseBytesField(paramBytes, 1);
                        var toBytes = ParseBytesField(paramBytes, 2);
                        ownerAddress = ownerBytes.Length > 0 ? Convert.ToHexString(ownerBytes).ToLowerInvariant() : "";
                        toAddress = toBytes.Length > 0 ? Convert.ToHexString(toBytes).ToLowerInvariant() : "";
                        amountSun = ParseVarintField(paramBytes, 3);
                        break;

                    case Transaction.Types.Contract.Types.ContractType.TriggerSmartContract:
                        // TriggerSmartContract: field 1 (bytes owner_address), field 2 (bytes contract_address),
                        //   field 3 (int64 call_value), field 4 (bytes data)
                        var tscOwner = ParseBytesField(paramBytes, 1);
                        var tscContract = ParseBytesField(paramBytes, 2);
                        var tscData = ParseBytesField(paramBytes, 4);
                        ownerAddress = tscOwner.Length > 0 ? Convert.ToHexString(tscOwner).ToLowerInvariant() : "";
                        contractAddress = tscContract.Length > 0 ? Convert.ToHexString(tscContract).ToLowerInvariant() : null;
                        contractData = tscData.Length > 0 ? Convert.ToHexString(tscData).ToLowerInvariant() : null;
                        break;

                    case Transaction.Types.Contract.Types.ContractType.FreezeBalanceV2Contract:
                    case Transaction.Types.Contract.Types.ContractType.UnfreezeBalanceV2Contract:
                        var fbOwner = ParseBytesField(paramBytes, 1);
                        ownerAddress = fbOwner.Length > 0 ? Convert.ToHexString(fbOwner).ToLowerInvariant() : "";
                        amountSun = ParseVarintField(paramBytes, 2);
                        break;

                    case Transaction.Types.Contract.Types.ContractType.DelegateResourceContract:
                    case Transaction.Types.Contract.Types.ContractType.UnDelegateResourceContract:
                        var drOwner = ParseBytesField(paramBytes, 1);
                        var drReceiver = ParseBytesField(paramBytes, 4);
                        ownerAddress = drOwner.Length > 0 ? Convert.ToHexString(drOwner).ToLowerInvariant() : "";
                        toAddress = drReceiver.Length > 0 ? Convert.ToHexString(drReceiver).ToLowerInvariant() : "";
                        amountSun = ParseVarintField(paramBytes, 3);
                        break;

                    case Transaction.Types.Contract.Types.ContractType.CreateSmartContract:
                        var cscOwner = ParseBytesField(paramBytes, 1);
                        ownerAddress = cscOwner.Length > 0 ? Convert.ToHexString(cscOwner).ToLowerInvariant() : "";
                        break;

                    default:
                        // For unrecognized types, try to extract owner_address (field 1) as best-effort
                        var defaultOwner = ParseBytesField(paramBytes, 1);
                        ownerAddress = defaultOwner.Length > 0 ? Convert.ToHexString(defaultOwner).ToLowerInvariant() : "";
                        break;
                }
            }
        }

        // gRPC Transaction protobuf 沒有 txID 欄位（txID 是 raw_data 的 SHA256 hash）
        // 有交易資料時用呼叫端的 txId（因為就是用它查的），空回應時回傳空字串
        var resolvedTxId = tx.RawData?.Contract.Count > 0 ? txId : "";
        return new TransactionInfoDto(resolvedTxId, 0, 0, contractResult, 0, 0, 0,
            contractType, ownerAddress, toAddress, amountSun, contractAddress, contractData);
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

    public Task<IReadOnlyList<TransactionInfoDto>> GetAccountTransactionsAsync(
        string address, int limit = 10, CancellationToken ct = default)
    {
        // TronGrid /v1/accounts/{address}/transactions is an HTTP-only API.
        // gRPC full nodes do not provide an equivalent endpoint.
        return Task.FromResult<IReadOnlyList<TransactionInfoDto>>(Array.Empty<TransactionInfoDto>());
    }

    public async Task<DelegatedResourceIndex> GetDelegatedResourceAccountIndexAsync(
        string address, CancellationToken ct = default)
    {
        var hexAddress = NormalizeToHex(address);
        var addressBytes = Convert.FromHexString(hexAddress);
        // BytesMessage: field 1 (bytes) = value
        var request = EncodeField(1, addressBytes);

        var response = await CallFullNodeAsync(GetDelegatedResourceAccountIndexV2Method, request, ct);
        return ParseDelegatedResourceIndex(response);
    }

    public async Task<IReadOnlyList<DelegatedResourceInfo>> GetDelegatedResourceAsync(
        string fromAddress, string toAddress, CancellationToken ct = default)
    {
        var hexFrom = NormalizeToHex(fromAddress);
        var hexTo = NormalizeToHex(toAddress);
        // DelegatedResourceMessage: field 1 (bytes fromAddress), field 2 (bytes toAddress)
        using var ms = new MemoryStream();
        var fromBytes = EncodeField(1, Convert.FromHexString(hexFrom));
        var toBytes = EncodeField(2, Convert.FromHexString(hexTo));
        ms.Write(fromBytes, 0, fromBytes.Length);
        ms.Write(toBytes, 0, toBytes.Length);
        var request = ms.ToArray();

        var response = await CallFullNodeAsync(GetDelegatedResourceV2Method, request, ct);
        return ParseDelegatedResources(response);
    }

    public async Task<SmartContractInfo> GetContractAsync(string contractAddress, CancellationToken ct = default)
    {
        var hexAddress = NormalizeToHex(contractAddress);
        var addressBytes = Convert.FromHexString(hexAddress);
        // BytesMessage: field 1 (bytes) = contract address
        var request = EncodeField(1, addressBytes);

        var response = await CallFullNodeAsync(GetContractMethod, request, ct);
        return ParseSmartContractInfo(response);
    }

    // --- Channel & call helpers ---

    // Cap per-message size to bound memory pressure from large or malicious responses.
    // Tron full-blocks can exceed the gRPC default (4 MB); 32 MB accommodates realistic
    // blocks while preventing unbounded allocations.
    private const int MaxMessageSizeBytes = 32 * 1024 * 1024;

    private static GrpcChannel CreateChannel(string endpoint)
    {
        // Ensure the endpoint has a scheme; default to https for gRPC
        if (!endpoint.StartsWith("http://") && !endpoint.StartsWith("https://"))
            endpoint = "https://" + endpoint;

        return GrpcChannel.ForAddress(endpoint, new GrpcChannelOptions
        {
            MaxReceiveMessageSize = MaxMessageSizeBytes,
            MaxSendMessageSize = MaxMessageSizeBytes,
        });
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

        // Parse frozenV2 (field 34, repeated sub-message).
        // Each FreezeV2 message: field 1 (enum type: 0=BANDWIDTH, 1=ENERGY), field 2 (int64 amount).
        long frozenBandwidth = 0;
        long frozenEnergy = 0;
        var frozenV2Entries = ParseRepeatedBytesField(data, 34);
        foreach (var entry in frozenV2Entries)
        {
            var amount = ParseVarintField(entry, 2);
            if (amount == 0) continue;
            var type = ParseVarintField(entry, 1); // 0 = BANDWIDTH, 1 = ENERGY
            if (type == 1)
                frozenEnergy += amount;
            else
                frozenBandwidth += amount;
        }

        return new AccountInfo(addrHex, balance, netUsage, energyUsage, createTime,
            frozenBandwidth, frozenEnergy);
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
            return new TransactionInfoDto("", 0, 0, "", 0, 0, 0);

        // TransactionInfo: field 1 (bytes id), field 2 (int64 fee), field 3 (int64 blockNumber),
        //   field 4 (int64 blockTimeStamp), field 8 (ResourceReceipt), field 9 (repeated bytes contractResult)
        var idBytes = ParseBytesField(data, 1);
        var id = idBytes.Length > 0 ? Convert.ToHexString(idBytes).ToLowerInvariant() : "";
        var fee = ParseVarintField(data, 2);
        var blockNum = ParseVarintField(data, 3);
        var blockTs = ParseVarintField(data, 4);

        // ResourceReceipt at field 8
        // Fields: 1=energy_usage, 2=energy_fee, 3=origin_energy_usage,
        //   4=energy_usage_total (net_usage), 5=net_usage, 6=net_fee
        var receiptBytes = ParseBytesField(data, 8);
        long energyUsage = 0;
        long netUsage = 0;
        long energyFee = 0;
        long netFee = 0;
        var receiptResult = "";
        if (receiptBytes.Length > 0)
        {
            energyUsage = ParseVarintField(receiptBytes, 1);
            netUsage = ParseVarintField(receiptBytes, 4);
            energyFee = ParseVarintField(receiptBytes, 2);
            netFee = ParseVarintField(receiptBytes, 6);
            // Field 7 = contractResult enum (varint)
            var resultCode = (int)ParseVarintField(receiptBytes, 7);
            receiptResult = resultCode switch
            {
                0 => "DEFAULT",
                1 => "SUCCESS",
                2 => "REVERT",
                10 => "OUT_OF_ENERGY",
                11 => "OUT_OF_TIME",
                14 => "TRANSFER_FAILED",
                _ => resultCode > 1 ? "FAILED" : ""
            };
        }

        var contractResults = ParseRepeatedBytesField(data, 9);
        var contractResult = contractResults.Count > 0
            ? Convert.ToHexString(contractResults[0]).ToLowerInvariant()
            : "";

        return new TransactionInfoDto(id, blockNum, blockTs, contractResult, fee, energyUsage, netUsage,
            EnergyFee: energyFee, NetFee: netFee, ReceiptResult: receiptResult);
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
        // field 7: int64 TotalNetLimit, field 8: int64 TotalNetWeight
        // field 9: int64 TotalEnergyLimit (field 11 in some versions), field 10: int64 TotalEnergyWeight
        var freeNetUsed = ParseVarintField(data, 1);
        var freeNetLimit = ParseVarintField(data, 2);
        var netUsed = ParseVarintField(data, 3);
        var netLimit = ParseVarintField(data, 4);
        var energyUsed = ParseVarintField(data, 5);
        var energyLimit = ParseVarintField(data, 6);
        var totalNetLimit = ParseVarintField(data, 7);
        var totalNetWeight = ParseVarintField(data, 8);
        var totalEnergyLimit = ParseVarintField(data, 13);
        var totalEnergyWeight = ParseVarintField(data, 14);

        return new AccountResourceInfo(
            FreeBandwidthLimit: freeNetLimit,
            FreeBandwidthUsed: freeNetUsed,
            EnergyLimit: energyLimit,
            EnergyUsed: energyUsed,
            TotalBandwidthLimit: netLimit,
            TotalBandwidthUsed: netUsed,
            NetworkTotalBandwidthLimit: totalNetLimit,
            NetworkTotalBandwidthWeight: totalNetWeight,
            NetworkTotalEnergyLimit: totalEnergyLimit,
            NetworkTotalEnergyWeight: totalEnergyWeight);
    }

    private static DelegatedResourceIndex ParseDelegatedResourceIndex(byte[] data)
    {
        if (data.Length == 0)
            return new DelegatedResourceIndex(Array.Empty<string>(), Array.Empty<string>());

        // DelegatedResourceAccountIndex: field 1 (bytes account),
        //   field 2 (repeated bytes toAccounts), field 3 (repeated bytes fromAccounts)
        var toEntries = ParseRepeatedBytesField(data, 2);
        var fromEntries = ParseRepeatedBytesField(data, 3);

        var toAddresses = toEntries
            .Select(b => Convert.ToHexString(b).ToLowerInvariant())
            .ToList();
        var fromAddresses = fromEntries
            .Select(b => Convert.ToHexString(b).ToLowerInvariant())
            .ToList();

        return new DelegatedResourceIndex(toAddresses, fromAddresses);
    }

    private static IReadOnlyList<DelegatedResourceInfo> ParseDelegatedResources(byte[] data)
    {
        if (data.Length == 0)
            return Array.Empty<DelegatedResourceInfo>();

        // DelegatedResourceList: field 1 (repeated DelegatedResource)
        // DelegatedResource: field 1 (bytes from), field 2 (bytes to),
        //   field 3 (int64 frozen_balance_for_bandwidth), field 4 (int64 frozen_balance_for_energy)
        var entries = ParseRepeatedBytesField(data, 1);
        var results = new List<DelegatedResourceInfo>();

        foreach (var entry in entries)
        {
            var fromBytes = ParseBytesField(entry, 1);
            var toBytes = ParseBytesField(entry, 2);
            var bw = ParseVarintField(entry, 3);
            var energy = ParseVarintField(entry, 4);

            var from = fromBytes.Length > 0 ? Convert.ToHexString(fromBytes).ToLowerInvariant() : "";
            var to = toBytes.Length > 0 ? Convert.ToHexString(toBytes).ToLowerInvariant() : "";
            results.Add(new DelegatedResourceInfo(from, to, bw, energy));
        }

        return results;
    }

    private static SmartContractInfo ParseSmartContractInfo(byte[] data)
    {
        if (data.Length == 0)
            return new SmartContractInfo("", "", null);

        // SmartContract: field 1 (bytes origin_address), field 2 (bytes contract_address), field 3 (abi)
        var originBytes = ParseBytesField(data, 1);
        var contractBytes = ParseBytesField(data, 2);

        var origin = originBytes.Length > 0 ? Convert.ToHexString(originBytes).ToLowerInvariant() : "";
        var contract = contractBytes.Length > 0 ? Convert.ToHexString(contractBytes).ToLowerInvariant() : "";

        return new SmartContractInfo(origin, contract, null);
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
