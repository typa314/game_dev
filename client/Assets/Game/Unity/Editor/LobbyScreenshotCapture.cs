using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Game.Unity.UI;

namespace Game.Unity.EditorTools
{
    public static class LobbyScreenshotCapture
    {
        [MenuItem("Game/Capture Lobby Screenshot")]
        public static void CaptureScreenshot()
        {
            Debug.Log("[LobbyScreenshotCapture] 開始渲染 UGUI 大廳首頁截圖...");

            // 1. 開啟 Boot 場景
            string scenePath = "Assets/Scenes/Boot.unity";
            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

            // 2. 獲取或建立 Camera
            Camera cam = Camera.main;
            if (cam == null)
            {
                var camGo = new GameObject("CaptureCamera");
                cam = camGo.AddComponent<Camera>();
                camGo.tag = "MainCamera";
            }
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.12f, 0.13f, 0.17f, 1f);

            // 3. 獲取 LobbyView
            LobbyView lobby = UnityEngine.Object.FindAnyObjectByType<LobbyView>();
            if (lobby == null)
            {
                var go = new GameObject("LobbyUI");
                lobby = go.AddComponent<LobbyView>();
            }

            lobby.EnsureUIHierarchy();

            // 設置時段背景（晨曦暖金色調）與問候語
            lobby.SetBackground("scene_lobby_default", Game.Core.UI.TimeOfDay.Morning);
            lobby.SetGreeting("「指揮官，歡迎回到作戰指揮部！\n作戰前要先去神社祈福求個大吉嗎？櫻會一直守護您的！」");

            // 設置頂部測試數據
            Transform topHeader = lobby.transform.Find("TopHeaderBar");
            if (topHeader != null)
            {
                var pName = topHeader.Find("PlayerName")?.GetComponent<Text>();
                if (pName != null) pName.text = "指揮官 (Commander) Lv.25";
                var gold = topHeader.Find("GoldLabel")?.GetComponent<Text>();
                if (gold != null) gold.text = "128,500";
                var gem = topHeader.Find("GemLabel")?.GetComponent<Text>();
                if (gem != null) gem.text = "3,450";
                var sta = topHeader.Find("StaminaLabel")?.GetComponent<Text>();
                if (sta != null) sta.text = "5/5";
            }

            // 4. 將 Canvas 調整為 ScreenSpaceCamera 以便離線渲染
            Canvas canvas = lobby.GetComponent<Canvas>();
            if (canvas != null)
            {
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = cam;
                canvas.planeDistance = 100;
            }

            Canvas.ForceUpdateCanvases();

            // 5. 渲染至 RenderTexture (1920 x 1080 Full HD)
            int width = 1920;
            int height = 1080;
            RenderTexture rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            cam.Render();

            RenderTexture.active = rt;
            Texture2D tex = new Texture2D(width, height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            tex.Apply();

            byte[] pngData = tex.EncodeToPNG();

            // 輸出路徑 (嚴禁放桌面)
            string outDir1 = @"C:\Users\typa3\agent_tmp\game_dev";
            if (!Directory.Exists(outDir1)) Directory.CreateDirectory(outDir1);
            string outPath1 = Path.Combine(outDir1, "lobby_screenshot.png");
            File.WriteAllBytes(outPath1, pngData);

            string outDir2 = @"C:\Users\typa3\.gemini\antigravity\brain\8fa3f2ab-7070-448a-a7f7-74f8c28716b1";
            if (!Directory.Exists(outDir2)) Directory.CreateDirectory(outDir2);
            string outPath2 = Path.Combine(outDir2, "lobby_screenshot.png");
            File.WriteAllBytes(outPath2, pngData);

            // 清理資源
            cam.targetTexture = null;
            RenderTexture.active = null;
            UnityEngine.Object.DestroyImmediate(rt);
            UnityEngine.Object.DestroyImmediate(tex);

            // 還原 Canvas 為 ScreenSpaceOverlay
            if (canvas != null)
            {
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.worldCamera = null;
            }

            Debug.Log("[LobbyScreenshotCapture] 截圖生成成功！\n" + outPath1 + "\n" + outPath2);
        }
    }
}
