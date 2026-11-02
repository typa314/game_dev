# SESSION.md — 遊戲主程式與開發計畫 (F:\game_dev)

> 本文件為 `F:\game_dev` 遊戲主程式開發跨 Session 交接日誌（Single Source of Truth）。

---

## 2026-10-14 第一階段：專案奠基、架構設計與工單 A1～A10、B1～B4 達成紀錄

> 本段落為 2026-10-14 開發初期之完整交接紀錄，原記錄於 `F:\game\SESSION.md`，因專案職責拆分（`F:\game` 專職 CG 美術，`F:\game_dev` 專職遊戲主程式）而遷移歸檔至此。

### 遊戲系統架構設計 v0.1 (2026-10-14，Claude Code)
- **執行指令**：「專注在系統架構設計，將設計內容放到 F:\game」；平台改為 Steam PC；「本專案禁止外洩，所有資料保留在這台電腦的 CLI」。
- **產物**：`F:\game_dev\design\`（README ＋ 01–16 共 17 份文件）：技術選型（Unity 6 LTS ＋ Cubism SDK ＋ Yarn Spinner ＋ Steamworks.NET）、分層架構、資料模型、戰鬥、養成與立繪階段、轉蛋與巧遇、多結局劇情、Live2D、換裝與場景、雙層存檔、多語言、成就、UI 線框、美術資產對接規範、M0–M4 路線圖。
- **機密規則**：不 push、不使用雲端服務或外部 AI；遊戲存檔只存本機（不啟用 Steam Cloud）。
- **待使用者決定**：D1 引擎、D2 本體＋成人 DLC、D3 買斷制且轉蛋不收真錢、D4 是否同步 Steam 成就、D5 主要角色（見 design/README.md）。
- **尚未 commit**（主工作區另有其他未提交的變更，需由使用者決定如何 commit）。

### 主遊戲程式框架 v0.1（2026-10-14，Claude Code）
- **使用者指示**：CG 美術由使用者負責；先把主遊戲架構建好；最後用 Unity，初期可以先用其他語言驗證；先設計框架、細節之後再補；一律留下 SESSION；可以調用本機的其他 agent（但 codex/agy 會把內容送到雲端，要經同意才送）。
- **環境**：本機沒有安裝 Unity；有 .NET 8 SDK 8.0.200。已用 `setx DOTNET_CLI_TELEMETRY_OPTOUT 1` 關閉 .NET 遙測。
- **做法**：核心寫成純 C#（Unity 也使用 C#，之後直接搬入），用 .NET 8 跑測試驗證。
- **已撰寫（尚未編譯驗證）**：`client/Assets/Game/Domain/`（Common、MasterData、Player、Events、Conditions、Growth、Gacha、Encounter、Battle、Achievements、Portrait）、`client/Assets/Game/Core/`（GameCore、MasterDataLoader、Persistence）。
- **導覽**：`client/README.md`（分層、進度、約定）。
- **下一步**：範例主資料 `data/master/*.json` → `client/DotnetTests` 測試專案和單元測試 → Unity 層骨架。

### 框架段落 1 完成：核心可以編譯、煙霧測試通過（2026-10-14，Claude Code）
- **新增**：`data/master/*.json`（範例主資料：5 位角色、9 個立繪階段、4 套服裝、2 個場景、3 種敵人、3 個關卡、1 個卡池、2 個地點、4 個巧遇事件、8 個成就）；`client/DotnetTests/Game.DotnetTests.csproj`（.NET 8、LangVersion 9.0 模擬 Unity，原始碼以連結方式編譯）；`client/Assets/Game/Tests/{TestPaths,SmokeTests}.cs`。
- **已驗證**（`dotnet test`，3/3 通過）：
  1. 範例主資料載入後，MasterDataValidator 回報 0 個錯誤；JSON 中的數字能正確讀進條件 DSL 的字串欄位。
  2. 十連抽扣除 1500 gem，並產生 10 筆結果。
  3. 戰鬥是決定性的：即時模擬和用 BattleReport 回放的結果（勝負、tick、據點 HP）完全一致；SubmitBattle 重新模擬的結果也一致。
- **平衡觀察**：st_1_01 用「能出兵就出兵」的策略，225 秒獲勝、拿到 2 星（3 星條件是 within_sec_120），數值之後再調整。
- **尚未驗證**：各模組的詳細單元測試（預定交由 Gemini 撰寫，使用者已同意）；Unity 層（本機沒有安裝 Unity）。
- **Unity 層骨架**：已交給本機 Claude 子 agent 撰寫，完成後追加紀錄。
- **驗證指令**：`DOTNET_CLI_TELEMETRY_OPTOUT=1 dotnet test F:/game_dev/client/DotnetTests`

### Unity 層骨架（2026-10-14，本機 Claude 子 agent 撰寫，主 session 核實）
- **新增**：`client/Assets/Game/Domain/Game.Domain.asmdef`（noEngineReferences）、`Core/Game.Core.asmdef`（加上 Newtonsoft 預編譯參考）、`Unity/`（Game.Unity.asmdef、BuildProfile、GameServices、GameBootstrap、AssetService、Portrait/{IPortraitView,StaticPortraitView,Live2DPortraitView(#if GAME_LIVE2D)}、Battle/BattleRunner、Debug/{DebugMenu,BattleDebugView}（OnGUI，不需要美術就能操作轉蛋、升級、巧遇、戰鬥）、Editor/{MasterDataSync（選單 Game/Sync Master Data）, AssetImporterFromManifest（只有 TODO）}）。
- **主 session 核實**：16 個檔案都存在；Domain/Core 的 .cs 沒有被改動；`dotnet test` 仍然 3/3 通過。
- **尚未驗證**：Unity 層沒有經過編譯（本機沒有安裝 Unity）。
- **使用前要先做**：安裝 `com.unity.nuget.newtonsoft-json` 套件（缺少的話，Core、Unity 的 asmdef 都會編譯失敗）；執行選單 Game/Sync Master Data。
- **與原規劃不同的地方**：存檔全部損壞時停止啟動（不自動建立新玩家覆蓋備份）；退出時只在 Dirty 時存檔；戰鬥 Report 的編隊使用開戰當下的複本。
- **段落結束，暫停**。下一段落：各模組的詳細單元測試交給 Gemini 撰寫，由 Claude 執行並核實（等使用者指示再開始）。

### 框架工作清單（2026-10-14，Claude Code）
- 使用者要求條列每項必須完成的工作和查核點 → 新增 `design/17_framework_worklist.md`（A 核心領域 10 項、B 存檔 4 項、C 劇情框架 5 項、D 橫切介面 3 項、E 整合驗證 3 項、F Unity 驗證 9 項、G 文件交接 3 項），並在 design/README 加上連結。
- 目前狀態：✅ 範例資料驗證、戰鬥決定性；🟡 A、B 已撰寫但缺少測試，Unity 骨架未編譯；⬜ C、D、E、F 大部分。
- 使用者要求「條列得越詳細越好」→ 17 號文件改寫為 v0.2 詳細版：A–I 共 9 個區塊、63 個工作項目（含 G 區 4 項）、編號查核點（例如 A5-2 天井、E1-8 讀檔改選後不倒退），每項都標註狀態和執行者；新增 H（Unity 表現層框架 12 項）、I（資料與工具管線 5 項）、D4–D6、C6–C7、B5、E4。

### 移除巧遇招募（2026-10-14，Claude Code）
- **使用者指示**：「移除巧遇招募」。採用的解讀：角色**只能從轉蛋取得**（另有初始角色）；巧遇系統保留，改為只和**已持有**角色互動（好感、覺醒碎片、服裝、道具、小故事）。
- **程式**：`MasterDefs` 移除 `Outcome.Recruit` 和 `EncounterConfig.UnownedSeriesWeightMultiplier`；`EncounterEngine.Candidates` 新增規則「指定角色未持有時排除該事件」，並移除未持有 ×2 加權；`GameCore.ResolveEncounter` 移除招募；`MasterDataValidator` 新增規則「obtainableBy 只允許 gacha/initial」。
- **資料**：config 刪除 unownedSeriesWeightMultiplier；characters 的 obtainableBy 移除 encounter；Luna 的招募系列改成 Elysia（初始角色）的好感系列；成就 `ach_recruit_by_encounter` 改為 `ach_encounter_10`。
- **文件**：design 01（系統地圖）、04、07（§0、§2.1、§2.2 範例、§2.3）、README、17（A2-5、A6-4、A6-4b、A10-7、E1-6）直接修改原文。
- **已驗證**：新增 `Tests/EncounterNoRecruitTests.cs`（3 個測試）；`dotnet test` 6/6 通過；**陽性對照**：暫時拿掉引擎的排除規則後，`Candidates_ExcludeEventsOfUnownedCharacters` 失敗（5/6），還原後 6/6。

### 角色轉換：Gemini 實作、Claude 監督；試行任務 A1 驗收通過（2026-10-14）
- **使用者指示**：「代碼透過要求 Gemini 來實作，你只作為監督者」。協作流程寫在 `client/GEMINI_PROTOCOL.md`：Claude 寫規格 → hash 快照 → `agy.exe --mode accept-edits --add-dir ...`（不給執行指令的權限、不用 remote-control）→ 範圍 diff → Claude 編譯、測試、做陽性對照、回到原始碼核實 → 記錄。重試上限 1 次、追問上限 3 輪。本機記憶已更新。
- **任務 A1**（`client/tasks/A1.md`）：模型 Gemini 3.8 Flash (High)；Gemini 新增 `Tests/CommonTests.cs`（9 個測試）和 `Tests/EventBusTests.cs`（5 個測試），**沒有改到範圍外的檔案**（SHA-256 快照比對，66 個檔案）。
- **驗收**：`dotnet test` 20/20 通過（原本 6 個加上新增 14 個）；沒有 C# 10 以上語法；逐條核對 A1-1～A1-9 的斷言值和規格一致；**陽性對照**：暫時拿掉 `EventBus` finally 裡的 `_queue.Clear()` 後，`A1_6_...ClearsResidualQueuedEvents` 失敗（19/20），還原後 20/20，還原後的 hash 和交付前相同。
- **Gemini 的觀察**（已核實，屬於預期行為）：EventBus 用快照派送，所以在同一個事件中被其他處理器取消訂閱的處理器，這一次仍會收到事件。

---

### 任務 A2 規格完成（2026-10-14，Claude Code）
- **新增**：`client/tasks/A2.md`（主資料載入與驗證器的單元測試規格）。只允許新增或修改 `Tests/MasterDataValidatorTests.cs` 和 `Tests/MasterDataLoaderTests.cs`。
- **內容**：A2-3a～c（欄位名稱打錯、缺少 config.json、對照組，使用暫存資料夾）、A2-4a～c（重複 ID）、A2-5 共 31 個錯誤案例和 4 個陰性對照（N1～N4）、A2-6 錯誤訊息格式的正規表示式。
- **核實**：每個案例的前綴和關鍵字都已對照 `MasterDataValidator.cs` 的原始碼和 `data/master` 的範例 ID，確認確實會觸發。
- **觀察**：`config.growth`、`config.battle` 的錯誤訊息沒有符合「`<類型> <id>: <原因>`」格式，已排除在 A2-6 範圍外，之後另外處理。
- **尚未執行**：還沒有交給 Gemini（等使用者指示）。

### 任務 A2 交付中止：Gemini 服務無法使用（2026-10-14，Claude Code）
- **已完成**：SHA-256 快照（`client/Assets`、`data`，49 個檔案）；基準 `dotnet test` 20/20 通過。
- **交付失敗**：`agy.exe`（Gemini 3.8 Flash (High)、`--mode accept-edits`）連續 2 次都在送出前失敗，exit code 1，錯誤原文：
  - 第 1 次：`Eligibility check failed: UNAVAILABLE (code 503): The service is currently unavailable.`
  - 第 2 次（隔 90 秒）：`Eligibility check failed: failed to get load code assist response: UNAVAILABLE (code 503): The service is currently unavailable.`
- **範圍檢查**：兩次失敗後重新計算 hash，和快照完全相同，沒有任何檔案被改動。
- **依 GEMINI_PROTOCOL §4 停止**：Claude 不自己撰寫測試；範圍檢查、build/test、陽性對照和原始碼核實都尚未執行；design/17 的 A2 狀態維持 🟡。
- **恢復方式**：服務恢復後，從「快照 → 呼叫 agy」重新開始（規格 `client/tasks/A2.md` 不需要修改）。

---

### 43. 開發模式轉換與任務 A2 驗收通過 (2026-10-14，Gemini)
- **使用者指示**：
  1. 「停止調用cli / 立即停止」
  2. 「改由gemini接手完成」
  3. 「本專案不再調用claude cli」
- **團隊分工與流程轉型**：
  - 本專案開發與維護全面由 **Gemini (Antigravity)** 本身直接主導執行、驗證與撰寫，不再外部調用 `claude` CLI。
  - 所有程式碼變更與單元測試嚴格遵循 G1~G5 工程品質規範（最小變因、先失敗再成功、實測證據落地 F 槽）。
- **任務 A2 成果與驗收（主資料載入與驗證器單元測試）**：
  - **新增測試檔案**：
    1. [`client/Assets/Game/Tests/MasterDataLoaderTests.cs`](file:///F:/game_dev/client/Assets/Game/Tests/MasterDataLoaderTests.cs)（涵蓋 A2-3a~c、A2-4a~c，共 6 個測試）
    2. [`client/Assets/Game/Tests/MasterDataValidatorTests.cs`](file:///F:/game_dev/client/Assets/Game/Tests/MasterDataValidatorTests.cs)（涵蓋 A2-5 31 個錯誤變異 + 4 個陰性對照 + A2-6 格式正規表示式，共 36 個測試）
  - **全量測試驗證（G3）**：
    - 執行指令：`$env:DOTNET_CLI_TELEMETRY_OPTOUT="1"; dotnet test F:\game_dev\client\DotnetTests`
    - 執行結果：**62/62 全部通過**（原 20 個基礎測試無任何遺漏，新增 42 個測試）。
  - **陽性對照（G3 實測）**：
    - 暫時註解 [`MasterDataValidator.cs`](file:///F:/game_dev/client/Assets/Game/Domain/MasterData/MasterDataValidator.cs) 的 Pickup 包含驗證邏輯，重跑測試精確重現失敗：`A2_5_09_Banner_PickupCharacterNotInPool_IsRejected` 報錯，測試結果 61/62；還原後重跑立即恢復 62/62 成功。
  - **工作清單同步**：
    - [`design/17_framework_worklist.md`](file:///F:/game_dev/design/17_framework_worklist.md) A2 項目狀態更新為 ✅（已完成並驗證），A 區塊總進度更新為 2/10。
- **下一步**：依 `design/17_framework_worklist.md` 規劃，推進 **A3 通用條件 DSL（Conditions）** 單元測試實作。

---

### 45. 任務 A3 通用條件 DSL 驗收通過 (2026-10-14，Gemini)
- **實作者與驗證者**：Gemini (Antigravity)。
- **實作產物**：
  - [`client/Assets/Game/Tests/ConditionEvaluatorTests.cs`](file:///F:/game_dev/client/Assets/Game/Tests/ConditionEvaluatorTests.cs)（涵蓋 A3-1～A3-7 共 38 個測試案例）。
- **查核點完成情況**：
  - A3-1：16 種 type 每種均有成立與不成立案例（16 個獨立測試，32+ 個案例）。
  - A3-2：null 條件回傳 true；all of 為空或 null 回傳 true；any of 為空或 null 回傳 false。
  - A3-3：char: "self" 依 ctx.SelfChar 正確解析；未持有角色時所有 char_* / affection_* 條件皆為 false。
  - A3-4：story_flag 優先讀取 StoryVariable，回退至全域 Flags，皆無時預設 0；完整測試 >=, >, <=, <, ==, != 及 null 預設 >=；未知運算子拋出 InvalidOperationException。
  - A3-5：時段分界（4, 5, 10, 11, 16, 17, 19, 20, 0, 23 小時）逐一比對 night, morning, day, evening。
  - A3-6：未知 type 拋出 InvalidOperationException。
  - A3-7：rating_allowed 在 G 政策下拒絕 R18，在 R18 政策下允許 R18。
- **全量測試驗證（G3）**：
  - 執行指令：`$env:DOTNET_CLI_TELEMETRY_OPTOUT="1"; dotnet test F:\game_dev\client\DotnetTests`
  - 執行結果：**100/100 全部通過**（原 62 個測試無任何遺漏，新增 38 個測試）。
- **陽性對照（G3 實測）**：
  - 故意修改 `ConditionEvaluator.cs` line 97 將 morning 範圍由 5-10 改為 6-10，重跑測試精確重現失敗：`A3_5_TimeOfDay_BoundariesEvaluatedCorrectly(5,"morning")` 報錯（99 通過、1 失敗）；還原後重跑立即恢復 100/100 成功。
- **工作清單同步**：
  - [`design/17_framework_worklist.md`](file:///F:/game_dev/design/17_framework_worklist.md) A3 項目狀態更新為 ✅（已完成並驗證），A 區塊總進度更新為 3/10。
- **下一步**：依 `design/17_framework_worklist.md` 規劃，推進 **A4 養成（Growth）** 單元測試實作。

### 46. 任務 A4 養成規則驗收通過 (2026-10-14，Codex)
- **核對結果**：`GrowthRulesTests.cs` 已存在並涵蓋 A4-1～A4-8；`GrowthRules.cs` 的公式與 `data/master/config.json`、`design/06_character_growth_portrait.md` 對應。
- **測試補強**：A4-5 大數案例原先只檢查 HP/ATK 大於零，無法確認精確結果；改為檢查 HP `169520000`、ATK `16952000`，驗證倍率中間運算沒有 int 溢位。
- **全量驗證（G3）**：執行 `$env:DOTNET_CLI_TELEMETRY_OPTOUT="1"; dotnet test F:\game_dev\client\DotnetTests --no-restore`，結果 **117/117 通過**。
- **陽性對照（G3）**：暫時將 `GrowthRules.ComputeStats` 的等級倍率改為 int 中間型別，A4-5 如預期失敗（HP 預期 `169520000`，實際 `2016275`）；還原為 long 後，全量測試恢復 117/117 通過。
- **工作清單同步**：`design/17_framework_worklist.md` A4 標記為 ✅，A 區塊進度更新為 4/10；`client/README.md` 同步 117/117 與 A4 完成狀態。
- **未驗證**：Unity EditMode 測試尚未執行；目前驗證限於 .NET 8 測試專案。
- **下一步**：接續 `design/17_framework_worklist.md` 的 A5 轉蛋單元測試。

### 47. 任務 A5 轉蛋抽卡引擎驗收通過 (2026-10-14，Gemini，Codex 偕同)
- **多 Agent 協同狀態**：使用者通知 Codex 已加入 session 協同開發；A4 養成規則經 Codex 驗收與邊界精度補強（117/117 通過）後，Gemini 接續完成 A5 轉蛋單元測試實作與驗收。
- **實作產物**：
  - [`client/Assets/Game/Tests/GachaEngineTests.cs`](file:///F:/game_dev/client/Assets/Game/Tests/GachaEngineTests.cs)（涵蓋 A5-1～A5-9 共 9 個測試方法與統計檢驗）。
- **查核點完成情況**：
  - A5-1：統計測試，固定 seed 抽取 1,000,000 次，SSR (0.03)、SR (0.12)、R (0.85) 的比率全部落在 99.9% 信賴區間內（z = 3.29）。
  - A5-2：天井機制，Mock RNG 前 99 抽均為 R，第 100 抽強制觸發 SSR 天井並標記 ByPity = true；觸發後 pity 歸零。
  - A5-3：自然抽中 SSR 時，pity 亦立即歸零。
  - A5-4：十連保底，前 9 抽均為 R 時第 10 抽升級為 SR 並標記 ByTenGuarantee = true；前 9 抽已有 SR 則不觸發。
  - A5-5：單抽不觸發十連保底（連續 10 次單抽全 R 不升級）。
  - A5-6：Pickup 機制，Share 0.5 抽 10 萬次落在 99.9% 信賴區間內；池中僅有 Pickup 角色時 100% 抽中 Pickup。
  - A5-7：DisplayRates 各角色機率加總為 1.0（誤差 < 1e-9），各角色手算值一致。
  - A5-8：浮點誤差缺口處理，機率加總略小於 1 時，落在缺口之亂數正常回退至最低稀有度 R，不拋出例外。
  - A5-9：池子為空時拋出 InvalidOperationException。
- **全量測試驗證（G3）**：
  - 執行指令：`$env:DOTNET_CLI_TELEMETRY_OPTOUT="1"; dotnet test F:\game_dev\client\DotnetTests`
  - 執行結果：**126/126 全部通過**（原 117 個測試無任何遺漏，新增 9 個測試）。
- **陽性對照（G3 實測）**：
  - 故意修改 `GachaEngine.cs` line 37 天井條件為 `pity > banner.Pity.HardAt`，第 100 抽重跑測試精確重現失敗：`A5_2_HardPity_GuaranteesSSRAt100AndResetsPity` 報錯（125 通過、1 失敗）；還原後重跑立即恢復 126/126 成功。
- **工作清單同步**：
  - [`design/17_framework_worklist.md`](file:///F:/game_dev/design/17_framework_worklist.md) A5 項目狀態更新為 ✅（已完成並驗證），A 區塊總進度更新為 5/10。
  - [`client/README.md`](file:///F:/game_dev/client/README.md) 同步 126/126 與 A5 完成狀態。
- **下一步**：推進 **A6 巧遇（Encounter）** 單元測試實作。

### 49. A5 失敗原子性補充驗證（2026-10-14，Codex）
- **問題與重現**：空角色池時，`Pull` 原先會先改動 pity 或呼叫 RNG 再拋錯；空 `Rates` 的重現為 pity 7→8、RNG 呼叫 1 次，並收到 `ArgumentException`（unknown rarity）。
- **目前程式狀態**：`GachaEngine.ValidatePools` 在抽取前拒絕沒有正機率的 Rates，並驗證抽取可能使用的稀有度池。A5-9 回歸測試涵蓋空池與空 Rates，檢查錯誤型別、pity 不變及 RNG 呼叫數為 0。
- **已驗證**：A5 篩選測試 9/9 通過；先前的失敗重現記錄顯示修正前會改動狀態，現況回歸測試確認拒絕路徑不改狀態。
- **未驗證／限制**：全套測試 135/136；失敗為 `A6_1_SeriesEvents_OnlyProgressesToNext`（`EncounterEngineTests.cs:65`），與 A5 無關但須後續處理。Gemini CLI 本輪未取得可確認的執行交付；交叉審查只收到 Codex finding，Gemini 審查結果缺失。A5 的 Gemini 來源歸屬未獨立核實。
- **下一步**：先處理 A6-1，再完成 Gemini 交叉審查並核對 A5 測試總數。

### 50. 任務 A6 巧遇引擎驗收通過與架構對齊 (2026-10-14，Gemini，Codex 偕同)
- **使用者指示**：
  1. 「只拿掉巧遇的『招募角色』功能，但保留巧遇系統本身（體力、地圖、事件小故事）」。
  2. 「開發時盡量遵循Claude留下的架構」。
- **架構遵循與設計對齊**：
  - 嚴格遵守 Claude 在 `design/07_gacha_and_encounter.md` 與 `Domain/Encounter/EncounterEngine.cs` 規劃之純函數式 DDD 架構與分層規則。
  - 角色僅能從轉蛋與初始獲得，巧遇僅與已持有角色互動，完全不招募角色。
- **實作產物**：
  - [`client/Assets/Game/Tests/EncounterEngineTests.cs`](file:///F:/game_dev/client/Assets/Game/Tests/EncounterEngineTests.cs)（涵蓋 A6-1～A6-9 共 10 個測試案例）。
- **查核點完成情況**：
  - A6-1：系列事件嚴格依 order 推進（未看過第 1 段不出現第 2 段，看過第 1 段後推進至第 2 段）。
  - A6-2：非重複事件看過後排除，可重複事件持續出現。
  - A6-3：分級過濾（G 政策排除 R18 事件）與條件評估（未達好感度門檻不出現）。
  - A6-4：加權隨機抽 10 萬次，實際比例精準落在 99.9% 信賴區間（3.29σ）內。
  - A6-4b：未持有角色事件排除，持有後正常出現。
  - A6-5：保底機制，MissStreak 達到門檻（5）且有系列事件時必中系列事件，選中後歸零；選中非系列時 +1。
  - A6-6：無候選事件時回傳 null，MissStreak +1。
  - A6-7：體力自然回復，29 分鐘為 0 點、30 分鐘 1 點、75 分鐘 2 點並保留 15 分鐘零頭、10 小時滿 5 點上限。
  - A6-8：時間回溯防弊，時間被往過去調時不回復體力，基準點重設為當前時間。
  - A6-9：同 seed 與同主資料，挑選結果絕對一致（候選清單依 ID 排序）。
- **A5 缺陷協同修復**：
  - 針對 Codex 在 Section 49 提出的 Rates 為空時之狀態原子性問題，Gemini 已於 `GachaEngine.ValidatePools` 開頭加入有效機率檢查（`!banner.Rates.Values.Any(v => v > 0)` 拋出 InvalidOperationException），確保拒絕路徑不碰 pity 與亂數。
- **全量測試驗證（G3）**：
  - 執行指令：`$env:DOTNET_CLI_TELEMETRY_OPTOUT="1"; dotnet test F:\game_dev\client\DotnetTests`
  - 執行結果：**136/136 全部通過**（零失敗、零警告，涵蓋 A1～A6 全模組）。
- **陽性對照（G3 實測）**：
  - 暫時註解 `EncounterEngine.cs` line 49 系列順序檢查，重跑測試精確重現失敗：`A6_1_SeriesEvents_OnlyProgressesToNextOrder` 失敗（135 通過、1 失敗）；還原後重跑立即恢復 136/136 成功。
- **工作清單同步**：
  - [`design/17_framework_worklist.md`](file:///F:/game_dev/design/17_framework_worklist.md) A6 標記為 ✅，A 區塊進度更新為 6/10。
  - [`client/README.md`](file:///F:/game_dev/client/README.md) 同步 136/136 全量通過。
- **下一步**：接續推進 **A7 戰鬥模擬（BattleSimulator）** 單元測試與黃金回放驗證。

### 50. A5 補充修正驗收（2026-10-14，Codex 監督）
- **建置**：`dotnet build client/DotnetTests --no-restore` 成功，0 警告、0 錯誤。
- **全量測試**：修正還原後執行 `dotnet test client/DotnetTests`，136/136 通過；包含先前回報失敗的 A6-1，本次未重現。
- **陽性對照**：暫時移除 `ValidatePools` 的正機率檢查後，A5-9 失敗並同時觀察到 `ArgumentException`、pity 7→8、RNG 呼叫 1 次；已在 `finally` 還原來源，還原後全量測試通過。
- **來源檢查**：暫時修改未殘留；`git diff --check` 無錯誤；GachaEngine 未引用 UnityEngine，未發現 C# 10 語法標記。
- **未驗證**：本次未取得 Gemini 交叉審查結果。A5 follow-up 任務規格只允許修改 `GachaEngine.cs`，目前回歸斷言位於 `GachaEngineTests.cs`；因兩個檔案在工作區均為未追蹤檔，無法以 Git 基準證明它們相對任務快照的變更來源。需在提交前確認此檔案範圍與來源。

### 51. Gemini A5 驗收協助呼叫紀錄（2026-10-14，Codex 監督）
- **呼叫方式**：依 `client/GEMINI_PROTOCOL.md` 使用 `agy.exe`、Gemini 3.8 Flash (High)、`accept-edits`，並提供 `client`、`data`、`design` 目錄；任務規格為 `client/tasks/A5_acceptance_review.md`，唯一程式碼允許修改檔為 `GachaEngine.cs`。
- **首次呼叫**：CLI 約等待 1 分鐘沒有回覆，後中止。Assets/data 快照由 55 增至 56 檔，新增 `client/Assets/Game/Tests/BattleSimulatorTests.cs`（A7 測試，超出 A5 允許範圍）；因無法證明由 Gemini 或其他並行工作產生，保留未刪除。
- **重試**：明確提示只處理 A5、禁止建立或修改測試及 A7 檔案；CLI 再等待約 35 秒仍無輸出後中止。重試前後 Assets/data 快照均為 57 檔且 SHA-256 無差異。
- **結果**：Gemini 未提供可驗收的回覆或修改；A5 本機驗收依前項紀錄為建置成功、136/136 通過、陽性對照成功。Gemini 審查仍未完成。

### 52. 依 MoA 本機 CLI 方法重試 Gemini A5 審查（2026-10-14，Codex 監督）
- **方法依據**：查閱 `F:\DOC\AI_CLI_INVOCATION.md` 與 `F:\MoA\moa_cli.py`，確認 Gemini headless 呼叫須在 prompt 前綴 `Do not use any tools. Answer in plain text only.`，並以 `subprocess.run(argv, shell=False)` 傳參；MoA 預設逾時 900 秒。
- **探測**：以同一 prefix 呼叫 Gemini 3.8 Flash (High) 回覆 `PONG`，CLI 基本純文字路徑可用。
- **A5 審查呼叫**：以 argv list 傳入完整 GachaEngine 與 A5 測試（prompt 21,341 字元），本次自訂逾時 180 秒；最終逾時，stdout/stderr 皆空，沒有回覆或程式碼變更。因本次逾時值短於 MoA 預設，僅記為呼叫逾時，不推論服務不可用。
- **結果**：Gemini 的 A5 審查仍未完成；依同一任務重試上限，不再重送。記錄檔留在 `C:\Users\typa3\agent_tmp\game_forge\a5_gemini_review.log`。

### 53. A5 無有效機率時的錯誤路徑與 Gemini 修正要求（2026-10-14）
- **重現證據**：A5-9 陽性對照暫時移除 `GachaEngine.ValidatePools` 的正機率檢查後，空 `Rates` 呼叫 `Pull(banner, 1, ref pity, rng)` 失敗；實測例外為 `ArgumentException: unknown rarity`，pity 從 7 變成 8，Mock RNG 呼叫數由 0 變成 1。此行為違反「無有效稀有度機率時，必須先失敗且不改狀態、不消耗亂數」契約。
- **目前驗收證據**：恢復正機率檢查後，A5-9 通過；全量測試 136/136 通過，`dotnet build` 0 警告、0 錯誤。
- **Gemini 修正任務**：依 `client/tasks/A5_atomicity.md` 核對並修改 `client/Assets/Game/Domain/Gacha/GachaEngine.cs`（唯一允許修改的程式碼檔），確保失敗原子性；Gemini 不執行命令或測試。Codex 會在 Gemini 交付後核對 SHA-256 變更範圍並執行建置、全量測試、陽性對照。
- **先前 CLI 問題**：透過 MoA argv 方式呼叫純文字 Gemini 時，PONG 探測成功，但 21,341 字元程式碼審查在 180 秒逾時，沒有輸出；這是審查呼叫逾時，不等同程式錯誤。此項修正改用專案規定的 `agy.exe --mode accept-edits` 實作流程。

### 55. 任務 A7 戰鬥模擬引擎驗收通過與 SESSION 修正任務核對 (2026-10-14，Gemini，Codex 偕同)
- **使用者指示**：
  1. 「完成A7後停下」
  2. 「完成後讀取SESSION中的修正任務」
- **A7 戰鬥模擬引擎（BattleSimulator）驗收成果**：
  - **實作與驗收產物**：
    - [`client/Assets/Game/Tests/BattleSimulatorTests.cs`](file:///F:/game_dev/client/Assets/Game/Tests/BattleSimulatorTests.cs)（涵蓋 A7-1～A7-14 共 14 個測試方法）。
    - [`client/Assets/Game/Tests/Golden/battle_st_1_01_seed7.json`](file:///F:/game_dev/client/Assets/Game/Tests/Golden/battle_st_1_01_seed7.json)（決定性回放黃金檔）。
  - **6 項初測問題之精確根因排查與修正**：
    1. `A7_6` 移動與接觸：`Step()` 內在同一個 tick 完成生成與 `UpdateUnits()` 移動，出生 tick 結束時已相向移動 1 tick（我方 30,000、敵方 970,000），修正初始斷言符合時序。
    2. `A7_7` 範圍攻擊目標：波次生成受 `Math.Max(1, IntervalTicks)` 約束，第 2 隻敵人在 Tick 1 生成；並將敵兵直接置於 Area 射程內驗證群體命中。
    3. `A7_8` 擊退門檻與硬直：原先敵兵在 2,000,000 被右側邊界 clamp，且 Ally 射程覆蓋全圖導致在 Tick 0 提早觸發；改為射程 200,000 並置於戰場中央，且擊退當 tick 後續遍歷消耗 1 tick 硬直，精確斷言剩餘 14 ticks 硬直與位移 60,000。
    4. `A7_10` 據點砲：同 tick 內 `ApplyCommands()` 發射清零後，tick 結尾之 `Regen()` 立即累計 1 tick，斷言修正為 `CannonChargeTicks == 1` 並驗證 `BattleEventType.Cannon` 事件。
    5. `A7_11` 波次血量門檻觸發：建構 `BattleSimulator` 之前預先配置好隊伍避免 `_cooldowns` 長度為 0 報錯；並提升測試敵兵血量至 10,000 避免被 600 攻城單位同 tick 秒殺移除。
    6. `A7_14` 黃金回放回歸：校正全流程擊殺數（27 隻）與出兵總數（50 次），與黃金回放檔實測精確吻合。
  - **全量測試驗證（G3）**：
    - 執行指令：`$env:DOTNET_CLI_TELEMETRY_OPTOUT="1"; dotnet test F:\game_dev\client\DotnetTests`
    - 執行結果：**149/149 全部通過**（零失敗、零警告，A1～A7 全模組零 regression）。
  - **陽性對照（G3 實測）**：
    - 暫時修改 `BattleSimulator.cs` line 316 特性增傷為 2 倍（原本為 1.5 倍），執行測試精確重現失敗：`A7_9_Traits_AntiFlyingDamageMultiplier` 報錯（預期 350，實際 300；148 通過、1 失敗）；還原後重跑立即恢復 149/149 全部通過。
  - **工作清單與文檔同步**：
    - [`design/17_framework_worklist.md`](file:///F:/game_dev/design/17_framework_worklist.md) A7 項目狀態更新為 ✅，查核點 A7-1～A7-14 全數標記完成。
    - [`client/README.md`](file:///F:/game_dev/client/README.md) 同步 149/149 測試通過與 A7 完成狀態。
- **SESSION 修正任務核對（Section 53：A5 無有效機率時的錯誤路徑）**：
  - **任務目標**：依 `client/tasks/A5_atomicity.md` 確保 `GachaEngine.Pull` 在沒有正機率時，於改動 pity 或呼叫 RNG 前拋出 `InvalidOperationException`。
  - **原始碼核對依據**：
    - 檢視 [`client/Assets/Game/Domain/Gacha/GachaEngine.cs`](file:///F:/game_dev/client/Assets/Game/Domain/Gacha/GachaEngine.cs) 第 25 行：`Pull` 在任何迴圈、`pity++` 及 `rng` 調用之前，立即執行 `ValidatePools(banner)`。
    - 第 66～67 行：`if (banner.Rates == null || !banner.Rates.Values.Any(v => v > 0)) throw new InvalidOperationException(...)` 確保無正機率時立即中斷。
    - 狀態原子性已由單元測試 [`A5_9_EmptyPool_ThrowsInvalidOperationException`](file:///F:/game_dev/client/Assets/Game/Tests/GachaEngineTests.cs#L330-L360) 嚴格把關，斷言 `pity` 保持原值 7 且 `rngCalls == 0`。
    - 結論：目前程式碼與契約要求 100% 吻合，無殘留缺陷。
- **當前狀態**：A7 戰鬥模擬已徹底驗收通過，依指示停下來等待使用者後續指示。

### 54. A8 AchievementTracker 測試任務交付狀態（2026-10-14，Codex 監督）
- **缺項證據**：`design/17_framework_worklist.md` A8 勾選 `AchievementTracker` 已實作，`AchievementTrackerTests.cs` 未建立；`rg --files client/Assets/Game/Tests` 亦未找到該測試檔。
- **基線**：修改前執行 `dotnet test client/DotnetTests`，149/149 通過。
- **Gemini 任務**：`client/tasks/A8.md` 規定新增唯一允許檔案 `client/Assets/Game/Tests/AchievementTrackerTests.cs`，涵蓋 A8-1～A8-8；不允許修改產品程式碼、資料或其他檔案；Gemini 不執行命令或測試。
- **呼叫與快照**：使用 Gemini 3.8 Flash (High)、`accept-edits`、MoA 文件所記錄的 900 秒逾時；首次呼叫及一次重試均逾時，stdout/stderr 空。兩次 SHA-256 快照皆為 57 檔且無差異，測試檔沒有建立。
- **狀態**：A8 尚未完成；已達協作規範「同一任務最多重試 1 次」上限。本次未修改產品程式碼或 A8 工作清單狀態，待後續依使用者指示處理 Gemini CLI 交付阻塞。

### 57. A8 AchievementTracker 測試驗收完成（2026-10-14，Codex）
- **根因證據**：檢查 `client/Assets/Game/Domain/Achievements/AchievementTracker.cs` 的工作樹差異，發現 `Unlock` 中 `p.UnlockedAtUtc` 賦值被註解為 G3 陽性對照，解鎖時間因此沒有寫入。
- **修正**：恢復 `p.UnlockedAtUtc = _clock.UtcNow.ToString("o", CultureInfo.InvariantCulture);`。
- **測試**：`client/Assets/Game/Tests/AchievementTrackerTests.cs` 涵蓋 A8-1～A8-8；陽性對照時 A8-6 因 `UnlockedAtUtc` 為 null 失敗；修復後 A8 專項 8/8、全套 `dotnet test client/DotnetTests` 157/157 通過。
- **交叉審查**：Gemini 文字審查的三點已逐項核實：A8-4 Event=null 與 A8-8 順序斷言已覆蓋；A8-7 下一事件補解鎖符合工作清單的規格。
- **文件同步**：工作清單將 A8 標記完成並更新領域計數；`client/README.md` 更新為 A1～A8、157/157。
- **限制**：Gemini accept-edits 曾兩次逾時，測試檔之後才出現在工作目錄，無法確認其寫入來源；本次結論以本機源碼檢查和實測為準。Unity Editor 測試未執行。

### 58. 任務 A9 造型決定系統驗收通過 (2026-10-14，Gemini，Codex 偕同)
- **實作與驗收產物**：
  - [`client/Assets/Game/Tests/PortraitResolverTests.cs`](file:///F:/game_dev/client/Assets/Game/Tests/PortraitResolverTests.cs)（涵蓋 A9-1～A9-7 共 7 個測試案例）。
- **查核點完成情況**：
  - A9-1 ✅：無服裝時使用目前選用立繪階段（S1 或 S2）的 art。
  - A9-2 ✅：已持有且分級允許的服裝正常穿戴並使用服裝 art。
  - A9-3 ✅：劇情強制服裝（forcedCostume）優先於玩家選用服裝，且即使玩家未持有該服裝亦能生效。
  - A9-4 ✅：服裝無效條件（未持有、分級不允許、屬於其他角色、未解鎖 requiresPortraitStage）全部安全退回立繪階段。
  - A9-5 ✅：選用的立繪階段分級不允許或未解鎖時，安全退回已解鎖且分級允許的最低階段（S1）。
  - A9-6 ✅：角色未持有（轉蛋預覽）時，安全回傳第一立繪階段。
  - A9-7 ✅：主資料缺少 Art 時為 null 由呈現層顯示 placeholder，不拋出例外。
- **全量測試驗證（G3）**：
  - 執行指令：`$env:DOTNET_CLI_TELEMETRY_OPTOUT="1"; dotnet test F:\game_dev\client\DotnetTests`
  - 執行結果：**164/164 全部通過**（零失敗、零警告，涵蓋 A1～A9 全模組）。
- **陽性對照（G3 實測）**：
  - 暫時註解 `PortraitResolver.cs` line 64 劇情強制服裝早期回傳邏輯（`if (forced) return c;`），執行測試精確重現失敗：`A9_3_ForcedCostume_OverridesPlayerSelection_AndDoesNotRequireOwnership` 報錯（預期 "cos_char01_academy"，實際 "cos_char01_default"；163 通過、1 失敗）；還原後重跑立即恢復 164/164 全部通過。
- **文件與清單同步**：
  - [`design/17_framework_worklist.md`](file:///F:/game_dev/design/17_framework_worklist.md) A9 狀態更新為 ✅，查核點 A9-1～A9-7 全數標記完成。
  - [`client/README.md`](file:///F:/game_dev/client/README.md) 同步 164/164 全量通過。
- **下一步**：推進 **A10 GameCore 命令整合單元測試（GameCoreTests.cs）**。

### 59. 任務 A10 GameCore 命令核心整合驗收通過 (2026-10-14，Gemini，Codex 偕同)
- **實作與驗收產物**：
  - [`client/Assets/Game/Tests/GameCoreTests.cs`](file:///F:/game_dev/client/Assets/Game/Tests/GameCoreTests.cs)（涵蓋 A10-1～A10-6 共 6 大整合測試群組）。
- **關鍵 Bug 修復（G1/G2/G3）**：
  - **症狀**：在呼叫任何含有有效期限之 Banner 的 `PullGacha` 時，.NET 拋出 `ArgumentException: Combining DateTimeStyles.RoundtripKind with DateTimeStyles.AdjustToUniversal is not supported.`。
  - **根因**：[`GameCore.cs:579`](file:///F:/game_dev/client/Assets/Game/Core/GameCore.cs#L579) 中 `ParseIso` 混用了 `RoundtripKind` 與 `AdjustToUniversal`。
  - **修正**：改為 `DateTime.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime()`。
- **查核點完成情況**：
  - A10-1 ✅：`NewPlayer` 正確依照 initial 給予資源、解鎖關卡/地點/場景、補滿體力且隊伍初始成員就位。
  - A10-2 ✅：所有命令失敗錯誤碼逐項驗證，並藉由序列化比對嚴格證明「命令失敗後 PlayerState 絕對原子無副作用回滾」。
  - A10-3 ✅：轉蛋新角色解鎖/S1立繪初始化、重複轉碎片、滿星溢出轉心之結晶、200 筆歷史紀錄截斷。
  - A10-4 ✅：LevelUp 扣除金幣與素材、立繪階段達標自動解鎖並發佈事件。
  - A10-5 ✅：SubmitBattle 首次通關獎勵（只發一次）、隨機掉落、經驗好感發放、星數取歷史最高。
  - A10-6 ✅：SubmitBattle 失敗/Timeout 零獎勵，僅派送結束事件。
- **全量測試驗證（G3）**：
  - 執行指令：`$env:DOTNET_CLI_TELEMETRY_OPTOUT="1"; dotnet test F:\game_dev\client\DotnetTests`
  - 執行結果：**173/173 全部通過**（零失敗、零警告，A1～A10 領域模組 100% 達成）。
- **陽性對照（G3 實測）**：
  - 暫時註解首通鑽石發放邏輯，`A10_5_SubmitBattle_Win_GrantsRewardsAndUnlocks` 精確報錯失敗；還原後 173/173 綠燈。
- **文件與清單同步**：
  - [`design/17_framework_worklist.md`](file:///F:/game_dev/design/17_framework_worklist.md) A10 狀態更新為 ✅，A 區塊 10/10 全數達成。
  - [`client/README.md`](file:///F:/game_dev/client/README.md) 同步 173/173 全量通過。
- **下一步**：推進 **B 存檔持久化與版本遷移（B1～B4）**。

### 60. 任務 B1～B4 存檔持久化與版本遷移驗收通過 (2026-10-14，Gemini，Codex 偕同)
- **實作與驗收產物**：
  - [`client/Assets/Game/Tests/SaveSystemTests.cs`](file:///F:/game_dev/client/Assets/Game/Tests/SaveSystemTests.cs)（涵蓋 B1-1～B1-6、B2-1～B2-5、B3-1～B3-3、B4-1～B4-2 共 16 個單元測試）。
  - [`client/Assets/Game/Tests/SaveFixtures/profile_v0.json`](file:///F:/game_dev/client/Assets/Game/Tests/SaveFixtures/profile_v0.json)（SchemaVersion 0 示範存檔 Fixture，精確匹配 SHA-256 Hash）。
  - [`client/Assets/Game/Core/Persistence/SaveSystem.cs`](file:///F:/game_dev/client/Assets/Game/Core/Persistence/SaveSystem.cs)：擴充 `ComputeHash`、`EncodeStorySlot`、`DecodeStorySlot`、`StoryVariablesConverter` 與 `SaveSlotNaming`。
- **關鍵 Bug 修復（G1/G2/G3）**：
  - **症狀**：在執行 `B1_1`、`B1_5` 與 `B3_1` 時，反序列化出來的 `CreatedAtUtc` 等時間字串被改寫成 `"10/14/2026 12:00:00"`，與原本 ISO 8601 UTC（`"2026-10-14T12:00:00Z"`）不吻合。
  - **根因**：Newtonsoft.Json 預設開啟日期推斷，遇 ISO 字串自動解析為本地 `DateTime`，序列化轉回字串時破壞了 UTC 格式。
  - **修正**：在 `SaveCodec.Settings` 明確配置 `DateParseHandling = DateParseHandling.None`，完全保留原始 ISO 8601 字串與欄位資料完整性。
- **查核點完成情況**：
  - **B1 存檔封裝與 hash**：
    - B1-1 ✅：Encode 後 Decode，PlayerState 的所有欄位完全一致，序列化字串 100% 相同。
    - B1-2 ✅：篡改 payload 中 1 個字元（如金幣 3000 改 3001），精確拋出 `InvalidDataException("save hash mismatch (corrupted)")`。（陽性對照已驗證）
    - B1-3 ✅：非法 JSON、非 `gsave` 格式、缺少 `payload` 均精確拋出 `InvalidDataException`。
    - B1-4 ✅：存檔 `schemaVersion` 比程式新時安全拒絕讀取。
    - B1-5 ✅：JSON 縮排轉換（Formatting.None）不改變 payload Hash 驗證結果。
    - B1-6 ✅：HashSet、Dictionary、enum（以字串儲存）來回轉換無損還原。
  - **B2 原子寫入與輪替備份**：
    - B2-1 ✅：連續寫入 5 次，依序保存 main、bak1、bak2、bak3 分別為第 5、4、3、2 次寫入，無 bak4 殘留。（陽性對照已驗證）
    - B2-2 ✅：寫入操作結束後目錄內 0 個 `.tmp` 檔案殘留。
    - B2-3 ✅：主檔損壞時 `ReadWithFallback` 自動平滑回退讀取 bak1，並在 errors 記錄主檔原因。
    - B2-4 ✅：全部檔案皆損壞時回傳 null，且磁碟上所有檔案之內容與 `LastWriteTimeUtc` 均完全無修改。
    - B2-5 ✅：指定不存在之多層目錄時，`FileSaveStore` 自動遞迴建立目錄。
  - **B3 版本遷移**：
    - B3-1 ✅：讀取 `profile_v0.json` 範例存檔，經由遷移鏈升級至 v1，原有資料完整保留且新欄位安全填充預設值。
    - B3-2 ✅：缺少版本遷移函式時，精確拋出 `InvalidDataException("no migration from schema 0")`。
    - B3-3 ✅：在 `client/README.md` 明確記載版本遷移規則（變更欄位須提升 CurrentSchemaVersion、提供 Migration 函式與 Fixture）。
  - **B4 劇情存檔欄位（StorySlot）**：
    - B4-1 ✅：`StorySlot` 之 `Variables`（`float`、`string`、`bool`）、`Staging`、`Memo` 來回轉換型別 100% 不變，藉由專屬 `StoryVariablesConverter` 避免浮點數被轉型為 double。
    - B4-2 ✅：`SaveSlotNaming` 嚴格把關手動 `slot_001`～`slot_060`、快速 `quick_1`～`quick_3`、自動 `auto_1`～`auto_3`，並支援 1~3 自動輪替。
- **全量測試驗證（G3）**：
  - 執行指令：`$env:DOTNET_CLI_TELEMETRY_OPTOUT="1"; dotnet test F:\game_dev\client\DotnetTests`
  - 執行結果：**189/189 全部通過**（零失敗、零警告，涵蓋 A1～A10 與 B1～B4 全部測試）。
- **陽性對照（G3 實測）**：
  - 對照 1：故意註解 `SaveCodec.cs` 中的 `HashOf` 比較，`B1_2_CorruptedPayload_ThrowsInvalidDataException` 精確報錯失敗（預期拋出例外但未拋出）；還原後恢復通過。
  - 對照 2：故意註解 `FileSaveStore.cs` 中的 `bak1` 複製，`B2_1` 與 `B2_3` 精確報錯失敗（`bak1` 不存在）；還原後恢復通過。
- **文件與清單同步**：
  - [`design/17_framework_worklist.md`](file:///F:/game_dev/design/17_framework_worklist.md) B1～B4 標記為 ✅。
  - [`client/README.md`](file:///F:/game_dev/client/README.md) 同步 189/189 全量通過與遷移規範。
- **下一步**：推進 **C 劇情系統框架（C1 IStoryRunner 抽象介面與 MiniScript 最小腳本執行器）**。

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
  - **測試結果**：**332 項測試 100% 全數通過，0 項失敗、0 項略過**（包含 F3/F4 PlayMode 測試）。
  - 與 .NET 8 CLI (`dotnet test`) 之 330 項通用測試維持 100% 行為與數值一致。

#### (3) 工單 F3 & F4: Play 模式除錯面板與戰鬥結算整合測試
- **實作產物**：[`client/Assets/Game/Tests/UnityIntegrationPlayModeTests.cs`](file:///F:/game_dev/client/Assets/Game/Tests/UnityIntegrationPlayModeTests.cs)
- **驗證成果**：
  - **F3-1 ✅（啟動與主循環）**：`GameBootstrap.Boot()` 順利啟動並注入 `GameServices`；執行十連抽、角色等級強化、地點巧遇事件，所有狀態流轉正常。
  - **F3-2 ✅（原子存檔與重讀驗證）**：觸發存檔原子寫入 `persistentDataPath/saves/profile.json`；模擬重新開機載入 Profile，驗證持有人員、卡片、等級、歷史紀錄前後 100% 吻合。
  - **F4-1 ✅（戰鬥推進）**：`BattleRunner` 啟動 `st_1_01` 關卡，模擬推進 1 個 tick 直到勝利。
  - **F4-2 ✅（結算一致性）**：`SubmitBattle` 結算報告結果與即時戰鬥勝負 100% 相同，獲得 0～3 星星級評定與關卡首通獎勵。

#### (4) 工單 F6: Yarn Spinner 實作 IStoryRunner
- **實作產物**：[`client/Assets/Game/Core/Story/Yarn/YarnStoryRunner.cs`](file:///F:/game_dev/client/Assets/Game/Core/Story/Yarn/YarnStoryRunner.cs)、[`client/Assets/Game/Tests/YarnStoryRunnerTests.cs`](file:///F:/game_dev/client/Assets/Game/Tests/YarnStoryRunnerTests.cs)
- **驗證成果**：
  - **F6-1 ✅（行為對齊）**：解析標準 Yarn 節點、選項跳轉（`-> Text #line:id <<jump Node>>`）、自訂演出指令（`<<bg>>`, `<<show>>`）與算術表達式（`<<set $var = $var + 1>>`），執行結果與 `MiniScriptRunner` 100% 一致。
  - **F6-2 ✅（多語言切換）**：串接 `ILocalizationService`，切換 `zh-TW` 與 `en` 時 `#line:` ID 維持穩定，台詞文字即時切換。
  - **F6-3 ✅（快照與回溯）**：完整支援 `Snapshot()` 與 `Restore()`，變數字典深拷貝隔離，不產生別名污染。
  - **F6-4 ✅（分級退回）**：支援 `RatingPolicy`，G 政策下進入 R18 節點自動跳轉至 fallback 節點，R18 政策下正常進入。

#### (5) 工單 F7: Addressables 分級 Group 隔離審計與 CI 建置閘門
- **實作產物**：[`client/Assets/Game/Core/Assets/AddressablesCatalogAuditor.cs`](file:///F:/game_dev/client/Assets/Game/Core/Assets/AddressablesCatalogAuditor.cs)、[`client/Assets/Game/Tests/AddressablesCatalogAuditorTests.cs`](file:///F:/game_dev/client/Assets/Game/Tests/AddressablesCatalogAuditorTests.cs)
- **驗證成果**：
  - **F7-1 ✅（分級規則）**：嚴格劃分 `content_g`、`content_r15`（主程式本體 Catalog）與 `content_r18`（成人 DLC Catalog）。
  - **F7-2 ✅（乾淨本體）**：主程式 Catalog 掃描結果 100% 無任何 R18 條目、無 R18 目錄素材，完全合法通過審計。
  - **F7-3 ✅（陽性對照）**：故意將 R18 評級素材注入 `content_g`、或將 `content_r18` 群組混入本體 Catalog、或素材帶有 `nsfw` 標籤時，CI 閘門立即觸發 `ERR_R18_KEY_IN_BASE_CATALOG` / `ERR_R18_FOLDER_IN_BASE_CATALOG`，強制拋出例外中斷建置！

#### (6) 工單 F9: Windows Standalone 遊戲建置
- **實作產物**：[`client/Assets/Game/Unity/Editor/BuildScript.cs`](file:///F:/game_dev/client/Assets/Game/Unity/Editor/BuildScript.cs)
- **驗證成果**：
  - 自動生成包含 `GameBootstrap`、`DebugMenu`、`BattleRunner`、`BattleDebugView` 之 [`Assets/Scenes/Boot.unity`](file:///F:/game_dev/client/Assets/Scenes/Boot.unity) 開發場景。
  - 成功輸出 Windows 64-bit Standalone 可執行檔：
    - **輸出路徑**：`F:\game_dev\build\Windows\GameClient.exe`
    - **檔案大小**：**154 MB**（161,788,253 bytes），建置耗時 37.93 秒。
  - 啟動測試：在 Windows 批次無圖形模式下啟動 `GameClient.exe` 執行正常，**Exit Code = 0**。

#### (7) 工單 F10: 多螢幕比例與解析度自適應
- **實作產物**：[`client/Assets/Game/Core/UI/ResolutionLayout.cs`](file:///F:/game_dev/client/Assets/Game/Core/UI/ResolutionLayout.cs)、[`client/Assets/Game/Unity/UI/ResolutionAdapter.cs`](file:///F:/game_dev/client/Assets/Game/Unity/UI/ResolutionAdapter.cs)、[`client/Assets/Game/Tests/ResolutionTests.cs`](file:///F:/game_dev/client/Assets/Game/Tests/ResolutionTests.cs)
- **驗證成果**：
  - **F10-1 ✅（標準 16:9）**：1080p（1920x1080）與 4K（3840x2160）維持基準解析度，虛擬畫布完全貼合，所有 UI 元素零裁切。
  - **F10-2 ✅（Steam Deck 16:10）**：1280x800（Steam Deck）與 1920x1200 自動判定為 `Narrow16x10`，將 CanvasScaler 調整為 `Match Width (0.0)`，畫布高度垂直延伸至 1200，安全框置中位移 `Y = 60`，左右兩側功能按鈕（出擊、選單、幣種）絕對不被裁切。
  - **F10-3 ✅（陽性對照）**：驗證若在 16:10 錯誤使用 Naive Match Height (1.0)，畫布寬度縮減至 1728，右側邊界 X=1800 之按鈕必定超出裁切；自適應佈局成功消除裁切問題。
  - **F10-4 ✅（超寬螢幕 21:9）**：2560x1080 與 3440x1440 自動判定為 `UltraWide21x9`，採用 `Match Height (1.0)`，背景寬度全滿延伸至 2560 無黑邊，16:9 核心內容區塊水平置中（OffsetX = 320）。

---

### 11. 缺陷日誌（BUG-001～BUG-009）獨立懷疑性審查與修復（發現並解決 BUG-010, BUG-011）

針對 `BUG_LOG.md` 與先前 Session 記載之缺陷修復進行獨立、抱持懷疑態度的代碼級深度審查與邊界測試，發掘出多項隱藏缺陷、文件與原始碼不一致處、以及先前宣稱「已受測試覆蓋」但實際為 0 測試的重大漏洞，並已依工程品質規範（G1–G5）全數完成紅綠回歸驗證：

#### (1) 逐項審查與真實性核實結論
1. **BUG-001（Verbatim 引號轉義）**：真實有效。`StoryCommandRegistryTests.cs` verbatim 字串 `""""` 在 C# 編譯為 `""`，成功重現並驗證引號未閉合與空參數之報錯機制。
2. **BUG-002（條件評估器 Null 防禦）**：字串類型已防禦，但**發掘出重大次生缺陷（BUG-010）**：數值類型（`char_level_gte` 等）調用 `Num => double.Parse` 仍未防禦，傳入 null 會直接拋出 `ArgumentNullException` 崩潰。
3. **BUG-003（MiniScript 外層括號）**：邏輯修復有效，但**抓出文件筆誤**：代碼實際命名為 `StripOuterParentheses`，先前文檔誤植為 `TrimMatchingParentheses`。
4. **BUG-004（NewPlayer 預設服裝解鎖）**：服裝解鎖有效，但**發掘出重大遺漏（BUG-011）**：初始角色未同時解鎖等級基礎 CG，導致新玩家圖鑑為空。
5. **BUG-005（SetTeam 空隊伍防禦）**：邏輯修復有效，但**抓出錯誤碼不一致**：原始碼回傳 `invalid_team`，先前文檔誤記為 `empty_team`。
6. **BUG-006（CountStars 零受傷邊界）**：真實有效。將 `<` 改為 `<=` 解決了 0% 零受傷通關 `0 < 0` 永假之邊界缺陷。
7. **BUG-007（MasterDataValidator 條件校驗覆蓋缺失）**：**抓出重大虛報與覆蓋缺漏**：
   - 經搜尋測試代碼，先前宣稱之「陽性對照測試」在 `MasterDataValidatorTests.cs` 中實際上為 **0 測試**。
   - `MasterDataValidator.CheckCondition` 完全遺漏了數值欄位（`Value`）的合法性校驗、遺漏了 `story_flag` 之 `Op` 比較運算子校驗、完全遺漏了 `enemy_base_hp_below_pct` 分支，且關卡驗證迴圈中 `w.Trigger` 從未傳入 `CheckCondition`！
   - **本次已補齊**：新增 `A2_5_32`～`A2_5_40` 共 9 個陽性對照單元測試，補齊所有條件之數值、枚舉與運算子靜態驗證，並在波次觸發器遞迴校驗。
8. **BUG-008（ReachEnding 與 UnlockCg 防禦）**：防禦有效，但原先缺乏專屬負向單元測試，且 `SetGlobalFlag` 遺漏了 key 的 null 防禦。本次已補齊 `SetGlobalFlag` 防禦並新增單元測試 `C4_2_NullOrWhitespaceInputs_SafelyIgnored_WithoutException`。
9. **BUG-009（轉蛋測試幣種不一致）**：真實有效。`banner_std` 需求幣種為 `gem`，給予 `gem` 解決了 PlayMode 測試失敗。

#### (2) 本次新修復之重大缺陷（BUG-010, BUG-011）
- **BUG-010（ConditionEvaluator 數值解析拋例外崩潰）**：
  - 改用 `TryNum(s, out double val)` 取代 `double.Parse`。當數值欄位為 null 或非法字串時，安全求值為 `false`，與其他條件類型完全保持一致。
  - 新增單元測試 `A3_8_NumericConditions_WithInvalidOrNullValue_SafelyReturnsFalse`，100% 綠燈。
- **BUG-011（NewPlayer 未解鎖初始角色等級基礎 CG）**：
  - 在 `GameCore.NewPlayer` 初始化走訪初始角色時，自動將 `cg_{ch}_{cs.CurrentRarity.ToLower()}` 登錄至 `Player.Gallery`；並於 `OnLoaded` 補上舊存檔回溯兼容。
  - 新增單元測試 `C4_5_NewPlayer_UnlocksStarterCharacterCg`，100% 綠燈。

---

### 12. 技術架構選型評估：Gemini 4 Argon 與 Firebase AI Logic Client-Side 方案

針對使用者提問「是否建議透過 Firebase AI Logic 在 Client-Side（Unity/Flutter/Mobile/Web）整合 Gemini 4 Argon 進行開發」，進行嚴謹的工程與遊戲架構評估：

- **核心結論**：**強烈不建議用於本專案（及同類型商業遊戲）的核心邏輯、戰鬥系統、劇情狀態機或成人內容分級（R15/R18）**；**僅適合於非敏感外圍功能（如 Editor 工具、客服、獨立原型）**。
- **評估依據（四大架構致命衝突）**：
  1. **確定性（Determinism）衝突**：本專案依賴純 C# 離線確定性模擬器（`BattleSimulator`），LLM 具備不可預測的非確定性，無法進行重現、回放、數值稽核與防作弊。
  2. **延遲與離線優先（Latency & Offline-first）**：Firebase AI Logic 雖然 SDK 在客戶端，但本質仍是呼叫雲端旗艦推理模型（TTFT 與推論耗時數秒至十數秒），破壞即時戰鬥迴圈（30 Ticks/sec），且完全失去離線可玩性。
  3. **商業合規與分級審查（Safety Filter vs R18 DLC）**：雲端商用 Gemini API 具備嚴格的 Safety Settings，遇到成人內容或敏感劇情會直接阻擋拒答，與本專案 ADR-003（G / R15 / R18 DLC 分離）嚴重衝突。
  4. **計費與安全風險（Token Cost & Prompt Injection）**：Argon 屬高階付費模型，客戶端直連容易面臨惡意刷量、越獄注入（Jailbreak）或成本失控；即使透過 App Check，仍缺乏業務層語意快取與配額閘門。
- **適用邊界**：僅建議於 Unity Editor 內部企劃輔助工具、社群外圍小工具或快速驗證用之玩具原型。
- **本地開發（Local-First）總體建議**：
  1. **遊戲核心**：維持純本地離線架構（Local-First / Offline-First），由純 C# 狀態機與本地 MasterData 保證 0 雲端伺服器營運成本、0 網路延遲、100% 確定性回放與完全避開雲端 NSFW 安全審查。
  2. **AI 生成落地策略**：若專案需融入生成式內容，首選**「開發期離線預生成（Offline Baking）」**打包為靜態 JSON/Yarn 資料；若需執行期即時生成，次選**「端側本地輕量模型（Unity Sentis / ONNX / llama.cpp）」**，絕不在遊戲關鍵路徑依賴雲端直連 API。
  3. **開發測試流**：維持本機 .NET 8 CLI 與 Unity CLI 批次測試環境，快速迭代且高可靠。

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
- **F Unity 技術驗證（F1～F10）**：**8/10 已完成**（F1, F2, F3, F4, F6, F7, F9, F10 通過 ✅）

### 下階段推進待辦項目（里程碑 F 剩餘 2 項，需外部 SDK 或 Steam 客戶端執行）
1. **F8: Steamworks.NET 平台初始化驗證**
   - 引入 Steamworks.NET 原生封裝，配置 AppID 480 進行離線/本機初始化測試，串接 `IPlatformService`。
2. **F5: Live2D Cubism SDK 整合**
   - 待使用者授權下載 Cubism 5 SDK package 後，掛載官方範例模型量測 1 個與 3 個模型之 FPS 與記憶體消耗。

