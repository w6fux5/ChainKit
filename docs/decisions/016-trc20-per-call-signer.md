# ADR 016: TRC20 Contract 改用 per-call signer（移除建構子 owner）

## 狀態

已實作（2026-04-17）

## 背景

原本 `Trc20Contract` 建構子接受 `TronAccount ownerAccount` 參數，所有寫入操作（Transfer/Approve/Mint/Burn/BurnFrom）固定使用該 account 簽章。而 `Erc20Contract` 建構子不綁定 account，每次寫入呼叫時顯式傳入 `EvmAccount from`。

這造成兩個問題：

1. **TRC20 / ERC20 API 不對稱**：同樣是代幣合約操作，兩邊簽章慣例不同
2. **「owner」命名誤導**：SDK 裡的 `ownerAccount` 實際上只是「預設簽章者」，但容易被誤讀為 Solidity 的 `Ownable.owner()`（鏈上合約管理者角色）。Tron 協定的 `owner_address` 欄位（protobuf）也只是「交易發起人」的意思，沒有特權語意

## 決策

**Option B（per-call signer）**：移除 `Trc20Contract` 建構子的 `ownerAccount` 參數，所有寫入方法改為第一個參數接受 `TronAccount signer`。`Erc20Contract` 維持原樣（已經是此模式）。

### API 變更（Breaking）

```
Trc20Contract(provider, contract, ownerAccount) → Trc20Contract(provider, contract)
TransferAsync(to, amount) → TransferAsync(signer, to, amount)
ApproveAsync(spender, amount) → ApproveAsync(signer, spender, amount)
MintAsync(to, amount) → MintAsync(signer, to, amount)
BurnAsync(amount) → BurnAsync(signer, amount)
BurnFromAsync(from, amount) → BurnFromAsync(signer, from, amount)
TronClient.GetTrc20Contract(addr, owner) → GetTrc20Contract(addr)
```

唯讀方法不變（NameAsync/SymbolAsync/DecimalsAsync/BalanceOfAsync/AllowanceAsync）。

## 原因

### 使用情境驅動

實際使用場景為「熱錢包 + N 個子錢包」後端系統：
- 同一個 USDT 合約，不同子錢包輪流簽 tx
- owner-in-constructor 模式下，每個子錢包都需要 `new Trc20Contract(...)`，管理 N 個實例或 Dictionary pool
- per-call signer 下，全系統共用一個 `Trc20Contract` instance，每次呼叫顯式帶入 signer

### call site 可讀性

```csharp
// per-call：一眼看懂誰簽、簽什麼
await usdt.TransferAsync(sub047, hotAddr, 100);

// owner-in-constructor：要回頭查 new Trc20Contract 那行
await usdt.TransferAsync(hotAddr, 100);
```

### 業界慣例

ethers.js（`contract.connect(signer)`）、viem（`writeContract({ account })`）、Nethereum、web3.py 都不在 Contract 建構子綁定 signer。

## 放棄的方案

- **Option A（owner in constructor）**：維持現狀。單錢包場景方便，但多錢包不自然。
- **Option C（hybrid — 兩種都支援）**：建構子接受 optional signer + method 也接受 override。API 表面變兩倍（10 個 method overload），增加學習/維護成本。未來若有需求可 additive 升級為 C，不需要先做。
- **Option D（ADR 記錄不對稱）**：保留不對稱，寫 ADR 說明原因。但原因偏弱（主要是避免 breaking change），且目前使用者只有一人，breaking 成本低。

## 後果

- `Trc20Contract` 不再持有 identity，可安全共用、thread-safe（無 per-signer state）
- 所有 caller 需遷移：Sandbox、E2E tests、TronClientTests 已一併更新
- 與 `Erc20Contract` 對稱
