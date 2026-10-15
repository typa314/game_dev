# 04 資料模型

資料分成兩類：**主資料（Master Data，唯讀，企劃維護）** 和 **玩家狀態（Player State，存檔）**。
以下 schema 都是 JSON 示意；實際的 CSV 欄位一一對應。

## 1. 共用型別
```jsonc
"Rating": "G" | "R15" | "R18"            // 見 02 ADR-003
"LocText": "loc:<table>/<key>"            // 所有顯示文字都是 key，不放原文（見 12）
"AssetKey": "char/char01_claire/portrait/stage1/stand"   // 見 15
"Condition": { "type": "...", ...params } // 通用條件，由 ConditionEvaluator 判定（見第 4 節）
```

## 2. 主資料

### 2.1 Character
```jsonc
{
  "id": "char01_claire",                  // 沿用 presets/characters_dna.json 的 id
  "name": "loc:char/char01_claire.name",
  "rarity": "SSR",                        // N / R / SR / SSR
  "role": "ranged",                       // tank / melee / ranged / area / support（戰鬥定位，見 05）
  "battle": {
    "cost": 450, "cooldownSec": 8.0,
    "baseHp": 1200, "baseAtk": 180, "range": 320, "attackIntervalSec": 1.6,
    "moveSpeed": 10, "knockbacks": 3, "target": "single",  // single / area
    "traits": ["anti_flying"],
    "growthCurve": "curve_ssr_standard"
  },
  "portraitStages": ["ps_char01_s1", "ps_char01_s2", "ps_char01_s3"],   // 見 06
  "defaultCostume": "cos_char01_default",
  "storyRoute": "route_char01",           // 見 08
  "obtainableBy": ["gacha"],               // 只允許 gacha / initial（巧遇不招募角色，由驗證器檢查）
  "voice": { "cv": "loc:char/char01_claire.cv" }
}
```

### 2.2 PortraitStage（立繪階段）
```jsonc
{
  "id": "ps_char01_s2",
  "character": "char01_claire",
  "order": 2,
  "unlock": { "type": "all", "of": [
      { "type": "char_level_gte", "char": "self", "value": 30 },
      { "type": "char_awaken_gte", "char": "self", "value": 2 } ] },
  "rating": "G",
  "art": {
    "stand": "char/char01_claire/portrait/stage2/stand",
    "bust":  "char/char01_claire/portrait/stage2/bust",
    "live2d": "char/char01_claire/live2d/stage2"     // 可留空 → 降級顯示靜態立繪
  },
  "statBonus": { "atkPct": 5 }
}
```

### 2.3 Costume / Scene
```jsonc
{ "id": "cos_char01_summer", "character": "char01_claire", "rating": "G",
  "unlock": { "type": "achievement", "id": "ach_summer_event" },
  "art": { "stand": "...", "live2d": "..." }, "battleSkin": "..." }

{ "id": "scene_hotel_night", "rating": "G", "bg": "scene/hotel/night",
  "variants": { "day": "scene/hotel/day", "night": "scene/hotel/night" },
  "bgm": "bgm/lounge_01", "unlock": { "type": "story_node", "node": "route_char03.ch2.end" } }
```

### 2.4 Stage / Banner / Encounter / Achievement
各系統的詳細欄位分別寫在 [05](05_battle.md)、[07](07_gacha_and_encounter.md)、[13](13_achievements.md)。

## 3. 玩家狀態（PlayerState / Profile）
```jsonc
{
  "schemaVersion": 1,
  "profile": { "createdAt": "...", "locale": "zh-TW", "ratingOptIn": "R18", "ageConfirmedAt": "..." },
  "wallet": { "gold": 0, "gem": 0, "gachaTicket": 0, "encounterStamina": 5 },
  "items": { "exp_s": 12, "awaken_shard_char01": 3 },
  "characters": {
    "char01_claire": { "level": 32, "exp": 1200, "awaken": 2, "affection": 340,
                        "portraitStage": "ps_char01_s2",   // 玩家目前選用的階段（可以切回較低階段）
                        "costume": "cos_char01_default", "obtainedAt": "..." } },
  "costumes": ["cos_char01_default"],
  "scenes": ["scene_lobby_default"],
  "lobby": { "scene": "scene_lobby_default", "character": "char01_claire", "costume": "cos_char01_default" },
  "team": { "slots": ["char01_claire", null, null, null, null, null, null, null, null, null] },
  "stages": { "st_1_01": { "cleared": true, "stars": 3 } },
  "gacha": { "pity": { "banner_std": 37 }, "history": [/* 保留最近 N 筆 */] },
  "encounter": { "pity": 4, "cooldowns": {}, "seenEvents": [] },
  "story": { "globalFlags": {}, "readNodes": ["..."] },   // 跨存檔欄位的「已讀」資料
  "endings": { "route_char01.good": "2026-..." },
  "gallery": ["cg_char01_01"],
  "achievements": { "ach_first_pull": { "progress": 1, "unlockedAt": "...", "claimed": true } },
  "settings": { "bgm": 0.8, "se": 0.8, "voice": 1.0, "textSpeed": 40, "autoSpeed": 1.0 }
}
```

> 存檔的「雙層結構」（Profile 和劇情存檔欄位分開）見 [11](11_save_load.md)。

## 4. 通用條件語言（ConditionEvaluator）
劇情分支、巧遇觸發、立繪解鎖、成就、服裝解鎖都用**同一套條件 DSL**，所以只需要實作一次，也只需要測試一次。

| type | 參數 | 例子 |
|---|---|---|
| `all` / `any` / `not` | `of` / `cond` | 組合條件 |
| `char_owned` | char | 持有角色 |
| `char_level_gte` / `char_awaken_gte` / `affection_gte` | char, value | `"char": "self"` 代表目前角色 |
| `stage_cleared` | stage | |
| `story_flag` | key, op, value | `{"key":"route_char01.trust","op":">=","value":3}` |
| `story_node` / `ending_reached` | node / ending | |
| `achievement` | id | |
| `time_of_day` / `weekday` | value | 巧遇用（裝置當地時間） |
| `rating_allowed` | rating | 內部使用：目前版本和玩家設定是否允許這個分級 |

## 5. ID 命名規約
小寫加底線，並加上類型前綴：`char*`、`ps_`、`cos_`、`scene_`、`st_`（關卡）、`banner_`、`enc_`、`route_`、`cg_`、`ach_`、`item_`。
**ID 一旦發佈就不能改**，因為存檔是用 ID 引用資料。
