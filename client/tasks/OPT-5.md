# 任務 OPT-5：角色養成、突破進階與換裝衣櫥整合畫面 (`CharacterRosterView` ＆ `WardrobeView`)

> **依據**：[`design/19_optimization_and_m1_implementation_plan.md`](../../design/19_optimization_and_m1_implementation_plan.md) Step 5、[`design/14_ui_screens.md`](../../design/14_ui_screens.md) §4、[`client/GEMINI_PROTOCOL.md`](../GEMINI_PROTOCOL.md)
> **目標**：解決 `ARCH-04` 養成與換裝缺口，實作 `CharacterRosterView`（角色列表、升級、覺醒突破、R→UR 位階進階）與 `WardrobeView`（立繪階段 S1～S3 切換、服裝穿戴、大廳秘書官與場景設定），並與 `LobbyView` 無縫連動。

## 允許新增或修改的檔案（嚴禁修改範圍外檔案）
- `client/Assets/Game/Core/UI/Presenters.cs`
- `client/Assets/Game/Unity/UI/CharacterRosterView.cs`（新增）
- `client/Assets/Game/Unity/UI/WardrobeView.cs`（新增）
- `client/Assets/Game/Unity/UI/LobbyView.cs`
- `client/Assets/Game/Unity/Editor/BuildScript.cs`
- `client/Assets/Game/Tests/CharacterAndWardrobeViewTests.cs`（新增）

## 實作細項與介面規格
1. **`CharacterRosterView.cs` (`namespace Game.Unity.UI`, 實作 `ICharacterDetailView`)**：
   - 繼承 `UnityScreenBase`，綁定 `CharacterDetailPresenter`。
   - 左側列出 `Master.Characters` 所有角色卡片（標示持有狀態、`DisplayGrade`、`Level`）；點擊切換檢視角色。
   - 中央顯示角色立繪與位階/星級徽章；右側顯示 HP / ATK / 射程 / 技能說明、好感度進度條，以及 3 個操作按鈕：
     - `[⬆️ 升級 (Level Up)]`：使用玩家持有的經驗道具（`exp_s` / `exp_m` / `exp_l`）升 1 級或連升。
     - `[⭐ 覺醒 (Awaken)]`：消耗角色碎片突破星級與等級上限。
     - `[👑 進階 (Advance)]`：呼叫 `Presenter.Advance(charId)` 進行同卡進階（+1～+5 晉升 SR/SSR/UR）。
2. **`WardrobeView.cs` (`namespace Game.Unity.UI`, 實作 `IWardrobeView`)**：
   - 繼承 `UnityScreenBase`，綁定 `WardrobePresenter` 與 `LobbyPresenter`。
   - 支援切換角色、切換已解鎖立繪階段（`S1`、`S2`、`S3`）、切換已持有服裝、切換大廳背景場景（`scene_lobby_default`、`scene_cafe`），並提供「`📌 設為大廳秘書官`」按鈕呼叫 `GameCore.SetLobby`。
3. **`LobbyView.cs` 連動修正**：
   - 點擊「部隊編組」開啟 `CharacterRosterView`；點擊「換裝衣櫥」開啟 `WardrobeView`。
   - 當透過 `SetLobby` 更新秘書官後，`LobbyView` 的立繪、左上角胸像、角色銘牌與問候語動態反映 `Player.Lobby.Character` 所指定的角色。

## 查核點（必須在測試中覆蓋）
- **OPT5-1**：在 `CharacterRosterView` 對持有角色執行升級、覺醒（達 Lv30 ★2 解鎖 `S2` 立繪）與位階進階，UI 文字與角色狀態同步更新。
- **OPT5-2**：在 `WardrobeView` 切換立繪階段並設為大廳秘書官，返回 `LobbyView` 後看板娘立繪與名稱即刻更新；對未持有角色呼叫設定秘書官時失敗並顯示錯誤（陽性對照）。

## 共同限制
- C# 9 以內：不使用 `record`、`init`、`file-scoped namespace`、`global using`。
- 註解使用繁體中文。

## 執行成果與驗收數據 (Status: COMPLETED ✅)
- **.NET 8 測試**：`359 / 359` 通過。
- **Unity 6 LTS EditMode 批次測試**：`405 / 405` 通過（耗時 28.44 秒，證書：`C:\Users\typa3\agent_tmp\game_dev\opt5_test_results.xml`）。
- **G3 陽性對照**：故意將大廳場景斷言改為 `"scene_fake_g3_fail"`，測試立即捕捉失敗並拋出 `Expected: "scene_fake_g3_fail", But was: "scene_cafe"`；還原後 100% 全綠。
- **資料庫與劇本靜態校驗**：`ValidateData` 與 `ValidateStory` 均 100% 通過。
