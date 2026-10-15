# 15 美術資產對接規範（程式 ↔ 生圖 Agent）

> 這份文件**不討論怎麼生成美術**，只定義「美術交付什麼、程式怎麼找到它」，讓兩條產線可以平行作業。

## 1. 邏輯資產 Key
程式只認得邏輯 key，不認得檔名：
```text
char/{charId}/portrait/{stageId|costumeId}/{stand|bust|face_{expr}}
char/{charId}/live2d/{stageId|costumeId}
char/{charId}/battle/{idle|walk|attack|hit|die}
char/{charId}/icon/{square|round}
cg/{cgId}
scene/{sceneId}/{variant}
enemy/{enemyId}/battle/...
ui/... , bgm/... , se/... , voice/{charId}/{lineId}
```

## 2. Manifest（對接的核心）
在 `data/asset_manifest.csv` 中，每一列對應一個資產：

| key | source（F:\game 底下的原檔路徑） | rating | status | note |
|---|---|---|---|---|
| char/char01_claire/portrait/ps_char01_s1/stand | sample/anime/characters_roster/anime_char01_claire_tsundere_stand_fullbody.png | G | draft | 需要去背 |
| char/char01_claire/portrait/cos_char01_academy/stand | sample/anime/characters_roster/anime_char01_claire_outfit2_academy_cardigan.png | G | draft | |
| cg/cg_char01_t3_01 | sample/anime/tier3_climax_poses/anime_tier3_char01_claire_pony.png | R18 | draft | |

- `status`：`placeholder` → `draft`（可以進遊戲測試）→ `final`（可以發行）。發行建置時，只要還有 `draft` 就跳出警告；有 `placeholder` 就讓建置失敗。
- **匯入腳本** `tools/import_assets.py`：讀取 manifest → 轉檔（縮放到目標尺寸、PNG 壓縮）→ 依 rating 複製到 `client/Assets/Game/Content/content_{g|r15|r18}/` → 產生 Addressables 的 key 對照表。
- **分級以 manifest 為準**，而且**匯入腳本會依來源資料夾強制檢查下限**：

| 來源資料夾 | 最低 rating |
|---|---|
| `tier1_dating/` | G（需人工確認） |
| `tier2_tease/` | **R18**（內容含露點） |
| `tier3_climax_poses/`、`tier4_aftermath/` | **R18** |
| `characters_roster/` | G（需人工確認） |

  如果 manifest 標示的分級低於資料夾的下限，匯入就會失敗。這可以防止 R18 素材被標錯分級，誤放進本體。

## 3. 現有美術庫和遊戲需求的差距
| 遊戲需要 | 現有狀況 | 差距 |
|---|---|---|
| 立繪 S1（50 位角色） | `characters_roster` 中有 stand_fullbody 和 bust | **需要去背（RGBA）**、統一尺寸和腳底基準線 |
| 立繪 S2 / S3（覺醒） | 沒有 | 每位角色還需要 2 張（`outfit2` 適合當服裝，不建議拿來當覺醒立繪） |
| 表情差分（ADV 用） | 只有少數樣品 | 每個造型至少需要 `neutral/smile/shy/angry/sad`，而且要頭身對位，頭部偏移 0 px |
| Live2D 用的分層 PSD | 沒有 | 見 [09](09_live2d.md)：先挑主要角色做 L1 |
| Q 版戰鬥單位 | 沒有 | 50 位角色加上敵人的動作序列：**這是新的美術類型，需要評估生成方式** |
| 場景背景（日、夜） | 沒有 | Lobby 和 ADV 用，初估 15–20 個場景 × 2–3 個時段 |
| G / R15 劇情 CG | Tier 1 有 8 張 | 本體版的劇情 CG 不足，需要補足 |
| R18 劇情 CG | Tier 2、3、4 各約 50 張 | 可以在 manifest 中對應到角色路線的章節 |
| UI、圖示、卡池橫幅 | 沒有 | 需要 UI 美術，或先用簡潔風格自製 |

## 4. 規格
| 類型 | 交付規格 | 遊戲內規格（匯入時轉換） |
|---|---|---|
| 立繪 stand | RGBA PNG，高度 ≥ 3000 px，腳底對齊畫布底部 | 高度 2048 |
| 立繪 bust / 頭像 | 由 stand 依固定框裁切（框的座標寫在 manifest） | 1024 / 256 |
| CG | RGB PNG 16:9，≥ 2560×1440（**現有 CG 多為直式，要評估是否改橫式或加上黑邊和背景**） | 1920×1080，圖鑑放大檢視保留原圖 |
| 場景 | RGB PNG 16:9，≥ 2560×1440；視差分層選做 | 1920×1080 ＋ 邊緣延伸（給 21:9 用） |
| Live2D | `.model3.json` 加上相關檔案，貼圖 2048 | 原樣使用 |
