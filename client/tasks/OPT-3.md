# 任務 OPT-3：關卡選擇地圖 (`StageSelectView`) 與戰前編隊整備系統

> **狀態**：✅ 已完成 (100%)
> **執行日**：2026-11-05
> **依據**：[`design/19_optimization_and_m1_implementation_plan.md`](../../design/19_optimization_and_m1_implementation_plan.md) Step 3、[`client/GEMINI_PROTOCOL.md`](../GEMINI_PROTOCOL.md)
> **目標**：解決 `ARCH-05`（解除大廳出擊按鈕硬編碼綁死 `st_1_01` 的限制，實作 `StageSelectPresenter` 與 `StageSelectView`，支援關卡選擇、敵方主堡特性預警、三星條件顯示與戰前 1～10 人快速編隊）。

## 允許新增或修改的檔案（嚴禁修改範圍外檔案）
- `client/Assets/Game/Core/UI/Presenters.cs`（修改：新增 `StageSelectPresenter` 與 `IStageSelectView`）
- `client/Assets/Game/Unity/UI/StageSelectView.cs`（新增）
- `client/Assets/Game/Unity/UI/LobbyView.cs`（修改：支援開啟 `StageSelectView`）
- `client/Assets/Game/Unity/Editor/BuildScript.cs`（修改：掛載 `StageSelectUI`）
- `client/Assets/Game/Tests/StageSelectTests.cs`（新增）

## 實作細項與介面規格
1. **`StageSelectPresenter` (`Presenters.cs`, 純 C#)**：
   - 建立 `StageSummaryItem` 資料結構：`StageId`、`NameKey`、`Chapter`、`IsUnlocked`、`Stars`、`EnemyBaseHp`、`EnemyBaseTraits`（如 `shockwave_on_hp_loss`、`immune_ranged`）、`StarConditions`、`FirstClearRewards`。
   - 提供 `GetStages()`（依 `Master.Stages` 章節順序回傳）、`SelectStage(string stageId)`、`ToggleTeamMember(string charId)`（若已在隊伍則移除，不在則加入，並呼叫 `_core.SetTeam`；若剩最後 1 人嘗試移除則回報 `invalid_team`）、`CanStartSelectedStage()`。
2. **`StageSelectView.cs` (`namespace Game.Unity.UI`)**：
   - 繼承 `UnityScreenBase`，實作 `IStageSelectView`，使用 `ProceduralUiKit` 打造二次元戰術作戰星圖介面。
   - 左側顯示關卡列表按鈕（`st_1_01`、`st_1_02`、`st_1_03`），標示解鎖狀態與星數 `★`。
   - 右上顯示當前選中關卡詳情：敵方主堡 HP、**主堡特殊防衛特性警示膠囊**（例如 `st_1_02` 顯示 `⚡ 主堡每損血 20% 觸發全屏震波擊退`、`st_1_03` 顯示 `🛡️ 主堡免疫遠程攻擊（射程>150無效，請編組近戰角色）`）。
   - 右下顯示「出戰編隊整備區」：列出玩家已持有角色頭像卡，點擊可切換上陣/下陣，以及「`▶ 開始作戰 (SORTIE)`」大按鈕（呼叫 `BattleRunner.StartBattle(_selectedStageId)`）。
3. **`LobbyView.cs` 修改**：
   - 點擊「出擊」按鈕不再寫死直接叫 `BattleRunner.StartBattle("st_1_01")`，而是呼叫 `OpenStageSelect()` 推進至關卡星圖畫面。
4. **`BuildScript.cs` 修改**：
   - 在自動構建 Boot 場景中掛載 `StageSelectUI`。

## 查核點驗收結果
- **OPT3-1 ✅**：新玩家載入時，`st_1_01` 為 `IsUnlocked = true`，`st_1_02` 與 `st_1_03` 為 `IsUnlocked = false`；嘗試選擇未解鎖關卡出擊時被阻擋（`CanStartSelectedStage() == false`，陽性對照）。
- **OPT3-2 ✅**：透過 `ToggleTeamMember` 新增與移除隊員正確同步至 `Player.Team`；嘗試移除最後一位隊員時失敗並回傳 `invalid_team`，且原隊伍不變。
- **OPT3-3 ✅**：`StageSelectView` 自動構建 UGUI 階層、顯示主堡特性警報文字，並能切換關卡與雙向堆疊回退。
- **測試總覽**：
  - `dotnet test`：359/359 全部通過（淨增 3 項）。
  - Unity EditMode：396/396 全部通過（淨增 5 項，耗時 28.84 秒）。
  - G3 陽性對照：故意破壞最後 1 人保護斷言捕捉紅燈（`Expected: unexpected_error_g3, But was: invalid_team`），還原後通過。

## 共同限制
- C# 9 以內：不使用 `record`、`init`、`file-scoped namespace`、`global using`。
- `Game.Core` 嚴禁引用 `UnityEngine`。
- 註解使用繁體中文。
