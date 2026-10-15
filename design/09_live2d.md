# 09 Live2D 整合

## 1. 重要前提（美術管線限制）
- Live2D 模型是把**分層 PSD**（前髮、後髮、眼白、虹膜、上下眼皮、嘴、手臂、身體、衣服各自一層）匯入 Cubism Editor，再由 rigger **手工綁定參數和製作動作**。
- **現有的 AI 生成立繪是單張平面 PNG，無法直接轉成 Live2D 模型。** 每個角色、每個立繪階段或服裝，都需要：
  1. 拆層成 PSD（人工繪製，或用 AI 輔助拆層後再人工修整）
  2. 在 Cubism Editor 中綁骨並製作動作（需要 Cubism Editor Pro 授權）
- 以 50 位角色 × 3 個立繪階段 × 多套服裝來算，數量非常龐大。**建議分級導入**：

| 等級 | 對象 | 呈現方式 |
|---|---|---|
| L0 | 所有角色的所有造型（預設） | 靜態立繪＋**程式動態效果**：呼吸縮放、輕微擺動、眨眼差分圖（如果有的話） |
| L1 | 主要角色（例如第一梯隊 char01–10）的目前立繪階段 | 完整 Live2D：待機、表情、觸碰反應、口型 |
| L2 | 人氣角色的最終立繪階段、限定服裝 | Live2D 加上專屬動作和背景互動 |

系統一律透過同一個介面呈現，所以**任何角色都可以之後再升級到 Live2D，不需要修改程式**。

## 2. 架構
```csharp
public interface IPortraitView {
    UniTask Load(PortraitSpec spec);        // 角色 + 立繪階段/服裝 + 分級
    void SetExpression(string expr);         // tsundere / shy / smile ...
    void PlayMotion(string motion, bool loop = false);
    void SetLipSync(float level);            // 0..1，由語音音量或口型資料驅動
    void LookAt(Vector2 screenPos);          // 視線跟隨滑鼠
    void OnTouched(TouchArea area);          // head / body / hand ...
}
// 實作：Live2DPortraitView（Cubism SDK）、StaticPortraitView（PNG＋程式動態效果）
```
- `PortraitFactory` 依資料決定實作：`art.live2d` 有值、資產存在，而且「設定 → 畫質」允許時，用 Live2D；否則用靜態立繪。
- 表情和動作名稱是**統一的語意詞彙**（見下表）。每個 Live2D 模型用一份對照表，把語意詞對應到自己的 `.exp3.json` 和 `.motion3.json`；靜態立繪則對應到差分 PNG。劇情腳本只寫語意詞，不需要知道底下是哪一種實作。

| 語意詞彙（初版） | 說明 |
|---|---|
| 表情 | `neutral` `smile` `tsundere` `shy` `angry` `sad` `surprised` `seduce` |
| 動作 | `idle`、`greet`、`tap_head`、`tap_body`、`happy`、`awaken`（覺醒演出用） |

## 3. 使用場景
| 場景 | 行為 |
|---|---|
| Lobby 主畫面 | 待機動作、視線跟隨滑鼠、點擊部位觸發台詞和動作（台詞依好感等級開放）、閒置一段時間後說閒聊台詞 |
| ADV 劇情 | `<<expr>>` / `<<motion>>` 指令驅動；說話時用語音音量驅動口型，沒有語音時用文字逐字顯示驅動簡易開合 |
| 角色詳情 | 全身展示，可以拖曳旋轉視角（參數 AngleX/Y） |
| 轉蛋、覺醒演出 | 播放 `greet` / `awaken` 動作 |

## 4. 效能規則
- 畫面上同時存在的 Live2D 模型 ≤ 3；ADV 中不在畫面上的角色立即卸載。
- 模型貼圖 atlas 以 2048 為主；4096 只給全螢幕展示用的模型。
- 只有一個模型時（例如 Lobby），可用 RenderTexture 快取靜止畫面來省 GPU：沒有動作時降頻。
- M0 技術驗證要量測：在最低配備目標機上，3 個模型同時播放時的 FPS 和記憶體。

## 5. 授權
- Cubism SDK for Unity：需同意 Live2D 的 SDK 授權條款，並依營收規模判斷是否需要簽訂出版授權。
- Cubism Editor：做模型的人需要 Pro 版授權。
- **上述條款以 Live2D 官網最新版本為準，商業發行前需要確認。**
