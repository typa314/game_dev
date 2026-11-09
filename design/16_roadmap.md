# 16 開發路線圖

> 每個里程碑結束都要有**可以執行的建置版本**和驗收清單。時程依投入人力另外估算，這裡只定義範圍和順序。

## M0 技術驗證（Tech Spike） 【已完成】
目的：先排除最大的技術風險，再進入正式開發。
- [x] 建立 Unity 6 LTS 專案（`client/`），設定好 asmdef 分層、純 C# 核心解耦。
- [x] Live2D / 2D 骨骼立繪：Lobby 動態立繪呼吸引導與表情點擊回饋。
- [x] 劇本系統：自研 MiniScript 引擎，支援選項分支、條件跳轉、分級標籤與自訂指令。
- [x] 分級防護：G 政策下自動降級/攔截 R18 節點與圖鑑過濾，AgeGate 18+ 二次確認保護。
- [x] Steamworks 整合：SteamPlatformService 初始化與降級機制（NullPlatformService）。
- **驗收**：已全部通過純 C# 單元測試與 Unity 6 EditMode / PlayMode 雙端驗收。

## M1 垂直切片（Vertical Slice） 【已完成】
目的：一條完整的玩法循環，用 3 位角色跑通所有核心系統。
- [x] 資料管線：CSV/JSON → ValidateData 靜態語法/交叉參照驗證器 → MasterDataSync。
- [x] 戰鬥：決定性模擬核心（BattleSimulator）、關卡星圖戰前編隊（StageSelectView）、能量、冷卻、擊退、據點砲、主堡警報與血條。
- [x] 養成：部隊名冊（CharacterRosterView）、升級、覺醒 ★2、立繪階段 S1 → S2 切換與換裝大廳秘書官（WardrobeView）。
- [x] 劇情：ADV 劇情播放器（StoryPlayerView）、Backlog、選項分支、多結局達成與 CG/回憶圖鑑（StoryHubView）。
- [x] 存檔：雙層存檔架構（全域 Profile 與快照 StorySaveService）、原子寫入、重啟無損驗證。
- [x] 多語言與系統：zh-TW 與 en 切換、字型支援、AudioService 音量控制與 AgeGate 年齡確認（SettingsModalView）。
- [x] UI 全閉環：LobbyView 導航堆疊（ScreenNavigator）、戰役榮譽成就（AchievementView）與 RedDotService 動態紅點更新。
- **驗收**：新玩家從大廳進入關卡星圖通關解鎖咖啡廳、轉蛋十連抽觸發成就、角色升級覺醒解鎖 S2 立繪並換裝設為大廳、巧遇互動增加好感度、播放分支劇本在選項前存檔走雙結局且養成不倒退、領取成就清除紅點並持久化至磁碟存檔（端到端整合測試 `M1_VerticalSlice_FullPlayerJourney_AcrossAllUiViews_Succeeds` 100% 通過，Windows Standalone `GameClient.exe` 建置成功）。

## M2 系統完整（Feature Complete） 【已完成】
- [x] 轉蛋（標準池、全稀有度跨階池、限定 pickup 輪替池 `banner_sakura_up`、天井、機率公開頁、動態看板舞台、統計測試）。
- [x] 巧遇（3 大探索地點含櫻花神社 `spot_shrine`、體力、多角色系列事件 Series 1～3、保底、ADV 巧遇劇本演出橋接）。
- [x] 換裝和場景（Dressing Room 服裝穿戴清單 `EquipCostume`、四時段早/中/夕/夜光線調和連動、大廳秘書官設定、`UnityAudioDriver` 實體音訊接線）。
- [x] 成就（追蹤器、多維度成就庫、大廳紅點連動、一鍵全部領取 `Claim All`、Steam 對應）。
- [x] 角色路線攻略與回憶（`StoryHubView` 角色路線 Ch.1～Ch.5 正式攻略、回憶重播模式 `Recollection Mode` 不污染存檔、CG 圖鑑、結局列表）。
- [x] 主線戰役章節擴充與多語系（第 1～2 章共 6 關卡、章節分頁切換、勝利結算「▶ 下一關」直接推進、`zh-TW` / `en` 即時動態語言切換）。
- **驗收**：四大管線 CLI（`ValidateData`、`ValidateStory`、`LocalizationTool`、`BalanceSim`）100% 通過；`.NET 8`（361/361）與 Unity 6 LTS EditMode（424/424，含端到端測試 `M2_8_FeatureComplete_EndToEnd_Chapter2_Shrine_Routes_Pickup_ClaimAll_Locale_Succeeds` 與 G3 陽性對照）全數通過；Windows Standalone `GameClient.exe` 封裝成功。

## M3 內容量產與在地化 【已完成】
- [x] 第一/二梯隊 12 位角色資料完整、S3 覺醒立繪階段（共 36 階段）、Ch.1～Ch.3 主線戰役關卡（`st_1_01`～`st_3_03`）與專屬好感路線劇本（含 G/R18 分級雙向 Fallback 劇本）、Live2D L1 正式串接（`PortraitFactory` 支援 L1 Cubism 官方模型與 L0 立繪無縫切換）。
- [x] 標題畫面（`TitleScreenView`）與多槽位雙層存讀檔 UI（`SaveLoadModalView`），支援 6 槽位儲存/讀取與空槽位安全回退。
- [x] 四語系在地化（`zh-TW` / `en` / `ja` / `zh-CN` 各 70 條鍵值 100% 對齊）、UI 全覆蓋與 `LocalizationValidator` UI 文字長度上限/富文字標籤閉合驗證器。
- [x] 成人 DLC 獨立封裝管線（`BuildScript.ExportDlcPackageManifest` 物理隔離拆分 `base_manifest.csv` 與 `dlc_adult_manifest.csv`）與全系統 R18 Fallback 安全審計（`AddressablesCatalogAuditor`）。
- [x] ADV 劇情分支拓樸圖（`StoryBranchGraphBuilder`）與已選選項打勾標記（`✓`）。
- [x] 純本機結構化當機日誌（`LocalCrashLogger`，檔案輪替上限 10 份、零外部傳輸）與發行就緒自動稽核器（`ReleaseReadinessAuditor`）。
- [x] M3 全系統平衡模擬（Ch.1～Ch.3 全 9 關 100% 通關勝率）、端到端全鏈路測試（`M3_ContentAndLive2D_EndToEnd_Title_SaveLoad_BranchGraph_FourLocales_Chapter3_Succeeds`）與 Windows Standalone 執行檔（`build/Windows/GameClient.exe`）成功封裝。
- **驗收**：四大 CLI 工具（`ValidateData`、`ValidateStory`、`LocalizationTool`、`BalanceSim`）100% 零錯誤通過；.NET 8 CLI（369/369）與 Unity 6 LTS EditMode（450/450）全量測試 100% 通過；Windows Standalone `GameClient.exe`（207 MB）打包成功。

## M4 上架準備
- [ ] Steamworks 商店頁、內容問卷、分級、Steam Deck 相容性（選做）。
- [ ] 效能和最低配備確認、當機 log（只存本機）。
- [ ] Beta 分支封閉測試（測試者需要簽 NDA，因為專案禁止外洩）。

## 主要風險
| 風險 | 影響 | 對策 |
|---|---|---|
| Live2D 製作量太大 | 時程 | L0 / L1 / L2 分級導入；系統可以隨時升級，不卡程式開發 |
| Q 版戰鬥美術完全沒有 | 戰鬥體驗 | M1 先用 placeholder，並平行評估生成方式 |
| 50 條角色路線的劇本量 | 時程 | 先做 10 位主要角色的完整路線，其他角色先做巧遇和短篇 |
| 分級標錯，R18 素材混進本體 | Steam 下架 | manifest 資料夾下限檢查加上 CI catalog 掃描，兩道防線 |
| AI 生成美術的版權和平台政策 | 上架 | Steam 要求申報 AI 生成內容；上架前需要確認最新政策和模型授權 |
| 專案機密外洩 | 商業 | repo 不 push 到公開位置；不使用雲端翻譯和雲端 AI；測試者簽 NDA |
