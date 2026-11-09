# 任務 OPT-7：巧遇約會地圖 (`EncounterView`)、榮譽成就 (`AchievementView`) 與系統設定/年齡確認 UI 【已完成】

> **狀態**：✅ **已完成驗收並通過全量雙端測試**（2026-11-05）
> **驗收紀錄**：
> - `dotnet test client/DotnetTests/Game.DotnetTests.csproj`：359/359 通過。
> - Unity 6 LTS CLI EditMode：413/413 全部通過（27.60 秒），含 `OPT7_1`～`OPT7_4` 綠燈與 G3 陽性對照。
> - 資料與劇本驗證：`tools/ValidateData` 與 `tools/ValidateStory` 均 100% 通過（0 錯誤）。
> - 解決架構缺口：`ARCH-04` 最後三個入口缺口（巧遇、成就、設定/AgeGate）全面填補。

> **依據**：[`design/19_optimization_and_m1_implementation_plan.md`](../../design/19_optimization_and_m1_implementation_plan.md) Step 7、[`design/07_gacha_and_encounter.md`](../../design/07_gacha_and_encounter.md)、[`design/13_achievements.md`](../../design/13_achievements.md)、[`client/GEMINI_PROTOCOL.md`](../GEMINI_PROTOCOL.md)
> **目標**：解決 `ARCH-04` 最後三個入口缺口，實作 `EncounterView`（巧遇探索地圖與事件結算）、`AchievementView`（戰役榮譽追蹤與一鍵領獎）及 `SettingsModalView`（音量、語言、文字速度、分級切換與 AgeGate 18+ 確認）。

## 允許新增或修改的檔案（嚴禁修改範圍外檔案）
- `client/Assets/Game/Core/UI/Presenters.cs`
- `client/Assets/Game/Unity/UI/EncounterView.cs`（新增）
- `client/Assets/Game/Unity/UI/AchievementView.cs`（新增）
- `client/Assets/Game/Unity/UI/SettingsModalView.cs`（新增）
- `client/Assets/Game/Unity/UI/LobbyView.cs`
- `client/Assets/Game/Unity/Editor/BuildScript.cs`
- `client/Assets/Game/Tests/EncounterAchievementSettingsViewTests.cs`（新增）

## 實作細項與介面規格
1. **`EncounterView.cs` (`namespace Game.Unity.UI`)**：
   - 繼承 `UnityScreenBase`，顯示所有地點（`spot_park`、`spot_cafe`），標示解鎖狀態、當前體力 `⚡ Stamina / Max` 與保底計數 `MissStreak`。
   - 點擊已解鎖地點呼叫 `GameCore.StartEncounter(spotId)`（若已有 `Pending` 事件則直接顯示該事件選項，不重複扣體力），展示巧遇劇情與回應選項按鈕（`choice0`、`choice1`），點選後呼叫 `GameCore.ResolveEncounter(choiceKey)` 並展示獲得的好感度與道具獎勵。
2. **`AchievementView.cs` (`namespace Game.Unity.UI`, 實作 `IAchievementView`)**：
   - 繼承 `UnityScreenBase`，綁定 `AchievementPresenter` 與 `RedDotService`。
   - 列出成就清單（名稱、條件說明、進度 `Current/Target`、寶石獎勵、領取按鈕），點擊領取呼叫 `Presenter.Claim(achId)` 並刷新紅點與頂部貨幣列。
3. **`SettingsModalView.cs` (`namespace Game.Unity.UI`)**：
   - 繼承 `UnityScreenBase`，整合 `SettingsService`、`AgeGateService`、`ILocalizationService`、`AudioService`。
   - 提供主音量/BGM/SE/語音調整、語言切換（`zh-TW` / `en`）、Live2D 開關、內容分級選擇（切換至 `R18` 且尚未通過 `AgeGate` 時彈出 18+ 年齡確認視窗，確認後才呼叫 `AgeGateService.ConfirmAge` 更新 `RatingOptIn`）。

## 查核點（必須在測試中覆蓋）
- **OPT7-1**：在 `EncounterView` 啟動 `spot_park` 巧遇並完成選項結算，體力扣除 1 點、角色好感度增加，且**角色持有數量不變**（巧遇不招募角色）。
- **OPT7-2**：在 `AchievementView` 領取已解鎖成就後寶石入帳且紅點更新；對未解鎖成就呼叫領取回報 `achievement_locked`（陽性對照）。
- **OPT7-3**：在 `SettingsModalView` 切換語言觸發 `LocaleChanged`，調整音量同步更新 `AudioService`，且未確認年齡前分級維持 `G`。

## 共同限制
- C# 9 以內：不使用 `record`、`init`、`file-scoped namespace`、`global using`。
- 註解使用繁體中文。
