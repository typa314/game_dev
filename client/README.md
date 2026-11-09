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
| Domain 全部模組 | ✅ .NET 8 與 Unity 6 LTS 編譯通過，嚴格 noEngineReferences |
| Core：GameCore、存檔、劇情、橫切服務、MVP Presenters | ✅ 編譯通過，純 C# 隔離保證 |
| .NET 8 單元測試 | ✅ 369/369 通過（A1～A11、B1～B5、C1～C7、D1～D6、E1～E4、I1～I5、H1～H12、OPT-1～OPT-8、M2-1～M2-8、M3-1～M3-8 回歸全數通過） |
| Unity Test Runner (EditMode) | ✅ 450/450 通過（雙端 100% 綠燈，包含 M1/M2/M3 PlayMode 全鏈路整合測試、F5 Live2D Cubism SDK 5-r.6.1 整合、F8 Steamworks.NET 與 M3-8 端到端全鏈路驗收） |
| 協作流程 | Gemini / Codex / Claude 協同開發，本機執行驗收 |
| A1～A11 領域單元測試 | ✅ A1～A11 查核點全部驗收完成（含 R→UR 進階與全稀有度扭蛋） |
| B1～B5 雙層存檔與持久化測試 | ✅ B1～B5 查核點全部驗收完成 |
| C1～C7 劇情系統與驗證器測試 | ✅ C1～C7 查核點全部驗收完成 |
| D1～D6 橫切服務測試 | ✅ D1～D6 查核點全部驗收完成 |
| E1～E4 整合驗證與效能測試 | ✅ 完整遊戲循環、平衡模擬、戰鬥與存檔效能全數達標 |
| I1～I5 資料與工具管線 | ✅ 主資料驗證、同步、資產匯入、劇本驗證、在地化工具全數完工 |
| H1～H12 Unity 表現層框架 | ✅ 畫面堆疊導航、MVP 架構、ADV 演出、圖鑑、音效與共用 UI 元件 |
| F1～F13 Unity 技術驗證與平台整合 | ✅ Unity 6 LTS CLI 編譯 0 錯誤、EditMode 450 綠燈、F5 Live2D Cubism SDK 5-r.6.1 官方模型與效能基準、Steamworks.NET 整合驗收、M3 全鏈路驗收 (13/13 全數完成) |
| 里程碑 M0 技術底座與架構驗證 | ✅ 領域邏輯、存檔、劇情、表現層骨架、工具管線、雙引擎測試與外部平台驗證全數達標 |
| 里程碑 M1 垂直切片 (Vertical Slice) | ✅ OPT-1～OPT-8 八大階段全數完成，端到端完整玩家旅程 100% 閉環，獨立執行檔成功輸出 |
| 里程碑 M2 系統完整 (Feature Complete) | ✅ M2-1～M2-8 八大系統擴充全數完成（第 2 章戰役、巧遇劇本橋接、角色路線攻略與回憶重播、衣櫥服裝與四時段燈光、一鍵領取成就、限定 Pickup 動態看板、即時多語系切換），424/424 測試全綠通過 |
| 里程碑 M3 內容量產與在地化 (Content & Localization) | ✅ M3-1～M3-8 八大任務全數完成（12 角色/S3 覺醒、Live2D L1 正式串接、標題與多槽位存讀檔、四語系對齊、成人 DLC 獨立封裝審計、ADV 分支拓樸圖、本機當機日誌、Ch.1～Ch.3 全 9 關平衡模擬、450/450 測試通過、Standalone 207 MB 執行檔封裝成功） |

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
