# ADR 012: Sandbox 測試介面從 Scalar 換成 Swagger UI

## 背景

Sandbox 原本使用 Scalar API Reference（`Scalar.AspNetCore`）作為測試介面。
測試過程中發現 Scalar 無法正確處理 array query parameter：

- 多個同名 query parameter（`?trc20=a&trc20=b`）只送出最後一個值
- 逗號分隔輸入也被 Scalar 拆開後只送最後一個

這是 Scalar 的已知 bug：
- [scalar/scalar#3054](https://github.com/scalar/scalar/issues/3054) — array query param 只送最後一個值
- [scalar/scalar#4189](https://github.com/scalar/scalar/issues/4189) — repeated fields 只保留最後一個值
- [scalar/scalar#4232](https://github.com/scalar/scalar/issues/4232) — array 型別參數無法新增多個值

截至 2026-04-06，該 bug 從 2024 年開始仍未修復。

## 決策

- 移除 `Scalar.AspNetCore` 和 `Microsoft.AspNetCore.OpenApi` 套件
- 只保留 `Swashbuckle.AspNetCore`，Swagger UI 作為唯一測試介面
- 測試介面 URL：`http://localhost:5178/swagger`

Swagger UI 正確支援 array query parameter 的 **Add item** 操作。

## Sandbox array query parameter 處理

`/api/account/{address}/balance` 的 `trc20` 參數保持 `string[]`（符合 OpenAPI 規範），
同時在 server 端支援逗號分隔作為 workaround：

```csharp
var contracts = (trc20 ?? [])
    .SelectMany(s => s.Split(','))
    .Select(s => s.Trim())
    .Where(s => s.Length > 0)
    .ToArray();
```

這樣 curl 和 Postman 可以用標準格式 `?trc20=a&trc20=b`，
也可以用逗號格式 `?trc20=a,b`。

## 放棄的方案

- 把 `string[]` 改成 `string` 來繞過 Scalar bug：犧牲正確的 OpenAPI 定義
- 同時保留 Scalar 和 Swagger UI：維護兩套設定沒必要
- 等 Scalar 修 bug：bug 從 2024 年就存在，無法確定修復時程
