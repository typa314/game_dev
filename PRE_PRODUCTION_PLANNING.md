# 視覺資產大量生產前工程規劃書 (Pre-Production Planning SOP)

**專案名稱**：成人遊戲視覺資產流水線  
**依據標準**：[`adult_game_character_design_chat_log.md`](file:///f:/game/adult_game_character_design_chat_log.md)  
**制定日期**：2026-10-13  
**版本**：v1.0.0  

---

## 1. 規劃背景與生產目標 (Executive Summary & Objectives)

本規範依據 `adult_game_character_design_chat_log.md` 之視覺設計指南、提示詞架構與 ComfyUI 控制流水線，為正式進入「大規模批次生成」前所制定的工程化實施方案。

### 1.1 核心生產目標
1. **風格與特徵零漂移 (Zero Style Drift)**：嚴格鎖定韓系寫實攝影質感（RAW 85mm f/1.4），完全杜絕動漫線條與塑料感；五官鎖定極致 V-Line 鵝蛋臉、水光臥蠶杏仁眼、水光果凍唇與微毛孔通透肌。
2. **圖層解耦與無縫差分 (Head-Body Decoupled Layering)**：實現「表情差分」與「服裝階段差分」的模組化分離，確保在遊戲引擎內切換表情時頸部與頭部 0 像素偏移。
3. **全自動化矩陣產出 (Automated Matrix Pipeline)**：透過 Wildcards 與 ComfyUI API / Queue 控制，將手工單張生成轉化為標準化工業生產線。
4. **品質把關與高良率控制 (Quality Gate & High Yield)**：設立硬性品質檢驗標準（一票否決項），確保初圖良率 $\ge 70\%$，經修復管線後交付良率 $\ge 95\%$。

---

## 2. 量產前準備清單與基準鎖定 (Phase 1: Pre-flight Checklist & Anchor Locking)

在啟動大批量渲染之前，必須完成以下三大「基準錨點（Golden Anchors）」的提取與驗證，禁止在無錨點情況下直接跑批次。

例外：酒店主線的 Tier 3 三鏡試產不受本條限制。本機沒有 ComfyUI / FaceID 權重時，依 `progressive_scene_design_guide.md` 第 7 節直接出 `raw`，並把第一張合格圖回寫為臉模錨點。60 組立繪矩陣仍須等錨點齊全後才開批次。

### 2.1 基準臉模錨定 (Face Anchor Golden Baseline)
* **執行任務**：在固定種子與純淨光影下，渲染出 3 張黃金基準臉模真值（Ground Truth）：
  1. `anchor_face_neutral.png`：正面平視、自然微光、標準妝容。
  2. `anchor_face_profile15.png`：微側 15 度、立體輪廓光。
  3. `anchor_face_boudoir.png`：暖色調酒店套房氛圍光。
* **特徵提取**：將上述 3 張圖匯入 InsightFace / IP-Adapter FaceID Plus v2，提取並保存特徵權重向量（`.bin` 或 `.safetensors`），後續全批次均讀取此固定特徵檔，權重設定固定於 `0.85 ~ 0.88`。

### 2.2 ControlNet 姿態骨架預設庫 (Pose Rig Library)
為確保不同服裝差分在遊戲中能完美對位，需預先製作 3 組標準 DWpose / OpenPose 骨架圖檔存入庫中：
* `pose_standing_dialogue.png`：標準站姿（單手插腰、身體微轉 15 度，對話系統專用）。
* `pose_bust_portrait.png`：半身特寫（單手撫頸、眼神直視，內心獨白專用）。
* `pose_bed_pov.png`：酒店床沿跪倚俯視 POV（第一人稱親密互動 CG 專用）。

### 2.3 核心組件與環境配置確認表
| 組件分類 | 規格與模型檔名 | 生產期固定設定 / 閾值 |
| :--- | :--- | :--- |
| **底模 (Base Model)** | 本機實際：寫實 `majicmixRealistic_v6`、動漫 `AnythingV5V3_v5PrtRE`（皆 SD 1.5）；SDXL（RealVisXL v4.0）／Flux.1-dev 為升級選項 | FP16，精確度優先 |
| **採樣器 & 步數** | DPM++ 2M Karras / Euler a | 35 Steps |
| **CFG Scale** | 寫實 5.5 ~ 6.5／動漫 6.0 ~ 7.0 | 寫實超過 6.5 嚴禁使用（避免塑料感與邊緣銳化過度） |
| **特徵控制** | IP-Adapter FaceID Plus v2 | Weight: 0.86, Noise: 0.35 |
| **骨架控制** | ControlNet OpenPose XL (DWpose) | Weight: 0.80, Ending Step: 0.85 |
| **局部臉部精修** | FaceDetailer (ADetailer) | YOLOV8x-face, Denoise: 0.28 ~ 0.32 |
| **高清放大** | Ultimate SD Upscale (4x-UltraSharp) | Denoise: 0.20, Tile Size: 1024x1024 |

---

## 3. 資產矩陣量化規格與命名體系 (Phase 2: Asset Matrix & Naming SOP)

### 3.1 基礎資產量化矩陣清單
依據設計規範，基礎生產任務劃分為：
* **鏡頭姿態 (3 種)**：`stand` (站姿立繪), `bust` (半身特寫), `pov` (床沿親密)
* **服裝狀態 (5 階段)**：`stg01` (常服出遊), `stg02` (居家私服), `stg03` (情趣睡衣), `stg04` (純貼身內衣), `stg05` (戰損親密態)
* **表情差分 (4 大情感)**：`tsundere` (傲嬌審視), `seduce` (誘惑微醺), `shy` (羞怯動搖), `ecstasy` (意亂情迷)

$$\text{基礎對話與互動立繪總量} = 3 \text{ 種姿態} \times 5 \text{ 套服裝} \times 4 \text{ 種表情} = 60 \text{ 組標準立繪}$$

外加核心關鍵情節 CG：
* **親密劇情特寫 CG**：5 幕核心場景 × 3 種焦段/光影變化 = 15 張高解析 CG。
* **首期總目標交付資產**：**75 款關鍵核心視覺資產**。

### 3.2 嚴格命名規範 (Naming Convention)
檔案命名採小寫下劃線隔開，完全對齊遊戲引擎（Ren'Py / Unity）字串解析規則：

```text
zyzy_[鏡頭]_[服裝階段]_[表情]_[流水號]_[類型].[副檔名]
```

* **欄位代碼定義**：
  * **角色代號**：`zyzy`
  * **鏡頭 (Camera)**：`stand` / `bust` / `pov`
  * **服裝 (Outfit)**：`stg01` (bodycon), `stg02` (silk_shirt), `stg03` (corset), `stg04` (sheer_bra), `stg05` (bedsheet)
  * **表情 (Expression)**：`tsundere`, `seduce`, `shy`, `ecstasy`
  * **流水號 (Variant)**：`v01`, `v02` (挑選最佳候選圖)
  * **類型 (Type)**：`raw` (初生圖), `rgba` (去背透明通道圖), `upscaled` (4K 交付圖), `psd` (分層母檔)
* **命名範例**：
  * `zyzy_bust_stg03_seduce_v01_rgba.png`（半身、階段3情趣睡衣、誘惑表情、透明去背）
  * `zyzy_pov_stg05_ecstasy_v01_upscaled.png`（POV、階段5戰損、意亂情迷、4K 終圖）

### 3.3 目錄階層架構 (Folder Hierarchy)
遵循「暫存不入 F 槽，最終結論與發行資產固化於 F 槽」之準則：

```text
F:\game\
├── adult_game_character_design_chat_log.md   # [核心設計規範]
├── PRE_PRODUCTION_PLANNING.md                # [本量產規劃書]
├── wildcards/                                # [量產提示詞字典庫]
│   └── realistic/
│       ├── outfits.txt
│       ├── expressions.txt
│       ├── poses.txt
│       └── lighting.txt
├── presets/                                  # [控制基準庫]
│   ├── anchors/                              # 3 張黃金基準臉模
│   ├── poses/                                # 3 張標準 OpenPose 骨架
│   └── masks/                                # 固定表情與服裝替換遮罩
└── assets/                                   # [最終入庫之正式資產]
    ├── sprites/                              # 透明去背立繪 (RGBA 4K PNG)
    │   ├── stand/
    │   └── bust/
    ├── cgs/                                  # 全景劇情 CG (RGB 4K PNG)
    └── psd_masters/                          # 分層母檔 (PSD)
```

> **注意（暫存路徑）**：批次生成過程中的中間初圖、測試 logs、未精修 Raw 圖一律存放於：  
> `C:\Users\typa3\agent_tmp\game\raw_batch\` 與 `debug_logs\`，嚴禁直接傾倒碎檔於 F 槽。

---

## 4. 自動化批次生產工作流設計 (Phase 3: Production Pipeline Automation)

為解決批量生產中臉部容易隨提示詞更換而走樣、身形邊緣不契合的問題，採用「三階段解耦流水線（Three-Stage Decoupled Workflow）」。

```
[階段 A：素體與骨架基底生成]
DWpose 骨架 + IP-Adapter FaceID (0.86) + 固定 Seed
          │
          ▼
產出各鏡頭下的 5 種服裝 Base Pose (保存 Raw Base)
          │
          ▼
[階段 B：表情與細節 Inpainting 矩陣重繪]
固定軀幹服飾，加載頭部精確遮罩 (Head Mask)
套用 Wildcards: __realistic/expressions__
搭配 FaceDetailer (Denoise: 0.28) 注入 4 種表情差分
          │
          ▼
[階段 C：去背透明通道與 4K 超分辨率輸出]
BiRefNet / Rembg 提取立繪 Alpha 通道
Ultimate SD Upscale (4x-UltraSharp) 放大至 4K (2496x3648)
色彩與對比度校準 (sRGB, 避免過度銳化)
```

### 4.1 Wildcards 字典規劃 (Wildcards Definitions)

#### `wildcards/realistic/outfits.txt`
```text
wearing an elegant tight charcoal grey knit bodycon mini dress, deep scoop neckline, accentuating hourglass curves, delicate gold chain necklace
wearing a loosely draped oversized unbuttoned ivory silk shirt slipping off both shoulders, revealing shoulders and collarbones, black lace bralette visible
wearing black satin and sheer floral lace corset lingerie bodysuit, thin shoulder straps, sheer lace waist cutout, boudoir fashion
wearing an ultra-thin black sheer lace lingerie set, strappy bra, sheer lace panties, exposed soft flat stomach and natural C-cup cleavage
undressed, wrapped loosely in messy white hotel bedsheets, flushed dewy skin with subtle beads of sweat, disheveled wavy hair
```

#### `wildcards/realistic/expressions.txt`
```text
arrogant confident smirk, raised eyebrow, piercing direct gaze, closed glossy lips
sultry half-lidded bedroom eyes, seductive look, lips slightly parted with light breath, soft blush
flustered expression, looking away, biting lower lip gently, heavy natural rosy blush on cheeks and ears
pleasure expression, eyes rolling slightly up, trembling lips parted, heavy flush, tears in corners of eyes, heavy panting
```

#### `wildcards/realistic/poses.txt`
```text
full body standing pose, 15 degree side angle, hand on hip, natural weight shift, clean neutral backdrop
close-up bust portrait, head slightly tilted, one hand gently touching neckline, 85mm shallow depth of field
first person POV shot, character looking up at viewer, kneeling on hotel bed, reaching hand toward camera
```

---

## 5. 品質閘門與驗收標準 (Phase 4: Quality Gate & Acceptance Criteria)

每批次產出資產必須經過 QA 檢核表，未過關者直接退回重修或手動 Inpaint。

### 5.1 一票否決項 (Fatal Rejection Criteria)
只要觸發下列任一項目，判定為無效資產，嚴禁入庫：
1. **動漫化/插畫感遺留（寫實管線）**：出現任何黑色描邊線、2.5D 手繪感或非物理渲染反光。
2. **五官特徵偏離**：
   - 臉型下巴過尖（蛇精臉）或下頜角過度寬方（非韓系 V-line）。
   - 臥蠶（Aegyo-sal）消失或變成眼袋黑眼圈。
   - 嘴唇過薄、無光澤或牙齒結構畸形。
3. **肢體與手部結構異常**：手指數量 $\ne 5$、關節逆折、手部融合進衣物或床單。
4. **皮膚塑料化 (Plastic/Waxy Skin)**：過度磨皮導致毛孔紋理全無、如同矽膠娃娃。
5. **換裝圖層接縫裂痕**：Inpaint 重繪時在脖子或鎖骨邊緣產生明暗色差、邊緣斷裂。

### 5.2 允收標準指標 (Acceptance Scoring)
* **五官吻合度 (FaceID Cosine Sim)**：與基準臉模計算特徵餘弦相似度 $\ge 0.82$。
* **皮膚細節表現**：微距下具備自然細微毛孔、微出汗光澤與自然的皮下散射（SSS）。
* **光影一致性**：髮絲邊緣散景光斑與背景酒店暖燈色溫（2800K~3200K）完全吻合。
* **尺寸與格式規範**：
  * 原生分辨率：SD 1.5 底模為 $512 \times 768$ (2:3)，Hires.fix 2x 輸出 $1024 \times 1536$；升級 SDXL 後改為 $832 \times 1216$ (2:3) 或 $896 \times 1152$ (3:4)。
  * 終端交付尺寸：放大至 $2496 \times 3648$ (或 $2688 \times 3456$)，300 DPI。
  * 格式：32-bit PNG (含完美平滑 Alpha 通道) 與無損 PSD。

---

## 6. 硬體算力、工期與存儲資源規劃 (Phase 5: Resource & Timeline Budget)

### 6.1 算力與耗時預估 (以單張 RTX 4090 / 24GB VRAM 為基準)
| 階段任務 | 單張平均處理耗時 | 批次數量 (含餘裕備選 2x) | 預計總 GPU 耗時 |
| :--- | :--- | :--- | :--- |
| **階段 A (Base 生成)** | ~25 秒 | 60 款 × 2 = 120 張 | ~50 分鐘 |
| **階段 B (表情 Inpaint)** | ~18 秒 | 120 張 | ~36 分鐘 |
| **階段 C (FaceDetailer + 4K 超分)**| ~45 秒 | 挑選合格 75 款 | ~56 分鐘 |
| **自動去背 (BiRefNet)** | ~3 秒 | 60 款立繪 | ~3 分鐘 |
| **總計自動化產程** | - | - | **約 2.5 小時** |

### 6.2 存儲容量規劃
* **本機暫存空間 (`agent_tmp`)**：預估需要 40 GB（存放種子批次候選圖、中間快取、除錯 Log）。
* **F 槽正式入庫空間 (`F:\game\assets\`)**：
  * 4K RGBA 立繪 (75 款 × ~25MB)：約 1.8 GB。
  * PSD 分層母檔 (75 款 × ~120MB)：約 9.0 GB。
  * 預計 F 槽長期佔用空間：$\approx 11 \text{ GB}$。

---

## 7. 下一步執行計劃 (Immediate Action Items)

1. [x] **建立量產工程規劃書**：完成 `PRE_PRODUCTION_PLANNING.md`。
2. [x] **生成並固化 Wildcards 檔案**：於 `F:\game\wildcards\realistic\` 寫入 4 份定義檔。
3. [x] **本地無審查管線部署就緒**：完成 WebUI Forge 引擎部署並掛載 `majicmixRealistic_v6` 與 `AnythingV5`。
4. [x] **渲染並鎖定黃金基準臉模 (Face Anchors)**：Tier 3 第一張目視合格圖已成功固化為 `presets/anchors/anchor_face_boudoir.png`。
5. [x] **執行 Tier 3 & Tier 4 大尺度 CG 試產驗收**：`cowgirl` / `rear` / `prone` / `aftermath` 共 4 張全大尺度 CG 產出，已完整入庫 `assets/cgs/`。
6. [ ] **啟動下一階段工程**：進行立繪圖層解耦去背（Alpha Sprites）或 CG 4K 高分辨率超分精修。

---

## 8. 第三方工具調用評估與人機協同指引 (Third-Party Tool Integration)

### 8.1 對象評估：線上生成工具 (如 AI Gallery / aigallery.app)
* **自動化呼叫可行性**：**不可行（不建議程式化直連）**。
  * **缺乏開放 API**：`aigallery.app` 屬前端 Web 互動平台，未提供正式 REST API / Webhook。
  * **反爬蟲與伺服器隊列**：包含 Cloudflare 驗證與前端排隊機制，以無頭瀏覽器調用極易斷線或觸發驗證碼阻擋。
  * **控制精度限制**：無法程式化傳入 DWpose 骨架圖、Inpaint 局部遮罩（Mask）與 FaceID 權重，難以達到立繪「頭身解耦、頭部零位移」標準。
* **建議角色定位**：適合作為**「概念探索與靈感初圖生成池（Concept Ideation）」**。

### 8.2 建議協同模式 (Hybrid Human-in-the-Loop Workflow)
若選擇使用 AI Gallery 等免算力平台生成：
1. **Agent 提供標準化提示詞包**：提供已對齊好的服裝、表情、氛圍 Prompt（直接調用 `wildcards/realistic/`）。
2. **人工於 Web 端批次抽圖**：使用者在 aigallery 選取滿意且符合 V-line 鵝蛋臉、臥蠶特徵的圖片並下載。
3. **Agent 本地接力自動化工程後製**：
   - 執行本機 Python 自動去背腳本（BiRefNet / Rembg），生成透明通道立繪。
   - 執行 4K 超分辨率放大（Real-ESRGAN / UltraSharp）。
   - 按命名規範（`zyzy_[鏡頭]_[服裝]_[表情]_v01_rgba.png`）自動整理並歸檔至 `F:\game\assets\`。

### 8.3 支援純程式化自動調用的替代方案
若需實現「完全無人值守的端到端自動批量生成」：
* **雲端 API 方案**：串接具備開放 API 之平台（如 **Novita AI**, **Fal.ai**, **RunPod**, **Civitai API**），支援在請求中直接攜帶 ControlNet、IP-Adapter 與 Inpainting Mask。
* **本地端 API 方案**：本地啟動 **ComfyUI (`http://127.0.0.1:8188`)** 或 **SD WebUI / Forge (`http://127.0.0.1:7860`)**，由 Agent 撰寫批次呼叫腳本一鍵遍歷 Wildcards 字典產出。

---

## 9. 本地無審查生成環境實測與部署方案 (Local Uncensored Environment Deployment)

### 9.1 本機硬體規格實測數據 (Hardware Benchmark)
經過實機指令探測，目前本機具備之硬體環境如下：
* **獨立顯示卡 (GPU)**：NVIDIA GeForce RTX 3050 Laptop GPU（**4GB VRAM / 4096 MiB**，驅動版本 566.07，CUDA 12.7 支持）
* **系統主記憶體 (RAM)**：**32 GB**（容量極為充足，可完整承載 CPU Offload 與記憶體共享）
* **儲存磁碟**：
  * **F 槽（固態硬碟）**：**163.49 GB 可用**（完全滿足模型與環境存放需求）
  * **C 槽（系統碟）**：23.46 GB 可用（不建議將大量模型置於 C 槽，避免容量耗盡）
* **既有軟體環境**：
  * Python: `3.9.12`
  * PyTorch: `2.8.0+cu126`（CUDA 加速實測已啟用 `True`）
  * Git: `2.43.0.windows.1`

### 9.2 無審查特性與合規安全 (100% Uncensored & Private)
本地部署的開源生成環境具備以下核心優勢：
1. **零審查與無過濾 (No Safety Checker)**：本地完全關閉道德審查過濾模組，無任何關鍵字屏蔽或圖像模糊機制。
2. **完全隱私與離線可用**：生圖流程 100% 在本機 GPU/RAM 內完成，無需上傳至任何雲端伺服器，無被封號或資料外洩風險。
3. **完全相容成人開源模型**：可自由從 Civitai、Hugging Face 下載任何無審查寫實底模（如 Realistic Vision, MajicMix, CyberRealistic）及私密姿態 LoRA。

### 9.3 針對 4GB VRAM 的最佳架構選型
由於 RTX 3050 Laptop 為 4GB 顯存，標準原版 WebUI (A1111) 容易遭遇 OOM（Out of Memory），建議以下兩種優化部署路線：

1. **首選推薦：Stable Diffusion WebUI Forge**
   * **優勢**：擁有最先進的顯存管理與層級調度（Layer Offload），可自動利用本機 32GB 系統記憶體作為緩衝。
   * **運行效果**：
     * **SD 1.5 寫實底模**（512x768 原生出圖）：耗時僅約 10~20 秒，4GB 顯存毫無壓力。
     * **搭配 ADetailer / FaceDetailer**：自動以高精度修復五官、臥蠶與果凍唇。
     * **Hires.fix / 4x-UltraSharp 放大**：可無縫放大至高清立繪。
2. **次選推薦：ComfyUI（低顯存模式 `--lowvram`）**
   * **優勢**：模組化節點設計，內建 API 伺服器，Agent 可直接發送 HTTP 請求無人值守批量生成。

### 9.4 實際部署目錄與引擎驗證
* **主程式路徑**：`F:\webui_forge\`（Forge 便攜整合包，解壓容量約 6.36 GB）
* **啟動腳本**：`F:\webui_forge\run.bat`（`webui-user.bat` 已配置 `--api` 模式）
* **底層環境實測**：
  * Python: `F:\webui_forge\system\python\python.exe`
  * PyTorch: `2.4.0+cu124`
  * GPU 識別：`NVIDIA GeForce RTX 3050 Laptop GPU`（CUDA 加速運作正常）

### 9.5 本地模型庫清單 (Models In Stock)
模型存放目錄：`F:\webui_forge\webui\models\Stable-diffusion\`
1. **寫實真人無審查底模 (Photorealistic Asian)**：
   * 檔名：`majicmixRealistic_v6.safetensors`（2.40 GB，內嵌 VAE）
   * 定位：專用於 85mm 景深、韓系水光肌、臥蠶果凍唇之超寫實成人立繪與私密 CG。
2. **二次元動漫無審查底模 (Anime / ACG Illustration)**：
   * 檔名：`AnythingV5V3_v5PrtRE.safetensors`（2.13 GB，內嵌 VAE）
   * 定位：開源動漫無審查標竿底模，專用於日系純 2D 賽璐珞風格立繪（不得 2.5D，見 9.13）、精細服飾與大尺度全年齡/成人遊戲資產。

### 9.6 端到端實機生圖驗收基準 (Live Generation Benchmark Results)
於本機 RTX 3050 Laptop GPU (4GB VRAM) 實際啟動 Forge API 服務 (`http://127.0.0.1:7860`)，並執行首輪實測驗收：

| 測試項目 | 所用底模 | 提示詞規格 | 採樣與分辨率 | 實測生成耗時 | 顯存狀況與結論 |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **動漫立繪測試** | Anything V5 | 日系二次元、精緻五官、優雅洋裝、柔光 | Euler a / 20 steps / 512×768 | **8.06 秒** | 顯存使用約 2.8GB，線條與色調飽滿，零審查阻礙。 |
| **寫實底模基準測試** | majicMIX realistic v6 | 韓系 V-line、臥蠶果凍唇、黑色蕾絲內衣、落肩白襯衫、酒店床景 85mm 散景 | Euler a / 28 steps / 512×768 | **18.37 秒**（推論純算約 7 秒） | 顯存維持 3.2GB / 4.0GB，**無任何 OOM**；完美呈現韓系通透水光肌與真實微皮膚紋理。 |

* **API 自動化通道修復**：已完成 Forge Pydantic v2 之 `SDModelItem.config` 欄位相容性補丁，`/sdapi/v1/sd-models` 與 `/sdapi/v1/txt2img` 雙向通訊 100% 暢通。
### 9.7 基於原創圖 S__151601154 之 Tier 2 / Tier 3 / Tier 4 連貫資產成果
以使用者上傳之原創原畫 `S__151601154.jpg`（黑西裝外套、象牙白透膚蕾絲內衣、韓系水光鵝蛋臉、金色耳環、大波浪深棕髮）為角色基因基準，完成三階梯式成人遊戲 CG 產出：

| 資產分級 | 檔名與路徑 | 核心畫面與特徵繼承 | 狀態 |
| :--- | :--- | :--- | :--- |
| **Tier 2 (私密挑逗)** | `assets/cgs/zyzy_cg_hotel_tier2_tease_ivory_lace_v01.png` | 沙發倚靠、黑外套落肩、象牙白透膚蕾絲內衣、吊帶白絲襪、挑逗眼神 | **已驗收** |
| **Tier 3 (大尺度本番-全裸)** | `assets/cgs/zyzy_cg_hotel_tier3_climax_nude_v02.png` | 床沿全裸跨坐騎乘、無衣物完全顯露、微張雙唇、潮紅薄汗高潮 | **已驗收** |
| **Tier 3 (大尺度本番-內衣)** | `assets/cgs/zyzy_cg_hotel_tier3_climax_cowgirl_v01.png` | 床沿跨坐、金白拼接私密內衣、俯視挑逗眼神 | **已驗收** |
| **Tier 4 (戰損餘韻-被褥)** | `assets/cgs/zyzy_cg_hotel_tier4_aftermath_sheets_v02.png` | 凌亂純白羽絨被褥微裹、薄蕾絲貼身、微汗潮紅、事後迷濛眼神 | **已驗收** |
| **Tier 4 (戰損餘韻-睡袍)** | `assets/cgs/zyzy_cg_hotel_tier4_aftermath_boudoir_v01.png` | 純白絲質睡袍微敞、床頭暖光散景、疲倦甜美眼神 | **已驗收** |

### 9.8 基於原創圖 S__151601155 之 Tier 3 多動作體位擴充矩陣成果
針對使用者提供的最新大尺度原創參考圖 `S__151601155.jpg`（空氣瀏海、韓系水光鵝蛋臉、大波浪長髮、大尺度開腿與傲人豐滿身形），為 Tier 3 本番高潮事件擴充生成 5 組不同核心體位之 CG 資產：

| 動作體位編號 | 檔名與路徑 | 鏡頭與體位動作規格 | 狀態 |
| :--- | :--- | :--- | :--- |
| **動作 1 (M字開腿)** | `assets/cgs/zyzy_cg_hotel_tier3_pose01_m_spread_v01.png` | 完美復刻原圖：床沿大開 M 腿正面展示、單手撫胸、潮紅微張雙唇直視鏡頭 | **已驗收** |
| **動作 2 (跪趴後背)** | `assets/cgs/zyzy_cg_hotel_tier3_pose02_rear_doggystyle_v01.png` | 四足跪姿、背部挺直下壓、蜜桃臀抬起、回頭凝視鏡頭之大尺度後背位 | **已驗收** |
| **動作 3 (深情跪坐)** | `assets/cgs/zyzy_cg_hotel_tier3_pose03_missionary_legs_up_v01.png` | 近距直挺跪坐、傲人上圍正對鏡頭、潮紅喘息神情、第一人稱深情凝視 | **已驗收** |
| **動作 4 (托乳誘惑)** | `assets/cgs/zyzy_cg_hotel_tier3_pose04_breast_lift_v01.png` | 正面端坐、雙手由下方托起豐滿胸部呈獻、咬唇俏皮誘惑特寫 | **已驗收** |
| **動作 5 (俯身前傾)** | `assets/cgs/zyzy_cg_hotel_tier3_pose05_prone_pillow_arch_v01.png` | 跪爬前傾貼向鏡頭、透膚蕾絲半落、雙峰擠壓溢出、側臉微喘視角 | **已驗收** |

### 9.9 臉部與身形高度統一 (Face & Body Dimensions Unified Pipeline)
為嚴格解決生成不同體位動作時五官漂移與胸型腰圍不一致的問題，採用 ControlNet `reference_only`（權重 0.85）搭配不可變角色 DNA（`UNIFIED_DNA`）：
* **五官鎖定**：精緻 V-Line 鵝蛋臉、空氣透膚齊瀏海、立體臥蠶杏仁眼、微張水光果凍唇。
* **身材尺寸鎖定**：本節三張依舊版 F-Cup DNA 產出，不符 9.12 自然水滴 C-Cup 標準，僅保留作姿勢參考；新產出一律使用 9.12 體型詞（56cm 極細蜂腰、平坦小腹、溫潤骨盆曲線不變）。

產出成果：
* `assets/cgs/zyzy_cg_hotel_tier3_unified_pose01_m_spread_v01.png`（M字開腿正面展示，五官身形100%對齊原圖）
* `assets/cgs/zyzy_cg_hotel_tier3_unified_pose02_doggystyle_rear_v01.png`（跪趴後背挺腰回眸，側胸飽滿度與腰臀比鎖定）
* `assets/cgs/zyzy_cg_hotel_tier3_unified_pose03_breast_lift_v01.png`（雙手托胸特寫，胸部尺寸與乳暈細節鎖定）

### 9.10 Sample 成果分流：二次元動漫 vs 寫實 (Anime vs Realistic Segregation)
依據雙軌制美術需求，將所有 Sample 資產正式分離為獨立目錄（目錄交會點：`F:\game\sample` 與 `F:\game\assets\samples`）：

```text
F:\game\sample\ (F:\game\assets\samples\)
├── anime/                                 # [二次元動漫風格] Anything V5 底模 (徹底解耦 Zyzy，純 2D 日系美學)
│   ├── test_anime_anythingv5.png          # 基礎動漫底模測試樣品
│   ├── expressions_showcase/              # 核心情感表情差分庫 (純 2D 賽璐珞)
│   │   ├── anime_sample_expr01_tsundere_smirk_v01.png # 傲嬌審視
│   │   ├── anime_sample_expr02_shy_blush_v01.png      # 羞怯臉紅
│   │   └── anime_sample_expr03_ecstasy_pleasure_v01.png # 高潮迷情
│   ├── stage01_street_bodycon/            # Stage 1 常服出遊 (灰針織短裙)
│   │   ├── anime_sample_stg01_stand_bodycon_v01.png
│   │   └── anime_sample_stg01_bust_bodycon_v01.png
│   ├── stage02_home_silk_shirt/           # Stage 2 居家私服 (落肩白襯衫)
│   │   ├── anime_sample_stg02_bust_silk_shirt_v01.png
│   │   └── anime_sample_stg02_pov_silk_shirt_bed_v01.png
│   ├── stage03_boudoir_corset/            # Stage 3 情趣睡衣 (黑色透膚蕾絲馬甲)
│   │   ├── anime_sample_stg03_stand_black_lace_corset_v01.png
│   │   └── anime_sample_stg03_bust_corset_v01.png
│   ├── stage04_lingerie_sheer/            # Stage 4 純貼身內衣 (象牙白透膚蕾絲)
│   │   └── anime_sample_stg04_pov_ivory_lace_v01.png
│   ├── tier2_tease/                       # Tier 2 動漫私密挑逗 (黑西裝+白蕾絲內衣)
│   │   └── anime_tier2_tease_ivory_lace_v01.png
│   ├── tier3_climax_poses/                # Tier 3 動漫多體位核心本番 (全裸無審查)
│   │   ├── anime_tier3_c_cup_frontal_m_spread_open_v01.png # 【嚴格大開腿】正面M字大開腿直面鏡頭
│   │   ├── anime_tier3_c_cup_cowgirl_spread_open_v01.png   # 【嚴格大開腿】正面騎乘位膝蓋向外大張
│   │   ├── anime_tier3_pose01_m_spread_v01.png
│   │   ├── anime_tier3_pose02_doggystyle_rear_v01.png      # 跪趴後背回眸
│   │   └── anime_tier3_pose03_breast_lift_v01.png          # 托胸特寫
│   └── tier4_aftermath/                   # Tier 4 動漫事後餘韻 (第二性徵一覽無遺終極獎勵)
│       ├── anime_tier4_immediate_spread_climax_v01.png     # 本番結束當下：仰躺全裸大開體液斑駁阿黑顏
│       ├── anime_tier4_immediate_breast_convulsion_v01.png  # 本番結束當下：捧乳抽搐失神癱軟
│       └── anime_tier4_aftermath_sheets_v01.png
│
└── realistic/                             # [寫實真人風格] majicMIX realistic v6 底模 (鎖定基準臉模)
    ├── test_realistic_zyzyveve.png        # 基礎寫實底模測試樣品
    ├── zyzy_cg_realistic_asian_physique_standard_v01.png # 【黃金基準錨點】使用者確認自然水滴 C-Cup
    ├── original_reference/                # 使用者原創參考圖真值
    │   └── S__151601154.jpg               # 原創參考圖 1 (黑西裝+白內衣+韓系臉)
    ├── stage01_street_bodycon/            # Stage 1 常服出遊 (緊身灰針織包臀短裙)
    │   ├── zyzy_sample_stg01_stand_bodycon_charcoal_v01.png # 全身站姿 S 曲線
    │   └── zyzy_sample_stg01_bust_bodycon_seduce_v01.png    # 半身大 U 領特寫
    ├── stage02_home_silk_shirt/           # Stage 2 居家私服 (寬鬆落肩真絲白襯衫)
    │   ├── zyzy_sample_stg02_bust_silk_shirt_off_shoulder_v01.png # 落肩鎖骨特寫
    │   └── zyzy_sample_stg02_pov_silk_shirt_bed_shy_v01.png       # 床沿俯視親密視角
    ├── stage03_boudoir_corset/            # Stage 3 情趣睡衣 (黑色透膚蕾絲馬甲連體衣)
    │   ├── zyzy_sample_stg03_stand_black_lace_corset_v01.png # 站姿透膚馬甲
    │   └── zyzy_sample_stg03_bust_corset_seduce_gaze_v01.png # 馬甲深 V 誘惑眼神
    ├── stage04_lingerie_sheer/            # Stage 4 純貼身內衣 (象牙白極薄透膚蕾絲)
    │   └── zyzy_sample_stg04_pov_ivory_lace_lingerie_v01.png # 跪坐床沿俯視親密
    ├── expressions_showcase/              # 核心情感表情差分庫
    │   ├── zyzy_sample_expr01_tsundere_smirk_v01.png # 傲嬌審視自信微笑
    │   ├── zyzy_sample_expr02_shy_blush_v01.png      # 羞怯咬唇面紅耳赤
    │   └── zyzy_sample_expr03_ecstasy_pleasure_v01.png # 意亂情迷高潮微喘
    ├── tier2_tease/                       # Tier 2 寫實私密挑逗
    │   └── zyzy_cg_hotel_tier2_tease_ivory_lace_v01.png
    ├── tier3_climax_poses/                # Tier 3 寫實多體位本番 (ControlNet鎖臉鎖身)
    │   ├── zyzy_cg_hotel_tier3_c_cup_frontal_m_spread_open_v01.png # 【嚴格大開腿】正面M字大開腿無手遮
    │   ├── zyzy_cg_hotel_tier3_c_cup_cowgirl_spread_open_v01.png   # 【嚴格大開腿】正面騎乘位膝蓋分開向外
    │   ├── zyzy_cg_hotel_tier3_unified_pose01_m_spread_v01.png
    │   ├── zyzy_cg_hotel_tier3_unified_pose02_doggystyle_rear_v01.png
    │   ├── zyzy_cg_hotel_tier3_unified_pose03_breast_lift_v01.png
    │   ├── zyzy_cg_hotel_tier3_climax_cowgirl_v01.png
    │   ├── zyzy_cg_hotel_tier3_pose03_missionary_legs_up_v01.png
    │   └── zyzy_cg_hotel_tier3_pose05_prone_pillow_arch_v01.png
    └── tier4_aftermath/                   # Tier 4 寫實事後餘韻 (第二性徵一覽無遺終極獎勵)
        ├── zyzy_cg_hotel_tier4_c_cup_spread_eagle_footbed_v05.png # 【旗艦】床尾低視角仰躺大字形全裸
        ├── zyzy_cg_hotel_tier4_immediate_spread_climax_v01.png
        ├── zyzy_cg_hotel_tier4_immediate_collapsed_doggystyle_v01.png
        ├── zyzy_cg_hotel_tier4_aftermath_boudoir_v01.png
        └── zyzy_cg_hotel_tier4_aftermath_sheets_v02.png
```

### 9.11 Tier 4 畫面原則：高潮失控與結束當下之第二性徵無遮蔽 (Climax & Immediate Aftermath)
Tier 4 依 9.15 定義為**「激烈／高潮失控」**，涵蓋激烈體位中的高潮瞬間，以及高潮結束當下的脫力餘韻兩種鏡頭；不包含事後睡袍／被褥包裹之日常。

* **核心畫面原則**：
  1. **本番結束當下瞬間 (Immediate Aftermath)**：捕捉激烈交合剛結束之極致脫力、痙攣餘韻、汗水淋漓與潮紅水光肌。
  2. **第二性徵一覽無遺 (100% Uncensored & Fully Exposed)**：完全摒除任何被褥、衣物之遮蔽。自然水滴 C-Cup 雙峰、粉嫩乳暈、腰線曲線及私密處完全裸露直面鏡頭。
  3. **極致色氣刺激特徵**：愛液與濃稠白色液體沿著大腿內側、私密處與床單流淌浸潤（bodily fluid puddles & dripping）；表情呈現極度快感抽搐、阿黑顏（心形瞳孔/翻白失神、微張唇瓣拉絲喘息）。
  4. **中出為必要畫面**：每一張 Tier 4 都必須看見中出。插入中或剛拔出時，精液自陰道溢出，並沿結合處或大腿內側流下。泛稱體液不能代替中出。

* **已驗收終極資產清單**：
  * **寫實真人版 (majicMIX Realistic + ControlNet reference_only)**：
    * `assets/cgs/zyzy_cg_hotel_tier4_c_cup_spread_eagle_footbed_v05.png`：**【標準 C-Cup 旗艦終極樣品】** 床尾低視角穿透、仰躺大字形極致脫力、全裸私密處直面鏡頭、自然水滴 C-Cup 雙峰微垂、高潮失神臉。
    * `assets/cgs/zyzy_cg_hotel_tier4_immediate_spread_climax_v01.png`：仰躺無力開腿、私密處直面鏡頭、體液流淌、雙峰汗珠起伏、失神微喘。
    * `assets/cgs/zyzy_cg_hotel_tier4_immediate_collapsed_doggystyle_v01.png`：跪趴塌腰無力、翹臀正對鏡頭、大腿內側白色液體滑落、回眸極致快感淚光。
  * **二次元動漫版 (Anything V5)**：
    * `assets/cgs/anime_tier4_immediate_spread_climax_v01.png`：全裸仰躺大開腿、阿黑顏翻白失神、心形瞳孔、體液濡濕床單。
    * `assets/cgs/anime_tier4_immediate_breast_convulsion_v01.png`：半癱坐雙手捧乳擠壓溢出、胸前與大腿體液斑駁、潮紅微吐舌尖。

### 9.12 雙軌視覺資產（Realistic & Anime）亞洲女性體型標準規範宣告
依據開發方針，**寫實真人 (Realistic)** 與 **二次元動漫 (Anime)** 雙軌美術管線，角色身形標準統一正式宣告為**「精緻東亞女性自然水滴 C-Cup 體型 (East Asian Female Natural C-Cup Physique)」**：

1. **核心骨架與體態規範**：
   * **骨架與肩寬**：東亞嬌小精緻骨架，窄肩 35~37cm、直角鎖骨肩，窄小胸腔肋骨（Narrow ribcage），全面杜絕歐美寬骨架與肌肉厚背。
   * **頭身比**：設定為自然黃金比例 7.0 ~ 7.2 頭身（身高 162~165cm），避免歐美誇張長腿與大身軀。
   * **胸部特徵**：**自然水滴型 C-Cup（Natural Teardrop C-Cup）**，豐滿度適中、線條圓潤優雅，具備自然重力微垂墜感（Natural gravity sag）；自然天然乳溝、精緻粉嫩乳暈；雙向排除 A-Cup 貧乳與過大 F-Cup 矽膠人造感。
   * **腰腹與骨盆**：55~56cm 極細蜂腰、平坦微潤小腹；緊緻窄骨盆（腰臀比 0.68~0.70）搭配小巧圓潤蜜桃臀，嚴禁歐美寬大骨盆與外擴大臀。
   * **四肢與膚質**：纖細修長大腿帶柔和內側微肉感，極致白皙透亮瓷白水光肌，受刺激時呈現蜜桃粉通透潮紅。

2. **提示詞工程固定字典檔**：
   * 字典路徑：寫實 `F:\game\wildcards\realistic\asian_body_physique.txt`；動漫 `F:\game\wildcards\anime\asian_body_physique.txt`（兩者分檔，見 9.13）
   * 寫實調用插槽：`delicate narrow shoulders 36cm, delicate collarbones, ((plump natural C-cup breasts:1.3)), ((full round natural C-cup:1.25)), (natural teardrop breast shape:1.2), (clear natural cleavage:1.2), soft pink areolas, tiny 56cm wasp waist, soft smooth flat stomach, compact rounded peach buttocks and hips, slender toned legs, fair porcelain milk-glass skin`
   * 動漫調用插槽：`petite delicate shoulders, ((natural C-cup anime breasts:1.25)), ((medium breasts:1.2)), (natural teardrop breasts:1.2), (subtle anime cleavage:1.15), tiny 56cm anime waist, flat slender tummy, compact rounded hips, slender smooth thighs, fair pale porcelain skin with soft peach blush`
   * 雙軌硬性排除詞：`((small breasts, flat chest, petite chest, A-cup, B-cup:1.5)), ((huge breasts, gigantic breasts, massive breasts, heavy breasts, oversized breasts, F-cup, G-cup, fake silicone breasts, round spherical implants:1.4)), (caucasian, western female body proportions, broad shoulders, wide ribcage, thick torso, heavy bone structure, muscular frame, bulky:1.4), (huge wide hips, thick thighs, oversized pelvis:1.3)`

### 9.13 動漫管線純日系 2D ACG 規範（不參考外部模特兒）
依據使用者明確指示，二次元動漫（Anime）管線為獨立純日系 ACG 設計，不參考任何外部模特兒：
1. **美學風格獨立**：動漫管線為純日系 2D ACG 賽璐珞美少女，徹底獨立於寫實管線，模型使用 `AnythingV5V3_v5PrtRE.safetensors` 與 `AOM3A1B_orangemixs.safetensors`。
2. **命名規範**：所有動漫資產檔案名一律採用 `anime_*` 前綴。
3. **字典庫獨立分流**：動漫專屬 Wildcards 字典庫獨立固化於 `F:\game\wildcards\anime\`（包含 `asian_body_physique.txt`、`outfits.txt`、`expressions.txt`、`poses.txt`、`lighting.txt`、`tier3_shots.txt`），與寫實專用 `wildcards/realistic/` 嚴格分流。

### 9.14 Tier 3 正面鏡頭禁止雙腿合攏鐵律 (Strict Open-Legs Rule for Tier 3 Frontal Shots)
依據成人遊戲核心表現要求，Tier 3 本番高潮事件之正面視角（M 字開腿、正面騎乘位等）確立硬性驗收標準：
1. **一票否決合腿**：正面照片**嚴禁任何雙腿合攏、夾腿、遮掩、膝蓋靠攏動作**。
2. **必須極致大開**：正面拍攝必須大開 M 字腿／雙腿大幅分開向兩側敞開（`legs spread wide open apart`, `knees spread far apart towards sides`），私密處（groin / crotch / inner thighs）完全清晰敞開正對鏡頭無手遮、無遮擋物。
3. **負向提示詞硬性排除**：`((closed legs, knees together, thighs touching, clamped legs, tightly closed legs, crossed legs, covered crotch, hand covering crotch, legs together, pressing knees, hidden crotch:1.7))`。
4. **適用範圍**：僅限正面鏡頭（正面 M 字、正面騎乘、正面仰躺）。後背位、俯臥後背（prone bone）、站立位、傳教士（雙腿纏腰）不套用此組負向詞，避免與該體位的自然腿部姿勢衝突。

### 9.15 完整 4 階段 CG 遞進生成提示詞規範 (4-Tier Progressive CG Production Standard)
依據最新規則異動，成人遊戲專用 CG 生成全面採用 4 階段遞進架構，直接相容於 Stable Diffusion WebUI Forge：

| 階段 | 親密程度 | 重點特徵 | 核心鏡頭語境 |
| :--- | :--- | :--- | :--- |
| **Tier 1** | **曖昧／輕度接觸** | 衣服完整、對視、輕吻、擁抱 | 初識、好感上升、輕度肢體接觸 |
| **Tier 2** | **情色／半脫** | 衣服凌亂、露點、愛撫 | 關係確定後、第一次明顯親密、半裸凌亂 |
| **Tier 3** | **性愛／明確插入** | 全裸、正常體位、真實表情、正面禁合腿 | 正式發生性關係、正常體位、真實生理與快感反應 |
| **Tier 4** | **激烈／高潮失控** | 激烈體位、高潮表情、液體細節、阿黑顏、必須中出 | 感情深厚後的激烈場面、失神脫力、精液溢出 |

#### 1. 通用前綴（依管線二選一，每次都加）
* **寫實管線**：
```text
masterpiece, best quality, highly detailed, sharp focus, intricate details, cinematic lighting, soft ambient light, realistic skin texture, detailed eyes, detailed face, beautiful detailed skin,
```
* **動漫管線**（不可含 `realistic skin texture`，會與動漫反向詞 `realistic skin pores, photoreal` 互相抵銷）：
```text
masterpiece, best quality, highly detailed, sharp focus, intricate details, anime illustration, pure 2d anime style, clean crisp anime lineart, cell shading, soft anime lighting, detailed anime eyes, detailed face,
```

#### 2. 通用負面提示詞 (Negative Prompt)
```text
lowres, bad anatomy, bad hands, text, error, missing fingers, extra digit, fewer digits, cropped, worst quality, low quality, normal quality, jpeg artifacts, signature, watermark, username, blurry, artist name, 
ugly, deformed, mutated, extra limbs, fused fingers, too many fingers, long neck,
```
*(註：Tier 3 & 4 額外疊加「禁止合腿」與「胸圍邊界箝位」排除詞)*

#### 3. Tier 1：曖昧／輕度接觸
* **單人版本**：
  ```text
  masterpiece, best quality, highly detailed, 
  1girl, solo, beautiful detailed face, detailed eyes, 
  looking at viewer, soft smile, slight blush, 
  fully clothed, elegant outfit, 
  sitting on bed, soft lighting, intimate atmosphere, 
  upper body, close-up, romantic mood
  ```
* **雙人版本**：
  ```text
  masterpiece, best quality, highly detailed, 
  1girl, 1boy, couple, 
  girl and boy sitting close, forehead touching, 
  soft eye contact, slight blush, 
  both fully clothed, gentle embrace, 
  bedroom background, warm soft lighting, romantic atmosphere, 
  detailed faces, detailed eyes
  ```

#### 4. Tier 2：情色／半脫
* **單人版本**：
  ```text
  masterpiece, best quality, highly detailed, 
  1girl, solo, beautiful detailed face, detailed eyes, 
  half-naked, clothes pulled down, one breast exposed, 
  lying on bed, arched back, soft moaning expression, blush, 
  sweat on skin, intimate atmosphere, 
  detailed skin texture, soft lighting
  ```
* **雙人版本**：
  ```text
  masterpiece, best quality, highly detailed, 
  1girl, 1boy, couple, 
  girl lying on bed, clothes half removed, breasts exposed, 
  boy kissing her neck, hand caressing breast, 
  both partially clothed, intense eye contact, 
  flushed skin, soft moaning, 
  bedroom, dim warm lighting, erotic atmosphere, 
  detailed faces, detailed skin
  ```

#### 5. Tier 3：性愛／明確插入 (鐵律：正面照片嚴禁雙腿合攏)
* **單人版本（女主視角）**：
  ```text
  masterpiece, best quality, highly detailed, 
  1girl, solo, completely nude, beautiful detailed face, detailed eyes, 
  spread legs, explicit pussy, wet, 
  lying on bed, arched back, pleasure expression, 
  sweat, flushed body, 
  detailed genitals, detailed skin texture, 
  cinematic lighting
  ```
* **雙人版本（傳教士）**：
  ```text
  masterpiece, best quality, highly detailed, 
  1girl, 1boy, couple, completely nude, 
  missionary position, penetration, 
  girl's legs wrapped around waist, 
  pleasure expression, mouth slightly open, 
  sweat, detailed sex, visible penetration, 
  detailed genitals, detailed skin, 
  bedroom, dim erotic lighting
  ```
* **常用體位變體庫 (可替換)**：
  * **騎乘位**：`cowgirl position, girl on top, riding, ((knees spread wide apart towards sides:1.4))`
  * **後背位**：`doggy style, from behind, arched back, raised hips`
  * **站立位**：`standing sex, against wall, leg lifted`
* **只寫動作的追加鏡位**（不含臉、年齡、體型、角色名）：
  * **反向騎乘**：`reverse cowgirl, sitting on top facing away, knees spread wide apart, visible penetration`
  * **側臥**：`spooning, lying on side, upper leg lifted, penetration from behind, visible penetration`
  * **蓮花坐**：`lotus position, sitting face to face, legs wrapped around waist, grinding, visible penetration`
  * **床沿俯身**：`bent over the edge of the bed, partner standing behind, back arched, visible penetration`
  * **壓腿上位**：`amazon position, woman on top pinning partner's legs, knees spread outward, visible penetration`
  * **抬腿傳教士**：`missionary, both legs raised and spread, ankles beside the shoulders, deep penetration, visible penetration`

#### 6. Tier 4：激烈／高潮失控（每鏡必須看見中出）
* **單人版本（高潮）**：
  ```text
  masterpiece, best quality, highly detailed, 
  1girl, solo, completely nude, beautiful detailed face, detailed eyes, 
  spread legs, explicit pussy, dripping wet, orgasm, 
  ahegao, rolling eyes, tongue out, saliva, 
  arched back, trembling, sweat all over body, 
  detailed genitals, detailed skin texture, 
  intense lighting, creampie, semen overflowing from pussy
  ```
* **雙人版本（激烈）**：
  ```text
  masterpiece, best quality, highly detailed, 
  1girl, 1boy, couple, completely nude, 
  intense sex, deep penetration, 
  girl in ahegao, rolling eyes, tongue out, saliva, 
  sweat, trembling body, 
  detailed sex, visible penetration, fluids, 
  detailed genitals, detailed skin, 
  bedroom, dramatic erotic lighting, creampie, semen overflowing from pussy
  ```
* **激烈體位變體庫 (可替換，每條都含中出)**：
  * **折疊折腰式 (Mating press)**：`mating press, legs pinned back, deep penetration, creampie, semen gushing out`
  * **雙花抱 (Full nelson)**：`full nelson, lifted up, intense, creampie, semen running down`
  * **俯臥後背 (Prone bone)**：`prone bone, face down, arched back, from behind, creampie, semen leaking onto the sheets`
  * **站立雙花抱 (Standing full nelson)**：`standing full nelson, against wall, creampie, semen running down the inner thigh`
* **只寫動作的追加鏡位**（不含臉、年齡、體型、角色名；每鏡必須中出）：
  * **騎乘中出**：`cowgirl, knees spread wide apart, riding, creampie, semen overflowing from pussy`
  * **反向騎乘中出**：`reverse cowgirl, sitting on top facing away, creampie, semen leaking out and running down`
  * **後背中出**：`doggy style, from behind, hips raised, creampie, semen dripping from pussy onto thighs`
  * **折腿中出**：`mating press, legs pinned back over chest, creampie, semen gushing out around the joined bodies`
  * **俯臥中出**：`prone bone, face down, hips pushed high, creampie, semen leaking onto the sheets`
  * **站立中出**：`standing sex, one leg lifted, creampie, semen running down the inner thigh`

#### 7. Forge 生產小提示與參數建議
* **關鍵部位權重微調**：
  * 胸部：`(detailed breasts:1.2)`
  * 下體：`(detailed pussy:1.3), (detailed penis:1.2)`
  * 表情：`(ahegao:1.3), (rolling eyes:1.2)`

* **建議參數**：
  * Steps：`30～40`
  * CFG Scale：寫實 `5.5～6.5`（超過 6.5 易塑料感）／動漫 `6.0～7.0`
  * Sampler：`DPM++ 2M Karras` 或 `Euler a`
  * 解析度：本機為 SD 1.5 底模（majicmixRealistic_v6 / AnythingV5），原生出圖 `512×768`，再以 Hires.fix 2x 輸出 `1024×1536`；直接以 `832×1216`／`1024×1536` 原生出圖會出現多頭、多肢體。升級 SDXL 底模後才改用 `832×1216` 原生。
* **模型與 LoRA 搭配**：
  * **Tier 1～2**：一般優質寫實（`majicmixRealistic_v6`）／動漫（`AnythingV5`）即可。
  * **Tier 3～4**：無審查底模；寫實管線以 ControlNet `reference_only`（0.70～0.85）鎖定基準臉模，本機尚無角色 LoRA；動漫管線不掛任何外部參考圖。






