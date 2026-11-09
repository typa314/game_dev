# 任務 OPT-8：M1 垂直切片全鏈路整合（End-to-End PlayMode 驗證與獨立執行檔封裝） 【已完成】

> **狀態**：✅ **已完成驗收並通過雙端全量測試與獨立執行檔建置**（2026-11-05）
> **驗收紀錄**：
> - `dotnet test client/DotnetTests/Game.DotnetTests.csproj`：359/359 通過。
> - Unity 6 LTS CLI EditMode：**414/414 全部通過**（30.65 秒），含 `M1_VerticalSlice_FullPlayerJourney_AcrossAllUiViews_Succeeds` 全鏈路綠燈與 G3 陽性對照。
> - Windows Standalone 獨立執行檔：`GameClient.exe` 封裝建置成功（Exit Code 0，大小 171 MB）。
> - 管線校驗：`tools/ValidateData` 與 `tools/ValidateStory` 均 100% 通過（0 錯誤）。
> - 達成 M1 垂直切片里程碑！

> **依據**：[`design/19_optimization_and_m1_implementation_plan.md`](../../design/19_optimization_and_m1_implementation_plan.md) Step 8、[`design/16_roadmap.md`](../../design/16_roadmap.md) M1、[`client/GEMINI_PROTOCOL.md`](../GEMINI_PROTOCOL.md)
> **目標**：將 `OPT-1` ～ `OPT-7` 所有畫面完整組裝至 `Boot.unity`，實作 `LobbyView` 紅點動態刷新，並撰寫涵蓋 M1 垂直切片完整玩家旅程的自動化整合測試。

## 允許新增或修改的檔案（嚴禁修改範圍外檔案）
- `client/Assets/Game/Unity/UI/LobbyView.cs`
- `client/Assets/Game/Unity/Editor/BuildScript.cs`
- `client/Assets/Game/Tests/UnityIntegrationPlayModeTests.cs`
- `design/16_roadmap.md`
- `design/17_framework_worklist.md`
- `client/README.md`

## 實作細項與介面規格
1. **`BuildScript.CreateBootScene()` 全畫面掛載與組裝**：
   - 在 `Boot.unity` 場景自動建立並配置 `GameApp`、`LobbyUI`、`GachaUI`、`StageSelectUI`、`CharacterRosterUI`、`WardrobeUI`、`StoryHubUI`、`StoryPlayerUI`、`EncounterUI`、`AchievementUI`、`SettingsUI`（非首頁畫面初始為隱藏，由 `LobbyView` 與 `ScreenNavigator` 按需開啟）。
2. **`LobbyView` 紅點與全導航閉環**：
   - `LobbyView` 的 6 大右側導航按鈕、右下角出擊按鈕與右上角設定按鈕 100% 綁定至對應的正式 UGUI View，不再有任何空殼 Toast。
   - 在 `RefreshPlayerHeader()` 中查詢 `RedDotService.HasClaimableAchievements()`，動態顯示或隱藏「戰役榮譽」按鈕上的紅點徽章。
3. **M1 垂直切片端到端整合測試 (`UnityIntegrationPlayModeTests.cs`)**：
   - 新增測試 `M1_VerticalSlice_FullPlayerJourney_AcrossAllUiViews_Succeeds`：
     1. 從 `LobbyView` 進入 `StageSelectView`，完成 `st_1_01` 與 `st_1_02` 通關結算，驗證 `spot_cafe` 與 `st_1_03` 解鎖。
     2. 進入 `GachaView` 十連抽，驗證獲得角色與觸發成就 `ach_first_pull`。
     3. 進入 `CharacterRosterView` 將克萊兒升至 Lv30、覺醒 ★2 解鎖 `S2` 立繪，並在 `WardrobeView` 換上 `S2` 設為大廳秘書官。
     4. 進入 `EncounterView` 完成一次巧遇互動獲得好感度。
     5. 進入 `StoryPlayerView` 播放分支劇本，在選項前存檔至 `slot_001` → 走結局 A → 讀檔走結局 B，驗證雙結局達成且角色養成與轉蛋紀錄無倒退。
     6. 進入 `AchievementView` 領取成就寶石，驗證紅點清除與存檔持久化一致。

## 查核點（必須在測試中覆蓋）
- **OPT8-1**：`M1_VerticalSlice_FullPlayerJourney_AcrossAllUiViews_Succeeds` 全鏈路斷言 100% 通過。
- **OPT8-2**：`.NET 8` CLI 測試與 Unity EditMode / PlayMode 測試 0 失敗，Windows Standalone `GameClient.exe` 建置 Exit Code 0。

## 共同限制
- C# 9 以內：不使用 `record`、`init`、`file-scoped namespace`、`global using`。
- 註解使用繁體中文。
