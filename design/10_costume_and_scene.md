# 10 換裝與場景系統

## 1. 服裝（Costume）
- 每套服裝屬於**特定角色**（`character` 欄位），沒有通用服裝，這樣就不需要處理「任何角色都要能穿任何衣服」的組合爆炸。
- 取得管道：預設服裝、好感等級、成就獎勵、巧遇結果、劇情章節完成、活動、日後的服裝 DLC。
- 服裝有 `rating`。本體版只會出現 G / R15 的服裝；R18 服裝放在成人 DLC。

### 造型決定規則（PortraitResolver）
```text
顯示造型 = 玩家選用的服裝（如果有，而且分級允許）
         ?? 玩家選用的立繪階段的預設造型
資產 key = 該造型的 art.{stand|bust|live2d}
          → 找不到 live2d 就用 stand（靜態）→ 找不到 stand 就用 placeholder
```
- 服裝可以宣告 `requiresPortraitStage`（例如覺醒後的限定服裝）。
- 劇情可以用 `<<show ... costume=xxx>>` **強制指定**服裝（例如泳裝回合）。強制指定只在該段劇情中有效，**不改變**玩家的設定。
- 戰鬥皮膚 `battleSkin` 是選填欄位；沒有填就沿用預設的 Q 版戰鬥單位。

### 服裝和 Live2D 的對應方式
| 做法 | 適用情況 |
|---|---|
| **每套服裝一個獨立模型**（建議） | 版型差異大的服裝；rigging 彼此獨立、互不影響 |
| 同一個模型切換 Part 的顯示 | 只有配件差異（帽子、眼鏡）；在對照表中用 `parts: {"Hat": 1}` 描述 |

## 2. 場景（Scene）
場景在兩個地方使用：
1. **Lobby 主畫面**：玩家選一個背景、一位角色、一套服裝，組合成自己的主畫面。
2. **ADV 劇情**：`<<bg scene_id variant>>`。

```jsonc
{
  "id": "scene_cafe", "name": "loc:scene/scene_cafe.name", "rating": "G",
  "variants": { "day": "scene/cafe/day", "evening": "scene/cafe/evening", "night": "scene/cafe/night" },
  "autoVariantByClock": true,          // Lobby 中依現實時間自動切換日夜
  "ambient": { "bgm": "bgm/cafe", "loopSe": "se/cafe_crowd", "fx": "fx/steam" },
  "lighting": { "day": "#FFFFFF", "night": "#C8D0FF" },   // 角色立繪的色調乘數，讓角色融入場景
  "unlock": { "type": "stage_cleared", "stage": "st_2_01" }
}
```
- **光線調和**：依場景時段，對角色立繪（包含 Live2D）套上乘色和邊緣光，避免角色看起來像「貼在背景上」。這是用低成本提升沉浸感的重點。
- 場景可以加上簡單的**視差層**（前景、中景、遠景），讓滑鼠移動時產生輕微位移。

## 3. 換裝與佈置畫面（Dressing Room）
```text
┌───────────────────────────────────────────────┐
│ [場景縮圖列 ◀ ▶]                       [儲存] │
│                                               │
│        （所選場景 ＋ 角色 Live2D 即時預覽）     │
│                                               │
│ 角色：[頭像列]  立繪階段：[S1][S2][S3🔒]       │
│ 服裝：[卡片列，未解鎖的顯示剪影＋取得方式]      │
└───────────────────────────────────────────────┘
```
- 預覽**不需要確認**，即時套用；按下儲存時才送出 `IGameService.SetLobby` 和 `EquipCostume`。
- 可以儲存 3 組「Lobby 預設」，方便快速切換。
