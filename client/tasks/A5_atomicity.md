# A5 follow-up：GachaEngine 無有效機率失敗原子性

## 任務目標
核對並確保 `GachaEngine.Pull` 在 banner 沒有任何正機率稀有度時，於改動 pity 或呼叫 RNG 前，以 `InvalidOperationException` 拒絕正數抽取。

## 根因與證據
- A5-9 回歸測試將 `banner_std.Rates` 清空，設定 `pity = 7`，並使用計數型 Mock RNG。
- 陽性對照暫時移除 `ValidatePools` 中「至少一個正機率」檢查後，測試重現三項錯誤：收到 `ArgumentException: unknown rarity`、pity 由 7 變成 8、RNG 呼叫 1 次。
- 工作區目前的 `GachaEngine.cs` 已有正機率檢查；本任務要求你依原始碼確認它確實在任何狀態變更和隨機呼叫之前執行，並修正任何仍存在的契約缺口。

## 完成條件
- 正數抽取且 Rates 為 null、空字典或無正值時，先拋 `InvalidOperationException`。
- 該失敗路徑不呼叫 IRandom，也不修改 pity。
- 正機率稀有度池、啟用中的天井池、十連保底池都在抽取前驗證為非空。
- 不改變有效 banner 的抽取行為。
- 不要只回報驗收結論：若程式碼不符合上述條件，完成最小修正；若已符合，保持程式碼不變並說明逐項核對依據。

## 允許修改檔案
- 僅 `client/Assets/Game/Domain/Gacha/GachaEngine.cs`。
- 禁止修改測試、master data、工作清單、README、SESSION 或其他任何檔案。

## 限制
- C# 9；保留注入式 IRandom。
- `Assets/Game/Domain` 不得引用 UnityEngine 或 Newtonsoft。
- 不執行命令或測試，由監督者負責驗收。
- 不使用 `--dangerously-skip-permissions` 或 `--remote-control`。
