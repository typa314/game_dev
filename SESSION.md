# SESSION.md — 遊戲主程式與開發計畫 (F:\game_dev)

> 本文件為 `F:\game_dev` 遊戲主程式開發跨 Session 交接日誌（Single Source of Truth）。

---

## 2026-10-15 專案狀態與進度記錄（第二階段：C3–C7 與 B5 全面達成）

### 1. 專案拆分與目錄定位
- **專案拆分**：因美術生成資產（CG、模型、Wildcards、Forge 生成產物）與遊戲程式碼架構龐大，已於 2026-10-15 完成專案職責拆分：
  - **`F:\game_dev`**：遊戲主程式客戶端（`client/`）、數值配置（`data/`）、系統企劃與框架工單（`design/`）。
  - **`F:\game`**：專門作為 CG 美術圖與 AI 生圖管線工作區（`assets/`、`sample/`、`wildcards/`、`presets/`、`tools/`）。
- **Git 倉庫狀態**：
  - Remote: `https://github.com/typa314/tower_game.git`
  - 分支: `main`

---

### 2. 本輪開發推進成果 (任務 B5 與 C3～C7 全項落實)

#### (1) 工單 C3: 分級 Fallback（`StoryRatingFallbackTests.cs` 4/4 通過）
- **節點分級 Fallback (`MiniScriptRunner.cs`)**：
  - 支援注入 `RatingPolicy`，實作 `ResolveNodeWithFallback` 於 `Start`, `Choose`, `If`, `Jump` 等各跳轉分支點。
  - **C3-1 ✅**：在 G 政策下進入 `rating:R18` 節點時，自動改走 `fallback:` 指定的節點。
  - **C3-2 ✅**：在 R18 政策下正常進入該節點。
- **CG 分級動態過濾 (`StoryCommandRegistry.cs`)**：
  - 在 `HandleCg` 實作：依 `CgRatingResolver`（或預設規則 `_t2_`, `_t3_`, `_t4_`, `_r18` 判定為 R18）取得 CG 分級；若 `core.Rating != null && !core.Rating.Allows(cgRating)` 則跳過 CG 解鎖與演出呼叫，劇情順暢繼續推進，不拋出例外（**C3-3 ✅**）。
- **C3-4 ✅**：R18 節點沒有 fallback 時，被劇本驗證器（C5）成功擋下。

#### (2) 工單 C4: 結局、已讀、圖鑑、回憶（`StoryFeatureTests.cs` 4/4 通過）
- **已讀追蹤與快進控制器 (`StoryPlaybackController.cs`)**：
  - `RecordCurrentAsRead`：自動將已造訪之節點加入 `Player.Story.ReadNodes`，並將已讀台詞加入 `ReadLines` 與 `ReadNodes`。
  - **C4-1 ✅**：快進邏輯（`CanSkipCurrent`）：當 `Settings.SkipUnread == false` 時，僅在該節點或台詞已讀時允許快進，遇到未讀即停止快進；當 `Settings.SkipUnread == true` 時允許快進未讀。
- **結局與成就整合 (`GameCore.cs`)**：
  - **C4-2 ✅**：`command ending` 指令透過 GameCore 寫入 `Player.Endings` 一次，發佈 `EndingReached` 領域事件，並自動觸發以 `ending_reached` 為條件之成就（如 `ach_ending_any`）解鎖。
- **回憶模式 (`StoryCommandRegistry.cs`)**：
  - **C4-3 ✅**：支援 `IsRecallMode` 屬性；回憶重播章節時使用獨立的暫存變數副本，獎勵類指令（`affection`, `give`, `ending`）在回憶模式中完全不生效，執行結束後 Profile 與存檔 100% 保持不變，`GameCore.Dirty` 不被標記。
- **CG 圖鑑服務 (`GalleryService.cs`)**：
  - **C4-4 ✅**：`GetGroupedGallery` 依角色（`CharacterId`）分組，標註 `IsUnlocked` 解鎖狀態；分級不允許的 CG（`!rating.Allows(cg.Rating)`）在圖鑑中**絕對不列出**。

#### (3) 工單 C5: 劇本驗證器 (`StoryValidator.cs`, `StoryValidatorTests.cs` 19/19 通過)
- 實作完整劇本靜態驗證器，查核點每項皆包含陽性對照（刻意寫錯的劇本）與陰性對照（正常劇本）：
  - **C5-1 ✅（節點存在性）**：檢查 `jump`, `if`, `option` 的目標節點是否存在於腳本中；陽性對照回傳 `ERR_TARGET_NODE_NOT_FOUND`。
  - **C5-2 ✅（台詞 ID 驗證）**：檢查台詞是否缺少 line ID 或 line ID 重複；陽性對照回傳 `ERR_MISSING_LINE_ID` 與 `ERR_DUPLICATE_LINE_ID`。
  - **C5-3 ✅（主資料與實體 ID 引用）**：檢查 `show`/`expr`/`motion`/`affection` 引用的角色、`bg` 引用的場景、`cg` 引用的 CG、`ending` 引用的結局是否存在於 MasterDatabase 或合法集合中；陽性對照回傳 `ERR_INVALID_CHARACTER_ID`, `ERR_INVALID_SCENE_ID`, `ERR_INVALID_CG_ID`, `ERR_INVALID_ENDING_ID`。
  - **C5-4 ✅（R18 節點 Fallback）**：檢查 Rating > G 節點是否具備存在的 fallback 節點；陽性對照回傳 `ERR_R18_MISSING_FALLBACK` 與 `ERR_FALLBACK_NODE_NOT_FOUND`。
  - **C5-5 ✅（可達性分析）**：以圖走訪計算從起點出發所有可達節點；列出所有無法到達的節點；檢查若有結局完全處於不可達節點或期待結局未被定義，陽性對照回傳 `ERR_UNREACHABLE_ENDING`。
  - **C5-6 ✅（指令合法性）**：檢查指令名稱是否都在 `StoryCommandRegistry.SupportedCommands` 中；陽性對照回傳 `ERR_UNREGISTERED_COMMAND`。

#### (4) 工單 C6: 巧遇和劇情的串接 (`StoryIntegrationTests.cs`)
- **C6-1 ✅**：`StartEncounter` 回傳的 `Story` 節點名稱（如 `Encounter_Elysia_01`）可直接由 `MiniScriptRunner` 載入並流暢播放。
- **C6-2 ✅**：玩家選項索引對應 outcomes 的 key（`choice0`、`choice1`），呼叫 `core.ResolveEncounter(choiceKey)` 順利結算好感度與素材獎勵，記錄至 `Seen` 並解除 `Pending`。
- **C6-3 ✅**：劇情中途離開時 `Player.Encounter.Pending` 保留；進入其他地點時受 `encounter_pending` 防護；再次進入同一個地點時順利繼續該事件，且**不重複扣除體力**。

#### (5) 工單 C7: 角色路線開放規則 (`CharacterRouteService.cs`, `StoryIntegrationTests.cs`)
- **C7-1 ✅**：角色路線每位角色 5 章，依好感等級 1–5 級對應開放第 1–5 章；未持有的角色路線全數鎖定；好感等級未達前置要求時後續章節鎖定。
- **C7-2 ✅**：章節完成後記錄至 `Player.Story.CompletedChapters`，在好感等級足夠時立即開放下一章。

#### (6) 工單 B5: 雙層存檔整合 (`StorySaveService.cs`, `DualLayerSaveTests.cs` 3/3 通過)
- **B5-1 ✅（雙層狀態隔離）**：在劇情存檔欄位 X 之後進行轉蛋、角色升級、消耗素材，再讀回劇情存檔 X：Profile（角色等級、抽卡歷史、道具庫存、成就）**完全沒有倒退**，只有劇情變數回到 X 的狀態。
- **B5-2 ✅（多結局累計保留）**：在分歧點存檔讀回後改走另一個分支，已達成的舊結局依然保留在 Profile 中，新結局也成功加入。
- **B5-3 ✅（節點遺失降級保護）**：劇情存檔記錄的節點若在劇本改版中被刪除，自動退回該章節開頭節點（`ChapterStartNode`），恢復劇情變數，並觸發警告回呼，不崩潰。

#### (7) 缺陷加固回歸測試全數通過（詳細請參見 `BUG_LOG.md`）
- **Bug 1（BUG-001）**：`StoryCommandRegistryTests.cs` verbatim 引號轉義修復，傳入合法空字串 `""""`。
- **Bug 2（BUG-002）**：`ConditionEvaluator.cs` null 防禦與安全 `Enum.TryParse`。
- **Bug 3（BUG-003）**：`MiniScriptRunner.cs` 條件求值外層括號剝除、賦值運算函式呼叫支援。
- **Bug 4（BUG-004）**：`GameCore.NewPlayer` 初始角色預設服裝自動解鎖至 `Costumes`。
- **Bug 5（BUG-005）**：`GameCore.SetTeam` 全空隊伍防禦。
- **Bug 6（BUG-006）**：`GameCore.CountStars` 零受傷滿星通關邊界修復。
- **Bug 7（BUG-007）**：`MasterDataValidator.cs` 補齊條件合法性校驗。
- **Bug 8（BUG-008）**：`GameCore.ReachEnding` 與 `UnlockCg` null 與空白防禦。

---

### 3. 測試驗證狀態
- **執行指令**：
  ```bash
  dotnet test client/DotnetTests/Game.DotnetTests.csproj
  ```
- **測試結果**：**通過 315 / 失敗 0 / 略過 0**（全部通過，無任何回歸）。
- **已驗證之工單進度**：
  - A 核心架構（A1～A11）：**全部完成 ✅**
  - B 存檔系統（B1～B5）：**全部完成 ✅**
  - C 劇情系統框架（C1～C7）：**全部完成 ✅**
  - D 橫切服務模組（D1～D6）：**全部完成 ✅**
  - E 整合驗證（E1～E4）：**全部完成 ✅**
  - I 資料與工具管線（I1～I5）：**全部完成 ✅**
  - H Unity 表現層框架（H1～H12）：**全部完成 ✅**

---

### 4. 橫切服務模組落實成果 (工單 D1～D6 全項達成)

#### (1) 工單 D1: 多語言服務與驗證器 (`LocalizationTests.cs` 6/6 通過)
- 實作 `ILocalizationService` 與 `LocalizationService`：
  - **D1-1 ✅**：支援 `loc:` 前綴自動正規化，`loc:char/char01_claire.name` 與 `char/char01_claire.name` 在 `zh-TW`（克萊兒）與 `en`（Claire）均正確回傳。
  - **D1-2 ✅**：切換語言觸發 `LocaleChanged` 事件，後續 `Get` 即時回傳新語言。
  - **D1-3 ✅**：當前語言缺少 key 時自動退回 `zh-TW` 並記錄警告；兩者皆無時安全回傳 `[key]`。
  - **D1-4 ✅**：支援 `{0}`、`{1}` 參數格式化；`LocalizationValidator` 抓出語系間佔位符數量或編號不一致之錯誤。
  - **D1-5 ✅**：`LocalizationValidator` 完整掃描 `MasterDatabase` 中所有 `loc:` 引用的 key，驗證基底語系是否存在該 key。
- 建立磁碟語系資料：`data/loc/zh-TW/strings.json` 與 `data/loc/en/strings.json`。

#### (2) 工單 D2: 平台服務與成就橋接器 (`PlatformServiceTests.cs` 3/3 通過)
- 實作 `IPlatformService`、`NullPlatformService` 與 `PlatformAchievementBridge`：
  - **D2-1 ✅**：在 `NullPlatformService` 下所有系統正常運作、無異常。
  - **D2-2 ✅**：監聽 `AchievementUnlocked` 領域事件，只有具備 `SteamApiName` 的全年齡/R15 成就才會轉送平台；**R18 成就永遠不送至平台**。
  - **D2-3 ✅**：當 `SyncEnabled == false` 時，完全不呼叫平台 API。

#### (3) 工單 D3: 資產 Manifest 與分級目錄保護 (`AssetCatalogTests.cs` 5/5 通過)
- 實作 `AssetCatalog`、`AssetResolution` 與 `AssetManifestValidator`：
  - 建立基底清單：`data/asset_manifest.csv`。
  - **D3-1 ✅**：在 G 政策下解析 R18 key，回傳 `Allowed = false` 與 `IsPlaceholder = true`。
  - **D3-2 ✅（目錄分級下限保護）**：來源目錄包含 `tier2_tease/`、`tier3_climax_poses/`、`tier4_aftermath/` 之素材若在 manifest 標註為 G 或 R15，驗證器拋出 `ERR_RATING_BELOW_FOLDER_MINIMUM` 強制攔截。
  - **D3-3 ✅**：主資料庫立繪、服裝、場景 art key 比對 manifest，缺漏項目回報警告。
  - **D3-4 ✅**：發行模式（Release Mode）下，任何 `placeholder` 狀態資產觸發 `ERR_PLACEHOLDER_IN_RELEASE` 阻止發行；`draft` 產生警告。

#### (4) 工單 D4: 設定持久化與分級上限限制 (`SettingsAndAgeGateTests.cs` 2/2 通過)
- 實作 `GameSettings` 與 `SettingsService`：
  - **D4-1 ✅**：音量（BGM/SE/Voice）、文字速度、自動播放速度、已讀快進、Live2D、語言與分級選擇完整序列化/反序列化。
  - **D4-2 ✅**：`GetClampedRating` 強制將玩家分級選擇截斷至建置版本上限（`BuildProfile.MaxRating`）。

#### (5) 工單 D5: 年齡確認閘門 (`SettingsAndAgeGateTests.cs` 2/2 通過)
- 實作 `AgeGateState` 與 `AgeGateService`：
  - **D5-1 ✅**：未通過年齡驗證時（`!IsVerified`），生效分級 `PlayerMax` 強制固定為 `Rating.G`，即便成人 DLC 已安裝亦同。
  - **D5-2 ✅**：通過驗證時以 `IClock` 記錄時間戳記，並將 `RatingOptIn` 限制在平台天花板內。

#### (6) 工單 D6: 確定性時間來源與全域架構守護 (`ClockTests.cs` 2/2 通過)
- **D6-1 ✅**：實作全自動化代碼靜態架構測試，逐檔掃描 `Domain/` 與 `Core/`，確保**完全沒有任何直接呼叫 `DateTime.Now` 或 `DateTime.UtcNow` 之程式碼**（唯一合法例外為 `SystemClock` 實體）。所有業務邏輯一律透過 `IClock`，完全支援時間測試與重播確定性。

---

### 5. 跨稀有度進階與全新扭蛋系統落實成果 (工單 A11 全項達成)

#### (1) 四項核心使用者需求實作細節
1. **每張卡片由 R 等級升到 UR**：
   - 擴充 `Rarity.Order` 加入 `"UR"`（`Order = { "N", "R", "SR", "SSR", "UR" }`），`Rarity.Rank("UR") == 4`。
   - 實作 `Rarity.Next`（`R -> SR -> SSR -> UR`）與 `Rarity.IsMax`。
   - `CharacterState` 擴充 `CurrentRarity`（預設初始 "R"）與 `DisplayGrade`（如 `R`, `R+1`..`+5`, `SR` 等）。
2. **收集同角色卡片進階 +1～+5，滿 +5 晉升下個等級**：
   - `GameCore.AdvanceCharacter(charId)`：消耗 1 張同角色卡片（或等量碎片），`Tier < 5` 時進階 `Tier++`（並同步 `Awaken`）；`Tier == 5` 時晉升至下一稀有度且重置 `Tier = 0`（`R+5 -> SR+0`, `SR+5 -> SSR+0`, `SSR+5 -> UR+0`）。
   - 陽性對照：已達最高階級 `UR+5` 時，再次進階會被安全擋下並回傳錯誤碼 `max_grade`。
3. **抽卡可抽到 R、SR、SSR、UR 四種等級，依機率出現**：
   - 在 `data/master/banners.json` 建立全新全稀有度卡池 `banner_ur`，設定 rates: `UR: 0.01, SSR: 0.03, SR: 0.16, R: 0.80`，5 位角色均在 R/SR/SSR/UR 池中。
   - `BannerDef` 引入 `AllowCrossRarity: true` 旗標與校驗支援，允許同角色跨稀有度在不同機率池中抽取。
   - `GameCore.PullGacha` 抽中更高稀有度時直接自動升級角色 `CurrentRarity`；重複抽中角色時發放同角色進階卡片 `card_<charId>` 與碎片。
4. **解鎖每個等級 & 進階自動解鎖對應 CG 美術圖並收藏**：
   - `GameCore.UnlockGradeCg`：每次進階（如 `cg_{charId}_{rarity}_plus_{tier}`）與晉升（如 `cg_{charId}_{rarity}`）自動生成 CG ID 並解鎖寫入 `Player.Gallery`。
   - `GalleryService.GetGroupedGallery`：動態納入玩家圖鑑中所有解鎖的進階與稀有度 CG，呈現於圖鑑中。

#### (2) 專屬單元測試 (`RarityProgressionTests.cs` 4/4 通過)
- `Req1_EveryCharacterCanBePromotedFromRToUR`：驗證角色從 R 依序進階晉升至 UR。
- `Req2_Advance_Plus1ThroughPlus5_AndPromoteToNextGrade`：驗證 +1 至 +5 進階、升階 SR、以及 UR+5 邊界防護。
- `Req3_GachaDraw_CanRoll_R_SR_SSR_UR`：驗證四種等級卡片抽取機率分佈與自動晉升。
- `Req4_Progression_Unlocks_CG_Art_In_Gallery`：驗證進階與升階觸發 CG 解鎖並納入圖鑑。

---

### 6. 測試驗證與展示狀態
- **執行單元測試**：
  ```bash
  dotnet test client/DotnetTests/Game.DotnetTests.csproj
  ```
  **測試結果**：**通過 300 / 失敗 0 / 略過 0**（全部通過，耗時 12 秒）。
- **實機展示執行**：
  ```bash
  dotnet run --project client/GameDemo -- --auto
  ```
  **展示成果**：
  - `banner_ur` 十連抽即時抽得 R、SR 卡，並獲取 `card_<charId>` 與心靈碎片。
  - `char01_claire` 連續進階展示：`R -> R+1 -> R+2 -> R+3 -> R+4 -> R+5 -> SR`，每次進階皆解鎖對應 CG！
  - CG 圖鑑即時顯示解鎖之 `cg_char01_claire_r_plus_1`～`5` 與 `cg_char01_claire_sr`。
  - 端對端流程全數通過，Exit Code: 0。

---

### 7. 里程碑達成：E 整合驗證（整個遊戲循環在邏輯上可行）

#### (1) 工單 E1: 不需畫面的完整遊玩測試 (`FullLoopTests.cs` 10 項查核點全過)
- **E1-1 ✅**：建立新玩家，驗證 G 政策與初始關卡/地點/場景/角色配置。
- **E1-2 ✅**：通關 `st_1_01` → 解鎖 `st_1_02`，首通 100 寶石到帳。
- **E1-3 ✅**：通關 `st_1_02` → 解鎖 `spot_cafe` 和 `scene_cafe`，首通 150 寶石到帳。
- **E1-4 ✅**：十連抽 → 獲得新卡與碎片，解鎖 `ach_first_pull` 與累計 `ach_gacha_100`。
- **E1-5 ✅**：提供素材與碎片，Claire 突破 ★2 並升至 Lv30 → 自動解鎖 S2 立繪，`PortraitResolver` 正確回傳 S2 stand art。
- **E1-6 ✅**：於 `spot_park` 完成 Elysia 系列巧遇（第 1 段好感 +30，第 2 段好感 +50、覺醒碎片 +5）→ 好感度與碎片增加，**角色總數絕對不變**。
- **E1-7 ✅**：播放測試劇本，在選項前儲存劇情進度至 `slot_001` → 選 A 走向結局 A，記錄至 Profile。
- **E1-8 ✅**：讀取 `slot_001` → 改選 B 走向結局 B → **結局 A 與結局 B 雙雙保留在 Profile 中；角色、等級、轉蛋紀錄、道具無任何倒退**。
- **E1-9 ✅**：Profile 序列化並寫入磁碟，以 `FileSaveStore` 滾動備份安全回讀，前後 JSON 100% 逐字元一致。
- **E1-10 ✅**：全程於 `NullPlatformService` 與 G 政策下執行，R18 節點順暢自動走 Fallback。

#### (2) 工單 E2: 平衡模擬器 (`tools/BalanceSim` CLI & `BalanceSimTests.cs` 3/3 通過)
- **E2-1 ✅**：產出勝率、平均通關秒數、星數、據點剩餘 HP 報表，支援 CSV 輸出。
- **E2-2 ✅**：通關能力階梯驗證：
  - `st_1_01`：初階隊伍（Lv1）勝率 100%，耗時 ~225s。
  - `st_1_02`：全可取得隊伍（Lv20）勝率 100%，耗時 ~254s。
  - `st_1_03`：Golem 巨石怪關卡，Lv20 隊伍超時（300s 邊界），經過培養之 Lv35 隊伍勝率 100%，耗時 ~289s 順暢通關。
- **E2-3 ✅**：決定性驗證：相同輸入產生 100% 完全相同的 CSV 報表。

#### (3) 工單 E3: 戰鬥效能驗證 (`IntegrationPerfTests.cs`)
- **E3-1 ✅ & E3-2 ✅**：雙方各 50 單位滿額、900 秒（27,000 tick）戰鬥壓力測試：
  - 總耗時：**1,764.8 ms**（1.76 秒跑完 900 秒實時戰鬥）。
  - 平均每 Tick 耗時：**0.0654 ms**，僅佔效能預算（< 1.0 ms）之 **6.5%**（比預算快 15 倍以上）。

#### (4) 工單 E4: 存檔大小與速度測試 (`IntegrationPerfTests.cs`)
- **E4-1 ✅**：50 角色、200 筆轉蛋歷史、80 個成就追蹤、100 種道具大檔案情境：
  - 存檔大小：**68.10 KB**（69,734 bytes），遠低於 500 KB 限制。
  - 序列化與 Hash 耗時：**2.39 ms**（標準 < 50 ms）。
  - 原子替換寫入與 3 份備份輪替耗時：**2.92 ms**（標準 < 100 ms）。
  - 磁碟安全回讀與 Hash 驗證耗時：**16.20 ms**。

---

### 8. 里程碑 I：資料與工具管線（I1～I5 全項驗收完畢）

本階段完成所有企劃、美術與在地化工具管線，全套自動化測試由 300 項擴充至 **305 項綠燈通過**，包含完整陽性對照：

#### (1) 工單 I1: 主資料驗證 CLI (`tools/ValidateData` & `DataPipelineTests.I1`)
- **實作產物**：[`tools/ValidateData/Program.cs`](file:///F:/game_dev/tools/ValidateData/Program.cs)、[`ValidateData.csproj`](file:///F:/game_dev/tools/ValidateData/ValidateData.csproj)。
- **驗證成果**：
  - 正向路徑：執行 `dotnet run --project tools/ValidateData -- data/master`，成功解析 50 角色、10 關卡、10 地點、8 場景、2 卡池、8 成就，Exit Code = 0。
  - 陽性對照：卡池 pickup 設定為不存在之角色時，校驗器精確捕捉並返回非 0 狀態碼（Exit Code = 1）。

#### (2) 工單 I2: 主資料同步工具 (`MasterDataSync.cs` & `DataPipelineTests.I2`)
- **實作產物**：[`client/Assets/Game/Unity/Editor/MasterDataSync.cs`](file:///F:/game_dev/client/Assets/Game/Unity/Editor/MasterDataSync.cs)。
- **驗證成果**：
  - 同步前強制執行 `MasterDataLoader` + `MasterDataValidator`，遇錯誤立即中止拷貝。
  - 拷貝至 `StreamingAssets/MasterData` 後自動清理目的端已被廢棄刪除之舊 JSON，維持資料一致性。

#### (3) 工單 I3: 資產 Manifest 匯入與隔離工具 (`tools/ImportAssets` & `DataPipelineTests.I3`)
- **實作產物**：[`client/Assets/Game/Core/Assets/AssetImportService.cs`](file:///F:/game_dev/client/Assets/Game/Core/Assets/AssetImportService.cs)、[`tools/ImportAssets/Program.cs`](file:///F:/game_dev/tools/ImportAssets/Program.cs)。
- **驗證成果**：
  - **實體分流硬隔離**：全年齡/R15 資產匯入主程式 Content 目錄；R18 資產**絕對不進入主程式目錄**，嚴格隔離導向 DLC 外部目錄。
  - 陽性對照：`tier2_tease/` 標註為 G 級時，嚴格觸發分級下限錯誤並拒絕匯入。

#### (4) 工單 I4: 劇本驗證 CLI (`tools/ValidateStory` & `DataPipelineTests.I4`)
- **實作產物**：[`tools/ValidateStory/Program.cs`](file:///F:/game_dev/tools/ValidateStory/Program.cs)、標準劇本範本 [`data/story/prologue.txt`](file:///F:/game_dev/data/story/prologue.txt)、[`data/story/encounter_park.txt`](file:///F:/game_dev/data/story/encounter_park.txt)。
- **驗證成果**：
  - 自動遞迴掃描目錄下所有劇本，執行 6 大靜態檢查（未定義跳轉、無效指令、角色存在性等）。
  - 陽性對照：非法指令（如 `drop_database`）或跳轉至不存在之節點時精確報錯。

#### (5) 工單 I5: 多語言字串匯出入工具 (`tools/LocalizationTool` & `DataPipelineTests.I5`)
- **實作產物**：[`client/Assets/Game/Core/Localization/LocalizationCsvTool.cs`](file:///F:/game_dev/client/Assets/Game/Core/Localization/LocalizationCsvTool.cs)、[`tools/LocalizationTool/Program.cs`](file:///F:/game_dev/tools/LocalizationTool/Program.cs)。
- **驗證成果**：
  - 離線雙向匯出入 CSV 無損保留所有語系資料。
  - 佔位符比對：跨語言驗證 `{0}`、`{1}` 格式參數一致性；陽性對照將佔位符改為 `{1}` 時精確被校驗器攔截。

---


### 9. 里程碑 H：Unity 表現層框架（H1～H12 全項完工驗收）

本階段完成表現層核心架構、導航堆疊、MVP Presenters、ADV 播放器、音效與共用 UI 模組，測試基線擴充至 **315 項全數綠燈通過**：

#### (1) 工單 H5: 堆疊導航系統 (`ScreenNavigator.cs` & `ScreenNavigatorTests.cs`)
- **實作產物**：[`client/Assets/Game/Core/UI/IScreen.cs`](file:///F:/game_dev/client/Assets/Game/Core/UI/IScreen.cs)、[`client/Assets/Game/Core/UI/ScreenNavigator.cs`](file:///F:/game_dev/client/Assets/Game/Core/UI/ScreenNavigator.cs)。
- **驗證成果**：
  - 生命週期嚴密分發：`Push`（前一畫面 `OnPause`、新畫面 `OnPush`）、`Pop`（頂層 `OnPop`、恢復層 `OnResume`）、`Replace`、`PopToRoot`。
  - **轉場鎖定（陽性對照）**：在 `BeginTransition()` 期間，任何 Push、Pop、Replace 以及 Esc 返回操作一律被拒絕並阻擋。
  - **返回鍵消費與保護**：當子浮動面板打開時，`OnBackRequested()` 自行消費返回鍵而不 Pop；最底層 Root 畫面受到保護不被自動退出。

#### (2) 工單 H6: MVP 畫面表現層架構樣板 (`MvpBase.cs`)
- **實作產物**：[`client/Assets/Game/Core/UI/MvpBase.cs`](file:///F:/game_dev/client/Assets/Game/Core/UI/MvpBase.cs)。
- **驗證成果**：
  - Presenter 僅依賴 `GameCore` 介面與純 C# View 介面，完全無 UnityEngine 依賴，保證 100% 可於 .NET 離線執行單元測試。
  - `ScreenBase<TView, TPresenter>` 統一銜接 `IScreen` 生命週期與 View 可見性控制。

#### (3) 工單 H8: ADV 劇情播放器 Presenter (`StoryPresenter.cs`)
- **實作產物**：[`client/Assets/Game/Core/Story/StoryPresenter.cs`](file:///F:/game_dev/client/Assets/Game/Core/Story/StoryPresenter.cs)。
- **驗證成果**：
  - 協同 `StoryPlaybackController` 進行台詞推進與自動記錄已讀。
  - **Backlog 回溯**：完整收集台詞歷史堆疊（角色名稱、台詞內容、語音 ID）。
  - **選項分支推進與防護**：選項顯示時阻擋直接 Advance 快進，必須點選選項推進；支援快照儲存與讀取（`CreateSnapshot` / `RestoreSnapshot`）。
  - **AutoPlay 與 FastForward 互斥切換**；支援 UI 隱藏（全螢幕觀賞 CG）與恢復。

#### (4) 工單 H9: 主大廳與換裝 Presenter (`Presenters.cs`)
- **實作產物**：[`client/Assets/Game/Core/UI/Presenters.cs`](file:///F:/game_dev/client/Assets/Game/Core/UI/Presenters.cs)（`LobbyPresenter`, `WardrobePresenter`）。
- **驗證成果**：
  - 時段判斷（早 6-12、午 12-18、夕 18-21、夜 21-6）動態切換背景。
  - 結合 `PortraitResolver` 解析立繪，點擊看板娘觸發問候。
  - 換裝與立繪階段切換，陽性對照未持有角色時精確返回 `character_not_owned`。

#### (5) 工單 H10: 轉蛋、角色詳情與成就 Presenter (`Presenters.cs`)
- **實作產物**：[`client/Assets/Game/Core/UI/Presenters.cs`](file:///F:/game_dev/client/Assets/Game/Core/UI/Presenters.cs)（`GachaPresenter`, `CharacterDetailPresenter`, `AchievementPresenter`）。
- **驗證成果**：
  - 單抽與十連抽連動 `GachaResultEntry`；非法卡池返回 `banner_not_found`。
  - 角色升級、覺醒突破、階級進階連動，進階時即時回報稀有度與階級字串（`R+1` ~ `UR+5`）。
  - 成就清單展示與獎勵領取。

#### (6) 工單 H11: 共用元件 (`CommonComponents.cs`)
- **實作產物**：[`client/Assets/Game/Core/UI/CommonComponents.cs`](file:///F:/game_dev/client/Assets/Game/Core/UI/CommonComponents.cs)。
- **驗證成果**：
  - `DialogManager`：Alert / Confirm 隊列式彈窗管理，支援確定/取消回呼，絕不調用作業系統原生對話框。
  - `ToastService` **陽性對照**：在劇情播放中（`IsInStoryPlayback = true`）Toast 暫存於佇列不干擾演出，退出劇情時批次釋放。
  - `RedDotService`：精確計算未領取之成就紅點。
  - `CurrencyBarPresenter`：頂部金幣、免費鑽石、付費鑽石、體力即時同步。

#### (7) 工單 H12: 音效與音樂服務 (`AudioService.cs`)
- **實作產物**：[`client/Assets/Game/Core/Audio/AudioService.cs`](file:///F:/game_dev/client/Assets/Game/Core/Audio/AudioService.cs)。
- **驗證成果**：
  - 音量乘算邏輯：`EffectiveVolume = MasterVolume * TrackVolume`。
  - 與 `SettingsState` 即時連動；BGM 相同時不重複播放；支援 SE 與 Voice 播放。

---

### 10. 里程碑 F：Unity 技術驗證成果 (F1, F2, F3, F4, F9 通過)

本機成功安裝並配置 Unity 6 LTS 官方環境，實現純 C# 核心與 Unity 6 LTS 雙引擎無縫接軌與全套自動化測試通過，並順利建置出 Windows Standalone 開發版：

#### (1) 工單 F1: Unity 6 LTS 環境與套件初始化
- **實作產物**：
  - `client/ProjectSettings/ProjectVersion.txt`（設定版本為 `6000.6.5f1`）。
  - `client/Packages/manifest.json`：配置 `com.unity.nuget.newtonsoft-json` (3.2.1) 與 `com.unity.test-framework` (1.4.5) 等必要依賴，排除廢棄的 VR 模組。
- **驗證成果**：
  - 透過 Unity CLI 執行批次模式編譯（`-batchmode -quit`），Console 回傳 **Exit Code = 0，0 個編譯錯誤**。
  - 核心組件邊界隔離防護：`Game.Domain` 與 `Game.Core` 維持 `"noEngineReferences": true`，確認純 C# 領域層與業務邏輯完全未沾染 UnityEngine。
  - 透過 `MasterDataSync.Sync` 自動同步驗證 11 份 MasterData JSON 至 `client/Assets/StreamingAssets/MasterData/`。

#### (2) 工單 F2: Unity Test Runner 雙引擎對齊驗證
- **實作產物**：
  - `client/Assets/Game/Tests/Game.Tests.asmdef`（配置 `"Editor"` 平台與 NUnit 參照）。
  - 修復跨 asmdef 擴充方法可見性（`DictExt` 宣告為 `public`）、解耦 `CountStars` 與拆解單獨斷言。
- **驗證成果**：
  - Unity CLI 批次執行 Test Runner EditMode：
    ```bash
    "C:\Program Files\Unity\Hub\Editor\6000.6.5f1\Editor\Unity.exe" -batchmode -projectPath "F:\game_dev\client" -runTests -testPlatform EditMode -testResults "C:\Users\typa3\agent_tmp\game_dev\editmode_test_results.xml" -logFile "C:\Users\typa3\agent_tmp\game_dev\unity_test.log"
    ```
  - **測試結果**：**328 項測試 100% 全數通過，0 項失敗、0 項略過**（包含 F3/F4 PlayMode 測試）。
  - 與 .NET 8 CLI (`dotnet test`) 之 326 項通用測試維持 100% 行為與數值一致。

#### (3) 工單 F3 & F4: Play 模式除錯面板與戰鬥結算整合測試
- **實作產物**：[`client/Assets/Game/Tests/UnityIntegrationPlayModeTests.cs`](file:///F:/game_dev/client/Assets/Game/Tests/UnityIntegrationPlayModeTests.cs)
- **驗證成果**：
  - **F3-1 ✅（啟動與主循環）**：`GameBootstrap.Boot()` 順利啟動並注入 `GameServices`；執行十連抽、角色等級強化、地點巧遇事件，所有狀態流轉正常。
  - **F3-2 ✅（原子存檔與重讀驗證）**：觸發存檔原子寫入 `persistentDataPath/saves/profile.json`；模擬重新開機載入 Profile，驗證持有人員、卡片、等級、歷史紀錄前後 100% 吻合。
  - **F4-1 ✅（戰鬥推進）**：`BattleRunner` 啟動 `st_1_01` 關卡，模擬推進 1 個 tick 直到勝利。
  - **F4-2 ✅（結算一致性）**：`SubmitBattle` 結算報告結果與即時戰鬥勝負 100% 相同，獲得 0～3 星星級評定與關卡首通獎勵。

#### (4) 工單 F7: Addressables 分級 Group 隔離審計與 CI 建置閘門
- **實作產物**：[`client/Assets/Game/Core/Assets/AddressablesCatalogAuditor.cs`](file:///F:/game_dev/client/Assets/Game/Core/Assets/AddressablesCatalogAuditor.cs)、[`client/Assets/Game/Tests/AddressablesCatalogAuditorTests.cs`](file:///F:/game_dev/client/Assets/Game/Tests/AddressablesCatalogAuditorTests.cs)
- **驗證成果**：
  - **F7-1 ✅（分級規則）**：嚴格劃分 `content_g`、`content_r15`（主程式本體 Catalog）與 `content_r18`（成人 DLC Catalog）。
  - **F7-2 ✅（乾淨本體）**：主程式 Catalog 掃描結果 100% 無任何 R18 條目、無 R18 目錄素材，完全合法通過審計。
  - **F7-3 ✅（陽性對照）**：故意將 R18 評級素材注入 `content_g`、或將 `content_r18` 群組混入本體 Catalog、或素材帶有 `nsfw` 標籤時，CI 閘門立即觸發 `ERR_R18_KEY_IN_BASE_CATALOG` / `ERR_R18_FOLDER_IN_BASE_CATALOG`，強制拋出例外中斷建置！

#### (5) 工單 F9: Windows Standalone 遊戲建置
- **實作產物**：[`client/Assets/Game/Unity/Editor/BuildScript.cs`](file:///F:/game_dev/client/Assets/Game/Unity/Editor/BuildScript.cs)
- **驗證成果**：
  - 自動生成包含 `GameBootstrap`、`DebugMenu`、`BattleRunner`、`BattleDebugView` 之 [`Assets/Scenes/Boot.unity`](file:///F:/game_dev/client/Assets/Scenes/Boot.unity) 開發場景。
  - 成功輸出 Windows 64-bit Standalone 可執行檔：
    - **輸出路徑**：`F:\game_dev\build\Windows\GameClient.exe`
    - **檔案大小**：**154 MB**（161,788,253 bytes），建置耗時 37.93 秒。
  - 啟動測試：在 Windows 批次無圖形模式下啟動 `GameClient.exe` 執行正常，**Exit Code = 0**。

#### (6) 工單 F10: 多螢幕比例與解析度自適應
- **實作產物**：[`client/Assets/Game/Core/UI/ResolutionLayout.cs`](file:///F:/game_dev/client/Assets/Game/Core/UI/ResolutionLayout.cs)、[`client/Assets/Game/Unity/UI/ResolutionAdapter.cs`](file:///F:/game_dev/client/Assets/Game/Unity/UI/ResolutionAdapter.cs)、[`client/Assets/Game/Tests/ResolutionTests.cs`](file:///F:/game_dev/client/Assets/Game/Tests/ResolutionTests.cs)
- **驗證成果**：
  - **F10-1 ✅（標準 16:9）**：1080p（1920x1080）與 4K（3840x2160）維持基準解析度，虛擬畫布完全貼合，所有 UI 元素零裁切。
  - **F10-2 ✅（Steam Deck 16:10）**：1280x800（Steam Deck）與 1920x1200 自動判定為 `Narrow16x10`，將 CanvasScaler 調整為 `Match Width (0.0)`，畫布高度垂直延伸至 1200，安全框置中位移 `Y = 60`，左右兩側功能按鈕（出擊、選單、幣種）絕對不被裁切。
  - **F10-3 ✅（陽性對照）**：驗證若在 16:10 錯誤使用 Naive Match Height (1.0)，畫布寬度縮減至 1728，右側邊界 X=1800 之按鈕必定超出裁切；自適應佈局成功消除裁切問題。
  - **F10-4 ✅（超寬螢幕 21:9）**：2560x1080 與 3440x1440 自動判定為 `UltraWide21x9`，採用 `Match Height (1.0)`，背景寬度全滿延伸至 2560 無黑邊，16:9 核心內容區塊水平置中（OffsetX = 320）。

---

## 後續 AGENT 協同開發與待辦事項 (Backlog)

### 里程碑狀態總覽
- **A 核心領域（A1～A11）**：11/11 全項完成 ✅
- **B 存檔系統（B1～B5）**：5/5 全項完成 ✅
- **C 劇情系統（C1～C7）**：7/7 全項完成 ✅
- **D 橫切服務（D1～D6）**：6/6 全項完成 ✅
- **E 整合驗證（E1～E4）**：4/4 全項完成 ✅ —— **遊戲循環在邏輯上完全可行！**
- **I 資料與工具管線（I1～I5）**：5/5 全項完成 ✅ —— **企劃與美術可獨立產出與驗證內容！**
- **H Unity 表現層框架（H1～H12）**：12/12 全項完成 ✅ —— **表現層框架完備，純 C# 隔離！**
- **F Unity 技術驗證（F1～F10）**：**7/10 已完成**（F1, F2, F3, F4, F7, F9, F10 通過 ✅）

### 下階段推進待辦項目（里程碑 F 剩餘 3 項）
1. **F6: Yarn Spinner 整合與 IStoryRunner 實作**
   - 將標準劇本轉為 Yarn 腳本，串接 `IStoryRunner` 介面，驗證雙語言 Line ID 切換與選項跳轉行為與 MiniScript 一致。
2. **F8: Steamworks.NET 平台初始化驗證**
   - 引入 Steamworks.NET，以測試 AppID 480 進行離線/本機初始化測試，串接 `IPlatformService`。
3. **F5: Live2D Cubism SDK 整合**
   - 待使用者授權下載 Cubism 5 SDK package 後，掛載官方範例模型量測 1 個與 3 個模型之 FPS 與記憶體消耗。

