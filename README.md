# Tower Game - 遊戲主程式與系統開發專案 (Game Development Project)

本專案為二次元戀愛互動塔防/戰鬥遊戲之**遊戲主程式客戶端（Client）、數值設定（Data）與系統設計企劃（Design）**集中開發庫。

---

## 1. 專案目錄結構

```text
F:\game_dev\
├── client/                     # Unity C# 遊戲客戶端主程式
│   ├── Assets/Game/            # 遊戲核心程式碼 (Core, Battle, Story, UI, Systems)
│   ├── DotnetTests/            # .NET 8 單元測試與回歸測試套件 (215+ tests)
│   └── tasks/                  # 開發工單與階段性驗收任務
├── data/                       # 遊戲 Master Data (數值、角色、關卡、故事等配置)
│   └── master/                 # characters.json, stages.json, config.json 等
├── design/                     # 系統企劃與技術架構規格書 (01 ~ 17)
│   ├── 01_overview_and_core_loop.md
│   ├── 02_tech_stack_and_platform.md
│   ├── 03_client_architecture.md
│   ├── 04_data_model.md
│   ├── ...
│   ├── 15_asset_pipeline_contract.md  # 與美術資產庫的對接規範
│   ├── 16_roadmap.md
│   └── 17_framework_worklist.md       # 框架任務清單 (A1~A10, B1~B6, C1~C8)
├── presets/                    # 角色 DNA 配置快照
├── tools/                      # 遊戲資產匯入與輔助打包工具
├── CHARACTERS_ROSTER_DNA.md    # 角色設定與特徵資料庫
├── PRE_PRODUCTION_PLANNING.md  # 前期系統與分鏡企劃
├── SESSION.md                  # 遊戲主程式開發交接與進度日誌
└── README.md                   # 本說明文件
```

---

## 2. 與美術資產庫 (F:\game) 的分工協作

依據架構設計規範，美術與程式採雙軌平行作業：
- **美術圖庫 (`F:\game`)**：專責 SD / Forge 批次生圖、Stable Diffusion 模型管理、Lora、Wildcards、Prompt 模板、各 Tier CG 生成與原圖儲存。
- **遊戲主程式庫 (`F:\game_dev`)**：專責 Unity 遊戲邏輯、狀態機、數值演算、戰鬥系統、劇情解析器與 UI 呈現。
- **對接媒介**：依據 `design/15_asset_pipeline_contract.md` 規範，由 `data/asset_manifest.csv` 映射 `F:\game` 底下的原檔路徑，並透過資產匯入工具轉檔導入 `client/Assets/Game/Content/`。

---

## 3. 測試與驗證

本專案全面採取測試驅動與自動化驗證：
```bash
# 執行全部 230+ 項單元與回歸測試
dotnet test client/DotnetTests/Game.DotnetTests.csproj
```
所有主程式邏輯改動必須在通過全部單元測試且無回歸後方可提交。
