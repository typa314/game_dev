using System;
using System.Collections.Generic;
using Game.Domain.MasterData;

namespace Game.Domain.Battle
{
    public enum Side { Ally, Enemy }

    public enum BattleOutcome { Ongoing, Win, Lose, Timeout }

    public enum CommandType { Deploy, UpgradeEnergy, FireCannon, CastSkill }

    /// <summary>
    /// 一種單位在戰鬥中使用的數值，已經換算成 tick 和千分位距離（milli-unit）。
    /// 全部使用整數，確保不同平台的模擬結果完全一致。
    /// </summary>
    public sealed class UnitTemplate
    {
        public string Id;
        public string Type = "normal";
        public int Cost;
        public int CooldownTicks;
        public int Hp;
        public int Atk;
        public long RangeMilli;
        public int AttackIntervalTicks;
        public long SpeedMilliPerTick;
        public int Knockbacks;
        public bool Area;
        public List<string> Traits = new List<string>();
        public CharacterSkillDef Skill;
        public int SkillChargeTicks;

        public static UnitTemplate From(string id, string type, BattleStats s, int hp, int atk, int ticksPerSecond)
        {
            return new UnitTemplate
            {
                Id = id,
                Type = type ?? "normal",
                Cost = s.Cost,
                CooldownTicks = SecToTicks(s.CooldownSec, ticksPerSecond),
                Hp = Math.Max(1, hp),
                Atk = atk,
                RangeMilli = (long)s.Range * 1000,
                AttackIntervalTicks = Math.Max(1, SecToTicks(s.AttackIntervalSec, ticksPerSecond)),
                SpeedMilliPerTick = (long)s.MoveSpeed * 1000 / ticksPerSecond,
                Knockbacks = Math.Max(0, s.Knockbacks),
                Area = s.Target == "area",
                Traits = s.Traits != null ? new List<string>(s.Traits) : new List<string>(),
                Skill = s.Skill ?? new CharacterSkillDef { Id = "skill_default", Name = "爆發重擊", EffectType = "damage", Value = Math.Max(50, atk * 2), ChargeSec = 15.0 },
                SkillChargeTicks = Math.Max(1, SecToTicks((s.Skill != null && s.Skill.ChargeSec > 0) ? s.Skill.ChargeSec : 15.0, ticksPerSecond)),
            };
        }

        public static int SecToTicks(double sec, int tps)
        {
            return (int)Math.Round(sec * tps, MidpointRounding.AwayFromZero);
        }
    }

    public sealed class BattleUnit
    {
        public int Uid;
        public Side Side;
        public UnitTemplate T;
        public long X;                 // milli-unit；我方據點在 0，敵方據點在 Length*1000
        public int Hp;
        public int KnockbacksTaken;
        public int AttackTimer;
        public int StunTicks;          // 被擊退後的硬直
        public bool Alive = true;
        public int AtkBuffTicks;
        public int AtkBuffPct;
        public int SlowTicks;
        public int SlowPct;
        public int PoisonTicks;
        public int PoisonDmgPerSec;
        public int BurnTicks;
        public int BurnDmgPerSec;
        public int HealTimer;
    }

    public sealed class BattleCommand
    {
        public int Tick;
        public CommandType Type;
        public int Slot;

        public BattleCommand() { }
        public BattleCommand(int tick, CommandType type, int slot) { Tick = tick; Type = type; Slot = slot; }
    }

    public enum BattleEventType { Deploy, Spawn, Hit, BaseHit, Knockback, Death, Cannon, EnergyUp, SkillCast, Heal, StatusApply, BaseShockwave, BaseImmune }

    /// <summary>給表現層（BattleView）播放動畫、特效用的事件；模擬本身不依賴它。</summary>
    public struct BattleEvent
    {
        public BattleEventType Type;
        public int Uid;
        public Side Side;
        public long X;
        public int Value;
    }

    public sealed class BattleSetup
    {
        public StageDef Stage;
        public BattleRules Rules;
        /// <summary>編隊（最多 10 格，可以有 null）。</summary>
        public List<UnitTemplate> Team = new List<UnitTemplate>();
        public Dictionary<string, UnitTemplate> EnemyTemplates = new Dictionary<string, UnitTemplate>();
        public ulong Seed;
        public int AllyBaseLevel = 1;
        public int AllyBaseHpOverride;
    }

    public sealed class BattleResult
    {
        public BattleOutcome Outcome;
        public int Ticks;
        public int AllyBaseHp;
        public int AllyBaseMaxHp;
        public int EnemiesDefeated;
        public int Deploys;
    }

    /// <summary>戰鬥紀錄：seed、編隊和指令序列，足以讓模擬器完整重現整場戰鬥。</summary>
    public sealed class BattleReport
    {
        public string StageId;
        public ulong Seed;
        public List<string> Team = new List<string>();
        public List<BattleCommand> Commands = new List<BattleCommand>();
        public int MasterDataVersion;
    }
}
