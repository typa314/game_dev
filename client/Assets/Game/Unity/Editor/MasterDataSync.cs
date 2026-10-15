using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Game.Core;
using Game.Domain.MasterData;
using UnityEditor;
using UnityEngine;

namespace Game.Unity.EditorTools
{
    /// <summary>
    /// 把主資料原始檔（data/master/*.json）複製到 Assets/StreamingAssets/MasterData/。
    /// 複製前先跑 MasterDataLoader＋MasterDataValidator，有錯誤就中止，絕不把損壞資料同步進遊戲。
    /// 同時移除目的地中來源已經不存在的 json 檔案。
    /// </summary>
    public static class MasterDataSync
    {
        [MenuItem("Game/Sync Master Data")]
        public static void Sync()
        {
            // Application.dataPath = <專案根>/Assets
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            string src = Path.GetFullPath(Path.Combine(projectRoot, "..", "data", "master"));
            if (!Directory.Exists(src))
            {
                src = Path.GetFullPath(Path.Combine(projectRoot, "data", "master"));
            }
            string dst = Path.Combine(Application.dataPath, "StreamingAssets", "MasterData");

            if (!Directory.Exists(src))
            {
                Debug.LogError("[MasterDataSync] 找不到主資料來源資料夾：" + src);
                return;
            }

            Debug.Log("[MasterDataSync] 開始驗證主資料：" + src);
            MasterDatabase master;
            try
            {
                master = MasterDataLoader.LoadFromDirectory(src);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[MasterDataSync] 主資料載入或 JSON 解析失敗，中止同步！原因：{ex.Message}");
                return;
            }

            List<string> errors = MasterDataValidator.Validate(master);
            if (errors != null && errors.Count > 0)
            {
                Debug.LogError($"[MasterDataSync] 主資料驗證失敗，發現 {errors.Count} 項錯誤，拒絕同步！");
                foreach (string err in errors)
                {
                    Debug.LogError($"  • [ERROR] {err}");
                }
                return;
            }

            Directory.CreateDirectory(dst);

            // 1. 移除目的地中多餘或已在來源中被刪除的 json
            string[] srcFiles = Directory.GetFiles(src, "*.json");
            var srcNames = new HashSet<string>(srcFiles.Select(Path.GetFileName), StringComparer.OrdinalIgnoreCase);

            string[] dstFiles = Directory.GetFiles(dst, "*.json");
            int removedCount = 0;
            foreach (string df in dstFiles)
            {
                string fn = Path.GetFileName(df);
                if (!srcNames.Contains(fn))
                {
                    File.Delete(df);
                    removedCount++;
                }
            }

            // 2. 複製有效檔案
            foreach (string sf in srcFiles)
            {
                File.Copy(sf, Path.Combine(dst, Path.GetFileName(sf)), true);
            }

            AssetDatabase.Refresh();
            Debug.Log($"[MasterDataSync] ✅ 主資料驗證通過並同步成功！已複製 {srcFiles.Length} 個檔案（清理廢棄檔案: {removedCount} 個）：{src} → {dst}");
        }
    }
}
