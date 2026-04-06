# ADR 013: 新增 TRX ↔ Energy/Bandwidth 雙向兌換率 API

## 背景

Staking 模組用 TRX 作為單位（`StakeTrxAsync(account, 100m, ResourceType.Energy)`），
但 consumer 無法知道質押多少 TRX 能換多少 Energy/Bandwidth，也無法反向估算。

Tron 的兌換率是動態的，取決於全網質押總量：
```
Energy = (myStakedTrx / TotalEnergyWeight) × TotalEnergyLimit
Bandwidth = (myStakedTrx / TotalNetWeight) × TotalNetLimit
```

## 決策

### 不需要新增 Provider API

`GetAccountResourceAsync` 的回應已包含全網數據（`TotalEnergyWeight`、`TotalEnergyLimit`、
`TotalNetWeight`、`TotalNetLimit`），只需擴充 `AccountResourceInfo` 解析這 4 個欄位。

### 新增高階 API

```csharp
var rate = await client.GetResourceExchangeRateAsync(ResourceType.Energy);
rate.ResourcePerTrx    // 1 TRX 能換多少 Energy
rate.TrxPerResource    // 1 Energy 需要多少 TRX
rate.EstimateResource(100m)  // 100 TRX → ? Energy
rate.EstimateTrx(50000)      // 50000 Energy → ? TRX
```

### 單位注意事項

`TotalEnergyWeight` / `TotalNetWeight` 的單位是 **TRX**（非 Sun）。
公式中不需要乘以 SunPerTrx：
```
resourcePerTrx = totalLimit / totalWeight
trxPerResource = totalWeight / totalLimit
```

初版實作誤乘了 SunPerTrx 導致結果大 100 萬倍，已修正。

## DTO 設計

```csharp
public record ResourceExchangeRate(
    ResourceType Resource,
    decimal ResourcePerTrx,
    decimal TrxPerResource,
    decimal NetworkTotalStakedTrx,  // 全網質押 TRX（TRX 單位）
    long NetworkTotalResourceLimit)  // 全網資源上限
{
    public decimal EstimateResource(decimal trxAmount);
    public decimal EstimateTrx(long resourceAmount);
}
```

## 影響範圍

- `AccountResourceInfo` 新增 4 個 optional 欄位（`NetworkTotal*`），不是 breaking change
- HTTP 和 gRPC provider 都更新解析
- `TronClient.GetResourceExchangeRateAsync(ResourceType)` 新增
- Sandbox `GET /api/staking/exchange-rate/{resource}` 新增
- 4 個單元測試
