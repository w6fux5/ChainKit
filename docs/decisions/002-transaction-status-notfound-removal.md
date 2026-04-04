# 002: 移除 TransactionStatus.NotFound

## 背景

`TransactionStatus` 原本定義四個狀態：`NotFound`、`Unconfirmed`、`Confirmed`、`Failed`。

檢視程式碼發現 `NotFound` 從未被使用。`GetTransactionDetailAsync` 查不到交易時回傳 `TronResult.Fail()`，不是 `Status = NotFound` 的成功結果。

## 決策

- 移除 `TransactionStatus.NotFound`
- 為剩餘 enum 值加上顯式數字定義：`Unconfirmed = 0, Confirmed = 1, Failed = 2`

## 原因

1. **`NotFound` 無人引用** — 全域搜尋 `TransactionStatus.NotFound` 零結果
2. **符合 Result Pattern 語意** — 查不到交易代表沒有有意義的資料，回 `Fail` 是正確的。回 `Ok` 搭配空的 `TronTransactionDetail`（所有欄位為空值/零值）反而讓消費者困惑
3. **Tron 出塊 3 秒** — 「廣播未打包」窗口極短，`NotFound` 作為交易狀態在實務上幾乎碰不到
4. **顯式數字定義** — 防止未來新增或移除 enum 值時意外改變既有值的序號

## 放棄的方案

**改為回傳 `Ok` + `Status = NotFound`：** 讓消費者能用單一 `Status` 欄位判斷所有狀態。放棄原因是 `Ok` 應代表有有意義的回傳資料，空資料的 `Ok` 違反 Result Pattern 的設計意圖。
