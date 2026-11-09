using System;
using System.Collections.Generic;
using System.Globalization;
using Game.Domain.MasterData;

namespace Game.Domain.Battle
{
    /// <summary>
    /// 決定性的推線戰鬥模擬（純 C#）。設計見 design/05_battle.md。
    /// 同一組 (BattleSetup, 指令序列) 一定得到同一個結果，所以可以用 BattleReport 回放和驗證。
    /// 表現層每一幀依經過時間呼叫 Step()，並讀取 Units / TickEvents 來繪製畫面。
    /// </summary>
    public sealed class BattleSimulator
    {
        private const long ContactSlackMilli = 20 * 1000;   // 雙方交錯時仍視為接觸的容許距離

        private readonly BattleSetup _setup;
        private readonly BattleRules _rules;
        private readonly int _tps;
        private readonly long _lengthMilli;
        private readonly List<BattleUnit> _units = new List<BattleUnit>();
        private readonly List<BattleCommand> _pending = new List<BattleCommand>();
        private List<BattleCommand> _replay;                 // 回放模式：預先排定的指令（已依 tick 穩定排序）
        private int _replayIndex;
        private readonly List<WaveState> _waves = new List<WaveState>();
        private readonly int[] _cooldowns;
        private readonly int[] _skillCharges;
        private int _lastAllyBaseHpCrossed;
        private int _lastEnemyBaseHpCrossed;
        private int _nextUid = 1;

        public int Tick { get; private set; }
        public BattleOutcome Outcome { get; private set; }
        public long EnergyMilli { get; private set; }
        public int EnergyLevel { get; private set; }
        public int CannonChargeTicks { get; private set; }
        public int CannonFullTicks { get; private set; }
        public int AllyBaseHp { get; private set; }
        public int AllyBaseMaxHp { get; private set; }
        public int EnemyBaseHp { get; private set; }
        public int EnemyBaseMaxHp { get; private set; }
        public int EnemiesDefeated { get; private set; }
        public int Deploys { get; private set; }

        public IReadOnlyList<BattleUnit> Units { get { return _units; } }
        public List<BattleEvent> TickEvents { get; private set; }
        /// <summary>實際生效的指令（用來產生 BattleReport）。</summary>
        public List<BattleCommand> Recorded { get; private set; }
        public int TicksPerSecond { get { return _tps; } }
        public long LengthMilli { get { return _lengthMilli; } }
        public int EnergyMax { get { return _rules.EnergyMaxByLevel[EnergyLevel]; } }

        public BattleSimulator(BattleSetup setup)
        {
            _setup = setup;
            _rules = setup.Rules;
            _tps = _rules.TicksPerSecond;
            _lengthMilli = (long)setup.Stage.Length * 1000;
            _cooldowns = new int[setup.Team.Count];
            _skillCharges = new int[setup.Team.Count];
            TickEvents = new List<BattleEvent>();
            Recorded = new List<BattleCommand>();

            AllyBaseMaxHp = AllyBaseHp = setup.Stage.AllyBaseHp > 0 ? setup.Stage.AllyBaseHp : _rules.DefaultAllyBaseHp;
            EnemyBaseMaxHp = EnemyBaseHp = setup.Stage.EnemyBaseHp;
            EnergyMilli = (long)_rules.StartEnergy * 1000;
            CannonFullTicks = UnitTemplate.SecToTicks(_rules.CannonChargeSec, _tps);

            foreach (WaveDef w in setup.Stage.Waves) _waves.Add(new WaveState(w, _tps));
        }

        // ---- 輸入 ----

        /// <summary>即時遊玩：在下一次 Step() 開頭套用。</summary>
        public void Enqueue(CommandType type, int slot = 0)
        {
            _pending.Add(new BattleCommand(-1, type, slot));
        }

        public bool CanDeploy(int slot)
        {
            if (slot < 0 || slot >= _setup.Team.Count) return false;
            UnitTemplate t = _setup.Team[slot];
            return t != null && _cooldowns[slot] == 0 && EnergyMilli >= (long)t.Cost * 1000 && CountSide(Side.Ally) < _rules.MaxUnitsPerSide;
        }

        public int CooldownOf(int slot) { return _cooldowns[slot]; }

        public int SkillChargeOf(int slot) { return (slot >= 0 && slot < _skillCharges.Length) ? _skillCharges[slot] : 0; }
        public int SkillFullTicksOf(int slot) { return (slot >= 0 && slot < _setup.Team.Count && _setup.Team[slot] != null) ? _setup.Team[slot].SkillChargeTicks : 0; }
        public bool IsSkillReady(int slot)
        {
            if (slot < 0 || slot >= _setup.Team.Count) return false;
            UnitTemplate t = _setup.Team[slot];
            return t != null && t.Skill != null && _skillCharges[slot] >= t.SkillChargeTicks;
        }

        /// <summary>
        /// 單次點擊格子：若技能條已滿則釋放特殊技能，否則出兵。
        /// </summary>
        public void TapSlot(int slot)
        {
            if (IsSkillReady(slot)) Enqueue(CommandType.CastSkill, slot);
            else Enqueue(CommandType.Deploy, slot);
        }

        public int? EnergyUpgradeCost
        {
            get { return EnergyLevel < _rules.EnergyUpgradeCost.Count && EnergyLevel < _rules.EnergyMaxByLevel.Count - 1 ? _rules.EnergyUpgradeCost[EnergyLevel] : (int?)null; }
        }

        // ---- 推進 ----

        public void Step()
        {
            if (Outcome != BattleOutcome.Ongoing) return;
            TickEvents = new List<BattleEvent>();

            ApplyCommands();
            Regen();
            SpawnWaves();
            UpdateUnits();
            _units.RemoveAll(u => !u.Alive);
            CheckOutcome();
            Tick++;
        }

        /// <summary>回放：從頭跑到結束，回傳結果。</summary>
        public static BattleResult Run(BattleSetup setup, IList<BattleCommand> commands)
        {
            // 用穩定排序保留同 tick 內的原始順序
            var ordered = new List<BattleCommand>(commands);
            var indexed = new List<KeyValuePair<int, BattleCommand>>();
            for (int i = 0; i < ordered.Count; i++) indexed.Add(new KeyValuePair<int, BattleCommand>(i, ordered[i]));
            indexed.Sort((a, b) => a.Value.Tick != b.Value.Tick ? a.Value.Tick.CompareTo(b.Value.Tick) : a.Key.CompareTo(b.Key));
            var sorted = new List<BattleCommand>();
            foreach (KeyValuePair<int, BattleCommand> kv in indexed) sorted.Add(kv.Value);

            var sim = new BattleSimulator(setup);
            sim._replay = sorted;
            while (sim.Outcome == BattleOutcome.Ongoing) sim.Step();
            return sim.Result();
        }

        public BattleResult Result()
        {
            return new BattleResult
            {
                Outcome = Outcome,
                Ticks = Tick,
                AllyBaseHp = AllyBaseHp,
                AllyBaseMaxHp = AllyBaseMaxHp,
                EnemiesDefeated = EnemiesDefeated,
                Deploys = Deploys,
            };
        }

        private void ApplyCommands()
        {
            var toApply = new List<BattleCommand>();
            if (_replay != null)
            {
                while (_replayIndex < _replay.Count && _replay[_replayIndex].Tick <= Tick)
                {
                    if (_replay[_replayIndex].Tick == Tick) toApply.Add(_replay[_replayIndex]);
                    _replayIndex++;
                }
            }
            toApply.AddRange(_pending);
            _pending.Clear();

            foreach (BattleCommand c in toApply)
            {
                // 不合法的指令（例如回放資料被竄改）直接忽略，不記錄
                if (TryApply(c)) Recorded.Add(new BattleCommand(Tick, c.Type, c.Slot));
            }
        }

        private bool TryApply(BattleCommand c)
        {
            switch (c.Type)
            {
                case CommandType.Deploy:
                {
                    if (!CanDeploy(c.Slot)) return false;
                    UnitTemplate t = _setup.Team[c.Slot];
                    EnergyMilli -= (long)t.Cost * 1000;
                    _cooldowns[c.Slot] = t.CooldownTicks;
                    BattleUnit u = Spawn(Side.Ally, t, 0);
                    Deploys++;
                    Emit(BattleEventType.Deploy, u, c.Slot);
                    return true;
                }
                case CommandType.UpgradeEnergy:
                {
                    int? cost = EnergyUpgradeCost;
                    if (cost == null || EnergyMilli < (long)cost.Value * 1000) return false;
                    EnergyMilli -= (long)cost.Value * 1000;
                    EnergyLevel++;
                    TickEvents.Add(new BattleEvent { Type = BattleEventType.EnergyUp, Value = EnergyLevel });
                    return true;
                }
                case CommandType.FireCannon:
                {
                    if (CannonFullTicks <= 0 || CannonChargeTicks < CannonFullTicks) return false;
                    CannonChargeTicks = 0;
                    long reach = (long)_rules.CannonRange * 1000;
                    TickEvents.Add(new BattleEvent { Type = BattleEventType.Cannon, X = reach });
                    foreach (BattleUnit e in _units)
                    {
                        if (!e.Alive || e.Side != Side.Enemy || e.X > reach) continue;
                        Damage(e, _rules.CannonDamage, true);
                    }
                    return true;
                }
                case CommandType.CastSkill:
                {
                    if (!IsSkillReady(c.Slot)) return false;
                    UnitTemplate t = _setup.Team[c.Slot];
                    _skillCharges[c.Slot] = 0;
                    ExecuteSkill(t, c.Slot);
                    return true;
                }
            }
            return false;
        }

        private void Regen()
        {
            long max = (long)EnergyMax * 1000;
            long regen = (long)_rules.EnergyRegenPerSecByLevel[EnergyLevel] * 1000 / _tps;
            EnergyMilli = Math.Min(max, EnergyMilli + regen);
            for (int i = 0; i < _cooldowns.Length; i++) if (_cooldowns[i] > 0) _cooldowns[i]--;
            if (CannonChargeTicks < CannonFullTicks) CannonChargeTicks++;
            for (int i = 0; i < _skillCharges.Length; i++)
            {
                if (_setup.Team[i] != null && _skillCharges[i] < _setup.Team[i].SkillChargeTicks)
                    _skillCharges[i]++;
            }
        }

        private void SpawnWaves()
        {
            int pct = EnemyBaseMaxHp > 0 ? (int)((long)EnemyBaseHp * 100 / EnemyBaseMaxHp) : 0;
            foreach (WaveState w in _waves)
            {
                if (!w.Started)
                {
                    bool start;
                    if (w.Def.Trigger != null)
                    {
                        if (w.Def.Trigger.Type != "enemy_base_hp_below_pct")
                            throw new InvalidOperationException("unsupported wave trigger: " + w.Def.Trigger.Type);
                        start = pct < double.Parse(w.Def.Trigger.Value, CultureInfo.InvariantCulture);
                    }
                    else start = Tick >= w.StartTick;
                    if (!start) continue;
                    w.Started = true;
                    w.BurstStart = Tick;
                    w.NextTick = Tick;
                    w.Remaining = w.Def.Count;
                }

                if (w.Remaining == 0)
                {
                    if (w.RepeatTicks <= 0) continue;
                    if (Tick < w.BurstStart + w.RepeatTicks) continue;
                    w.BurstStart += w.RepeatTicks;
                    w.NextTick = Tick;
                    w.Remaining = w.Def.Count;
                }

                if (Tick < w.NextTick) continue;
                if (CountSide(Side.Enemy) >= _rules.MaxUnitsPerSide) continue; // 滿了就下一個 tick 再試

                UnitTemplate t;
                if (!_setup.EnemyTemplates.TryGetValue(w.Def.Enemy, out t))
                    throw new InvalidOperationException("enemy template missing: " + w.Def.Enemy);
                BattleUnit u = Spawn(Side.Enemy, t, _lengthMilli);
                Emit(BattleEventType.Spawn, u, 0);
                w.Remaining--;
                w.NextTick = Tick + Math.Max(1, w.IntervalTicks);
            }
        }

        private void UpdateUnits()
        {
            // _units 依 Uid 遞增排列（只會在尾端新增），迭代順序固定
            for (int i = 0; i < _units.Count; i++)
            {
                BattleUnit u = _units[i];
                if (!u.Alive) continue;

                // 狀態效果遞減與持續傷害
                if (u.AtkBuffTicks > 0) u.AtkBuffTicks--;
                if (u.SlowTicks > 0) u.SlowTicks--;
                if (Tick % 15 == 0)
                {
                    int dot = 0;
                    if (u.PoisonTicks > 0) dot += Math.Max(1, u.PoisonDmgPerSec / 2);
                    if (u.BurnTicks > 0) dot += Math.Max(1, u.BurnDmgPerSec / 2);
                    if (dot > 0) Damage(u, dot, false);
                }
                if (u.PoisonTicks > 0) u.PoisonTicks--;
                if (u.BurnTicks > 0) u.BurnTicks--;

                if (u.AttackTimer > 0) u.AttackTimer--;
                if (u.StunTicks > 0) { u.StunTicks--; continue; }

                List<BattleUnit> targets;
                bool baseInRange;
                FindTargets(u, out targets, out baseInRange);

                if (targets.Count == 0 && !baseInRange)
                {
                    Move(u);
                    continue;
                }
                if (u.AttackTimer > 0) continue;

                u.AttackTimer = u.T.AttackIntervalTicks;
                if (targets.Count > 0)
                {
                    if (u.T.Area) foreach (BattleUnit t in targets) Damage(t, DamageAgainst(u, t), false);
                    else Damage(targets[0], DamageAgainst(u, targets[0]), false);
                }
                else
                {
                    HitBase(u);
                }
            }
        }

        private void FindTargets(BattleUnit u, out List<BattleUnit> targets, out bool baseInRange)
        {
            targets = new List<BattleUnit>();
            BattleUnit nearest = null;
            long nearestDist = long.MaxValue;
            foreach (BattleUnit o in _units)
            {
                if (!o.Alive || o.Side == u.Side) continue;
                long dist = u.Side == Side.Ally ? o.X - u.X : u.X - o.X;   // 往前方向的距離
                if (dist < -ContactSlackMilli || dist > u.T.RangeMilli) continue;
                targets.Add(o);
                if (dist < nearestDist) { nearestDist = dist; nearest = o; }
            }
            if (!u.T.Area && nearest != null)
            {
                targets.Clear();
                targets.Add(nearest);
            }
            long toBase = u.Side == Side.Ally ? _lengthMilli - u.X : u.X;
            baseInRange = toBase <= u.T.RangeMilli;
        }

        private void Move(BattleUnit u)
        {
            long speed = u.T.SpeedMilliPerTick;
            if (u.SlowTicks > 0 && u.SlowPct > 0)
            {
                speed = speed * Math.Max(10, 100 - u.SlowPct) / 100;
            }
            if (u.Side == Side.Ally) u.X = Math.Min(_lengthMilli, u.X + speed);
            else u.X = Math.Max(0, u.X - speed);
        }

        private static int DamageAgainst(BattleUnit attacker, BattleUnit target)
        {
            int dmg = attacker.T.Atk;
            if (attacker.AtkBuffTicks > 0 && attacker.AtkBuffPct > 0)
            {
                dmg = dmg * (100 + attacker.AtkBuffPct) / 100;
            }
            if (attacker.T.Traits.Contains("anti_" + target.T.Type)) dmg = dmg * 3 / 2;
            return dmg;
        }

        private void Damage(BattleUnit t, int dmg, bool forceKnockback)
        {
            if (!t.Alive) return;
            t.Hp -= dmg;
            Emit(BattleEventType.Hit, t, dmg);
            if (t.Hp <= 0)
            {
                t.Hp = 0;
                t.Alive = false;
                if (t.Side == Side.Enemy) EnemiesDefeated++;
                Emit(BattleEventType.Death, t, 0);
                return;
            }
            bool knock = forceKnockback;
            if (t.T.Knockbacks > 0)
            {
                int crossed = (int)((long)(t.T.Hp - t.Hp) * t.T.Knockbacks / t.T.Hp);
                if (crossed > t.KnockbacksTaken) { t.KnockbacksTaken = crossed; knock = true; }
            }
            if (knock) Knockback(t);
        }

        private void Knockback(BattleUnit t)
        {
            long d = (long)_rules.KnockbackDistance * 1000;
            t.X = t.Side == Side.Ally ? Math.Max(0, t.X - d) : Math.Min(_lengthMilli, t.X + d);
            t.StunTicks = _tps / 2;
            t.AttackTimer = t.T.AttackIntervalTicks;
            Emit(BattleEventType.Knockback, t, 0);
        }

        private void HitBase(BattleUnit u)
        {
            Side targetSide = u.Side == Side.Ally ? Side.Enemy : Side.Ally;
            List<string> traits = targetSide == Side.Enemy ? _setup.Stage.EnemyBaseTraits : _setup.Stage.AllyBaseTraits;

            if (traits != null && traits.Count > 0)
            {
                // 傷害類型免疫檢查
                if (traits.Contains("immune_ranged") && u.T.RangeMilli > 150 * 1000)
                {
                    TickEvents.Add(new BattleEvent { Type = BattleEventType.BaseImmune, Uid = u.Uid, Side = targetSide, X = u.X });
                    return;
                }
                if (traits.Contains("immune_area") && u.T.Area)
                {
                    TickEvents.Add(new BattleEvent { Type = BattleEventType.BaseImmune, Uid = u.Uid, Side = targetSide, X = u.X });
                    return;
                }
                if (traits.Contains("immune_single") && !u.T.Area)
                {
                    TickEvents.Add(new BattleEvent { Type = BattleEventType.BaseImmune, Uid = u.Uid, Side = targetSide, X = u.X });
                    return;
                }
            }

            int prevHp = targetSide == Side.Enemy ? EnemyBaseHp : AllyBaseHp;
            int maxHp = targetSide == Side.Enemy ? EnemyBaseMaxHp : AllyBaseMaxHp;

            if (u.Side == Side.Ally) EnemyBaseHp = Math.Max(0, EnemyBaseHp - u.T.Atk);
            else AllyBaseHp = Math.Max(0, AllyBaseHp - u.T.Atk);

            TickEvents.Add(new BattleEvent { Type = BattleEventType.BaseHit, Uid = u.Uid, Side = u.Side, X = u.X, Value = u.T.Atk });

            // 主堡控場反擊 (Base CC: 每損失 20% 血量爆發一次衝擊波震退敵軍)
            if (traits != null && traits.Count > 0 && maxHp > 0)
            {
                int curHp = targetSide == Side.Enemy ? EnemyBaseHp : AllyBaseHp;
                if (traits.Contains("shockwave_on_hp_loss"))
                {
                    int crossed = (maxHp - curHp) * 5 / maxHp;
                    if (targetSide == Side.Enemy)
                    {
                        if (crossed > _lastEnemyBaseHpCrossed)
                        {
                            _lastEnemyBaseHpCrossed = crossed;
                            TriggerBaseShockwave(Side.Enemy);
                        }
                    }
                    else
                    {
                        if (crossed > _lastAllyBaseHpCrossed)
                        {
                            _lastAllyBaseHpCrossed = crossed;
                            TriggerBaseShockwave(Side.Ally);
                        }
                    }
                }
            }
        }

        private void TriggerBaseShockwave(Side baseSide)
        {
            long baseX = baseSide == Side.Enemy ? _lengthMilli : 0;
            long waveReach = 600 * 1000;
            TickEvents.Add(new BattleEvent { Type = BattleEventType.BaseShockwave, Side = baseSide, X = baseX });

            foreach (BattleUnit u in _units)
            {
                if (!u.Alive || u.Side == baseSide) continue;
                long dist = baseSide == Side.Enemy ? _lengthMilli - u.X : u.X;
                if (dist <= waveReach)
                {
                    Knockback(u);
                    u.StunTicks = Math.Max(u.StunTicks, _tps * 1);
                }
            }
        }

        private void ExecuteSkill(UnitTemplate t, int slot)
        {
            CharacterSkillDef sk = t.Skill;
            if (sk == null) return;

            TickEvents.Add(new BattleEvent { Type = BattleEventType.SkillCast, Side = Side.Ally, Value = slot });

            switch (sk.EffectType)
            {
                case "cannon":
                {
                    long reach = sk.Range > 0 ? (long)sk.Range * 1000 : _lengthMilli;
                    TickEvents.Add(new BattleEvent { Type = BattleEventType.Cannon, X = reach, Value = sk.Value });
                    foreach (BattleUnit e in _units)
                    {
                        if (!e.Alive || e.Side != Side.Enemy || e.X > reach) continue;
                        Damage(e, sk.Value, true);
                    }
                    break;
                }
                case "heal":
                {
                    foreach (BattleUnit a in _units)
                    {
                        if (!a.Alive || a.Side != Side.Ally) continue;
                        a.Hp = Math.Min(a.T.Hp, a.Hp + sk.Value);
                        a.SlowTicks = 0;
                        a.PoisonTicks = 0;
                        a.BurnTicks = 0;
                        TickEvents.Add(new BattleEvent { Type = BattleEventType.Heal, Uid = a.Uid, Side = Side.Ally, Value = sk.Value });
                    }
                    break;
                }
                case "buff_atk":
                {
                    int durTicks = UnitTemplate.SecToTicks(sk.DurationSec > 0 ? sk.DurationSec : 6.0, _tps);
                    foreach (BattleUnit a in _units)
                    {
                        if (!a.Alive || a.Side != Side.Ally) continue;
                        a.AtkBuffTicks = durTicks;
                        a.AtkBuffPct = sk.Value;
                        TickEvents.Add(new BattleEvent { Type = BattleEventType.StatusApply, Uid = a.Uid, Side = Side.Ally, Value = sk.Value });
                    }
                    break;
                }
                case "slow":
                {
                    int durTicks = UnitTemplate.SecToTicks(sk.DurationSec > 0 ? sk.DurationSec : 6.0, _tps);
                    long reach = sk.Range > 0 ? (long)sk.Range * 1000 : _lengthMilli;
                    foreach (BattleUnit e in _units)
                    {
                        if (!e.Alive || e.Side != Side.Enemy || e.X > reach) continue;
                        e.SlowTicks = durTicks;
                        e.SlowPct = sk.Value > 0 ? sk.Value : 50;
                        TickEvents.Add(new BattleEvent { Type = BattleEventType.StatusApply, Uid = e.Uid, Side = Side.Enemy, Value = e.SlowPct });
                    }
                    break;
                }
                case "poison":
                {
                    int durTicks = UnitTemplate.SecToTicks(sk.DurationSec > 0 ? sk.DurationSec : 6.0, _tps);
                    long reach = sk.Range > 0 ? (long)sk.Range * 1000 : _lengthMilli;
                    foreach (BattleUnit e in _units)
                    {
                        if (!e.Alive || e.Side != Side.Enemy || e.X > reach) continue;
                        e.PoisonTicks = durTicks;
                        e.PoisonDmgPerSec = sk.Value > 0 ? sk.Value : 40;
                        TickEvents.Add(new BattleEvent { Type = BattleEventType.StatusApply, Uid = e.Uid, Side = Side.Enemy, Value = e.PoisonDmgPerSec });
                    }
                    break;
                }
                case "burn":
                {
                    int durTicks = UnitTemplate.SecToTicks(sk.DurationSec > 0 ? sk.DurationSec : 5.0, _tps);
                    long reach = sk.Range > 0 ? (long)sk.Range * 1000 : _lengthMilli;
                    foreach (BattleUnit e in _units)
                    {
                        if (!e.Alive || e.Side != Side.Enemy || e.X > reach) continue;
                        Damage(e, sk.Value, false);
                        e.BurnTicks = durTicks;
                        e.BurnDmgPerSec = Math.Max(10, sk.Value / 3);
                        TickEvents.Add(new BattleEvent { Type = BattleEventType.StatusApply, Uid = e.Uid, Side = Side.Enemy, Value = e.BurnDmgPerSec });
                    }
                    break;
                }
                case "stun":
                {
                    int durTicks = UnitTemplate.SecToTicks(sk.DurationSec > 0 ? sk.DurationSec : 3.0, _tps);
                    long reach = sk.Range > 0 ? (long)sk.Range * 1000 : _lengthMilli;
                    foreach (BattleUnit e in _units)
                    {
                        if (!e.Alive || e.Side != Side.Enemy || e.X > reach) continue;
                        e.StunTicks = Math.Max(e.StunTicks, durTicks);
                        TickEvents.Add(new BattleEvent { Type = BattleEventType.StatusApply, Uid = e.Uid, Side = Side.Enemy, Value = durTicks });
                    }
                    break;
                }
                default:
                {
                    long reach = sk.Range > 0 ? (long)sk.Range * 1000 : _lengthMilli;
                    foreach (BattleUnit e in _units)
                    {
                        if (!e.Alive || e.Side != Side.Enemy || e.X > reach) continue;
                        Damage(e, sk.Value, false);
                    }
                    break;
                }
            }
        }

        private void CheckOutcome()
        {
            if (EnemyBaseHp <= 0) { Outcome = BattleOutcome.Win; return; }
            if (AllyBaseHp <= 0) { Outcome = BattleOutcome.Lose; return; }
            int limit = _setup.Stage.TimeLimitSec > 0 ? UnitTemplate.SecToTicks(_setup.Stage.TimeLimitSec, _tps) : int.MaxValue;
            int hardCap = UnitTemplate.SecToTicks(_rules.MaxBattleSec, _tps);
            if (Tick + 1 >= Math.Min(limit, hardCap)) Outcome = BattleOutcome.Timeout;
        }

        private BattleUnit Spawn(Side side, UnitTemplate t, long x)
        {
            var u = new BattleUnit { Uid = _nextUid++, Side = side, T = t, X = x, Hp = t.Hp };
            _units.Add(u);
            return u;
        }

        private int CountSide(Side side)
        {
            int n = 0;
            foreach (BattleUnit u in _units) if (u.Alive && u.Side == side) n++;
            return n;
        }

        private void Emit(BattleEventType type, BattleUnit u, int value)
        {
            TickEvents.Add(new BattleEvent { Type = type, Uid = u.Uid, Side = u.Side, X = u.X, Value = value });
        }

        private sealed class WaveState
        {
            public readonly WaveDef Def;
            public readonly int StartTick;
            public readonly int IntervalTicks;
            public readonly int RepeatTicks;
            public bool Started;
            public int BurstStart;
            public int NextTick;
            public int Remaining;

            public WaveState(WaveDef def, int tps)
            {
                Def = def;
                StartTick = UnitTemplate.SecToTicks(def.AtSec, tps);
                IntervalTicks = UnitTemplate.SecToTicks(def.IntervalSec, tps);
                RepeatTicks = UnitTemplate.SecToTicks(def.RepeatEverySec, tps);
            }
        }
    }
}
