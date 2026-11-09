using System;
using System.Collections.Generic;
using System.Linq;
using Game.Core;
using Game.Domain;
using Game.Domain.MasterData;
using Game.Domain.Player;
using UnityEngine;

namespace Game.Unity
{
    /// <summary>
    /// OnGUI 除錯面板：沒有美術、沒有 UI 也能操作核心系統（道具、角色、轉蛋、養成、巧遇、戰鬥）。
    /// 掛在 Boot 場景任一 GameObject 上即可；BattleRunner 未指定時會在同一物件上取得或新增。
    /// TODO: 正式 UI（design/14）完成後只在 Development Build 啟用。
    /// </summary>
    public sealed class DebugMenu : MonoBehaviour
    {
        private const string BannerId = "banner_std";
        private const string ExpItem = "exp_s";
        private const string SpotId = "spot_park";
        private const string ChoiceKey = "choice0";

        [SerializeField] private BattleRunner battleRunner;
        [SerializeField] private bool visible = true;

        private Vector2 _scroll;
        private readonly List<string> _log = new List<string>();

        private void Awake()
        {
            if (battleRunner == null) battleRunner = GetComponent<BattleRunner>();
            if (battleRunner == null) battleRunner = gameObject.AddComponent<BattleRunner>();
        }

        private void OnGUI()
        {
            if (GUI.Button(new Rect(10, 10, 110, 24), visible ? "隱藏除錯面板" : "除錯面板")) visible = !visible;
            if (!visible) return;

            GUILayout.BeginArea(new Rect(10, 40, 420, Screen.height - 50), GUI.skin.box);
            _scroll = GUILayout.BeginScrollView(_scroll);

            GameCore core = GameServices.Core;
            if (core == null)
            {
                GUILayout.Label("GameServices 尚未就緒（請看 Console 的 [Boot] 錯誤）");
            }
            else
            {
                DrawItems(core.Player);
                DrawCharacters(core.Player);
                DrawGacha(core);
                DrawGrowth(core);
                DrawEncounter(core);
                DrawBattle(core);
                DrawLog();
            }

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawItems(PlayerState p)
        {
            GUILayout.Label("== 道具 ==");
            foreach (KeyValuePair<string, long> kv in p.Items.OrderBy(x => x.Key, StringComparer.Ordinal))
                GUILayout.Label(kv.Key + " : " + kv.Value);
        }

        private void DrawCharacters(PlayerState p)
        {
            GUILayout.Label("== 角色（" + p.Characters.Count + "）==");
            foreach (KeyValuePair<string, CharacterState> kv in p.Characters.OrderBy(x => x.Key, StringComparer.Ordinal))
            {
                CharacterState c = kv.Value;
                GUILayout.Label(kv.Key + "  Lv" + c.Level + "  覺醒" + c.Awaken + "  好感" + c.Affection + "  立繪:" + c.PortraitStage);
            }
        }

        private void DrawGacha(GameCore core)
        {
            GUILayout.Label("== 轉蛋 " + BannerId + " ==");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("單抽")) Pull(core, 1);
            if (GUILayout.Button("十連")) Pull(core, 10);
            GUILayout.EndHorizontal();

            if (GUILayout.Button("打開契約招募介面 (GachaUI)"))
            {
                var gacha = FindAnyObjectByType<Game.Unity.UI.GachaView>(FindObjectsInactive.Include);
                var lobby = FindAnyObjectByType<Game.Unity.UI.LobbyView>(FindObjectsInactive.Include);
                if (gacha != null)
                {
                    gacha.Open(lobby);
                    if (lobby != null) lobby.gameObject.SetActive(false);
                }
            }
        }

        private void Pull(GameCore core, int count)
        {
            Result<List<GachaResultEntry>> r = core.PullGacha(BannerId, count);
            if (!r.Ok) { Log("轉蛋失敗：" + r.Error); return; }
            foreach (GachaResultEntry e in r.Value)
            {
                string conv = string.Join(", ", e.Converted.Select(x => x.Item + "x" + x.N));
                Log("抽到 " + e.Draw.Rarity + " " + e.Draw.Char + (e.IsNew ? " [NEW]" : " → " + conv));
            }
        }

        private void DrawGrowth(GameCore core)
        {
            string first = core.Player.Characters.Keys.OrderBy(x => x, StringComparer.Ordinal).FirstOrDefault();
            GUILayout.Label("== 養成（對象：" + (first ?? "無") + "）==");
            if (first != null && GUILayout.Button("使用 1 個 " + ExpItem + " 升級"))
            {
                Result<int> r = core.LevelUp(first, new List<ItemStack> { new ItemStack(ExpItem, 1) });
                Log(r.Ok ? first + " 升了 " + r.Value + " 級" : "升級失敗：" + r.Error);
            }
        }

        private void DrawEncounter(GameCore core)
        {
            EncounterState st = core.Player.Encounter;
            GUILayout.Label("== 巧遇 " + SpotId + "（體力 " + st.Stamina + "，待結算：" + (st.Pending ?? "無") + "）==");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("開始巧遇"))
            {
                Result<EncounterStart> r = core.StartEncounter(SpotId);
                if (!r.Ok) Log("巧遇失敗：" + r.Error);
                else if (r.Value.Event == null)
                    Log("沒有事件，獲得 " + string.Join(", ", r.Value.FallbackReward.Select(x => x.Item + "x" + x.N)));
                else Log("巧遇事件：" + r.Value.Event.Id + "（" + r.Value.Event.Character + "）");
            }
            if (GUILayout.Button("結算 " + ChoiceKey))
            {
                Result<List<Outcome>> r = core.ResolveEncounter(ChoiceKey);
                Log(r.Ok ? "巧遇結算完成（" + r.Value.Count + " 項結果）" : "結算失敗：" + r.Error);
            }
            GUILayout.EndHorizontal();
        }

        private void DrawBattle(GameCore core)
        {
            GUILayout.Label("== 戰鬥 ==");
            if (battleRunner.IsRunning)
            {
                GUILayout.Label("戰鬥中：" + battleRunner.StageId + "（操作見戰場面板）");
                return;
            }
            foreach (KeyValuePair<string, StageProgress> kv in core.Player.Stages.OrderBy(x => x.Key, StringComparer.Ordinal))
            {
                if (!kv.Value.Unlocked) continue;
                string label = kv.Key + (kv.Value.Cleared ? "  ★" + kv.Value.Stars : "");
                if (GUILayout.Button("開始 " + label) && battleRunner.StartBattle(kv.Key)) Log("開始戰鬥 " + kv.Key);
            }
            if (battleRunner.LastReward != null)
            {
                GUILayout.Label(battleRunner.LastReward.Ok
                    ? "上一場：" + battleRunner.LastReward.Value.Result.Outcome + "  ★" + battleRunner.LastReward.Value.Stars
                    : "上一場提交失敗：" + battleRunner.LastReward.Error);
            }
        }

        private void DrawLog()
        {
            GUILayout.Label("== 紀錄 ==");
            for (int i = _log.Count - 1; i >= 0; i--) GUILayout.Label(_log[i]);
        }

        private void Log(string msg)
        {
            _log.Add(msg);
            if (_log.Count > 30) _log.RemoveAt(0);
            Debug.Log("[DebugMenu] " + msg);
        }
    }
}
