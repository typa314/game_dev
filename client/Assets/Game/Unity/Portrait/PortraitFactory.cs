#if UNITY_5_3_OR_NEWER
using System;
using Game.Domain.Portrait;
using Live2D.Cubism.Core;
using UnityEngine;

namespace Game.Unity
{
    /// <summary>
    /// 立繪工廠（design/09 §2、design/21 §Task M3-1）：
    /// 當 SettingsState.UseLive2d == true 且角色對應之 Live2D 模型資產存在時選用 Live2DPortraitView；
    /// 否則無縫退回靜態立繪（StaticPortraitView），絕不拋出例外。
    /// </summary>
    public static class PortraitFactory
    {
        /// <summary>
        /// 建立或掛載立繪視圖。
        /// </summary>
        /// <param name="host">掛載組件之目標 GameObject</param>
        /// <param name="portrait">決策完成之立繪資料物件</param>
        /// <param name="useLive2d">是否啟用 Live2D (例如讀取自設定)</param>
        /// <param name="prefabLoader">自訂 Prefab 載入委派 (若為 null 則嘗試 Resources 或 AssetDatabase)</param>
        /// <returns>實作 IPortraitView 之組件 (Live2DPortraitView 或 StaticPortraitView)</returns>
        public static IPortraitView CreateOrAttach(
            GameObject host,
            ResolvedPortrait portrait,
            bool useLive2d,
            Func<string, GameObject> prefabLoader = null)
        {
            if (host == null)
            {
                throw new ArgumentNullException(nameof(host));
            }

            GameObject modelPrefab = null;

            if (useLive2d)
            {
                string key = portrait?.Art?.Live2d;
                if (string.IsNullOrEmpty(key))
                {
                    key = portrait?.Character;
                }

                if (!string.IsNullOrEmpty(key) && prefabLoader != null)
                {
                    try
                    {
                        modelPrefab = prefabLoader(key);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[PortraitFactory] 透過自訂 loader 載入 Live2D Prefab '{key}' 失敗: {ex.Message}");
                        modelPrefab = null;
                    }
                }

#if UNITY_EDITOR
                if (modelPrefab == null && !string.IsNullOrEmpty(key))
                {
                    string[] candidatePaths = new[]
                    {
                        $"Assets/Live2D/Cubism/Samples/Models/{key}/{key}.prefab",
                        "Assets/Live2D/Cubism/Samples/Models/Mao/Mao.prefab",
                        "Assets/Live2D/Cubism/Samples/Models/Koharu/Koharu.prefab",
                        "Assets/Live2D/Cubism/Samples/Models/Natori/Natori.prefab"
                    };

                    for (int i = 0; i < candidatePaths.Length; i++)
                    {
                        if (candidatePaths[i].IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            modelPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(candidatePaths[i]);
                            if (modelPrefab != null) break;
                        }
                    }

                    // 若指定 key 找不到但仍有 Mao 官方模型可供測試回退
                    if (modelPrefab == null && (key.IndexOf("Mao", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                key.IndexOf("Sample", StringComparison.OrdinalIgnoreCase) >= 0))
                    {
                        modelPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Live2D/Cubism/Samples/Models/Mao/Mao.prefab");
                    }
                }
#endif
            }

            // 判斷是否成功載入具備 CubismModel 的 Prefab
            bool hasValidCubismModel = modelPrefab != null &&
                (modelPrefab.GetComponent<CubismModel>() != null || modelPrefab.GetComponentInChildren<CubismModel>() != null);

            if (useLive2d && hasValidCubismModel)
            {
                // 停用靜態立繪視圖（若存在）
                var staticView = host.GetComponent<StaticPortraitView>();
                if (staticView != null)
                {
                    staticView.enabled = false;
                }

                var live2dView = host.GetComponent<Live2DPortraitView>() ?? host.AddComponent<Live2DPortraitView>();
                live2dView.enabled = true;
                live2dView.LoadFromPrefab(modelPrefab);
                if (portrait != null)
                {
                    live2dView.Show(portrait);
                }
                return live2dView;
            }
            else
            {
                // 退回至靜態立繪視圖 (StaticPortraitView)
                var live2dView = host.GetComponent<Live2DPortraitView>();
                if (live2dView != null)
                {
                    live2dView.enabled = false;
                    if (live2dView.Model != null && live2dView.Model.gameObject != null)
                    {
                        live2dView.Model.gameObject.SetActive(false);
                    }
                }

                var staticView = host.GetComponent<StaticPortraitView>() ?? host.AddComponent<StaticPortraitView>();
                staticView.enabled = true;
                if (portrait != null)
                {
                    staticView.Show(portrait);
                }
                return staticView;
            }
        }
    }
}
#endif
