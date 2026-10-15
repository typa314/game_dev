# A5 驗收補充：Gemini 實作核對

## 目標
依 `A5_atomicity.md` 核對目前 `GachaEngine.cs` 是否完整履行無有效正機率時 fail-before-side-effect 契約。若程式碼仍有缺口，僅修正允許檔案；若已符合，保持不變並回覆核對依據。

## 查核規格
- 空 Rates 或所有 rates <= 0 時，正數抽取必須先拋 `InvalidOperationException`。
- 失敗前不得呼叫 IRandom、不得修改 pity。
- 空正機率池及天井／十連保底可能使用的空池仍須在抽取前拒絕。
- 有效 banner 的抽取行為維持既有規則。

## 允許修改檔案
- 僅 `client/Assets/Game/Domain/Gacha/GachaEngine.cs`。
- 不修改測試、資料、工作清單、文件或其他檔案。

## 限制與完成條件
- C# 9；保留注入式 IRandom；Domain 不引用 UnityEngine/Newtonsoft。
- 不執行命令或測試，由監督者驗收。
- 若不需修改，明確回覆「未修改」，列出核對的方法與理由；若有修改，摘要修改行為及對應契約。
