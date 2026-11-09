# 任務 OPT-6：ADV 劇情播放器 (`StoryPlayerView`)、雙層存讀檔與回憶/CG 圖鑑畫面 (`StoryHubView`)

> **依據**：[`design/19_optimization_and_m1_implementation_plan.md`](../../design/19_optimization_and_m1_implementation_plan.md) Step 6、[`design/08_story_branching.md`](../../design/08_story_branching.md)、[`design/11_save_load.md`](../../design/11_save_load.md)、[`client/GEMINI_PROTOCOL.md`](../GEMINI_PROTOCOL.md)
> **目標**：解決 `ARCH-04` 劇情與圖鑑缺口，實作 `StoryHubView`（主線、角色好感路線、回憶模式、CG 圖鑑、結局列表）與 `StoryPlayerView`（實作 `IStoryView` 之全螢幕 ADV 播放器 + Backlog + 60 槽雙層劇情存讀檔介面）。

## 允許新增或修改的檔案（嚴禁修改範圍外檔案）
- `client/Assets/Game/Unity/UI/StoryHubView.cs`（新增）
- `client/Assets/Game/Unity/UI/StoryPlayerView.cs`（新增）
- `client/Assets/Game/Unity/UI/LobbyView.cs`
- `client/Assets/Game/Unity/Editor/BuildScript.cs`
- `client/Assets/Game/Tests/StoryPlayerViewTests.cs`（新增）

## 實作細項與介面規格
1. **`StoryPlayerView.cs` (`namespace Game.Unity.UI`, 實作 `IStoryView`)**：
   - 繼承 `UnityScreenBase`，內部連接 `StoryPresenter`、`StoryPlaybackController` 與 `StorySaveService`。
   - 實作 `IStoryView` 全部方法：`SetSpeaker`、`SetDialogue`、`ShowOptions`、`HideOptions`、`SetBackground`、`SetCharacterVisible`、`SetCharacterExpression`、`SetCharacterMotion`、`SetCg`、`SetUiVisible`、`UpdateBacklog`、`PlayBgm`、`PlaySe`、`PlayVoice`。
   - 提供控制按鈕：`[推進/下一句]`、`[自動 Auto]`、`[快進 Skip]`、`[Backlog 歷史彈窗]`、`[💾 劇情存檔]`、`[📂 劇情讀檔]`、`[隱藏 UI]`、`[離開]`。
   - **劇情存讀檔彈窗**：支援將當前 `StorySnapshot` 透過 `StorySaveService` 寫入指定欄位（如 `slot_001`～`slot_060`、`quick_1`），並可從欄位讀取恢復劇情狀態（保持 `PlayerState` Profile 養成進度不倒退）。
2. **`StoryHubView.cs` (`namespace Game.Unity.UI`)**：
   - 繼承 `UnityScreenBase`，整合 `CharacterRouteService` 與 `GalleryService`。
   - 提供【主線序章】（載入 `data/story/prologue.txt`）、【角色路線】（顯示 Ch.1～Ch.5 好感解鎖進度與回憶重播按鈕）、【CG 圖鑑】（依角色分組顯示已解鎖 CG 與進階 CG，支援點擊全螢幕預覽）、【結局紀錄】分頁。

## 查核點（必須在測試中覆蓋）
- **OPT6-1**：`StoryPlayerView` 載入分支劇本推進至選項時，選項按鈕正確生成；選項未選前呼叫一般推進不會越過選項（陽性對照）；點選選項後跳轉至對應結局並解鎖 `Ending`。
- **OPT6-2**：透過 `StoryPlayerView` 在選項前存入 `slot_001` → 選 A 達成結局 A → 期間升級角色 → 從 `StoryPlayerView` 讀取 `slot_001` 改選 B 達成結局 B：驗證兩個結局皆已記錄且角色等級不倒退。
- **OPT6-3**：`StoryHubView` 的 CG 圖鑑正確依據 `GalleryService` 與 `RatingPolicy` 列出已解鎖 CG（G 政策下過濾 R18 CG）。

## 共同限制
- C# 9 以內：不使用 `record`、`init`、`file-scoped namespace`、`global using`。
- 註解使用繁體中文。

## 執行成果與驗收數據 (Status: COMPLETED ✅)
- **.NET 8 測試**：`359 / 359` 通過。
- **Unity 6 LTS EditMode 批次測試**：`409 / 409` 全部通過（耗時 28.31 秒，證書：`C:\Users\typa3\agent_tmp\game_dev\opt6_test_results.xml`）。
- **G3 陽性對照**：故意將結局解鎖斷言改為 `"fake_ending_g3_fail"`，測試立即捕捉失敗並拋出 `Expected: True, But was: False`；還原後 100% 全綠。
- **資料庫與劇本靜態校驗**：`ValidateData` 與 `ValidateStory` 均 100% 通過。
