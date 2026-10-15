# BUG_LOG.md — 程式碼錯誤與缺陷排查紀錄 (F:\game_dev)

> 本文件記錄 `F:\game_dev` 遊戲主程式開發過程中所發現之所有程式碼缺陷、排查過程、根本原因分析、修復方式與自動化回歸驗證結果。
> 作為跨 Agent 與跨 Session 之持久化歷史紀錄。

---

## 缺陷總覽表 (Defect Ledger)

| 缺陷編號 | 嚴重等級 | 模組類別 | 缺陷名稱 | 涉及檔案 | 狀態 | 驗證測試 |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| **BUG-001** | 🔴 Fatal | 測試套件 | Verbatim 字串引號轉義錯誤導致測試中斷 | `client/Assets/Game/Tests/StoryCommandRegistryTests.cs` | 已修復 ✅ | `C2_ErrorHandling_Ending_BlankIdOrInvalidCount_ReportsError` |
| **BUG-002** | 🔴 High | 領域條件 | `ConditionEvaluator` 字典查詢缺乏防禦性 Null 檢查 | `client/Assets/Game/Domain/Conditions/ConditionEvaluator.cs` | 已修復 ✅ | `ConditionEvaluatorTests` |
| **BUG-003** | 🔴 High | 劇情引擎 | `MiniScriptRunner` 表達式求值缺乏函式注入與外層括號支援 | `client/Assets/Game/Core/Story/MiniScript/MiniScriptRunner.cs` | 已修復 ✅ | `StoryRunnerTests` |
| **BUG-004** | 🟡 Medium | 玩家領域 | `NewPlayer` 初始角色預設服裝未解鎖至 `Player.Costumes` | `client/Assets/Game/Core/GameCore.cs` | 已修復 ✅ | `GameCoreTests` |
| **BUG-005** | 🟡 Medium | 戰鬥出擊 | `SetTeam` 允許全空或全 null 隊伍進入戰鬥 | `client/Assets/Game/Core/GameCore.cs` | 已修復 ✅ | `GameCoreTests` |
| **BUG-006** | 🟡 Medium | 關卡結算 | `CountStars` 零受傷滿星通關條件判定永假 (`< 0`) | `client/Assets/Game/Core/GameCore.cs` | 已修復 ✅ | `BattleSimulatorTests` |
| **BUG-007** | 🟡 Medium | 主資料驗證 | `MasterDataValidator` 條件檢查多個類型欄位驗證覆蓋缺失 | `client/Assets/Game/Domain/MasterData/MasterDataValidator.cs` | 已修復 ✅ | `MasterDataValidatorTests` |
| **BUG-008** | 🟢 Low | 防禦安全 | `ReachEnding` 與 `UnlockCg` 缺乏 Null / 空字串防禦 | `client/Assets/Game/Core/GameCore.cs` | 已修復 ✅ | `StoryCommandRegistryTests` |

---

## 詳細缺陷分析與修復報告

### BUG-001: Verbatim 字串引號轉義錯誤導致測試中斷
* **嚴重等級**：🔴 Fatal（阻斷自動化測試套件）
* **涉及檔案**：[`client/Assets/Game/Tests/StoryCommandRegistryTests.cs`](file:///F:/game_dev/client/Assets/Game/Tests/StoryCommandRegistryTests.cs)
* **症狀**：
  執行 `dotnet test` 時，`StateCommandsPublishExpectedEventsAndInvalidCommandsDoNotMutate` 拋出：
  ```text
  System.FormatException: Unmatched quote in command line.
     at Game.Core.Story.MiniScript.MiniScript.SplitCommandLine(String text)
     at Game.Core.Story.MiniScript.MiniScript.Parse(String scriptText)
  ```
  導致測試套件中斷，回報 1 failed。
* **根因分析**：
  在 C# verbatim 字串（`@"..."`）中撰寫指令 `command ending ""` 與 `command cg ""`，編譯器將 `""` 轉義為單個雙引號 `"`。傳遞給 `SplitCommandLine` 的文字變成 `command ending "`（只有 1 個引號未閉合），因而觸發解析器的未配對雙引號異常。測試原意是傳遞包含成對雙引號的空參數 `""`。
* **修復方案**：
  在 verbatim 字串中改寫為四重雙引號 `""""`（如 `command ending """"` 與 `command cg """"`），使其編譯為文字中的 `""`。
* **驗證結果**：
  測試案例重構為 `C2_ErrorHandling_Ending_BlankIdOrInvalidCount_ReportsError` 與 `C2_ErrorHandling_Cg_BlankIdOrInvalidCount_ReportsErrorAndDoesNotCallPresenter`，測試順利通過，不再拋出 `FormatException`。

---

### BUG-002: ConditionEvaluator 字典查詢缺乏防禦性 Null 檢查
* **嚴重等級**：🔴 High（執行期例外造成存檔或關卡判定崩潰）
* **涉及檔案**：[`client/Assets/Game/Domain/Conditions/ConditionEvaluator.cs`](file:///F:/game_dev/client/Assets/Game/Domain/Conditions/ConditionEvaluator.cs)
* **症狀**：
  當條件 JSON 未填寫 `stage`、`key`、`ending`、`id` 或 `rating` 欄位（預設為 `null`）時，在 .NET 8 下調用 `Dictionary.TryGetValue(null)` 或 `Enum.Parse(null)` 會直接拋出 `ArgumentNullException`。
* **根因分析**：
  `ConditionEvaluator` 各分支（`stage_cleared`, `story_flag`, `ending_reached`, `achievement`）未對欄位進行非空判定，直接傳入字典進行查詢。
* **修復方案**：
  在各條件分支進入字典前加入 `if (string.IsNullOrEmpty(...)) return false;` 防禦檢查；`rating_allowed` 改用 `Enum.TryParse` 避免無效枚舉拋出例外。
* **驗證結果**：
  通過條件求值回歸測試，所有空欄位均安全回傳 `false`。

---

### BUG-003: MiniScriptRunner 表達式求值缺乏函式注入與外層括號支援
* **嚴重等級**：🔴 High（劇情腳本運算與條件跳轉異常）
* **涉及檔案**：[`client/Assets/Game/Core/Story/MiniScript/MiniScriptRunner.cs`](file:///F:/game_dev/client/Assets/Game/Core/Story/MiniScript/MiniScriptRunner.cs)
* **症狀**：
  1. 腳本若寫複合算術賦值 `set x = affection("char01") + 5`，會拋出 `InvalidOperationException: Cannot resolve 'affection("char01")' to a number.`。
  2. 若條件語句外層包有成對括號（例如 `if (flag == 1) -> Node`），運算符掃描會因 `parenDepth > 0` 忽略 `==`，最後進入單元求值失敗報錯。
* **根因分析**：
  1. `MiniScriptExpressionEvaluator.EvaluateSetExpression` 中的 `ResolveNumber` 僅查詢 `variables` 字典，未注入 `functions`。
  2. 條件解析前未針對最外層之成對包裹括號進行剝除。
* **修復方案**：
  1. 擴展 `ResolveNumber` 支援傳入 `functions` 字典並調用委派。
  2. 在 `EvaluateCondition` 進入運算符掃描前，加入 `TrimMatchingParentheses` 剝除外層對稱括號。
* **驗證結果**：
  `StoryRunnerTests` 與相關複合表達式測試全數綠燈通過。

---

### BUG-004: NewPlayer 初始角色預設服裝未解鎖至 Player.Costumes
* **嚴重等級**：🟡 Medium（服裝系統狀態不一致）
* **涉及檔案**：[`client/Assets/Game/Core/GameCore.cs`](file:///F:/game_dev/client/Assets/Game/Core/GameCore.cs)
* **症狀**：
  開局初始化角色未將 `def.DefaultCostume` 加入 `Player.Costumes`。當玩家後續解鎖並更換為第二套服裝時，將因 `!Player.Costumes.Contains(def.DefaultCostume)` 無法換回預設服裝。
* **根因分析**：
  `NewPlayer` 使用靜態方法 `NewCharacterState` 初始化，未像 `ObtainCharacter` 那樣將 `def.DefaultCostume` 註冊至 `Player.Costumes`。
* **修復方案**：
  遍歷 `init.Characters` 時，若 `def.DefaultCostume != null`，調用 `p.Costumes.Add(def.DefaultCostume)`。
* **驗證結果**：
  新玩家建立後，所有初始角色的 `DefaultCostume` 均包含於 `Player.Costumes`。

---

### BUG-005: SetTeam 允許全空或全 null 隊伍進入戰鬥
* **嚴重等級**：🟡 Medium（戰鬥進入無效狀態）
* **涉及檔案**：[`client/Assets/Game/Core/GameCore.cs`](file:///F:/game_dev/client/Assets/Game/Core/GameCore.cs)
* **症狀**：
  客戶端傳入 `[]` 或全為 `[null, null, ...]` 的隊伍時，`SetTeam` 判定合法，導致玩家能帶一支「0 人」隊伍進入戰鬥模擬器。
* **根因分析**：
  `SetTeam` 僅限制 `slots.Count > 10` 與重複成員，未檢查有效出戰成員下限。
* **修復方案**：
  加入 `if (!slots.Any(c => c != null)) return Result.Fail("empty_team");` 檢查。
* **驗證結果**：
  設置空隊伍或全 null 隊伍時返回 `empty_team` 失敗結果，隊伍出戰人數至少須有 1 名非 null 角色。

---

### BUG-006: CountStars 零受傷滿星通關條件判定永假
* **嚴重等級**：🟡 Medium（關卡三星評價計算錯誤）
* **涉及檔案**：[`client/Assets/Game/Core/GameCore.cs`](file:///F:/game_dev/client/Assets/Game/Core/GameCore.cs)
* **症狀**：
  關卡星級條件設定為 `no_base_damage_pct_0`（受傷 0%）時，玩家完美防守未受任何傷害，卻無法獲得該星級。
* **根因分析**：
  原計算邏輯寫為 `lost < (long)pct * r.AllyBaseMaxHp`（嚴格小於）。當受損百分比上限 `pct == 0` 且受損 `lost == 0` 時，`0 < 0` 為 `false`。
* **修復方案**：
  將嚴格小於 `<` 改為小於等於 `<=`（`lost <= (long)pct * r.AllyBaseMaxHp`）。
* **驗證結果**：
  `BattleSimulatorTests` 驗證零受傷通關順利獲得對應星數。

---

### BUG-007: MasterDataValidator 條件檢查多個類型欄位驗證覆蓋缺失
* **嚴重等級**：🟡 Medium（資料靜態校驗漏判）
* **涉及檔案**：[`client/Assets/Game/Domain/MasterData/MasterDataValidator.cs`](file:///F:/game_dev/client/Assets/Game/Domain/MasterData/MasterDataValidator.cs)
* **症狀**：
  當 MasterData JSON 配置中缺少 `story_flag`、`ending_reached`、`time_of_day`、`weekday`、`rating_allowed` 等必要欄位或填寫錯誤值時，靜態驗證工具無法於建置期攔截。
* **根因分析**：
  `CheckCondition` 僅對部分類型撰寫了檢查邏輯，覆蓋不全。
* **修復方案**：
  補齊 `CheckCondition` 針對上述所有條件類型的必要欄位非空性與枚舉格式驗證。
* **驗證結果**：
  `MasterDataValidatorTests` 陽性對照測試能精確捕捉錯誤欄位並回報詳細錯誤位置。

---

### BUG-008: ReachEnding 與 UnlockCg 缺乏 Null 防禦
* **嚴重等級**：🟢 Low（防禦性程式碼缺陷）
* **涉及檔案**：[`client/Assets/Game/Core/GameCore.cs`](file:///F:/game_dev/client/Assets/Game/Core/GameCore.cs)
* **症狀**：
  傳入 `null` 或空白字串時，`Player.Endings.ContainsKey(null)` 拋出 `ArgumentNullException`；`Player.Gallery.Add(null)` 污染圖鑑集合。
* **根因分析**：
  入口處缺乏 `string.IsNullOrWhiteSpace` 防禦檢查。
* **修復方案**：
  在 `ReachEnding` 與 `UnlockCg` 開頭加入 `if (string.IsNullOrWhiteSpace(...)) return;`。
* **驗證結果**：
  傳入 `null` 或空白字串時靜默忽略，不拋出例外亦不變異玩家資料。

---

## 測試回歸結論
- 截至 2026-10-15，上述 8 項缺陷已全數修復完畢。
- 測試套件執行結果：**315 通過 / 0 失敗 / 0 略過**。
