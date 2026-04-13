# 015: CI/CD — Tag-Based NuGet 發佈

## 背景

ChainKit 0.1.0 版本的 NuGet 發佈是手動打包（`dotnet pack` → `dotnet nuget push`），流程繁瑣且容易遺漏套件。新增 ChainKit.Evm 後有三個套件需要同步發佈，需要自動化。

## 決策

### 觸發方式：Git Tag

使用 GitHub Actions，在 push `v*` tag 時自動觸發 build → test → pack → push to NuGet.org。

**流程：**
1. 開發者在 `src/Directory.Build.props` 更新版本號
2. Commit + push
3. `git tag v0.2.0 && git push origin v0.2.0`
4. GitHub Actions 自動：Restore → Build → Test → Pack → Push

**原因：**
- 日常 push 不會意外發佈到 NuGet
- 版本號由 `Directory.Build.props` 統一控制，tag 只是觸發器
- `--skip-duplicate` 防止重複發佈

**放棄的方案：**
- (A) 每次 push 到 main 自動發佈 — 風險太高，開發中的程式碼可能被發佈
- (C) 手動觸發（GitHub Actions UI 按鈕）— 多一步操作，且無法從命令列觸發

### NuGet API Key 設定

- 存在 GitHub Repository Secrets（`NUGET_API_KEY`）
- Key 使用 glob pattern `W6fux5.ChainKit.*` 涵蓋所有套件（含未來新增的鏈）
- Key 有效期 365 天

### Workflow 檔案

`.github/workflows/nuget-publish.yml`

- Runner: `ubuntu-latest`
- .NET: `10.0.x`
- 測試過濾：`Category!=Integration&Category!=E2E`（CI 環境無 Anvil）
- Pack: `dotnet pack --configuration Release --output nupkg`
- Push: `dotnet nuget push "nupkg/*.nupkg" --skip-duplicate`

### 發佈的套件

| Package ID | 說明 |
|------------|------|
| `W6fux5.ChainKit.Core` | 跨鏈共用核心 |
| `W6fux5.ChainKit.Tron` | Tron SDK |
| `W6fux5.ChainKit.Evm` | EVM SDK（Ethereum、Polygon） |

三個套件共用同一個版本號（`Directory.Build.props`），同步發佈。
