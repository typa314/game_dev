# client：遊戲程式框架

> 狀態：**框架 v0.1 施工中**（2026-10-14）。設計依據見 `F:\game\design\`。
> ⚠ 專案禁止外洩：不 push、不送雲端 AI、不用雲端服務。

## 1. 策略
- 最終引擎是 **Unity 6 LTS**。初期先把**核心邏輯寫成純 C#**，用 .NET 8 驗證；Unity 也使用 C#，所以這些程式碼**之後直接搬進 Unity 使用，不需要改寫**。
- 先把框架搭好，細節之後再補。每個階段完成後，都在 `F:\game\SESSION.md` 留下紀錄。

## 2. 分層與目錄
```text
client/
├── Assets/Game/
│   ├── Domain/            純 C#，不依賴 Unity 和 JSON（asmdef: Game.Domain, noEngineReferences）
│   │   ├── Common/         Rating、Rarity、Result、IRandom(SplitMix64)、IClock、RatingPolicy
│   │   ├── MasterData/     主資料定義、MasterDatabase、MasterDataValidator
│   │   ├── Player/         PlayerState（Profile 存檔內容）
│   │   ├── Events/         DomainEvent、Ev 常數、EventBus（佇列式，可重入）
│   │   ├── Conditions/     通用條件 DSL 的評估器
│   │   ├── Growth/         等級、覺醒、數值、好感、立繪階段解鎖
│   │   ├── Gacha/          抽取（天井、十連保底、pickup）、機率公開
│   │   ├── Encounter/      巧遇挑選（只限已持有角色、系列順序、保底）、體力回復，不招募角色
│   │   ├── Battle/         決定性推線戰鬥模擬（30 tick/s、整數運算、可回放）
│   │   ├── Achievements/   事件驅動的成就追蹤（sum/count/distinct/state）
│   │   └── Portrait/       造型決定（劇情強制 → 服裝 → 立繪階段，依分級退回）
│   ├── Core/              純 C# ＋ Newtonsoft.Json（asmdef: Game.Core）
│   │   ├── GameCore.cs     所有狀態變更命令的唯一入口（之後可換成連線版）
│   │   ├── MasterDataLoader.cs
│   │   └── Persistence/    存檔封裝、SHA-256 損壞偵測、原子寫入＋3 份備份、版本遷移、劇情存檔欄位
│   ├── Unity/             MonoBehaviour 骨架：Bootstrap、AssetService、立繪、BattleRunner、OnGUI 除錯面板（未編譯）
│   └── Tests/             NUnit 測試（Unity EditMode 和 .NET 共用同一份；目前只有 SmokeTests）
└── DotnetTests/           .NET 8 測試專案：直接編譯 Domain、Core、Tests 的原始碼
```
- 主資料原始檔：`F:\game\data\master\*.json`（範例資料）。

## 3. 進度
| 項目 | 狀態 |
|---|---|
| Domain 全部模組 | ✅ .NET 8（C# 9）編譯通過 |
| Core：GameCore、存檔、劇情、橫切服務 | ✅ 編譯通過 |
| .NET 測試 | ✅ 290/290 通過（A1～A10、B1～B5、C1～C7、D1～D6 全數通過） |
| 協作流程 | Gemini / Codex 協同開發，本機執行驗收 |
| A1～A10 領域單元測試 | ✅ A1～A10 查核點全部驗收完成（共 173 個測試） |
| B1～B5 雙層存檔與持久化測試 | ✅ B1～B5 查核點全部驗收完成（共 19 個測試） |
| C1～C7 劇情系統與驗證器測試 | ✅ C1～C7 查核點全部驗收完成（共 78 個測試） |
| D1～D6 橫切服務測試 | ✅ D1～D6 查核點全部驗收完成（多語言、平台、資產、設定、年齡確認、時鐘，共 20 個測試） |
| 實機試跑展示 | ✅ `dotnet run --project client/GameDemo -- --auto` 端對端執行通過 |
| Unity 層骨架 | 🟡 已撰寫，**沒有經過編譯**（本機沒有安裝 Unity）；需要先安裝 com.unity.nuget.newtonsoft-json |
| Live2D、Yarn Spinner、Addressables、Steamworks | ⏳ M0 技術驗證時接入 |

## 4. 關鍵約定（細節之後補充）
- **狀態只能透過 `GameCore` 的命令修改**；UI 只讀取 `PlayerState`，並訂閱 `EventBus`。
- **戰鬥結果不信任呼叫端**：`SubmitBattle` 依 `BattleReport`（seed、編隊、指令）重新模擬後才發放獎勵。
- 所有隨機都透過 `IRandom`；測試用固定 seed。
- 主資料 JSON 使用 camelCase，載入時開啟 `MissingMemberHandling.Error`，打錯欄位名稱會直接報錯。
- 分級：`RatingPolicy` = min(建置版本上限, 玩家設定)。立繪階段禁止 R18；R18 成就禁止設定 Steam API name（由驗證器檢查）。
- **版本遷移規則**：改 `PlayerState` 欄位，就要提高 `CurrentSchemaVersion`，並新增 `Migration` 和範例存檔（置於 `Tests/SaveFixtures/`）。缺少 Migration 會拒絕載入並拋出例外。

## 5. 驗證指令（測試專案建立後）
```bash
# 關閉 .NET 遙測（已經用 setx 永久設定）
DOTNET_CLI_TELEMETRY_OPTOUT=1 dotnet test F:/game/client/DotnetTests
```
