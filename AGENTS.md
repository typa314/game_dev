# 專案 Agent 規則

適用於在本 repository 工作的所有 agent（Codex、Claude、Gemini 及其他自動化工具）。開始生圖、編輯角色 seed 或新增生成紀錄前，必須先閱讀 `design/18_image_generation_seed_tracking.md`。

## 角色生圖 Seed 與紀錄

- `F:\game\generation\character_seed_db.json` 是所有遊戲與美術 agent 共用的角色 seed 指派及生圖執行紀錄資料庫；結構依 `F:\game\generation\character_seed_db.schema.json`。不得只在 prompt、聊天紀錄或暫存 log 留下 seed。
- 50 位角色的基準 seed 已從 `presets/characters_dna.json` 登記。這些是 seed 指派，不代表對應圖片已生成或驗收。
- 同一角色、同一資產版本的重現生成必須沿用原紀錄的 seed 與生成參數。探索新版本可選新 seed；每次嘗試都要新增獨立紀錄，包含失敗與被拒絕的結果。
- 動作圖使用「角色 + 動作 + 資產/變體」各自的 seed 紀錄。不可讓全體角色共用同一個動作 seed；同一組角色與動作的重現沿用原 seed，新變體另建紀錄並填 `action_key`、`variant_id`。
- 每次生圖都明確把 planned 紀錄中的 seed 傳給生圖工具；不可依賴工具預設 seed 或隱藏的批次遞增。若工具不能指定 seed，先停止該次生成並回報，不可留下不可追溯的輸出。
- 生圖前先登記 `planned` 紀錄；完成後更新該筆結果與輸出雜湊。終態紀錄不得覆寫或刪除。修正結果時新增紀錄並用 `parent_record_id` 關聯。
- 每筆紀錄至少包含角色 ID、asset key、seed、精確 prompt/negative prompt、生成工具與版本、checkpoint 檔名與 SHA-256、sampler、steps、CFG、尺寸、LoRA/ControlNet 與參考圖資訊、時間、輸出相對路徑及輸出 SHA-256。工具不支援的欄位填 `null` 或空陣列並說明；不得猜填。
- Seed 必須是 `0..4294967295` 的整數；禁止 `-1`、隱式隨機 seed 或未記錄的自動 seed。Seed 單獨不足以重現圖片，所有實際影響結果的參數都要記錄。
- 角色基準 seed 以資料庫為記錄來源，修改時同步更新 `presets/characters_dna.json` 的 `default_seed`。保留其他角色及既有紀錄，不要整檔重建或重排。
- 多 agent 同時工作時，各 agent 先在自己的變更中新增紀錄；整合者合併前檢查 `run_id` 唯一性並保留所有紀錄，避免後寫覆蓋先寫。
- 只有使用者明確接受或指定驗收者留下驗收證據後，才可標成 `accepted`。回報時清楚區分已生成、已驗收與未驗證。

## 工作目錄與 Unity 執行資料

- 唯一的工作副本是本機 `C:\Users\typa3\dev\game_dev`。Unity 專案（`client/`）只能從這個本機路徑開啟、建置與測試。
- Unity 及 dotnet 的執行與建置資料（`Library/`、`Temp/`、`Logs/`、`UserSettings/`、`obj/`、`bin/`、`build/`）不得寫到 Google Drive（`F:`、`G:`、`H:` 都是 Drive 虛擬磁碟），避免暫存檔反覆上傳干擾環境。
- 舊的 Drive 副本（`H:\...\我的筆記型電腦\game_dev`／`F:\game_dev`）不可再當工作目錄使用，也不可在那裡開 Unity 或執行 build/test。

## 其他工程規範

- 遵守使用者提供的工程品質規範、程式碼範圍限制與檔案存放規則；本檔只補充生圖 seed 流程，不取代它們。
- 未獲授權不得將檔案或訊息送往外部服務；使用外部生圖服務前遵照使用者對該工具的授權及專案管線要求。
