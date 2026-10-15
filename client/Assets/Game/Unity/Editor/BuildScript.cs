using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.Unity.EditorTools
{
    public static class BuildScript
    {
        private const string ScenePath = "Assets/Scenes/Boot.unity";

        [MenuItem("Game/Setup Boot Scene")]
        public static void CreateBootScene()
        {
            string dir = Path.GetDirectoryName(Path.Combine(Application.dataPath, "..", ScenePath));
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // 1. Camera
            var camGo = new GameObject("Main Camera");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.12f, 0.12f, 0.14f);
            camGo.tag = "MainCamera";

            // 2. GameApp (Bootstrap & Debug UI)
            var appGo = new GameObject("GameApp");
            appGo.AddComponent<GameBootstrap>();
            var runner = appGo.AddComponent<BattleRunner>();
            appGo.AddComponent<DebugMenu>();
            appGo.AddComponent<BattleDebugView>();

            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.Refresh();
            Debug.Log("[BuildScript] Boot 場景建立完成：" + ScenePath);
        }

        [MenuItem("Game/Build Windows Standalone")]
        public static void BuildWindows()
        {
            // 確保主資料已同步
            MasterDataSync.Sync();

            // 確保 Boot 場景存在
            if (!File.Exists(Path.Combine(Application.dataPath, "..", ScenePath)))
            {
                CreateBootScene();
            }

            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            string buildDir = Path.GetFullPath(Path.Combine(projectRoot, "..", "build", "Windows"));
            string exePath = Path.Combine(buildDir, "GameClient.exe");

            if (!Directory.Exists(buildDir))
            {
                Directory.CreateDirectory(buildDir);
            }

            var buildPlayerOptions = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = exePath,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.Development
            };

            Debug.Log("[BuildScript] 開始建置 Windows Standalone：" + exePath);
            var report = BuildPipeline.BuildPlayer(buildPlayerOptions);

            if (report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded)
            {
                Debug.Log($"[BuildScript] 建置成功！大小：{report.summary.totalSize} bytes，耗時：{report.summary.totalTime.TotalSeconds:F2} 秒");
            }
            else
            {
                Debug.LogError($"[BuildScript] 建置失敗！結果：{report.summary.result}，錯誤數：{report.summary.totalErrors}");
                throw new Exception($"Build failed with result: {report.summary.result}");
            }
        }
    }
}
