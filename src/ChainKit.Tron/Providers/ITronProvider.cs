using ChainKit.Tron.Models;
using ChainKit.Tron.Protocol.Protobuf;

namespace ChainKit.Tron.Providers;

public interface ITronProvider
{
    Task<AccountInfo> GetAccountAsync(string address, CancellationToken ct = default);
    Task<BlockInfo> GetNowBlockAsync(CancellationToken ct = default);
    Task<BlockInfo> GetBlockByNumAsync(long num, CancellationToken ct = default);
    Task<Transaction> CreateTransactionAsync(Transaction transaction, CancellationToken ct = default);
    Task<BroadcastResult> BroadcastTransactionAsync(Transaction signedTx, CancellationToken ct = default);
    Task<TransactionInfoDto> GetTransactionByIdAsync(string txId, CancellationToken ct = default);
    Task<TransactionInfoDto> GetTransactionInfoByIdAsync(string txId, CancellationToken ct = default);
    Task<Transaction> TriggerSmartContractAsync(
        string ownerAddress, string contractAddress,
        string functionSelector, byte[] parameter,
        long feeLimit, long callValue = 0,
        CancellationToken ct = default);
    Task<byte[]> TriggerConstantContractAsync(
        string ownerAddress, string contractAddress,
        string functionSelector, byte[] parameter,
        CancellationToken ct = default);
    Task<AccountResourceInfo> GetAccountResourceAsync(string address, CancellationToken ct = default);
    Task<long> EstimateEnergyAsync(
        string ownerAddress, string contractAddress,
        string functionSelector, byte[] parameter,
        CancellationToken ct = default);

    // Account transactions (TronGrid v1 API)
    Task<IReadOnlyList<TransactionInfoDto>> GetAccountTransactionsAsync(
        string address, int limit = 10, CancellationToken ct = default);

    // Smart contract queries
    Task<SmartContractInfo> GetContractAsync(string contractAddress, CancellationToken ct = default);

    // Delegation resource queries (Stake 2.0)
    Task<DelegatedResourceIndex> GetDelegatedResourceAccountIndexAsync(
        string address, CancellationToken ct = default);
    Task<IReadOnlyList<DelegatedResourceInfo>> GetDelegatedResourceAsync(
        string fromAddress, string toAddress, CancellationToken ct = default);
}
