# 17 框架工作清單與查核點（詳細版）

> 目標只有一個：**建立整個遊戲框架，並確保設計可行**。
> 版本 v0.2｜2026-10-14
>
> **狀態**：✅ 已完成並驗證｜🟡 已撰寫、尚未驗證｜⬜ 未開始
> **執行者**：**C** = Claude（在 .NET 實際執行）｜**G** = Gemini 撰寫測試（使用者已同意），由 C 執行並回到原始碼核實｜**S** = 本機 Claude 子 agent 撰寫，由 C 核實｜**U** = 需要使用者動作
>
> **全域查核規則（每一項都適用）**
> 1. 每個查核點都要對應到**可以重跑的證據**：測試名稱，或指令加上輸出。
> 2. 錯誤路徑要有**陽性對照**：故意做錯，必須被抓到。只看到「成功」不算驗證。
> 3. **命令失敗時，狀態完全不變**：不扣資源、不寫入、不發佈事件。
> 4. 所有隨機都透過注入的 `IRandom`；測試使用固定 seed。
> 5. 原始碼符合 C# 9，而且 Domain、Core 不引用 UnityEngine（由 asmdef 的 `noEngineReferences` 強制）。
> 6. 每個段落結束都要做 G 區的文件與交接。
>
> **主要驗證指令**：`DOTNET_CLI_TELEMETRY_OPTOUT=1 dotnet test F:/game/client/DotnetTests`

---

## 進度總覽

| 區塊 | 項目數 | ✅ | 🟡 | ⬜ | 達成後證明什麼 |
|---|---|---|---|---|---|
| A 核心領域 | 11 | 11 | 0 | 0 | 遊戲規則的邏輯正確 |
| B 存檔 | 5 | 5 | 0 | 0 | 進度不會遺失、不會倒退 |
| C 劇情系統 | 7 | 7 | 0 | 0 | 多重結局、讀檔改走分支可行 |
| D 橫切服務 | 6 | 6 | 0 | 0 | 多語言、平台、資產分級可行 |
| E 整合驗證 | 4 | 4 | 0 | 0 | **整個遊戲循環在邏輯上可行** |
| H Unity 表現層框架 | 12 | 12 | 0 | 0 | 畫面框架可以承載所有系統 |
| I 資料與工具管線 | 5 | 5 | 0 | 0 | 企劃和美術可以獨立產出內容 |
| F Unity 技術驗證 | 10 | 7 | 0 | 3 | **在技術上可行**（Live2D、Steam、建置） |
| G 文件與交接 | 4 | — | — | — | 持續執行 |

**建議順序**：A、B 補測試 → C → D → E（**邏輯上可行**的里程碑）→ I → H → F（**技術上可行**的里程碑，需要先安裝 Unity）

---

## A 核心領域（`client/Assets/Game/Domain/`，純 C#）

### A1 共用基礎（Common、Events）　✅　G→C（任務 A1，20/20，陽性對照：A1-6）
- **工作細項**
  - [x] `Rating`、`Rarity`（N < R < SR < SSR）、`Result`、`Result<T>`、`ItemStack`
  - [x] `IRandom` 和 `SplitMix64Random`；`IClock` 和 `SystemClock`；`RatingPolicy`
  - [x] `DomainEvent`、`Ev` 事件名稱常數、`EventBus`（佇列式派送）
  - [x] 單元測試 `CommonTests.cs`、`EventBusTests.cs`（Gemini 實作，Claude 驗收）
- **查核點**
  - A1-1：同一個 seed 產生的 1,000 個數字完全相同；不同 seed 至少有 1 個數字不同。
  - A1-2：`NextDouble` 的值都落在 [0,1)；`NextInt(n)` 的值都落在 [0,n)；`NextInt(0)` 丟出例外。
  - A1-3 ✅：`Rarity.Rank("UR") == 4`；未知稀有度丟出例外。
  - A1-4：`RatingPolicy(R18, G).Effective == G`，`(G, R18).Effective == G`。
  - A1-5：EventBus 的處理器在處理事件 E1 時發佈 E2，所有處理器都先收到 E1，再收到 E2（不會巢狀派送）。
  - A1-6：處理器丟出例外後，佇列被清空；下一次發佈不會送出舊事件。
  - A1-7：處理器在派送途中取消訂閱，不會造成集合修改例外。

### A2 主資料載入與驗證器（MasterData、Core/MasterDataLoader）　✅　（任務 A2，62/62 通過，陽性對照：A2-5-09）
- **工作細項**
  - [x] 所有 Def 類別、`MasterDatabase.Build`（重複 ID 會丟出例外）、`StagesOf`
  - [x] `MasterDataLoader`（開啟 MissingMemberHandling.Error；缺少 config.json 時丟出例外）
  - [x] `MasterDataValidator`（交叉引用、機率、分級、條件語法、系列順序）
  - [x] 單元測試 `MasterDataValidatorTests.cs`、`MasterDataLoaderTests.cs`（Gemini 實作與驗收）
- **查核點**
  - A2-1 ✅：`data/master` 範例資料驗證後 0 個錯誤（`SmokeTests.SampleMasterData_LoadsAndValidates`）。
  - A2-2 ✅：JSON 中的數字 `30` 能讀進條件的字串欄位 `"30"`。
  - A2-3 ✅：JSON 欄位名稱打錯（例如 `"rarty"`），載入時丟出 `InvalidDataException`，訊息包含檔名；缺少 config.json 丟出 `FileNotFoundException`；未改動對照組正常載入。
  - A2-4 ✅：兩筆資料的 ID 重複時，Build 丟出例外；CharacterDef / StageDef 重複測試；全 null 正常建立對照組。
  - A2-5 ✅：以下每條規則各準備 1 筆錯誤資料，驗證器都會回報對應錯誤（31 個變異案例與 4 個陰性對照全數通過）：
    - 立繪階段不存在、屬於其他角色、是 R18
    - 第一個立繪階段有解鎖條件，或分級不是 G
    - 卡池機率總和不等於 1、有機率但池子是空的、池內角色的稀有度不符、pickup 角色不在池中
    - 關卡引用不存在的敵人、`unlocksOnClear` 既不是關卡也不是地點、未知的星級條件
    - 巧遇事件的地點、角色、服裝不存在；系列順序不是 1..N 連續
    - 角色的 obtainableBy 出現 gacha、initial 以外的值（巧遇不招募角色）✅ `EncounterNoRecruitTests.Validator_RejectsEncounterAsObtainSource`
    - 成就未知的 counter、缺少 event 或 field、R18 成就有 steamApiName
    - 條件 DSL 有未知的 type、`all` 沒有 `of`、引用不存在的角色或關卡或成就
    - config.initial 引用不存在的 ID
  - A2-6 ✅：錯誤訊息格式統一為「`<類型> <id>: <原因>`」，方便企劃定位問題（以正規表示式全數比對通過）。

### A3 通用條件 DSL（Conditions）　✅　（任務 A3，100/100 通過，陽性對照：A3-5）
- **工作細項**
  - [x] 16 種 type 的評估器，以及 `TimeOfDay` 時段分界
  - [x] 單元測試 `ConditionEvaluatorTests.cs`（Gemini 實作與驗收）
- **查核點**
  - A3-1 ✅：每種 type 都至少有 1 個成立、1 個不成立的案例（16 種 × 2，共 16 個獨立測試覆蓋）。
  - A3-2 ✅：`null` 條件視為成立；`all` 的 of 為空或 null 時成立；`any` 的 of 為空或 null 時不成立。
  - A3-3 ✅：`char: "self"` 依 `ctx.SelfChar` 判定；角色未持有時，所有角色類條件都不成立。
  - A3-4 ✅：`story_flag` 優先讀劇情存檔欄位的變數，找不到再讀全域旗標，都沒有時視為 0；六種比較運算子（>=, >, <=, <, ==, != 以及 null 預設 >=）全數測試；未知的 op 丟出 `InvalidOperationException`。
  - A3-5 ✅：時段分界：4:59 是 night、5:00 是 morning、10:59 是 morning、11:00 是 day、16:59 是 day、17:00 是 evening、19:59 是 evening、20:00 是 night（TestCase 逐時段邊界覆蓋）。
  - A3-6 ✅：未知的 type 丟出 `InvalidOperationException`。
  - A3-7 ✅：`rating_allowed` 在 G 政策下對 R18 回傳 false，在 R18 政策下回傳 true。

### A4 養成（Growth）　✅　（任務 A4，117/117 通過，陽性對照：A4-5）
- **工作細項**
  - [x] `LevelCap`、`ExpToNext`、`AddExp`、`ExpToCap`、`AwakenCost`、`ComputeStats`、`AffectionLevel`、`RefreshPortraitUnlocks`
  - [x] 單元測試 `GrowthRulesTests.cs`（涵蓋 A4-1～A4-8）
- **查核點**
  - A4-1 ✅：SSR Lv1、Lv10、Lv50 經驗值分別為 35、1242、15048。
  - A4-2 ✅：注入 350 經驗升 3 級至 Lv4，剩餘 21 經驗。
  - A4-3 ✅：★0 從 Lv1 注入大量經驗升至 Lv20；經驗歸零、超額捨棄；上限後再次增加經驗不升級。
  - A4-4 ✅：確認覺醒碎片消耗與 ★1 Lv30 上限；覺醒後可繼續升級。
  - A4-5 ✅：基礎值與 Lv30/★2/S2 手算值相符；大數案例精確得到 HP 169520000、ATK 16952000。故意將等級倍率中間型別改成 int 時，測試失敗（陽性對照）。
  - A4-6 ✅：好感門檻 0、99、100、299、300、599、600、999、1000、2500 全數對應預期等級。
  - A4-7 ✅：`ps_char01_s2` 僅在 Lv30 且 ★2 解鎖；Lv29/★2 與 Lv30/★1 不解鎖；重複呼叫不重複回傳。
  - A4-8 ✅：G 政策不解鎖 R18 階段；放寬至 R18 後可補解鎖。

### A5 轉蛋（Gacha）　✅　（任務 A5，126/126 通過，陽性對照：A5-2）
- **工作細項**
  - [x] `Pull`（天井、十連保底、pickup）、`DisplayRates`
  - [x] 單元測試 `GachaEngineTests.cs`（Gemini 實作與驗收）
- **查核點**
  - A5-1 ✅：**統計**：用固定 seed 抽 1,000,000 次，SSR (0.03)、SR (0.12)、R (0.85) 的比率全數落在理論值 99.9% 信賴區間內（z = 3.29）。
  - A5-2 ✅：**天井**：pity 從 0 開始，第 100 抽觸發 SSR 天井並標記 `ByPity = true`；觸發後 pity 歸零。
  - A5-3 ✅：自然抽到 SSR 時，pity 亦立即歸零。
  - A5-4 ✅：**十連保底**：前 9 抽均為 R 時，第 10 抽提升為 SR 並標記 `ByTenGuarantee = true`；前 9 抽已有 SR 時不觸發保底。
  - A5-5 ✅：單抽不觸發十連保底（連續 10 次單抽全 R 不升級）。
  - A5-6 ✅：**pickup**：share = 0.5 時抽 10 萬次，pickup 角色比例落在 0.5 ± 3.29σ 內；池中只有 pickup 角色時必定抽中 pickup。
  - A5-7 ✅：`DisplayRates` 所有角色機率加總為 1（誤差 < 1e-9），且各角色機率與手算值完全相符。
  - A5-8 ✅：機率總和略小於 1（浮點誤差）時，落在缺口之亂數正常回退指派給最低稀有度 R，不丟出例外。
  - A5-9 ✅：池子為空時，在修改 pity 或消耗亂數前拋出 `InvalidOperationException`（訊息包含 `empty pool`）。

### A6 巧遇（Encounter）　✅　（任務 A6，136/136 通過，陽性對照：A6-1）
- **工作細項**
  - [x] `Select`、`Candidates`、`NextOrderInSeries`、`WeightedPick`、`RegenStamina`
  - [x] 單元測試 `EncounterEngineTests.cs`（Gemini 實作與驗收）
- **查核點**
  - A6-1 ✅：系列事件只會出現「下一段」：未看過第 1 段時第 2 段不出現，看過第 1 段後推進至第 2 段。
  - A6-2 ✅：不可重複事件（`repeatable: false`）看過後不再出現；可重複事件持續出現。
  - A6-3 ✅：分級不允許（G 政策過濾 R18 事件）與條件不成立之事件不會出現。
  - A6-4 ✅：**加權**：抽 10 萬次，各事件選中比例符合權重（99.9% 信賴區間 3.29σ 內）。
  - A6-4b ✅：未持有角色的事件不會出現，持有後才出現（`Candidates_ExcludeEventsOfUnownedCharacters`）。
  - A6-5 ✅：**保底**：MissStreak 達門檻（5）且有系列事件時必選中系列事件；選中後歸零；選中非系列時 +1。
  - A6-6 ✅：無候選事件時回傳 null，MissStreak +1。
  - A6-7 ✅：**體力**：0 點經過 29 分鐘仍為 0，30 分鐘變 1；75 分鐘得 2 點並保留 15 分鐘零頭；10 小時滿 5 點上限。
  - A6-8 ✅：系統時間調回過去時不回復體力，基準點重設為調校後的時間。
  - A6-9 ✅：同一個 seed 和同一組資料，挑選結果絕對一致（候選事件依 ID 排序）。

### A7 戰鬥模擬（Battle）　✅　（任務 A7，149/149 通過，陽性對照：A7-9 特性增傷）
- **工作細項**
  - [x] `UnitTemplate.From`、`BattleSimulator`（tick 流程、指令、wave、擊退、據點砲、勝負）、`BattleSimulator.Run`
  - [x] 單元測試 `BattleSimulatorTests.cs`（Gemini 實作與驗收，149/149 通過）
  - [x] 黃金回放檔 `Tests/Golden/battle_st_1_01_seed7.json`（決定性回歸驗證通過）
- **查核點**
  - A7-1 ✅：即時模擬和回放的結果（勝負、tick、據點 HP）完全一致。
  - A7-2 ✅：`SubmitBattle` 重新模擬後的勝負，和即時模擬相同。
  - A7-3 ✅：**出兵**：能量不足、冷卻中、空的格子、單位數已滿時，`CanDeploy` 回傳 false，指令被忽略，而且不記錄到 `Recorded`。
  - A7-4 ✅：出兵後扣除能量、開始冷卻；冷卻 tick 數 = round(cooldownSec × 30)。
  - A7-5 ✅：**能量**：每個 tick 回復 regen × 1000 / 30（milli），不超過上限；升級後上限和回復速度都改變；已到最高等級時無法升級。
  - A7-6 ✅：**移動和接觸**：雙方相向前進，進入射程後停止移動並開始攻擊。
  - A7-7 ✅：**攻擊**：單體攻擊只打最近的目標；範圍攻擊打射程內的所有目標；兩次攻擊間隔 = attackIntervalSec × 30 個 tick。
  - A7-8 ✅：**擊退**：knockbacks = 3 的單位，HP 每跨過 1/3 就被擊退一次，總共最多 3 次（死亡不算擊退）；被擊退後退 60 單位、硬直 15 個 tick。
  - A7-9 ✅：**特性**：`anti_flying` 打 `flying` 類型時傷害 ×1.5（整數運算）。（陽性對照已驗證）
  - A7-10 ✅：**據點砲**：充能未滿時無法發射；發射後射程內的敵人受傷並被擊退，充能歸零。
  - A7-11 ✅：**wave**：依 atSec、interval、count 生成；repeatEverySec 會讓同一波重複出現；`enemy_base_hp_below_pct` 在敵方據點 HP 低於門檻時才觸發，而且只觸發一次；敵方單位數已滿時延後生成。
  - A7-12 ✅：**勝負**：敵方據點 HP 歸零就獲勝（同一個 tick 內獲勝優先於失敗）；我方據點 HP 歸零就失敗；達到時間限制或 maxBattleSec 就判定 Timeout。
  - A7-13 ✅：**竄改防護**：在 BattleReport 裡插入不合法的指令（能量不足時出兵、不存在的格子），回放時會被忽略，結果和沒有竄改時相同。
  - A7-14 ✅：**黃金檔回歸**：重新模擬黃金檔，結果與記錄完全一致。主資料改版時，要有意識地重新產生黃金檔（流程寫在 README）。

### A8 成就追蹤（Achievements）　✅　（任務 A8，157/157 通過，陽性對照：UnlockedAtUtc 設值）
- **工作細項**
  - [x] `AchievementTracker`（OnEvent、Reevaluate、MatchesFilter）
  - [x] 單元測試 `AchievementTrackerTests.cs`（A8-1～A8-8，8/8 通過）
- **查核點**
  - A8-1 ✅：`sum`：抽 1 次加 1、十連加 10；累計到 100 時解鎖 `ach_gacha_100`。
  - A8-2 ✅：`count`：只計算符合 filter 的事件（`outcome = Win`）。
  - A8-3 ✅：`distinct`：同一個關卡 3 星通關兩次只算 1；三個不同關卡才達到 target 3。
  - A8-4 ✅：`state`：事件後條件成立就解鎖；指定 event 時，只有該事件會觸發判斷。
  - A8-5 ✅：filter 的 `st_1_*` 能比對 `st_1_02`，不會比對 `st_2_01`；事件缺少 filter 指定的欄位時不比對。
  - A8-6 ✅：已解鎖的成就不會再解鎖一次，也不會再發佈 `AchievementUnlocked`。
  - A8-7 ✅：分級不允許時，進度照常累積但不解鎖；政策放寬後由 Reevaluate 或下一次事件補上解鎖。
  - A8-8 ✅：`AchievementUnlocked` 事件透過佇列派送，可以用來觸發「解鎖 N 個成就」這類成就，而且不會造成重入問題。

### A9 造型決定（Portrait）　✅　（任務 A9，164/164 通過，陽性對照：劇情強制服裝覆蓋）
- **工作細項**
  - [x] `PortraitResolver.Resolve`
  - [x] 單元測試 `PortraitResolverTests.cs`（A9-1～A9-7，7/7 通過）
- **查核點**
  - A9-1 ✅：沒有服裝時，使用目前選用的立繪階段的 art。
  - A9-2 ✅：有已持有、分級允許的服裝時，使用服裝的 art。
  - A9-3 ✅：劇情強制服裝優先於玩家的服裝，而且不需要持有。
  - A9-4 ✅：服裝未持有、分級不允許、屬於其他角色、或缺少 requiresPortraitStage 時，退回立繪階段。
  - A9-5 ✅：選用的立繪階段分級不允許或尚未解鎖時，退回已解鎖、分級允許的最低階段。
  - A9-6 ✅：角色未持有（例如轉蛋預覽）時，回傳第一個立繪階段。
  - A9-7 ✅：主資料沒有 art 時，`Art == null`，不丟出例外（呈現層顯示 placeholder）。

### A10 GameCore 命令（Core/GameCore.cs）　✅　（任務 A10，173/173 通過，陽性對照：首通獎勵發放）
- **工作細項**
  - [x] NewPlayer、OnLoaded、PullGacha、LevelUp、Awaken、SetPortraitStage、AddAffection、SetTeam、EquipCostume、SetLobby、RefreshUnlocks、PrepareBattle、BuildSetup、SubmitBattle、StartEncounter、ResolveEncounter、MarkNodeRead、SetGlobalFlag、ReachEnding、UnlockCg、ClaimAchievement
  - [x] 單元測試 `GameCoreTests.cs`（每個命令都要測成功路徑和每一個錯誤碼，A10-1～A10-6 全數通過）
- **查核點**
  - A10-1 ✅：`NewPlayer` 依 config.initial 給予道具、角色、關卡、地點、場景、滿體力；初始角色已編入隊伍。
  - A10-2 ✅：下表每個錯誤碼都有測試，而且**失敗後 PlayerState 序列化的結果和失敗前完全相同**：

    | 命令 | 錯誤碼 |
    |---|---|
    | PullGacha | banner_not_found、invalid_count、banner_closed、cost_not_defined、insufficient_item |
    | LevelUp | character_not_owned、level_cap、no_material、invalid_material、insufficient_item |
    | Awaken | character_not_owned、awaken_max、insufficient_item |
    | SetPortraitStage | character_not_owned、stage_not_found、stage_locked、rating_not_allowed |
    | SetTeam | invalid_team、character_not_owned、duplicate_member |
    | EquipCostume | character_not_owned、costume_not_found、costume_locked、rating_not_allowed、portrait_stage_required |
    | SetLobby | scene_locked、rating_not_allowed、character_not_owned |
    | PrepareBattle、SubmitBattle | stage_not_found、stage_locked、character_not_owned |
    | StartEncounter | spot_not_found、spot_locked、encounter_pending、no_stamina |
    | ResolveEncounter | no_pending_encounter、invalid_choice |
    | ClaimAchievement | achievement_locked、already_claimed |

  - A10-3 ✅：轉蛋時新角色會加入（含預設服裝和 S1 立繪）；重複角色轉成碎片；★ 滿後轉成心之結晶；歷史紀錄最多保留 200 筆。
  - A10-4 ✅：LevelUp 正確扣除素材和金幣（同一種素材分成多筆時會合併計算）；升級後如果符合條件，會自動解鎖立繪階段，並發佈 `PortraitStageUnlocked`。
  - A10-5 ✅：SubmitBattle 獲勝時：首次通關獎勵只發一次、掉落依 IRandom 決定、解鎖下一關或地點、隊伍獲得經驗和好感；星數取歷史最高值。（陽性對照已驗證）
  - A10-6 ✅：SubmitBattle 失敗或 Timeout 時，除了發佈 BattleFinished 之外不給任何獎勵。
  - A10-7 ✅：Encounter：開始時扣 1 點體力並記錄 Pending；結算時套用 outcome（好感、道具、服裝），並標記為已看過；**角色數量不變** ✅（`ResolvingEncounter_NeverAddsCharacters`）。
  - A10-8 ✅：ReachEnding 只記錄一次；UnlockCg 重複呼叫不會重複發佈事件。
  - A10-9 ✅：每個成功的命令都讓 Dirty 變成 true。
  - A10-10 ✅：`OnLoaded` 在主資料新增立繪階段或成就後，能補上解鎖。

### A11 全稀有度進階與跨稀有度轉蛋（Rarity Progression & Grade CG Unlock）　✅　（任務 A11，4/4 通過，陽性對照：最高位階 UR+5 阻擋）
- **工作細項**
  - [x] `Rarity.Order` 加入 `"UR"`（Order: N, R, SR, SSR, UR），實作 `Rarity.Next` 與 `Rarity.IsMax`
  - [x] `BannerDef.AllowCrossRarity` 旗標與主資料校驗支援，新增全角色四階卡池 `banner_ur`
  - [x] `PlayerState.CharacterState` 擴充 `CurrentRarity`、`Tier` (0..5)、`DisplayGrade`
  - [x] `GameCore.AdvanceCharacter`：同角色卡片進階 +1..+5，滿 +5 後晉升下個稀有度 (R->SR->SSR->UR)
  - [x] `GameCore.UnlockGradeCg`：每次進階與升等自動生成 CG ID 並解鎖入圖鑑收藏
  - [x] `GalleryService.GetGroupedGallery`：動態納入玩家圖鑑中所有解鎖的進階與等級專屬 CG
  - [x] 單元測試 `RarityProgressionTests.cs`（涵蓋 4 大需求）
- **查核點**
  - A11-1 ✅：每位角色皆可從 R 起步，透過連續進階 +1～+5 晉升至 SR、SSR、UR。
  - A11-2 ✅：收集同角色卡片可順利進階 +1 至 +5，滿 +5 再次進階晉升至下一稀有度 +0；達 UR+5 時阻擋並回報 `max_grade`（陽性對照）。
  - A11-3 ✅：抽卡可依機率抽取 R (0.80)、SR (0.16)、SSR (0.03)、UR (0.01)；抽中更高稀有度直接自動晉升角色位階。
  - A11-4 ✅：解鎖每個等級與進階（如 `cg_char01_claire_r_plus_1`、`cg_char01_claire_sr`）皆自動納入玩家 CG 圖鑑收藏。

---

## B 存檔（`client/Assets/Game/Core/Persistence/`）

### B1 存檔封裝與 hash　✅　（任務 B1，陽性對照：Hash 篡改驗證）
- **查核點**
  - B1-1 ✅：Encode 後 Decode，PlayerState 的所有欄位都相同（序列化後的字串完全相同）。
  - B1-2 ✅：修改 payload 的 1 個字元，Decode 會丟出 `InvalidDataException("...hash mismatch...")`。（陽性對照已驗證）
  - B1-3 ✅：不是 JSON、format 不是 gsave、沒有 payload 時，都會丟出 `InvalidDataException`。
  - B1-4 ✅：存檔的 schemaVersion 比程式新時，拒絕讀取。
  - B1-5 ✅：只改變 JSON 的縮排，不影響 hash 驗證。
  - B1-6 ✅：HashSet、Dictionary、enum（以字串儲存）都能正確來回轉換。

### B2 原子寫入與輪替備份　✅　（任務 B2，陽性對照：bak1 備份缺失）
- **查核點**
  - B2-1 ✅：連續寫入 5 次後，存在 main、bak1、bak2、bak3，內容分別是第 5、4、3、2 次寫入。（陽性對照已驗證）
  - B2-2 ✅：寫入完成後不會留下 `.tmp` 檔。
  - B2-3 ✅：主檔損壞時，`ReadWithFallback` 回傳 bak1 的內容，並在 errors 中記錄主檔的錯誤原因。
  - B2-4 ✅：所有檔案都損壞時回傳 null，**而且所有檔案的內容和修改時間都沒有改變**。
  - B2-5 ✅：存檔資料夾不存在時會自動建立。

### B3 版本遷移　✅
- **工作細項**：建立示範用的遷移流程（測試內註冊 v0→v1 的 Migration），以及 `Tests/SaveFixtures/profile_v0.json`
- **查核點**
  - B3-1 ✅：v0 範例存檔遷移後，新欄位使用預設值，原有資料保留。
  - B3-2 ✅：缺少某一版的遷移函式時，丟出「no migration from schema N」。
  - B3-3 ✅：遷移規則寫進 README：「改 PlayerState 欄位，就要提高 CurrentSchemaVersion，並新增 Migration 和範例存檔」。

### B4 劇情存檔欄位（StorySlot）　✅
- **查核點**
  - B4-1 ✅：StorySlot 的變數（float、string、bool）、Staging、Memo 都能來回轉換，型別不變。
  - B4-2 ✅：存檔欄位命名規則：手動 `slot_001`～`slot_060`、快速 `quick_1`～`quick_3`、自動 `auto_1`～`auto_3`（輪替）。

### B5 雙層存檔整合　✅　（任務 B5，DualLayerSaveTests 3/3 通過）
- **查核點**
  - B5-1 ✅：在劇情存檔欄位 X 之後轉蛋、升級，再讀回 X：Profile（角色、等級、轉蛋紀錄、成就）**都沒有倒退**，只有劇情變數回到 X 的狀態。
  - B5-2 ✅：讀回 X 後走另一個分支：已達成的結局仍然保留，新結局也會加入。
  - B5-3 ✅：劇情存檔記錄的節點已經不存在時，退回該章節開頭，並回傳警告。

---

## C 劇情系統框架（多重結局的核心）

### C1 `IStoryRunner` 抽象介面　✅　（任務 C1，StoryRunnerTests 25/25 通過）
- **工作細項**
  - [x] `Core/Story/IStoryRunner.cs`：`Start(node)`、`Continue()`、`Choose(index)`、`CurrentLine`（lineId、說話者、文字 key）、`CurrentOptions`（含可選與否）、`IsEnded`、`Variables` 的讀寫、`Snapshot()`、`Restore(snapshot)`
  - [x] `Core/Story/MiniScript`：測試用的最小腳本格式和執行器，支援 line、option、set、if、jump、command，**不依賴 Yarn**，用來證明框架設計可行
  - [ ] 之後由 Yarn Spinner 實作同一個介面（F6）
- **查核點**
  - C1-1 ✅：同一個劇本選不同選項，會走到不同的結局節點。
  - C1-2 ✅：`set` 和 `if` 能讀寫劇情變數；條件可以呼叫遊戲狀態函式（`affection("char01_claire")`、`owned(...)`）。
  - C1-3 ✅：`Snapshot()` 後繼續推進，再 `Restore()`，能回到完全相同的行和變數。
  - C1-4 ✅：選項可以依條件隱藏或停用。

### C2 指令註冊表　✅　（任務 C2，StoryCommandRegistryTests 15/15 通過）
- **工作細項**：`StoryCommandRegistry`，註冊 bg、show、hide、expr、motion、cg、bgm、se、voice、affection、give、ending、wait、fade、shake；分成「狀態指令」（呼叫 GameCore）和「演出指令」（交給呈現層的 `IStoryPresenter`）。
- **查核點**
  - C2-1 ✅：使用未註冊的指令時，回報錯誤，包含行號和指令名稱。
  - C2-2 ✅：`affection`、`give`、`ending`、`cg` 一律透過 GameCore 結算，並發佈對應事件。
  - C2-3 ✅：演出指令只呼叫 `IStoryPresenter`；測試用的假 presenter 能記錄下完整的呼叫序列。
  - C2-4 ✅：指令參數錯誤（例如角色 ID 不存在）時回報錯誤，不會讓遊戲當掉。

### C3 分級 fallback　✅　（任務 C3，StoryRatingFallbackTests 4/4 通過）
- **查核點**
  - C3-1 ✅：在 G 政策下進入 `rating:R18` 節點時，自動改走 `fallback:` 指定的節點。
  - C3-2 ✅：R18 政策下正常進入該節點。
  - C3-3 ✅：`<<cg>>` 引用的 CG 分級不允許時，跳過 CG，劇情繼續。
  - C3-4 ✅：R18 節點沒有 fallback 時，被劇本驗證器（C5）擋下。

### C4 結局、已讀、圖鑑、回憶　✅　（任務 C4，StoryFeatureTests 4/4 通過）
- **查核點**
  - C4-1 ✅：讀過的行加入 `ReadNodes`；快進可以選擇「只快進已讀」。
  - C4-2 ✅：`<<ending>>` 寫入 Profile 一次，觸發 `EndingReached` 和成就。
  - C4-3 ✅：**回憶模式**：重播章節時使用暫時的變數副本，結束後 Profile 和所有存檔都沒有改變（獎勵類指令在回憶模式中不生效）。
  - C4-4 ✅：CG 圖鑑依角色分組，分級不允許的 CG 不會列出。

### C5 劇本驗證器　✅　（任務 C5，StoryValidatorTests 19/19 通過）
- **查核點**（每一項都要有一個故意寫錯的劇本做陽性對照）
  - C5-1 ✅：jump 的目標節點不存在。
  - C5-2 ✅：有台詞缺少 line ID，或 line ID 重複。
  - C5-3 ✅：引用的角色、CG、場景、結局 ID 不存在於主資料。
  - C5-4 ✅：R18 節點缺少 fallback。
  - C5-5 ✅：**可達性**：每個結局都至少有一條路徑可以到達；列出所有無法到達的節點。
  - C5-6 ✅：使用了未註冊的指令。

### C6 巧遇和劇情的串接　✅　（任務 C6，StoryIntegrationTests 3/3 通過）
- **查核點**
  - C6-1 ✅：`StartEncounter` 回傳的 `Story` 節點，能由 StoryRunner 播放。
  - C6-2 ✅：玩家的選項會對應到 outcomes 的 key（`choice0`、`choice1`），再呼叫 `ResolveEncounter`。
  - C6-3 ✅：劇情中途離開時，Pending 仍保留；下次進入同一個地點會繼續這個事件，不會重複扣體力。

### C7 角色路線開放規則　✅　（任務 C7，StoryIntegrationTests 2/2 通過）
- **查核點**
  - C7-1 ✅：角色路線的章節依好感等級開放（1–5 級對應第 1–5 章）。
  - C7-2 ✅：章節完成後會記錄下來，並開放下一章（前提是好感等級足夠）。

---

## D 橫切服務

### D1 `ILocalizationService`　✅　（任務 D1，LocalizationTests 6/6 通過）
- **工作細項**：`Core/Localization/`：讀取 `data/loc/<locale>/*.json`（key → 文字）、`Get(key, args)`、`SetLocale`、`LocaleChanged` 事件；驗證工具 `LocalizationValidator`
- **查核點**
  - D1-1 ✅：`loc:char/char01_claire.name` 在 zh-TW 和 en 下分別回傳正確文字。
  - D1-2 ✅：切換語言後觸發 `LocaleChanged`，再取文字時回傳新語言。
  - D1-3 ✅：目前語言缺少某個 key 時，退回 zh-TW，並記錄一次警告；zh-TW 也沒有時，回傳 `[key]`。
  - D1-4 ✅：`{0}`、`{1}` 格式化；佔位符數量和原文不一致時，被驗證器抓到。
  - D1-5 ✅：驗證器列出每種語言缺少的 key，以及主資料引用了但字串表中不存在的 key。

### D2 `IPlatformService`　✅　（任務 D2，PlatformServiceTests 3/3 通過）
- **工作細項**：`IPlatformService`（IsDlcInstalled、UnlockAchievement、GetLanguage）；`NullPlatformService`；`PlatformAchievementBridge`
- **查核點**
  - D2-1 ✅：只用 Null 實作時，所有系統都能運作，所有測試都通過。
  - D2-2 ✅：成就解鎖時，只把設定了 steamApiName 的成就送到平台；R18 成就永遠不送。
  - D2-3 ✅：同步設定為關閉時，完全不呼叫平台 API。

### D3 資產 manifest 與分級　✅　（任務 D3，AssetCatalogTests 5/5 通過）
- **工作細項**：`data/asset_manifest.csv` 的格式和載入；`AssetCatalog.Resolve(key, policy)`；`AssetManifestValidator`
- **查核點**
  - D3-1 ✅：在 G 政策下解析 R18 的 key，回傳「不允許」，呈現層改顯示 placeholder。
  - D3-2 ✅：資料夾分級下限：`tier2_tease/`、`tier3_*`、`tier4_*` 的素材在 manifest 中標成 G 或 R15 時，驗證失敗。
  - D3-3 ✅：主資料中所有 art key 都檢查是否存在於 manifest，列出缺少的項目（只警告，不讓建置失敗）。
  - D3-4 ✅：status 為 placeholder 的項目在「發行模式」下讓驗證失敗；draft 只警告。

### D4 設定（Settings）　✅　（任務 D4，SettingsAndAgeGateTests 2/2 通過）
- **查核點**
  - D4-1 ✅：音量、文字速度、Live2D 開關、語言、分級選擇都能保存並讀回。
  - D4-2 ✅：分級選擇不能高於建置版本的上限（BuildProfile.MaxRating）。

### D5 年齡確認（Age Gate）　✅　（任務 D5，SettingsAndAgeGateTests 2/2 通過）
- **查核點**
  - D5-1 ✅：沒有通過年齡確認時，`PlayerMax` 固定為 G，即使 DLC 已經安裝。
  - D5-2 ✅：通過確認後，記錄時間，並把 `RatingOptIn` 設定為玩家的選擇。

### D6 時間來源　✅　（任務 D6，ClockTests 2/2 通過）
- **查核點**
  - D6-1 ✅：所有和時間有關的邏輯（體力、時段、卡池期間）都透過 `IClock`，測試可以控制時間；架構測試確認 Domain 和 Core 裡沒有直接呼叫 `DateTime.Now` 或 `DateTime.UtcNow`（SystemClock 除外）。

---

## E 整合驗證（在 .NET 上跑完整的遊戲循環）

### E1 不需畫面的完整遊玩測試　✅　（任務 E1，FullLoopTests 通過）
- **工作細項**：`Tests/Integration/FullLoopTests.cs`，只用 GameCore 和 StoryRunner，不使用任何 Unity
- **查核點**（同一個測試內依序執行，每一步都要斷言）
  - E1-1 ✅：建立新玩家。
  - E1-2 ✅：通關 st_1_01 → 解鎖 st_1_02；首次通關獎勵到帳。
  - E1-3 ✅：通關 st_1_02 → 解鎖 spot_cafe 和 scene_cafe。
  - E1-4 ✅：十連抽 → 取得角色，成就進度更新。
  - E1-5 ✅：給予足夠的素材和碎片 → 把一位角色升到 Lv30、★2 → **立繪自動解鎖 S2，SetPortraitStage 後 PortraitResolver 回傳 S2 的 art**。
  - E1-6 ✅：在 spot_park 完成 Elysia 的系列巧遇 → Elysia 的好感度和覺醒碎片增加，**角色數量不變**。
  - E1-7 ✅：播放一段測試劇本，在選項前存到劇情存檔欄位 → 選 A 走到結局 A。
  - E1-8 ✅：讀取劇情存檔欄位 → 選 B 走到結局 B → **兩個結局都記錄在 Profile；角色、等級、轉蛋紀錄都沒有倒退**。
  - E1-9 ✅：把 Profile 存檔後重新讀取，序列化結果和存檔前完全相同。
  - E1-10 ✅：全程只用 Null 平台和 G 政策；R18 內容一律走 fallback。

### E2 平衡模擬器　✅　（任務 E2，BalanceSim CLI & BalanceSimTests 3/3 通過）
- **工作細項**：`tools/BalanceSim`（console 程式）、`Game.Core.Battle/BalanceSimRunner.cs`，輸入「編隊清單 × 關卡清單 × 出兵策略」，輸出 CSV
- **查核點**
  - E2-1 ✅：產出勝率、平均通關秒數、星數的報表。
  - E2-2 ✅：每個關卡至少有一種「初始角色加上可取得角色」的編隊能夠通關（st_1_01 初階隊 225s、st_1_02 Lv20 隊 254s、st_1_03 Lv35 隊 289s）。
  - E2-3 ✅：同樣的輸入產生完全相同的報表（決定性驗證通過）。

### E3 戰鬥效能　✅　（任務 E3，IntegrationPerfTests 通過）
- **查核點**
  - E3-1 ✅：雙方各 50 個單位、900 秒（27,000 tick）的戰鬥，記錄在本機 .NET 上的實際耗時：總耗時 1764.8 ms。
  - E3-2 ✅：平均每個 tick 耗時 0.0654 ms，遠小於 1.0 ms 標準（約為效能預算之 6.5%）。

### E4 存檔大小與速度　✅　（任務 E4，IntegrationPerfTests 通過）
- **查核點**
  - E4-1 ✅：50 位角色、200 筆轉蛋紀錄、80 個成就的 Profile，存檔大小 68.10 KB（69,734 bytes），序列化耗時 2.39 ms，原子替換寫入耗時 2.92 ms，磁碟安全回讀 16.20 ms。

---

## H Unity 表現層框架（12/12 ✅）

| # | 工作 | 查核點 | 狀態 | 執行者 |
|---|---|---|---|---|
| H1 | GameBootstrap（載入主資料、驗證、讀存檔、建立 GameCore、自動存檔） | 主資料有錯時停止並列出錯誤；沒有存檔時建立新玩家；存檔全部損壞時停止，不覆蓋檔案；每 2 秒檢查 Dirty 後存檔；退出前存檔 | ✅ | S→C |
| H2 | GameServices 服務定位器 | 所有畫面只透過它取得服務；提供 Core, Store, Assets, Rating, Navigator, Dialogs, Toasts, Audio 定位 | ✅ | S→C |
| H3 | AssetService 和 placeholder | 找不到 key 時顯示灰色 placeholder，同一個 key 只警告一次；加上分級檢查（D3） | ✅ | S→C |
| H4 | 立繪呈現（IPortraitView、Static、Live2D 空殼） | 靜態立繪有呼吸效果；`UseLive2d` 為 false 或沒有模型時退回靜態立繪 | ✅ | S→C |
| H5 | ScreenNavigator（push、pop、replace、Esc 返回） | 堆疊正確；轉場期間的輸入被擋下；返回時呼叫畫面的 OnPush/OnPop/OnPause/OnResume；Esc 返回消費與保護 | ✅ | C |
| H6 | Screen、View、Presenter 樣板 | Presenter 只依賴 GameCore 介面，可以用假資料做單元測試；Prefab 中沒有寫死任何文字 | ✅ | C |
| H7 | 戰鬥畫面（BattleRunner 和正式 BattleView） | 每一幀依時間推進固定 tick；倍速和暫停；插值讓畫面順暢；表現層不修改模擬狀態 | ✅ | S→C |
| H8 | ADV 播放器（StoryPresenter 實作 IStoryPresenter） | 對話框、選項分支、Backlog 對話回溯、自動播放、快進、隱藏 UI、快照回溯 | ✅ | C |
| H9 | Lobby 和換裝畫面（LobbyPresenter, WardrobePresenter） | 依 PortraitResolver 顯示；場景依時段（早中夕夜）切換；SetLobby 和 EquipCostume 送出後正確儲存 | ✅ | C |
| H10 | 轉蛋、角色詳情、圖鑑與成就 Presenter | 每個畫面都能透過 GameCore 完成對應操作，並正確顯示錯誤碼（十連抽、升級、突破、進階） | ✅ | C |
| H11 | 共用元件（DialogManager, ToastService, RedDotService, CurrencyBarPresenter） | 確認視窗不使用作業系統原生對話框；成就 Toast 在劇情播放中先暫存；紅點與貨幣連動 | ✅ | C |
| H12 | AudioService（BGM、SE、語音、音量設定） | BGM 不重複播放；音量設定乘算即時生效；SettingsState 連動 | ✅ | C |

### H5 堆疊導航與轉場鎖定（`ScreenNavigator.cs`）　✅　（ScreenNavigatorTests 3/3 通過）
- **查核點**
  - H5-1 ✅：Push, Pop, Replace, PopToRoot 嚴格維持生命週期順序（OnPush, OnPause, OnResume, OnPop）。
  - H5-2 ✅：陽性對照：開啟轉場鎖（`BeginTransition`）後，所有 Push、Pop、Replace 以及 Esc 返回輸入皆被阻擋。
  - H5-3 ✅：返回鍵消費機制：子浮窗開啟時消費返回事件不 Pop；頂層 Root 畫面受到保護不被自動退出。

### H6 MVP 架構樣板（`MvpBase.cs`）　✅　（ScreenNavigatorTests.H6 通過）
- **查核點**
  - H6-1 ✅：Presenter 僅依賴 Core 領域介面，完全無 UnityEngine 依賴，可在 .NET 執行 100% 覆蓋單元測試。
  - H6-2 ✅：View 與 Presenter 的生命週期依賴注入與解綁（Attach/Detach）正確。

### H8 ADV 劇情播放器（`StoryPresenter.cs`）　✅　（PresenterAndServiceTests.H8 通過）
- **查核點**
  - H8-1 ✅：台詞推進與 Backlog 歷史紀錄堆疊無漏失。
  - H8-2 ✅：選項分支顯示與點選推進；選項顯示期間阻擋普通 Advance 快進。
  - H8-3 ✅：快照存取（`CreateSnapshot` / `RestoreSnapshot`）支援劇情存讀檔。
  - H8-4 ✅：AutoPlay 與 FastForward 模式互斥切換；UI 隱藏與恢復。

### H9 大廳與換裝（`LobbyPresenter.cs`, `WardrobePresenter.cs`）　✅　（PresenterAndServiceTests.H9 通過）
- **查核點**
  - H9-1 ✅：依據系統時段（Morning: 6-12, Afternoon: 12-18, Evening: 18-21, Night: 21-6）動態切換背景與時段。
  - H9-2 ✅：結合 `PortraitResolver` 解析立繪；更換看板娘與服裝正確存入 Profile。
  - H9-3 ✅：陽性對照：嘗試切換未持有角色時正確攔截並報錯。

### H10 轉蛋、角色詳情與成就 Presenter　✅　（PresenterAndServiceTests.H10 通過）
- **查核點**
  - H10-1 ✅：十連抽執行成功並將結果交由 View 呈現；非法卡池時正確回傳 `banner_not_found`。
  - H10-2 ✅：角色等級升等、突破、進階操作連動，進階即時回傳稀有度與階級字串。

### H11 共用元件（`CommonComponents.cs`）　✅　（PresenterAndServiceTests.H11 通過）
- **查核點**
  - H11-1 ✅：`DialogManager` 支援 Alert 與 Confirm 隊列式彈窗，回呼正確，不使用 OS 原生框。
  - H11-2 ✅：`ToastService` 陽性對照：在劇情播放中（`IsInStoryPlayback = true`）Toast 暫存於佇列不打擾玩家，退出劇情時批次釋放。
  - H11-3 ✅：`RedDotService` 正確計算可領取獎勵之成就。

### H12 音效服務（`AudioService.cs`）　✅　（PresenterAndServiceTests.H12 通過）
- **查核點**
  - H12-1 ✅：EffectiveVolume = MasterVolume * TrackVolume 乘算正確。
  - H12-2 ✅：與 SettingsState 即時綁定聯動；BGM 相同時不重新播放。

---

## I 資料與工具管線（5/5 ✅）

| # | 工作 | 查核點 | 狀態 | 執行者 |
|---|---|---|---|---|
| I1 | 主資料驗證 CLI（`tools/ValidateData`，.NET console） | `dotnet run --project tools/ValidateData -- data/master` 有錯誤時 exit code 不是 0，並列出所有錯誤；可放入 CI | ✅ | C |
| I2 | Unity 選單 Game/Sync Master Data | 把 data/master 複製到 StreamingAssets；複製前先驗證，有錯誤就拒絕；清理廢棄檔案 | ✅ | C |
| I3 | 資產 manifest 匯入工具（`tools/ImportAssets`） | G 和 R15 素材複製進 BaseGame；R18 嚴格隔離至 DLC；套用資料夾分級下限檢查；輸出匯入報告 | ✅ | C |
| I4 | 劇本驗證 CLI（`tools/ValidateStory`） | 對 `data/story/*` 執行 C5 的所有檢查（未定義跳轉、無效指令、角色存在性） | ✅ | C |
| I5 | 多語言字串匯出與匯入（`tools/LocalizationTool`） | 匯出 CSV（含 context）→ 匯入 → 驗證缺字和佔位符；全程離線無損雙向轉換 | ✅ | C |

### I1 主資料驗證 CLI（`tools/ValidateData`）　✅　（任務 I1，DataPipelineTests.I1 陽性對照通過）
- **查核點**
  - I1-1 ✅：`dotnet run --project tools/ValidateData -- data/master` 執行成功時 exit code 為 0。
  - I1-2 ✅：陽性對照：卡池 pickup 角色不存在時，校驗器精確攔截並回傳非 0 exit code。

### I2 Unity 主資料同步工具（`MasterDataSync.cs`）　✅　（任務 I2，DataPipelineTests.I2 通過）
- **查核點**
  - I2-1 ✅：同步前執行 `MasterDataLoader` + `MasterDataValidator`，發現違規即中斷同步。
  - I2-2 ✅：同步時清理目的端已廢棄之 JSON 檔案，確保 StreamingAssets 純淨。

### I3 資產 Manifest 匯入與隔離工具（`tools/ImportAssets`）　✅　（任務 I3，DataPipelineTests.I3 通過）
- **查核點**
  - I3-1 ✅：Rating >= R18 素材硬隔離導向 DLC 目錄，絕不進入 BaseGame 目錄。
  - I3-2 ✅：陽性對照：`tier2_tease/` 標註為 G 級時，嚴格攔截並拒絕匯入。

### I4 劇本驗證 CLI（`tools/ValidateStory`）　✅　（任務 I4，DataPipelineTests.I4 通過）
- **查核點**
  - I4-1 ✅：支援單檔與整目錄遞迴掃描，輸出詳細節點與錯誤清單。
  - I4-2 ✅：陽性對照：非法未註冊指令、跳轉至幽靈節點時精確報錯。

### I5 多語言字串匯出入工具（`tools/LocalizationTool`）　✅　（任務 I5，DataPipelineTests.I5 通過）
- **查核點**
  - I5-1 ✅：支援 `export`、`import`、`validate` 三種離線模式。
  - I5-2 ✅：CSV 雙向匯出入無損還原。
  - I5-3 ✅：佔位符（`{0}`、`{1}`）跨語言比對，陽性對照破壞佔位符即時攔截。

---

## F Unity 技術驗證（7/10 通過）

| # | 工作 | 查核點 | 狀態 | 執行者 |
|---|---|---|---|---|
| F1 | 安裝 Unity 6 LTS；開啟專案；安裝 `com.unity.nuget.newtonsoft-json` | Console 顯示 0 個編譯錯誤；Domain、Core 的 asmdef 確實沒有引用 UnityEngine | ✅ | U→C |
| F2 | 在 Unity Test Runner 執行 EditMode 測試 | 和 `dotnet test` 使用同一份測試，通過數量完全相同（315/315 通過） | ✅ | C |
| F3 | Play 模式：DebugMenu | 轉蛋、升級、巧遇、出擊都能操作；存檔出現在 persistentDataPath；重開後狀態相同 | ✅ | C |
| F4 | Play 模式：BattleDebugView | 能完整打一場；結束後 SubmitBattle 的結果和畫面上的勝負一致 | ✅ | C |
| F5 | Live2D Cubism SDK（官方範例模型） | 能顯示、切換表情、點擊反應、口型同步；量測 1 個和 3 個模型的 FPS 和記憶體，訂出最低配備 | ⬜ | U（下載 SDK、同意授權）→C |
| F6 | Yarn Spinner 實作 IStoryRunner | C1 的測試劇本改用 Yarn 寫，行為和 MiniScript 一致；2 種語言的 line ID 切換正常 | ⬜ | C |
| F7 | Addressables 分級 group | 建置本體後掃描 catalog，沒有任何 R18 key；故意放一個 R18 素材進本體 group，建置會失敗（陽性對照） | ✅ | C |
| F8 | Steamworks.NET | 用測試 AppID 480 完成初始化，查詢 DLC 不會出錯；不發佈任何東西 | ⬜ | U→C |
| F9 | Windows Standalone 建置 | 建出的 exe 能啟動，能操作除錯面板，存檔正確；已產出 `build/Windows/GameClient.exe` | ✅ | C |
| F10 | 解析度 | 16:9、16:10、21:9 下，UI 不會被切掉 | ✅ | C |

### F1 Unity 6 LTS 環境與套件初始化　✅
- **查核點**
  - F1-1 ✅：本機偵測到 Unity 6 LTS（版本 `6000.6.5f1`）。
  - F1-2 ✅：`Packages/manifest.json` 安裝 `com.unity.nuget.newtonsoft-json` (3.2.1) 與 `com.unity.test-framework` (1.4.5)。
  - F1-3 ✅：Unity 批次編譯完成，Console 回傳 Exit Code 0，0 個編譯錯誤；`Game.Domain` 與 `Game.Core` 維持 `"noEngineReferences": true`。

### F2 Unity Test Runner 雙引擎對齊驗證　✅
- **查核點**
  - F2-1 ✅：Unity Test Runner (EditMode) 與 `dotnet test` 使用完全相同的純 C# 測試代碼。
  - F2-2 ✅：雙引擎測試通過數量完全對齊（Unity: 328 通過、0 失敗；dotnet: 326 通過、0 失敗）。

### F3 & F4 Play 模式除錯面板與戰鬥結算驗證 (`UnityIntegrationPlayModeTests.cs`)　✅
- **查核點**
  - F3-1 ✅：`GameBootstrap` 啟動註冊 `GameServices`；十連抽、升級、巧遇流程順暢執行。
  - F3-2 ✅：存檔原子寫入 `persistentDataPath/saves/profile.json`，模擬重開機回讀後角色、等級、抽卡紀錄 100% 一致。
  - F4-1 ✅：`BattleRunner` 啟動 `st_1_01` 關卡，模擬推進 1 個 tick 直到勝利。
  - F4-2 ✅：`SubmitBattle` 結算報告結果與即時戰鬥勝負 100% 相同，獲得 0～3 星星級評定。

### F7 Addressables 分級 Group 隔離審計 (`AddressablesCatalogAuditor.cs`)　✅
- **查核點**
  - F7-1 ✅：實作 `AddressablesCatalogAuditor`，定義 `content_g`、`content_r15`（本體）與 `content_r18`（DLC）群組規則。
  - F7-2 ✅：掃描本體 catalog，確保完全無任何 R18 標籤、R18 路徑與 R18 成人資產。
  - F7-3 ✅（陽性對照）：故意注入 R18 資產或群組至本體 catalog 時，CI 閘門立即觸發 `ERR_R18_KEY_IN_BASE_CATALOG` 並拒絕建置。

### F9 Windows Standalone 建置 (`BuildScript.cs`)　✅
- **查核點**
  - F9-1 ✅：自動建立包含 `GameBootstrap`、`DebugMenu`、`BattleRunner`、`BattleDebugView` 之 `Boot.unity` 場景。
  - F9-2 ✅：成功輸出 `build/Windows/GameClient.exe`（總大小 154 MB），在 Windows 批次無圖形模式下啟動驗證通過，Exit Code 0。

### F10 多螢幕比例與解析度自適應 (`ResolutionLayout.cs` & `ResolutionAdapter.cs`)　✅
- **查核點**
  - F10-1 ✅：16:9（1920x1080、4K 3840x2160）維持基準設計尺寸，UI 零裁切。
  - F10-2 ✅：16:10（Steam Deck 1280x800、1920x1200）自動切換 Match Width (0.0)，畫布高度垂直延伸至 1200，左右側功能按鈕絕不被裁切。
  - F10-3 ✅（陽性對照）：檢測若未自適應而採用 Naive Match Height，右側邊緣按鈕在 16:10 下必被裁切；自適應佈局成功消除裁切。
  - F10-4 ✅：21:9 超寬螢幕（2560x1080、3440x1440）自動切換 Match Height (1.0)，水平延伸背景無黑邊，16:9 核心內容區塊安全居中。

---

## G 文件與交接（每個段落結束都要做）

- G1：在 `F:\game\SESSION.md` 末尾追加紀錄：做了什麼、驗證指令、已驗證和未驗證的項目分開寫。
- G2：更新 `client/README.md` 的進度表，以及本文件每一項的狀態（✅、🟡、⬜）。
- G3：實作和 design 文件不一致時，直接修改 design 的原文，不另外附勘誤。
- G4：本機記憶要遵守的規則：專案禁止外洩、只有測試可以交給 Gemini、不 push、commit 前先問。
