# 遊戲系統架構設計（Design Index）

> 版本 v0.1｜2026-10-14｜狀態：**草案，等待使用者確認關鍵決策**
> 範圍：系統架構。美術生成另見根目錄的 `PRE_PRODUCTION_PLANNING.md`。
> ⚠ **本專案禁止外洩**：所有資料只保留在本機；不 push、不使用雲端服務、不送外部 AI。

## 一句話
Steam PC 平台的「類貓咪大戰爭」推線塔防，加上角色收集（只透過轉蛋）、三軸養成（等級、覺醒換立繪、好感度）、多結局 ADV 劇情、Live2D、換裝與場景、雙層存檔、多語言和成就。

## 文件地圖
| # | 文件 | 重點 |
|---|---|---|
| 01 | [概觀與核心循環](01_overview_and_core_loop.md) | 系統地圖、三條成長軸、設計原則 |
| 02 | [技術選型與平台（ADR）](02_tech_stack_and_platform.md) | Unity 6 ＋ Cubism SDK ＋ Yarn Spinner；Steam；本體＋成人 DLC 分級 |
| 03 | [用戶端架構](03_client_architecture.md) | 分層、IGameService 命令、事件匯流排、AssetService、目錄結構 |
| 04 | [資料模型](04_data_model.md) | 主資料 schema、PlayerState、通用條件 DSL、ID 規約 |
| 05 | [戰鬥](05_battle.md) | 決定性模擬、tick 流程、Stage／wave 資料 |
| 06 | [養成與立繪階段](06_character_growth_portrait.md) | 等級、覺醒、好感度；升級後換立繪 |
| 07 | [轉蛋與巧遇](07_gacha_and_encounter.md) | 卡池、天井、機率公開；巧遇只和已持有角色互動（好感、碎片），不招募 |
| 08 | [劇情與多重結局](08_story_branching.md) | Yarn 腳本、自訂指令、分級 fallback、變數範圍、分支圖 |
| 09 | [Live2D](09_live2d.md) | L0／L1／L2 分級導入、IPortraitView、效能和授權 |
| 10 | [換裝與場景](10_costume_and_scene.md) | 造型決定規則、場景時段與光線調和、Dressing Room |
| 11 | [存檔與讀檔](11_save_load.md) | Profile 和劇情存檔欄位雙層設計、原子寫入、遷移、只存本機 |
| 12 | [多語言](12_localization.md) | String Table 和 Yarn line ID、字型、離線翻譯流程 |
| 13 | [成就](13_achievements.md) | 事件驅動的追蹤器、counter 類型、Steam 對應（選用） |
| 14 | [介面畫面](14_ui_screens.md) | 畫面流程、Lobby、戰鬥、角色詳情、ADV 的線框圖 |
| 15 | [美術資產對接規範](15_asset_pipeline_contract.md) | 邏輯 key、manifest、分級下限、美術差距清單 |
| 16 | [路線圖](16_roadmap.md) | M0–M4 範圍和驗收、主要風險 |
| 17 | [框架工作清單與查核點](17_framework_worklist.md) | 框架和可行性驗證的每一項工作、查核點、狀態、執行者 |

## 需要使用者決定的事項
| # | 決策 | 建議 | 影響 |
|---|---|---|---|
| D1 | 引擎 | **Unity 6 LTS** | 全部 |
| D2 | 成人內容的發行方式 | **本體（G/R15）＋免費成人 DLC** | 建置流程、美術排程（本體需要另外補 G 版 CG） |
| D3 | 商業模式 | **買斷制，轉蛋只用遊戲內貨幣** | 不需要伺服器、避開抽獎法規 |
| D4 | 是否同步 Steam 成就 | 預設只存本機；同步與否由你決定 | 對外通訊 |
| D5 | 主要角色（Live2D L1 和完整路線的對象） | 第一梯隊 char01–char10 | 美術和劇本排程 |

## 下一步
使用者確認 D1–D3 後，開始 M0 技術驗證：建立 `F:\game\client` 的 Unity 專案骨架，並驗證 Live2D、Yarn、Addressables 分級和 Steamworks。
