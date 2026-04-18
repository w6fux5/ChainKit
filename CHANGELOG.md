# Changelog

All notable changes to this project are documented in this file.
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/)
and the project adheres to [Semantic Versioning](https://semver.org/).

## [0.3.0] - 2026-04-18

### Breaking Changes

- `Trc20Contract` uses per-call signer (Option B); the `owner` constructor
  parameter was removed. Call sites now pass the signer on each operation.
  (ADR 016)
- `TronClient.Dispose` no longer disposes an externally owned `ITronProvider`.
  Callers keep ownership of providers they injected. (ADR 017)

### Added

- Wait-for-on-chain helpers:
  - `TronClient.WaitForOnChainAsync` — polls Full Node `/wallet/gettransactioninfobyid`
  - `EvmClient.WaitForOnChainAsync` and `EvmClient.WaitForReceiptAsync` —
    poll `eth_getTransactionReceipt`
  - All three honour configurable `timeout` / `pollInterval` / `maxConsecutiveFailures`
- `ITronProvider.GetTransactionInfoByIdAsync(txId, useSolidity)` — new
  `useSolidity` parameter (defaults to `true` to preserve watcher behaviour)
- Node health watchers (event-based poll, reports raw metrics):
  - `TronNodeHealthWatcher` — reachable / latency / block number / block age
  - `EvmNodeHealthWatcher` — same, plus chainId match (ADR 018)
- `IEvmProvider.GetChainIdAsync` (supports the EVM health watcher)
- `InvalidArgument` added to `TronErrorCode` and `EvmErrorCode`

### Fixed

- `PollingBlockStream` (Tron) now backfills every block in
  `(lastBlockNumber, currentHead]` instead of jumping directly to head.
  Previously, when Tron's ~3 s block time aligned with the 3 s poll interval,
  any skew made head advance by more than one between polls and intermediate
  blocks were silently dropped — watchers never saw those transactions.
  Adds a `maxBlocksPerPoll` cap (default 100) so long disconnect recovery
  does not produce an unbounded serial fetch. On mid-catch-up failure the
  failed block is retried next poll with no duplicate yield.
- `TronGrpcProvider` message size capped at 32 MB (previously unbounded)
- `TronAccount` / `EvmAccount` zero the BIP-39 seed after HD derivation
- `System.Security.Cryptography.Xml` upgraded to 10.0.6 (CVE patches)

### Docs

- ADR 016 — TRC20 per-call signer
- ADR 017 — Unified dispose for externally owned provider
- ADR 018 — Node health watcher design
- EVM + Tron SDK usage guides extended with wait-for-on-chain examples

[0.3.0]: https://github.com/w6fux5/ChainKit/releases/tag/v0.3.0
