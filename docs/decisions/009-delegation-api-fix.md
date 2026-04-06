# 009: Delegation API 端點修正

## 背景

`GetResourceInfoAsync` 的 `delegationsOut` / `delegationsIn` 始終回傳空陣列。手動測試質押 + 委託後仍然為空。

## 問題根因

### Bug 1：端點名稱大小寫錯誤

SDK 使用大寫 `V2`，Tron 節點只接受小寫 `v2`：

| SDK 使用 | 正確端點 |
|----------|---------|
| `/wallet/getdelegatedresourceaccountindexV2` | `/wallet/getdelegatedresourceaccountindexv2` |
| `/wallet/getdelegatedresourceV2` | `/wallet/getdelegatedresourcev2` |

節點回傳 405（Method Not Allowed），被 `GetResourceInfoAsync` 的 try-catch 吞掉，默默回傳空陣列。

### Bug 2：JSON 序列化 key 不匹配

`TronHttpProvider` 全域使用 `JsonNamingPolicy.SnakeCaseLower`，導致：

```
// SDK 序列化後
{"from_address":"41...","to_address":"41..."}

// Tron API 期望
{"fromAddress":"41...","toAddress":"41..."}
```

Tron 節點收到 snake_case key 後回傳 `{}`（空結果），SDK 解析為空陣列。

## 決策

1. 端點名稱改為小寫 `v2`
2. `getdelegatedresourcev2` 改用 `PostRawAsync` 手動組 JSON，繞過 `SnakeCaseLower`，保持 camelCase key

## 注意事項

Tron API 的請求 body 是 **camelCase**（`fromAddress`、`toAddress`、`ownerAddress`），但其他 endpoint 碰巧用的 property name 本身就是 snake_case（如 `owner_address`），所以 `SnakeCaseLower` 不會造成問題。只有 delegation API 的 `fromAddress`/`toAddress` 被錯誤轉換。

未來新增 Tron API 呼叫時，需注意 request body 的 key naming 是否會被 `SnakeCaseLower` 影響。
