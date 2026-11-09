# 任務 OPT-4：戰鬥畫面 (`BattlePageView`) 演出進化——技能 Cut-in、傷害/免疫跳字與狀態視覺化

> **依據**：[`design/19_optimization_and_m1_implementation_plan.md`](../../design/19_optimization_and_m1_implementation_plan.md) Step 4、[`design/05_battle.md`](../../design/05_battle.md)、[`client/GEMINI_PROTOCOL.md`](../GEMINI_PROTOCOL.md)
> **目標**：解決 `ARCH-06`（串接 `BattleRunner` 的 `TickEvents` 緩衝佇列至 `BattlePageView`，呈現角色施放大招時的半身 Cut-in 橫幅、主堡 `immune_ranged` 免疫提示、`shockwave_on_hp_loss` 震波警示、傷害/治療跳字以及單位 Buff/Debuff 狀態標籤）。
> **狀態**：✅ 已完成 (100% 通過全量回歸與陽性對照)

## 允許新增或修改的檔案（嚴禁修改範圍外檔案）
- `client/Assets/Game/Unity/Battle/BattleRunner.cs`
- `client/Assets/Game/Unity/Battle/BattlePageView.cs`
- `client/Assets/Game/Tests/BattlePageViewTests.cs`（新增）

## 實作細項與介面規格
1. **`BattleRunner.cs` 事件累積緩衝**：
   - 新增 `private readonly List<BattleEvent> _pendingViewEvents = new List<BattleEvent>()`。
   - 在 `StartBattle` 與 `Abort` 時清空 `_pendingViewEvents`。
   - 在 `Update()` 推進 `Sim.Step()` 時，將 `Sim.TickEvents` 內容加入 `_pendingViewEvents`（上限保留最近 256 筆防止無讀取時無限增長）。
   - 新增 `public void ConsumeViewEvents(List<BattleEvent> buffer)` 將累積事件複製至呼叫端 `buffer` 並清空 `_pendingViewEvents`。
2. **`BattlePageView.cs` 視覺回饋擴充**：
   - **角色大招 Cut-in 面板 (`_skillCutInPanel`)**：當收到 `BattleEventType.SkillCast` 時，顯示施放技能的角色名稱與大招名稱（如 `★ SKILL CAST // 女武神戰歌`）及角色半身圖，持續顯示 1.2 秒後自動淡出。
   - **主堡特性視覺回饋 (`_baseAlertText`)**：
     - 收到 `BattleEventType.BaseImmune` 時，在畫面主堡區顯示醒目警示 `🛡️ BASE IMMUNE! (主堡免疫此攻擊類型)`。
     - 收到 `BattleEventType.BaseShockwave` 時，顯示 `⚡ BASE SHOCKWAVE! (主堡反擊衝擊波)`。
   - **單位狀態標籤**：在單位名牌或 HP 旁標示當前生效狀態（如 `[ATK↑]`、`[SLOW]`、`[PSN]`、`[BRN]`、`[STUN]`）。
   - **暴露唯讀測試屬性**：`LastSkillCutInText`、`LastBaseAlertText`，方便 EditMode 測試精確斷言。

## 查核點驗收成果
- **OPT4-1**：單一幀內連續執行多次 `Sim.Step()`，`BattleRunner.ConsumeViewEvents` 能完整取得所有 tick 產生的 `BattleEvent` 不漏失（陽性對照通過）。
- **OPT4-2**：在 `BattlePageView` 中當角色施放技能（`SkillCast`）時，Cut-in 面板啟動且 `LastSkillCutInText` 包含技能名稱（`"女武神戰歌"`）；當遠程角色攻擊 `st_1_03` 的 `immune_ranged` 主堡產生 `BaseImmune` 事件時，`LastBaseAlertText` 顯示免疫警示（包含 `"BASE IMMUNE"`）。
- **OPT4-3**：敵方主堡受到攻城單位高額扣血（超過 20% 門檻）產生 `BaseShockwave` 衝擊波時，主堡警報橫幅正確顯示 `"⚡ BASE SHOCKWAVE! (主堡反擊衝擊波)"`。
- **OPT4-4**：單位被施加 Slow / Poison / Burn / Stun / AtkBuff 時，單位頭頂狀態標籤即時反映 `[ATK↑]`、`[SLOW]`、`[STUN]` 等狀態。
- **OPT4-5**：事件累積佇列具備上限保護（Max 256），溢位時自動捨棄最舊事件保留最新事件，杜絕記憶體洩漏風險。

## 測試數據
- **.NET 8 CLI (`dotnet test client/DotnetTests`)**：**359 / 359 全部通過 (0 失敗 / 0 略過)**。
- **Unity 6 LTS EditMode 批次測試 (`-runTests`)**：**401 / 401 全部通過 (0 失敗 / 0 略過)**（耗時 27.80 秒，證書：`C:\Users\typa3\agent_tmp\game_dev\opt4_test_results.xml`）。
- **G3 陽性對照**：將 Cut-in 斷言故意改為 `"fake_skill_g3_fail"`，測試立即捕捉失敗並拋出 `Expected: String containing "fake_skill_g3_fail", But was: "★ SKILL CAST // 女武神戰歌"`；還原後 100% 綠燈恢復。
- **Validator**：`tools/ValidateData` 與 `tools/ValidateStory` 均 100% 通過。
