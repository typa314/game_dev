# Gemini 實作／Claude 監督 協作流程

> 使用者指示（2026-10-14）：「代碼透過要求 Gemini 來實作，你（Claude）只作為監督者。」
> 專案禁止外洩：Gemini（agy）是本專案**唯一**獲准接收程式碼的外部模型；codex、grok 仍然禁止。

## 1. 角色
| 角色 | 負責 | 不做 |
|---|---|---|
| **Gemini**（agy.exe） | 依任務規格實作或修改程式碼與測試 | 不執行指令、不 git、不連網查資料、不改規格範圍外的檔案 |
| **Claude**（監督者） | 撰寫任務規格、驗收、回饋失敗結果、記錄 SESSION、更新工作清單狀態 | **不自己寫產品程式碼**；只做驗收需要的操作（陽性對照時暫時修改並還原） |

## 2. 呼叫方式
```bash
agy.exe -p "<任務規格>" \
  --model "Gemini 3.8 Flash (High)" \
  --mode accept-edits \
  --add-dir "F:\game\client" --add-dir "F:\game\data" --add-dir "F:\game\design"
```
- 只用 `--mode accept-edits`，讓 Gemini 能改檔；**不使用** `--dangerously-skip-permissions`，所以它不能執行指令。
- **不使用** `--remote-control`。
- 提示詞受 Windows 命令列長度限制，約 30,000 字元以內；長規格寫成檔案放在 `client/tasks/`，提示詞只要求 Gemini 去讀那個檔案。

## 3. 每個任務的流程
1. **規格**：Claude 在 `client/tasks/<ID>.md` 寫好任務，內容包含：
   - 目標、對應的工作清單項目與查核點
   - **允許修改的檔案清單**
   - 介面限制
   - 完成條件
2. **快照**：交付前，對 `client/Assets`、`data` 底下所有檔案計算 SHA-256。
3. **交付**：呼叫 agy。
4. **範圍檢查**：重新計算 hash，列出新增、修改、刪除的檔案；**改到允許清單以外的檔案，直接退回**。
5. **驗收**（全部由 Claude 執行）：
   - `dotnet build` 0 個錯誤
   - `dotnet test` 全部通過，而且原本的測試數量沒有減少
   - **陽性對照**：至少挑 1 個新測試，暫時把被測程式改壞，確認測試會失敗，再還原
   - 回到原始碼核實：每條查核點都有測試對應，測試的斷言值和規格一致（不是照抄程式的輸出）
   - 檢查是否有 Domain、Core 引用 UnityEngine，或使用 C# 10 以上語法
6. **回饋**：沒有通過時，把錯誤輸出原文交給 Gemini 修正。
7. **紀錄**：在 SESSION.md 記錄任務 ID、Gemini 模型、修改的檔案、驗收結果和陽性對照；並更新 `design/17` 的狀態。

## 4. 防無限循環（沿用常設規則）
- 同一個任務交給 Gemini **最多重試 1 次**，追問最多 **3 輪**。
- 超過上限仍然沒有通過時，就停下來，把失敗原因和原始資料回報給使用者；Claude **不會自己動手改寫**。

## 5. 共同限制（每份任務規格都會附上）
- C# 9 以內：不使用 record、init、file-scoped namespace、global using。
- `Assets/Game/Domain` 和 `Assets/Game/Core` 不可以引用 UnityEngine；Domain 也不可以引用 Newtonsoft。
- 測試使用 NUnit 3，必須同時能在 Unity Test Runner 和 `client/DotnetTests` 中執行。
- 所有隨機都透過 `IRandom`，所有時間都透過 `IClock`。
- 註解使用繁體中文，風格和現有程式碼一致。
