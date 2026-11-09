# 任務 OPT-1：核心契約修復、服務註冊補齊與資產分級防線整合

> **依據**：[`design/19_optimization_and_m1_implementation_plan.md`](../../design/19_optimization_and_m1_implementation_plan.md) Step 1、[`client/GEMINI_PROTOCOL.md`](../GEMINI_PROTOCOL.md)
> **目標**：修復 `ARCH-03`（貨幣與角色預設 ID 與 `data/master` 不一致）與 `ARCH-07`（補齊 `GameServices` 橫切服務註冊，並於 `AssetService` 實作 `AssetCatalog` 分級第二層過濾防線）。

## 允許新增或修改的檔案（嚴禁修改範圍外檔案）
- `client/Assets/Game/Core/UI/CommonComponents.cs`
- `client/Assets/Game/Core/UI/Presenters.cs`
- `client/Assets/Game/Unity/GameServices.cs`
- `client/Assets/Game/Unity/GameBootstrap.cs`
- `client/Assets/Game/Unity/AssetService.cs`
- `client/Assets/Game/Tests/PresenterAndServiceTests.cs`
- `client/Assets/Game/Tests/AssetCatalogTests.cs`

## 實作細項與介面規格
1. **`CurrencyBarPresenter` (`CommonComponents.cs`)**：
   - `Refresh()` 讀取金幣改為查詢 `_core.Master?.Config?.Growth?.GoldItem ?? "gold"`（若為 0 則向下相容加上 `"item_gold"`）。
   - 免費鑽石/寶石改為查詢 `"gem"`（若 `"item_dia_free"` > 0 則向下相容加上 `"item_dia_free"`），確保既有單元測試與正式 `config.json`（使用 `"gold"` 與 `"gem"`）雙向 100% 兼容。
2. **`LobbyPresenter` (`Presenters.cs`)**：
   - `Refresh()` 與 `OnCharacterClicked()` 的預設場景 fallback 改為 `"scene_lobby_default"`（若 `Player.Lobby.Scene` 非空則優先使用），預設角色 fallback 改為 `"char01_claire"`（相容舊測試：若 `"char01_claire"` 不在 Master 中則退回 `"char_claire"`）。
3. **`GameServices` 與 `GameBootstrap` (`GameServices.cs`, `GameBootstrap.cs`)**：
   - 在 `GameServices` 新增可選靜態屬性：`ILocalizationService Localization`、`SettingsService Settings`、`AgeGateService AgeGate`、`RedDotService RedDots`、`StorySaveService StorySaves`（在 `Install` 提供預設 null 參數以維持向下相容，並在 `Clear()` 中重置）。
   - 在 `GameBootstrap.Boot()` 建立 `RedDotService(core)` 與 `StorySaveService(store, core)` 並傳入 `GameServices.Install`。
4. **`AssetService` (`AssetService.cs`)**：
   - 建構子新增可選參數 `AssetCatalog catalog = null`（保存為 `public AssetCatalog Catalog { get; }`）。
   - 在 `LoadSprite(string key)` 中，若 `Catalog != null`，先呼叫 `AssetResolveResult res = Catalog.Resolve(key, _rating)`；當 `!res.Allowed` 時，呼叫 `WarnOnce` 並直接回傳 `Placeholder`（不執行 `Resources.Load` 或檔案讀取）。
   - 新增 `LoadStreamingSprite(string relativePath, Vector2 pivot)` 與 `ClearCache()`（銷毀動態建立之 Texture2D 並清空 `_cache`），供 Unity UI 層統一載入 `StreamingAssets` 圖片。

## 查核點（必須在測試中覆蓋）
- **OPT1-1**：`CurrencyBarPresenter` 對 `GameCore.NewPlayer`（使用 `data/master` 正式設定：`gold=5000, gem=3000`）呼叫 `Refresh()`，View 收到的金幣為 `5000`、寶石為 `3000`、體力為 `5`。
- **OPT1-2**：`AssetService` 注入含 `R18` 條目的 `AssetCatalog` 時，在 `RatingPolicy(Rating.G, Rating.G)` 下呼叫 `LoadSprite("cg/cg_char01_t3_01")` 必定攔截並回傳 `Placeholder`。
- **OPT1-3**：既有 353 項 .NET 測試與 372 項 Unity EditMode 測試全數通過無退化。

## 共同限制
- C# 9 以內：不使用 `record`、`init`、`file-scoped namespace`、`global using`。
- `Game.Domain` 與 `Game.Core` 嚴禁引用 `UnityEngine`。
- 註解使用繁體中文。
