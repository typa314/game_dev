# 任務 OPT-2：共用程序化 UI 紋理快取庫 (`ProceduralUiKit`) 與 `ScreenNavigator` 統一橋接

> **狀態**：✅ 已完成 (100%)
> **執行日**：2026-11-05
> **依據**：[`design/19_optimization_and_m1_implementation_plan.md`](../../design/19_optimization_and_m1_implementation_plan.md) Step 2、[`client/GEMINI_PROTOCOL.md`](../GEMINI_PROTOCOL.md)
> **目標**：解決 `ARCH-01`（抽取共用且帶記憶體快取之 `ProceduralUiKit`，消除 `LobbyView` 與 `GachaView` 重複的逐像素紋理生成與記憶體洩漏）與 `ARCH-02`（建立 `UnityScreenBase` 實作 `IScreen`，讓畫面切換納入 `GameServices.Navigator` 堆疊管理）。

## 允許新增或修改的檔案（嚴禁修改範圍外檔案）
- `client/Assets/Game/Unity/UI/ProceduralUiKit.cs`（新增）
- `client/Assets/Game/Unity/UI/UnityScreenBase.cs`（新增）
- `client/Assets/Game/Unity/UI/LobbyView.cs`（修改）
- `client/Assets/Game/Unity/UI/GachaView.cs`（修改）
- `client/Assets/Game/Tests/ProceduralUiAndNavigationTests.cs`（新增）

## 實作細項與介面規格
1. **`ProceduralUiKit.cs` (`namespace Game.Unity.UI`)**：
   - 提供靜態快取方法：
     - `GetPillSprite(int w, int h, Color fill, Color border)`
     - `GetCircleSprite(int size, Color fill, Color border, int borderWidth)`
     - `GetGradientSprite(int w, int h, Color top, Color bottom, Color? border = null, int borderWidth = 0, int radius = 0)`
     - `GetSpeechBubbleSprite(int w, int h, Color fill, Color border)`
     - `GetHoloRingSprite(int w, int h, Color color)`
     - `GetSilhouetteSprite(int w, int h)`
   - 以量化後的 `Color32` 與尺寸組成結構體 Key 快取於靜態 `Dictionary`；`public static int CachedSpriteCount { get; }` 回傳目前快取數量；`public static void ClearCache()` 銷毀快取的 `Sprite` 與對應 `Texture2D` 並清空字典。
   - 提供共用 UI 節點輔助：`CreateUiObject(string name, Transform parent)`、`StretchFull(RectTransform rt, float margin = 0f)`、`GetDefaultFont()`。
2. **`UnityScreenBase.cs` (`namespace Game.Unity.UI`)**：
   - `public abstract class UnityScreenBase : MonoBehaviour, IScreen`
   - 實作 `IScreen`：`OnPush(object args)`、`OnPause()`、`OnResume()`、`OnPop()`、`OnBackRequested()`。
   - 維持與既有 `LobbyView.IsVisible`、`GachaView.Open(LobbyView)` / `Close()` 呼叫介面 100% 向下相容；當 `GameServices.Navigator != null` 時，同步維護 `Navigator` 堆疊狀態。
3. **重構 `LobbyView.cs` 與 `GachaView.cs`**：
   - 繼承 `UnityScreenBase`，將內部私有的 `CreatePillSprite`、`CreateGradientSprite` 等方法改為委派呼叫 `ProceduralUiKit`，大幅精簡重複代碼，同時確保既有 `LobbyViewTests` 與 `GachaViewTests` 全數通過。

## 查核點驗收結果
- **OPT2-1 ✅**：相同參數呼叫 `ProceduralUiKit.GetPillSprite`、`GetGradientSprite` 與 `GetCircleSprite` 多次，回傳同一個 `Sprite` 參考（`Assert.AreSame`），且 `CachedSpriteCount` 保持 1 不會重複增加；`ClearCache()` 成功將快取清空至 0。
- **OPT2-2 ✅**：驗證 `UnityScreenBase` 搭配 `ScreenNavigator` 的 `Push` -> `OnPause` -> `Pop` -> `OnResume` 生命週期與 `OnBackRequested` 子彈窗優先攔截行為；實體大廳與抽卡畫面在 Esc 按下時優先關閉彈窗，彈窗全關閉後才執行 Pop 返回大廳。
- **OPT2-3 ✅**：既有 `LobbyViewTests`（5 項）與 `GachaViewTests`（7 項）全數 100% 綠燈通過。
- **測試總覽**：
  - `dotnet test`：356/356 全部通過。
  - Unity EditMode：391/391 全部通過（耗時 28.07 秒）。
  - G3 陽性對照：故意破壞斷言為 999 捕捉紅燈（`Expected: 999, But was: 1`），還原後通過。

## 共同限制
- C# 9 以內：不使用 `record`、`init`、`file-scoped namespace`、`global using`。
- 註解使用繁體中文。
