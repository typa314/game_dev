# 07 轉蛋與巧遇

- **轉蛋是取得角色的唯一管道**（另外還有遊戲開始時的初始角色）。
- **巧遇不招募角色**，只和**已持有**的角色互動：提升好感度、推進劇情、取得覺醒碎片和道具。

## 1. 轉蛋（Gacha）

### 1.1 主資料：Banner
```jsonc
{
  "id": "banner_std", "type": "standard",          // standard / pickup / beginner
  "name": "loc:gacha/banner_std.name", "art": "gacha/banner/std",
  "cost": { "single": { "gem": 150 }, "ten": { "gem": 1500 }, "ticket": 1 },
  "rates": { "SSR": 0.03, "SR": 0.12, "R": 0.85 },
  "pool": { "SSR": ["char01_claire", "..."], "SR": ["..."], "R": ["..."] },
  "pickup": { "chars": ["char03_scarlet"], "shareOfRarity": 0.5 },  // pickup 卡池才有
  "pity": { "hardAt": 100, "rarity": "SSR", "carryOver": false },
  "tenPullGuarantee": "SR",
  "period": { "start": null, "end": null }
}
```

### 1.2 抽取演算法（GachaEngine，Domain 層）
```text
for 每一抽：
  pity += 1
  if pity >= hardAt → 稀有度 = SSR（天井）
  else 依 rates 抽稀有度
  if 十連的最後一抽而且前 9 抽都沒有 ≥ SR → 稀有度至少為 SR
  抽到 SSR → pity 歸零
  在該稀有度的池中抽角色（pickup 卡池先以 shareOfRarity 決定是不是 pickup 角色）
  重複角色 → 轉換成覺醒素材（★ 未滿）或「心之結晶」貨幣（★ 已滿）
```
- `IRandom` 是可以注入的介面：測試時用固定 seed，正式執行用系統 RNG。
- **機率公開**：轉蛋畫面有「機率詳情」頁，顯示每個稀有度和每個角色的機率（從主資料計算，不手寫），以及天井規則。
- **統計驗證**：單元測試抽 100 萬次，各稀有度的實際比率要落在理論值的 99.9% 信賴區間內；天井和十連保底要逐一驗證。
- 轉蛋紀錄保留最近 200 筆，可以在遊戲內查詢。

### 1.3 演出
點選 → 轉場動畫（顏色預告稀有度）→ 逐一或一次揭曉 → 新角色先顯示**立繪 S1 ＋ Live2D 登場動作＋語音**。可以快轉跳過，但第一次獲得的角色一定完整播放一次。

### 1.4 貨幣來源（ADR-004：遊戲內貨幣限定）
關卡首次通關、星級、成就、巧遇、每日／每週任務、劇情章節完成。**不提供真錢購買**。

## 2. 巧遇（Encounter）

### 2.1 玩法
- 主畫面有一張「城市地圖」，上面有多個地點：咖啡廳、學院、公園、酒店酒吧…。地點透過通關關卡或劇情解鎖。
- 玩家花費**巧遇體力**（每 N 分鐘回復 1 點，現實時間；上限 5）前往一個地點，系統依條件挑選一個**巧遇事件**。
- 巧遇事件就是一段短劇情（使用 [08](08_story_branching.md) 的 StoryPlayer），包含 1–2 個選項。結果可能是好感度、覺醒碎片、服裝或道具。**沒有招募**。
- **指定角色的事件，只有在玩家已持有該角色時才會出現**（由 EncounterEngine 判定，不依賴各事件的條件設定）。
- 每個角色可以有一串**巧遇系列**事件（例如 3 段），依順序推進，做為角色的日常小故事。系列完成時可以給予專屬服裝或大量好感度。

### 2.2 主資料：EncounterEvent
```jsonc
{
  "id": "enc_char07_kagura_01", "spot": "spot_park", "character": "char07_kagura",
  "series": "encs_char07", "order": 1,
  "weight": 10,
  "conditions": { "type": "all", "of": [
      { "type": "time_of_day", "value": "night" },
      { "type": "stage_cleared", "stage": "st_2_05" } ] },
  "story": "Encounter_Kagura_01",          // Yarn 節點
  "outcomes": {                             // 沒有 recruit；可用 affectionChar / item / costume
    "choice0": [{ "affectionChar": "char07_kagura", "n": 30 }],
    "choice1": [{ "item": "awaken_shard_char07_kagura", "n": 5 }]
  },
  "rating": "G",
  "repeatable": false
}
```

### 2.3 挑選演算法（EncounterEngine）
1. 篩選出同一地點、條件成立、分級允許，**而且角色已持有**的事件；系列事件只取「系列中的下一段」。
2. 依 `weight` 加權抽一個事件。
3. 巧遇保底：連續 N 次沒有出現任何系列事件，下一次強制出現系列事件。
4. 沒有可用的事件時，觸發通用的「路人小事件」，給予少量道具，避免玩家撲空。

### 2.4 時間
`time_of_day` 使用玩家裝置的本地時間。這是單機遊戲，玩家改系統時間只會影響自己，不做防範。日後如果改成連線版，再改用伺服器時間。
