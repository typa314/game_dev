# 18 角色生圖 Seed 與生成紀錄

## 目的與資料來源

`F:\game\generation\character_seed_db.json` 是角色基準 seed 與每次生圖嘗試的共用版本化紀錄；格式定義位於同目錄的 `character_seed_db.schema.json`。`F:\game\presets\characters_dna.json` 保留角色 DNA 和生圖工具使用的 `default_seed` 欄位；資料庫中的 `seed_assignments` 是 seed 指派紀錄，兩處數值必須一致。

資料庫初始匯入了 50 位美術角色 roster 目前的 `default_seed`。我核對時，`presets/characters_dna.json` 有 50 位、`data/master/characters.json` 有 5 位；多出的 seed 指派屬於尚未出現在遊戲 master data 的美術角色，不應據此推斷角色已可在遊戲中使用。這只代表已有基準 seed 指派，**不代表已經用這些 seed 生成圖片**。只有 `generation_records` 中有完整輸出資訊的項目，才是生圖執行紀錄。

## Seed 使用規則

1. 角色初始外觀使用該角色的 `character_identity_baseline` seed。
2. 動作圖按「角色 + 動作 + 資產/變體」記錄 seed；不同角色的同一動作不共用同一 seed。這可分開追溯每個角色的動作版本，不代表 seed 本身能鎖定姿勢或保證畫面一致。
3. 重現同一資產版本時，沿用原紀錄的 seed、prompt、模型與參數。Seed 不可單獨視為可重現的充分條件。
4. 新服裝、表情、構圖、動作變體或其他變體可分配新 seed；每次執行都建立新的紀錄，不得以新值覆蓋舊紀錄。
5. 每次生圖都明確把 planned 紀錄中的 seed 傳給生成工具；不可依賴工具預設 seed 或未登記的批次遞增。工具無法指定 seed 時，先停止生成並回報。
6. Seed 使用 `0..4294967295` 整數。禁止 `-1`、未記錄的隨機 seed 或把一次失敗嘗試從歷史中刪除。
7. 每筆 `generation_records` 以 UUID `run_id` 識別。狀態依序為 `planned` → `generated` → `accepted` 或 `rejected`；生成失敗使用 `failed`。只有使用者明確接受或指定驗收者留下驗收證據後才可使用 `accepted`。每筆只可由 `planned` 進入結果狀態，結果狀態不可回退或覆寫。
8. 輸出路徑以美術資產庫根目錄 `F:\game` 為基準使用相對路徑；原始輸出留在資產庫。暫存 log、截圖和中間產物依工程規範放在 `C:\Users\typa3\agent_tmp\`。

## 資料格式

資料庫根物件包含 `schema_version`、seed 範圍、`seed_assignments` 與 `generation_records`。每筆 `seed_assignments` 包含 `character_id`、`seed`、`purpose`、`source`。結構驗證規則見 `data/generation/character_seed_db.schema.json`；更新資料庫時應同時符合該 JSON Schema。

新增生成紀錄時至少包含以下欄位；`null` 只用於工具不提供的欄位，並在 `notes` 說明：

| 欄位 | 說明 |
|---|---|
| `run_id` | UUID，唯一識別一輪生成 |
| `character_id`、`asset_key` | 角色 ID 與 `design/15_asset_pipeline_contract.md` 定義的邏輯資產 key |
| `action_key`、`variant_id` | 動作代碼（非動作資產為 `null`）與明確的資產變體代碼 |
| `seed`、`status` | 實際使用的 seed 與本次執行狀態 |
| `started_at_utc`、`finished_at_utc` | ISO 8601 UTC 時間；尚未完成時 `finished_at_utc` 為 `null` |
| `prompt`、`negative_prompt` | 實際送給生成器的完整文字，不用概述代替 |
| `generator` | 工具名稱、版本與必要的工作流版本 |
| `checkpoint`、`vae` | 檔名與 SHA-256；無 VAE 時明確記錄 `null` |
| `parameters` | sampler、scheduler、steps、CFG、clip skip、寬高、批次數、denoise 等有效參數 |
| `adapters`、`references` | LoRA、ControlNet、參考圖的名稱/相對路徑、版本或雜湊與權重 |
| `output_path`、`output_sha256` | 輸出相對路徑和檔案 SHA-256；未產生輸出時為 `null` |
| `parent_record_id`、`notes` | 衍生生成的來源紀錄及必要說明；無來源時為 `null` |

每次嘗試都要記錄，包括失敗或被拒絕的結果。若基於既有圖片做 img2img/inpaint，必須把來源圖片列入 `references`，並記錄 denoise 與所有遮罩/控制資訊。不得用「seed 相同」推論結果必然逐像素相同；工具、模型、硬體和參數差異都要如實記錄。

## 多 Agent 協作

所有 agent 依 repository 根目錄 `AGENTS.md` 執行。多人同時生圖時使用不同 UUID 紀錄；整合時合併新增項目，不覆蓋資料庫既有紀錄。發現 seed 指派或紀錄不一致時先保留原始資料並回報，不自行猜測哪一筆正確。
