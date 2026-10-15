using Game.Domain.Battle;
using UnityEngine;

namespace Game.Unity
{
    /// <summary>
    /// OnGUI 戰場除錯畫面：雙方據點 HP、能量、單位（依 X 位置畫成小方塊，我方藍、敵方紅）、
    /// 出擊按鈕列（鍵盤 1–0 對應 slot 0–9）、能量升級、據點砲、倍速/暫停。
    /// 只讀取 BattleRunner.Sim 的狀態；輸入一律經由 BattleRunner 轉成指令。
    /// TODO: 正式的 BattleView 改用 SpriteRenderer＋動畫，並消化 Sim.TickEvents 播特效。
    /// </summary>
    public sealed class BattleDebugView : MonoBehaviour
    {
        private const float UnitSize = 14f;

        [SerializeField] private BattleRunner battleRunner;

        private void Awake()
        {
            if (battleRunner == null) battleRunner = GetComponent<BattleRunner>();
        }

        private void OnGUI()
        {
            // DebugMenu 可能在自己的 Awake 才新增 BattleRunner，所以這裡再補抓一次
            if (battleRunner == null) battleRunner = GetComponent<BattleRunner>();
            if (battleRunner == null || battleRunner.Sim == null) return;
            BattleSimulator sim = battleRunner.Sim;

            HandleKeys();

            float left = 440f, width = Mathf.Max(200f, Screen.width - left - 20f);
            float top = 40f;

            // 狀態列
            int tps = sim.TicksPerSecond;
            GUI.Label(new Rect(left, top, width, 22),
                "Stage " + battleRunner.StageId + "  " + sim.Outcome + "  t=" + (sim.Tick / tps) + "s"
                + "  能量 " + (sim.EnergyMilli / 1000) + "/" + sim.EnergyMax + " (Lv" + sim.EnergyLevel + ")"
                + "  擊倒 " + sim.EnemiesDefeated);
            top += 24f;
            GUI.Label(new Rect(left, top, width / 2f, 22), "我方據點 " + sim.AllyBaseHp + "/" + sim.AllyBaseMaxHp);
            GUI.Label(new Rect(left + width / 2f, top, width / 2f, 22), "敵方據點 " + sim.EnemyBaseHp + "/" + sim.EnemyBaseMaxHp);
            top += 26f;

            // 戰場：X=0 我方據點（左），X=LengthMilli 敵方據點（右）
            Rect field = new Rect(left, top, width, 60f);
            GUI.Box(field, "");
            Color prev = GUI.color;
            foreach (BattleUnit u in sim.Units)
            {
                if (!u.Alive) continue;
                float nx = sim.LengthMilli > 0 ? (float)((double)u.X / sim.LengthMilli) : 0f;
                float x = field.x + nx * (field.width - UnitSize);
                float y = field.y + (u.Side == Side.Ally ? 10f : 34f);
                GUI.color = u.Side == Side.Ally ? Color.blue : Color.red;
                GUI.Box(new Rect(x, y, UnitSize, UnitSize), "");
            }
            GUI.color = prev;
            top += 68f;

            // 出擊按鈕列
            for (int slot = 0; slot < 10; slot++)
            {
                bool can = sim.CanDeploy(slot);
                GUI.enabled = can && battleRunner.IsRunning;
                string key = slot == 9 ? "0" : (slot + 1).ToString();
                if (GUI.Button(new Rect(left + slot * 52f, top, 50f, 30f), "[" + key + "]")) battleRunner.Deploy(slot);
            }
            GUI.enabled = true;
            top += 36f;

            int? upCost = sim.EnergyUpgradeCost;
            GUI.enabled = battleRunner.IsRunning && upCost != null;
            if (GUI.Button(new Rect(left, top, 140f, 26f), upCost != null ? "能量升級 (" + upCost.Value + ")" : "能量已滿級"))
                battleRunner.UpgradeEnergy();
            GUI.enabled = battleRunner.IsRunning && sim.CannonFullTicks > 0 && sim.CannonChargeTicks >= sim.CannonFullTicks;
            int pct = sim.CannonFullTicks > 0 ? sim.CannonChargeTicks * 100 / sim.CannonFullTicks : 0;
            if (GUI.Button(new Rect(left + 150f, top, 140f, 26f), "據點砲 " + pct + "%")) battleRunner.FireCannon();
            GUI.enabled = true;

            if (GUI.Button(new Rect(left + 300f, top, 70f, 26f), battleRunner.Paused ? "繼續" : "暫停"))
                battleRunner.Paused = !battleRunner.Paused;
            if (GUI.Button(new Rect(left + 375f, top, 70f, 26f), "x" + battleRunner.Speed))
                battleRunner.Speed = battleRunner.Speed >= 4f ? 1f : battleRunner.Speed * 2f;
            if (!battleRunner.IsRunning && GUI.Button(new Rect(left + 450f, top, 70f, 26f), "關閉"))
                battleRunner.Abort();
        }

        private void HandleKeys()
        {
            Event e = Event.current;
            if (e == null || e.type != EventType.KeyDown || !battleRunner.IsRunning) return;
            int slot = -1;
            if (e.keyCode >= KeyCode.Alpha1 && e.keyCode <= KeyCode.Alpha9) slot = e.keyCode - KeyCode.Alpha1;
            else if (e.keyCode == KeyCode.Alpha0) slot = 9;
            if (slot < 0) return;
            battleRunner.Deploy(slot);
            e.Use();
        }
    }
}
