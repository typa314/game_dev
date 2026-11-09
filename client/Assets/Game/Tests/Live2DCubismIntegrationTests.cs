#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using System.Diagnostics;
using Game.Unity;
using Game.Unity.UI;
using Live2D.Cubism.Core;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Profiling;

namespace Game.Tests
{
    /// <summary>
    /// F5：Live2D Cubism SDK (5-r.6.1) 官方範例模型整合與效能基準測試（design/09_live2d.md、design/17 §F5）。
    /// 涵蓋：
    /// - F5-1：載入官方範例模型（Mao / Koharu / Natori），驗證 CubismModel、Parameters、Drawables 顯示與更新正常。
    /// - F5-2：切換表情（CubismExpressionController）與點擊部位反應（Head / Body），含未知部位攔截之陽性對照。
    /// - F5-3：口型同步（SetLipSync -> ParamMouthOpenY）與視線跟隨（LookAt -> ParamAngleX/Y、ParamEyeBallX/Y）。
    /// - F5-4：量測 1 個模型與 3 個模型同時播放時的每幀耗時（等效 FPS）與記憶體佔用，確立最低配備基準。
    /// </summary>
    [TestFixture]
    public sealed class Live2DCubismIntegrationTests
    {
        private const string MaoPrefabPath = "Assets/Live2D/Cubism/Samples/Models/Mao/Mao.prefab";
        private const string KoharuPrefabPath = "Assets/Live2D/Cubism/Samples/Models/Koharu/Koharu.prefab";
        private const string NatoriPrefabPath = "Assets/Live2D/Cubism/Samples/Models/Natori/Natori.prefab";
        private const string MaoMotionPath = "Assets/Live2D/Cubism/Samples/Models/Mao/motions/mtn_01.anim";

        private readonly List<GameObject> _spawned = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            for (int i = _spawned.Count - 1; i >= 0; i--)
            {
                if (_spawned[i] != null)
                {
                    UnityEngine.Object.DestroyImmediate(_spawned[i]);
                }
            }
            _spawned.Clear();
        }

        private GameObject Track(GameObject go)
        {
            if (go != null) _spawned.Add(go);
            return go;
        }

        [Test]
        public void F5_1_Live2DCubismSdk_LoadsOfficialSampleModel_AndDisplaysDrawablesAndParameters()
        {
            GameObject maoPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(MaoPrefabPath);
            Assert.IsNotNull(maoPrefab, $"應成功載入官方範例模型 Prefab: {MaoPrefabPath}");

            AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(MaoMotionPath);
            GameObject hostGo = Track(new GameObject("F5_1_Live2DHost"));
            Live2DPortraitView view = hostGo.AddComponent<Live2DPortraitView>();

            CubismModel model = view.LoadFromPrefab(maoPrefab, clip != null ? new[] { clip } : null);
            Assert.IsNotNull(model, "CubismModel 實例不應為 null");
            Assert.IsNotNull(model.Parameters, "CubismModel.Parameters 不應為 null");
            Assert.Greater(model.Parameters.Length, 20, "官方範例模型 Mao 應包含超過 20 個控制參數");
            Assert.IsNotNull(model.Drawables, "CubismModel.Drawables 不應為 null");
            Assert.Greater(model.Drawables.Length, 20, "官方範例模型 Mao 應包含超過 20 個繪製網格 (Drawables)");
            Assert.IsNotNull(view.ExpressionController, "應成功綁定 CubismExpressionController");
            Assert.IsNotNull(view.MotionController, "應成功綁定 CubismMotionController");
        }

        [Test]
        public void F5_2_ExpressionSwitching_And_TouchReactions_And_InvalidArea_PositiveControl()
        {
            GameObject maoPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(MaoPrefabPath);
            AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(MaoMotionPath);

            GameObject hostGo = Track(new GameObject("F5_2_Live2DHost"));
            Live2DPortraitView view = hostGo.AddComponent<Live2DPortraitView>();
            view.LoadFromPrefab(maoPrefab, clip != null ? new[] { clip } : null);

            // 1. 切換表情
            view.SetExpression("smile");
            Assert.AreEqual("smile", view.CurrentExpression);
            Assert.AreEqual(1, view.CurrentExpressionIndex, "smile 語意詞應對應至第 1 號表情索引");

            view.SetExpression("shy");
            Assert.AreEqual("shy", view.CurrentExpression);
            Assert.AreEqual(3, view.CurrentExpressionIndex, "shy 語意詞應對應至第 3 號表情索引");

            view.SetExpression("exp_06");
            StringAssert.Contains("exp_06", view.ExpressionController.ExpressionsList.CubismExpressionObjects[view.CurrentExpressionIndex].name, "直接指定 exp_06 應精確匹配至 exp_06.exp3 表情資產");

            // 2. 點擊反應：Head 與 Body
            Assert.AreEqual(0, view.TouchReactionCount);
            Assert.IsTrue(view.OnTouched("Head"), "點擊 Head 應觸發反應");
            Assert.AreEqual(1, view.TouchReactionCount);
            Assert.AreEqual("Head", view.LastTouchedArea);
            Assert.AreEqual("tap_head", view.CurrentMotion);

            Assert.IsTrue(view.OnTouched("Body"), "點擊 Body 應觸發動作反應");
            Assert.AreEqual(2, view.TouchReactionCount);
            Assert.AreEqual("Body", view.LastTouchedArea);
            Assert.AreEqual("tap_body", view.CurrentMotion);

            // 3. 陽性對照：未知或空白點擊區域不應觸發反應
            Assert.IsFalse(view.OnTouched("InvalidFootArea"), "未知點擊區域必須回傳 false (陽性對照)");
            Assert.IsFalse(view.OnTouched(""), "空白點擊區域必須回傳 false");
            Assert.AreEqual(2, view.TouchReactionCount, "無效點擊不得增加反應計數");
        }

        [Test]
        public void F5_3_LipSync_And_LookAt_DrivesCubismParametersDeterministically()
        {
            GameObject maoPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(MaoPrefabPath);
            GameObject hostGo = Track(new GameObject("F5_3_Live2DHost"));
            Live2DPortraitView view = hostGo.AddComponent<Live2DPortraitView>();
            view.LoadFromPrefab(maoPrefab);

            // 1. 口型同步 (SetLipSync -> ParamMouthOpenY)
            view.SetLipSync(0.85f);
            view.StepModelUpdate();
            Assert.AreEqual(0.85f, view.LipSyncLevel, 1e-4f);
            Assert.AreEqual(0.85f, view.MouthOpenParameterValue, 1e-4f, "ParamMouthOpenY 應隨 SetLipSync 同步更新");

            // 邊界夾制驗證 (Clamp01)
            view.SetLipSync(2.5f);
            Assert.AreEqual(1.0f, view.MouthOpenParameterValue, 1e-4f);
            view.SetLipSync(-0.5f);
            Assert.AreEqual(0.0f, view.MouthOpenParameterValue, 1e-4f);

            // 2. 視線跟隨 (LookAt -> ParamAngleX / ParamAngleY)
            view.LookAt(new Vector2(0.5f, -0.5f));
            view.StepModelUpdate();
            Assert.AreEqual(15.0f, view.AngleXParameterValue, 1e-3f, "LookAt X=0.5 應驅動 ParamAngleX 至 +15 度");
            Assert.AreEqual(-15.0f, view.AngleYParameterValue, 1e-3f, "LookAt Y=-0.5 應驅動 ParamAngleY 至 -15 度");
        }

        [Test]
        public void F5_4_PerformanceBenchmark_1Model_And_3Models_FpsAndMemoryMeasurement()
        {
            GameObject maoPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(MaoPrefabPath);
            GameObject koharuPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(KoharuPrefabPath);
            GameObject natoriPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(NatoriPrefabPath);
            Assert.IsNotNull(maoPrefab);
            Assert.IsNotNull(koharuPrefab);
            Assert.IsNotNull(natoriPrefab);

            const int frames = 300;

            // --- 基準 1：單一 Live2D 模型 (Lobby 標準場景) ---
            GC.Collect();
            long memBefore1 = Profiler.GetTotalAllocatedMemoryLong();

            GameObject host1 = Track(new GameObject("F5_4_Bench_1Model"));
            Live2DPortraitView view1 = host1.AddComponent<Live2DPortraitView>();
            view1.LoadFromPrefab(maoPrefab);

            // 預熱 10 幀
            for (int i = 0; i < 10; i++) view1.StepModelUpdate();

            Stopwatch sw1 = Stopwatch.StartNew();
            for (int f = 0; f < frames; f++)
            {
                float t = f * 0.05f;
                view1.SetLipSync(Mathf.Abs(Mathf.Sin(t)));
                view1.LookAt(new Vector2(Mathf.Cos(t) * 0.5f, Mathf.Sin(t) * 0.5f));
                if (f % 60 == 0) view1.SetExpression((f / 60) % 2 == 0 ? "smile" : "shy");
                view1.StepModelUpdate();
            }
            sw1.Stop();
            long memAfter1 = Profiler.GetTotalAllocatedMemoryLong();
            double avgMs1 = sw1.Elapsed.TotalMilliseconds / frames;
            double eqFps1 = 1000.0 / Math.Max(0.0001, avgMs1);
            double memDeltaMb1 = Math.Max(0, memAfter1 - memBefore1) / (1024.0 * 1024.0);

            UnityEngine.Object.DestroyImmediate(host1);
            _spawned.Remove(host1);

            // --- 基準 2：3 個 Live2D 模型同時運作 (ADV 劇情上限 3 人同場) ---
            GC.Collect();
            long memBefore3 = Profiler.GetTotalAllocatedMemoryLong();

            GameObject host3A = Track(new GameObject("F5_4_Bench_3Models_Mao"));
            GameObject host3B = Track(new GameObject("F5_4_Bench_3Models_Koharu"));
            GameObject host3C = Track(new GameObject("F5_4_Bench_3Models_Natori"));
            Live2DPortraitView vA = host3A.AddComponent<Live2DPortraitView>();
            Live2DPortraitView vB = host3B.AddComponent<Live2DPortraitView>();
            Live2DPortraitView vC = host3C.AddComponent<Live2DPortraitView>();
            vA.LoadFromPrefab(maoPrefab);
            vB.LoadFromPrefab(koharuPrefab);
            vC.LoadFromPrefab(natoriPrefab);

            for (int i = 0; i < 10; i++)
            {
                vA.StepModelUpdate();
                vB.StepModelUpdate();
                vC.StepModelUpdate();
            }

            Stopwatch sw3 = Stopwatch.StartNew();
            for (int f = 0; f < frames; f++)
            {
                float t = f * 0.05f;
                vA.SetLipSync(Mathf.Abs(Mathf.Sin(t)));
                vA.LookAt(new Vector2(Mathf.Cos(t) * 0.5f, Mathf.Sin(t) * 0.5f));
                vA.StepModelUpdate();

                vB.SetLipSync(Mathf.Abs(Mathf.Cos(t)));
                vB.LookAt(new Vector2(Mathf.Sin(t) * 0.4f, Mathf.Cos(t) * 0.4f));
                vB.StepModelUpdate();

                vC.SetLipSync(Mathf.Abs(Mathf.Sin(t * 1.3f)));
                vC.LookAt(new Vector2(-Mathf.Cos(t) * 0.3f, Mathf.Sin(t) * 0.3f));
                vC.StepModelUpdate();
            }
            sw3.Stop();
            long memAfter3 = Profiler.GetTotalAllocatedMemoryLong();
            long totalAllocMb = memAfter3 / (1024 * 1024);
            double avgMs3 = sw3.Elapsed.TotalMilliseconds / frames;
            double eqFps3 = 1000.0 / Math.Max(0.0001, avgMs3);
            double memDeltaMb3 = Math.Max(0, memAfter3 - memBefore3) / (1024.0 * 1024.0);

            UnityEngine.Debug.Log(
                $"[F5 Live2D Benchmark] 1 Model (Mao): AvgFrame={avgMs1:F3} ms (EqFPS={eqFps1:F1}), MemDelta={memDeltaMb1:F2} MB | " +
                $"3 Models (Mao+Koharu+Natori): AvgFrame={avgMs3:F3} ms (EqFPS={eqFps3:F1}), MemDelta={memDeltaMb3:F2} MB, TotalAllocated={totalAllocMb} MB");

            Assert.Less(avgMs1, 16.67, "單一 Live2D 模型每幀更新耗時應遠低於 16.67ms (60 FPS 預算)");
            Assert.Less(avgMs3, 16.67, "3 個 Live2D 模型同場每幀更新耗時應低於 16.67ms (60 FPS 預算)");
        }

        [Test]
        public void M3_1_1_PortraitFactory_WhenLive2dEnabledAndPrefabExists_ReturnsLive2DPortraitView_AndBindsCubismModel()
        {
            var resolved = new Game.Domain.Portrait.ResolvedPortrait
            {
                Character = "char01_claire",
                Art = new Game.Domain.MasterData.PortraitArt
                {
                    Live2d = "Mao",
                    Stand = "char01_claire"
                }
            };

            GameObject hostGo = Track(new GameObject("M3_1_1_Host"));
            IPortraitView view = PortraitFactory.CreateOrAttach(hostGo, resolved, useLive2d: true);

            Assert.IsNotNull(view);
            Assert.IsInstanceOf<Live2DPortraitView>(view, "useLive2d 為 true 且資產存在時應建立 Live2DPortraitView");
            var l2d = (Live2DPortraitView)view;
            Assert.IsNotNull(l2d.Model, "應成功載入並綁定 CubismModel");
            Assert.AreEqual("Mao", l2d.Model.name);
            Assert.IsTrue(l2d.enabled);
        }

        [Test]
        public void M3_1_2_PortraitFactory_WhenLive2dDisabled_ReturnsStaticPortraitView_PositiveControl()
        {
            var resolved = new Game.Domain.Portrait.ResolvedPortrait
            {
                Character = "char01_claire",
                Art = new Game.Domain.MasterData.PortraitArt
                {
                    Live2d = "Mao",
                    Stand = "char01_claire"
                }
            };

            GameObject hostGo = Track(new GameObject("M3_1_2_Host"));
            IPortraitView view = PortraitFactory.CreateOrAttach(hostGo, resolved, useLive2d: false);

            Assert.IsNotNull(view);
            Assert.IsInstanceOf<StaticPortraitView>(view, "useLive2d 為 false 時必須強制退回 StaticPortraitView (陽性對照)");
            var staticView = (StaticPortraitView)view;
            Assert.IsTrue(staticView.enabled);
        }

        [Test]
        public void M3_1_3_StoryPlayerView_And_LobbyView_ScriptAndTap_DrivesLive2DExpressions_Motions_AndLipSync()
        {
            GameObject maoPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(MaoPrefabPath);
            GameObject hostGo = Track(new GameObject("M3_1_3_Host"));
            Live2DPortraitView l2d = hostGo.AddComponent<Live2DPortraitView>();
            l2d.LoadFromPrefab(maoPrefab);

            // 1. StoryPlayerView 串接測試
            GameObject storyGo = Track(new GameObject("M3_1_3_StoryPlayerUI"));
            StoryPlayerView storyView = storyGo.AddComponent<StoryPlayerView>();
            storyView.BindLive2DCharacter("Claire", l2d);

            Assert.AreSame(l2d, storyView.ActiveLive2DView);

            // 驗證台詞推進驅動口型開合
            storyView.SetDialogue("指揮官，今天也要一起努力喔！");
            Assert.AreEqual(0.6f, l2d.LipSyncLevel, 1e-4f, "顯示台詞時應自動驅動口型開合至 0.6");

            // 驗證語音播放驅動口型開合
            storyView.PlayVoice("v_claire_001");
            Assert.AreEqual(0.85f, l2d.LipSyncLevel, 1e-4f, "播放語音時應提升口型開合至 0.85");

            // 驗證 <<expr Claire shy>>
            storyView.SetCharacterExpression("Claire", "shy");
            Assert.AreEqual("shy", l2d.CurrentExpression);
            Assert.AreEqual(3, l2d.CurrentExpressionIndex, "shy 表情應對應至第 3 號表情索引");

            // 驗證 <<motion Claire happy>>
            storyView.SetCharacterMotion("Claire", "happy");
            Assert.AreEqual("happy", l2d.CurrentMotion);

            // 2. LobbyView 看板娘點擊串接測試
            GameObject lobbyGo = Track(new GameObject("M3_1_3_LobbyUI"));
            LobbyView lobbyView = lobbyGo.AddComponent<LobbyView>();
            lobbyView.EnsureUIHierarchy();
            lobbyView.BindLive2DPortrait(l2d);

            int touchBefore = l2d.TouchReactionCount;
            lobbyView.OnCharacterTapped();
            Assert.AreEqual(touchBefore + 1, l2d.TouchReactionCount, "大廳首次點擊應觸發 Head 部位反應");
            Assert.AreEqual("Head", l2d.LastTouchedArea);

            lobbyView.OnCharacterTapped();
            Assert.AreEqual(touchBefore + 2, l2d.TouchReactionCount, "大廳第二次點擊應觸發 Body 部位反應");
            Assert.AreEqual("Body", l2d.LastTouchedArea);
        }
    }
}
#endif
