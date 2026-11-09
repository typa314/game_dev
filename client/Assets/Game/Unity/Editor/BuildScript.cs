using System;
using System.IO;
using Game.Core.Assets;
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

            // 1.5 EventSystem (UGUI 獨立執行檔互動必需)
            var esGo = new GameObject("EventSystem");
            esGo.AddComponent<UnityEngine.EventSystems.EventSystem>();
            esGo.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();

            // 2. GameApp (Bootstrap & BattleRunner & BattlePageView & Debug UI)
            var appGo = new GameObject("GameApp");
            appGo.AddComponent<GameBootstrap>();
            var runner = appGo.AddComponent<BattleRunner>();
            appGo.AddComponent<BattlePageView>();
            appGo.AddComponent<DebugMenu>();
            appGo.AddComponent<BattleDebugView>();

            // 3. 正式 UGUI 大廳首頁 (design/14 §2)
            var lobbyGo = new GameObject("LobbyUI");
            lobbyGo.AddComponent<Game.Unity.UI.LobbyView>();

            // 4. 正式 UGUI 抽卡契約招募介面 (design/07)
            var gachaGo = new GameObject("GachaUI");
            gachaGo.AddComponent<Game.Unity.UI.GachaView>();
            gachaGo.SetActive(false);

            // 5. 正式 UGUI 關卡星圖與戰前編隊介面 (OPT-3)
            var stageSelectGo = new GameObject("StageSelectUI");
            stageSelectGo.AddComponent<Game.Unity.UI.StageSelectView>();
            stageSelectGo.SetActive(false);

            // 6. 正式 UGUI 部隊名冊與角色養成介面 (OPT-5)
            var rosterGo = new GameObject("CharacterRosterUI");
            rosterGo.AddComponent<Game.Unity.UI.CharacterRosterView>();
            rosterGo.SetActive(false);

            // 7. 正式 UGUI 戰術衣櫥與秘書官設定介面 (OPT-5)
            var wardrobeGo = new GameObject("WardrobeUI");
            wardrobeGo.AddComponent<Game.Unity.UI.WardrobeView>();
            wardrobeGo.SetActive(false);

            // 8. 正式 UGUI 劇情圖鑑與回憶紀錄介面 (OPT-6)
            var storyHubGo = new GameObject("StoryHubUI");
            storyHubGo.AddComponent<Game.Unity.UI.StoryHubView>();
            storyHubGo.SetActive(false);

            // 9. 正式 UGUI ADV 劇情播放器介面 (OPT-6)
            var storyPlayerGo = new GameObject("StoryPlayerUI");
            storyPlayerGo.AddComponent<Game.Unity.UI.StoryPlayerView>();
            storyPlayerGo.SetActive(false);

            // 10. 正式 UGUI 巧遇約會地圖介面 (OPT-7)
            var encounterGo = new GameObject("EncounterUI");
            encounterGo.AddComponent<Game.Unity.UI.EncounterView>();
            encounterGo.SetActive(false);

            // 11. 正式 UGUI 戰役榮譽成就介面 (OPT-7)
            var achievementGo = new GameObject("AchievementUI");
            achievementGo.AddComponent<Game.Unity.UI.AchievementView>();
            achievementGo.SetActive(false);

            // 12. 正式 UGUI 系統設定彈窗介面 (OPT-7)
            var settingsGo = new GameObject("SettingsModalUI");
            settingsGo.AddComponent<Game.Unity.UI.SettingsModalView>();
            settingsGo.SetActive(false);

            // 13. 正式 UGUI 標題啟動畫面 (M3-2)
            var titleGo = new GameObject("TitleScreenUI");
            titleGo.AddComponent<Game.Unity.UI.TitleScreenView>();
            titleGo.SetActive(false);

            // 14. 正式 UGUI 多槽位雙層存讀檔介面 (M3-2)
            var saveLoadGo = new GameObject("SaveLoadModalUI");
            saveLoadGo.AddComponent<Game.Unity.UI.SaveLoadModalView>();
            saveLoadGo.SetActive(false);

            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.Refresh();
            Debug.Log("[BuildScript] Boot 場景建立完成：" + ScenePath);
        }

        /// <summary>
        /// 輸出本體與成人 DLC 獨立打包清單並自動執行 R18 隔離審計 (M3-5)
        /// 若本體混入任何 R18 項目則拋出例外終止建置。
        /// </summary>
        [MenuItem("Game/Export DLC Package Manifest")]
        public static SplitManifestResult ExportDlcPackageManifest()
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            string manifestSrc = Path.GetFullPath(Path.Combine(projectRoot, "..", "data", "asset_manifest.csv"));
            if (!File.Exists(manifestSrc))
            {
                manifestSrc = Path.Combine(Application.streamingAssetsPath, "asset_manifest.csv");
            }

            if (!File.Exists(manifestSrc))
            {
                throw new FileNotFoundException("Cannot find asset_manifest.csv to export DLC manifest: " + manifestSrc);
            }

            var catalog = new AssetCatalog();
            catalog.LoadFromFile(manifestSrc);

            var splitResult = AddressablesCatalogAuditor.BuildSplitManifests(catalog);
            if (!splitResult.BaseAuditReport.IsValid)
            {
                Debug.LogError($"[BuildScript] Base Catalog 審計失敗！發現 {splitResult.BaseAuditReport.Errors.Count} 項 R18 違規！");
                foreach (var err in splitResult.BaseAuditReport.Errors)
                {
                    Debug.LogError($"[BuildScript]   - {err}");
                }
                throw new InvalidOperationException($"Base Catalog 混入 R18 項目，終止建置！{string.Join("; ", splitResult.BaseAuditReport.Errors)}");
            }

            // 輸出 Split Manifests 到 StreamingAssets/Manifests
            string outDir = Path.Combine(Application.streamingAssetsPath, "Manifests");
            if (!Directory.Exists(outDir))
            {
                Directory.CreateDirectory(outDir);
            }

            ExportCatalogCsv(splitResult.BaseCatalog, Path.Combine(outDir, "base_manifest.csv"));
            ExportCatalogCsv(splitResult.AdultDlcCatalog, Path.Combine(outDir, "dlc_adult_manifest.csv"));

            Debug.Log($"[BuildScript] DLC 打包清單輸出完成：Base={splitResult.BaseCatalog.Entries.Count} 筆, Adult DLC={splitResult.AdultDlcCatalog.Entries.Count} 筆。");
            return splitResult;
        }

        private static void ExportCatalogCsv(AddressablesCatalog catalog, string targetPath)
        {
            using (var writer = new StreamWriter(targetPath, false, System.Text.Encoding.UTF8))
            {
                writer.WriteLine("key,source,rating,status,note");
                foreach (var entry in catalog.Entries)
                {
                    writer.WriteLine($"{entry.AddressKey},{entry.AssetPath},{entry.Rating},final,{entry.GroupName}");
                }
            }
        }

        [MenuItem("Game/Build Windows Standalone")]
        public static void BuildWindows()
        {
            // 確保主資料已同步
            MasterDataSync.Sync();

            // 確保本體與成人 DLC 審計 100% 通過
            ExportDlcPackageManifest();

            // 每次建置重新產生最新完整的 Boot 場景（確保所有 UI 畫面節點與 EventSystem 100% 掛載）
            CreateBootScene();

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
