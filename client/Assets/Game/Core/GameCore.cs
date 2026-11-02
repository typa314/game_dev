using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Game.Domain;
using Game.Domain.Achievements;
using Game.Domain.Battle;
using Game.Domain.Conditions;
using Game.Domain.Encounter;
using Game.Domain.Events;
using Game.Domain.Gacha;
using Game.Domain.Growth;
using Game.Domain.MasterData;
using Game.Domain.Player;
using Game.Core.Story;

namespace Game.Core
{
    public sealed class GachaResultEntry
    {
        public GachaDraw Draw;
        public bool IsNew;
        public List<ItemStack> Converted = new List<ItemStack>();   // 重複角色轉換成的道具
    }

    public sealed class BattleReward
    {
        public BattleResult Result;
        public int Stars;
        public bool FirstClear;
        public List<ItemStack> Items = new List<ItemStack>();
        public List<string> Unlocked = new List<string>();
    }

    public sealed class EncounterStart
    {
        public EncounterEventDef Event;  // null = 沒有事件，已直接發放 Fallback
        public List<ItemStack> FallbackReward = new List<ItemStack>();
    }

    /// <summary>
    /// 單機版的遊戲服務：所有修改玩家狀態的命令都從這裡進入（design/03 §2）。
    /// 流程：驗證 → 呼叫領域規則 → 修改 PlayerState → 發佈領域事件 → 標記需要存檔（Dirty）。
    /// 之後改成連線版時，以同樣的命令介面換成伺服器實作。
    /// </summary>
    public sealed class GameCore
    {
        public readonly MasterDatabase Master;
        public readonly PlayerState Player;
        public readonly EventBus Bus;
        public readonly GrowthRules Growth;
        public readonly RatingPolicy Rating;
        private readonly IRandom _rng;
        private readonly IClock _clock;
        public IClock Clock => _clock;
        private readonly ConditionContext _ctx;
        private readonly AchievementTracker _achievements;

        /// <summary>狀態有變更、需要寫入存檔。由存檔服務讀取後清除。</summary>
        public bool Dirty { get; set; }

        public GameCore(MasterDatabase master, PlayerState player, IRandom rng, IClock clock, RatingPolicy rating, EventBus bus = null)
        {
            Master = master;
            Player = player;
            _rng = rng;
            _clock = clock;
            Rating = rating;
            Bus = bus ?? new EventBus();
            Growth = new GrowthRules(master.Config.Growth);
            _ctx = new ConditionContext { Player = player, Master = master, Rating = rating, Clock = clock };
            _achievements = new AchievementTracker(master, _ctx, Bus, clock);
            _achievements.Attach();
            Bus.Subscribe(_ => Dirty = true);
        }

        /// <summary>建立新玩家（依 config.initial）。</summary>
        public static PlayerState NewPlayer(MasterDatabase master, IClock clock)
        {
            var p = new PlayerState();
            string now = Iso(clock.UtcNow);
            p.Profile.CreatedAtUtc = now;
            InitialState init = master.Config.Initial;
            foreach (ItemStack it in init.Items) p.AddItem(it.Item, it.N);
            foreach (string st in init.UnlockedStages) p.Stages[st] = new StageProgress { Unlocked = true };
            foreach (string sp in init.UnlockedSpots) p.Encounter.UnlockedSpots.Add(sp);
            foreach (string sc in init.Scenes) p.Scenes.Add(sc);
            p.Encounter.Stamina = master.Config.Encounter.StaminaMax;
            p.Encounter.StaminaUpdatedAtUtc = now;
            foreach (string ch in init.Characters)
            {
                IngestCharacter(p, master, ch, now);
                if (p.Team.Count < 10) p.Team.Add(ch);
            }
            p.Lobby.Scene = init.Scenes.FirstOrDefault();
            p.Lobby.Character = init.Characters.FirstOrDefault();
            return p;
        }

        /// <summary>統一角色納入與預設資產登錄工廠，確保無論開局贈送、轉蛋或掉落皆具備一致的領域不變量。</summary>
        internal static CharacterState IngestCharacter(PlayerState player, MasterDatabase master, string charId, string nowIso, string initialRarity = null)
        {
            CharacterState s = NewCharacterState(master, charId, nowIso);
            if (!string.IsNullOrEmpty(initialRarity))
                s.CurrentRarity = initialRarity;
            player.Characters[charId] = s;
            CharacterDef def = master.Characters.GetOrNull(charId);
            if (def != null && !string.IsNullOrEmpty(def.DefaultCostume))
            {
                player.Costumes.Add(def.DefaultCostume);
            }
            string baseCg = $"cg_{charId}_{s.CurrentRarity.ToLower()}";
            player.Gallery.Add(baseCg);
            return s;
        }

        /// <summary>載入存檔後呼叫：補上新增的立繪解鎖、成就追溯（例如更新主資料或安裝 DLC 之後）。</summary>
        public void OnLoaded()
        {
            foreach (KeyValuePair<string, CharacterState> kv in Player.Characters.ToList())
            {
                RefreshPortraits(kv.Key, kv.Value);
                CharacterDef def = Master.Characters.GetOrNull(kv.Key);
                if (def != null && !string.IsNullOrEmpty(def.DefaultCostume))
                    Player.Costumes.Add(def.DefaultCostume);
                string baseCg = $"cg_{kv.Key}_{kv.Value.CurrentRarity.ToLower()}";
                Player.Gallery.Add(baseCg);
            }
            _achievements.Reevaluate();
        }

        public ConditionContext Context { get { return _ctx; } }

        // ================= 轉蛋 =================

        public Result<List<GachaResultEntry>> PullGacha(string bannerId, int count)
        {
            BannerDef b = Master.Banners.GetOrNull(bannerId);
            if (b == null) return Result<List<GachaResultEntry>>.Fail("banner_not_found");
            if (count != 1 && count != 10) return Result<List<GachaResultEntry>>.Fail("invalid_count");
            if (!InPeriod(b)) return Result<List<GachaResultEntry>>.Fail("banner_closed");

            List<ItemStack> cost = count == 1 ? b.Cost.Single : b.Cost.Ten;
            if (cost.Count == 0) return Result<List<GachaResultEntry>>.Fail("cost_not_defined");
            if (!HasItems(cost)) return Result<List<GachaResultEntry>>.Fail("insufficient_item");
            ConsumeItems(cost);

            int pity;
            Player.Gacha.Pity.TryGetValue(b.Id, out pity);
            List<GachaDraw> draws = GachaEngine.Pull(b, count, ref pity, _rng);
            Player.Gacha.Pity[b.Id] = pity;
            Player.Gacha.TotalPulls += count;

            var results = new List<GachaResultEntry>();
            string now = Iso(_clock.UtcNow);
            foreach (GachaDraw d in draws)
            {
                var entry = new GachaResultEntry { Draw = d };
                CharacterState s = Player.Char(d.Char);
                if (s == null)
                {
                    entry.IsNew = true;
                    ObtainCharacter(d.Char, "gacha", d.Rarity);
                }
                else
                {
                    // 若抽到更高稀有度，自動直接升階至該稀有度
                    if (Rarity.Rank(d.Rarity) > Rarity.Rank(s.CurrentRarity))
                    {
                        string oldRarity = s.CurrentRarity;
                        s.CurrentRarity = d.Rarity;
                        UnlockGradeCg(d.Char, s.CurrentRarity, s.Tier);
                        Bus.Publish(new DomainEvent(Ev.CharacterRarityPromoted)
                            .With("char", d.Char)
                            .With("oldRarity", oldRarity)
                            .With("newRarity", s.CurrentRarity));
                    }

                    // 重複抽取獲得同角色卡片（供 +1～+5 進階使用）
                    Player.AddItem("card_" + d.Char, 1);
                    entry.Converted.Add(ConvertDuplicate(d.Char, s));
                }
                Player.Gacha.History.Add(new GachaHistoryEntry { Banner = b.Id, Char = d.Char, Rarity = d.Rarity, AtUtc = now });
                Bus.Publish(new DomainEvent(Ev.GachaDraw).With("banner", b.Id).With("char", d.Char).With("rarity", d.Rarity).With("isNew", entry.IsNew));
                results.Add(entry);
            }
            int limit = Master.Config.GachaHistoryLimit;
            if (Player.Gacha.History.Count > limit) Player.Gacha.History.RemoveRange(0, Player.Gacha.History.Count - limit);
            Bus.Publish(new DomainEvent(Ev.GachaPulled).With("banner", b.Id).With("count", count));
            return Result<List<GachaResultEntry>>.Success(results);
        }

        private ItemStack ConvertDuplicate(string charId, CharacterState s)
        {
            GrowthConfig g = Master.Config.Growth;
            ItemStack item = s.Awaken < Growth.MaxAwaken
                ? new ItemStack(Growth.ShardItem(charId), g.ShardsPerDuplicate)
                : new ItemStack(g.MaxedDuplicateItem, g.MaxedDuplicateCount);
            Player.AddItem(item.Item, item.N);
            return item;
        }

        private bool InPeriod(BannerDef b)
        {
            DateTime now = _clock.UtcNow;
            if (b.PeriodStartUtc != null && now < ParseIso(b.PeriodStartUtc)) return false;
            if (b.PeriodEndUtc != null && now >= ParseIso(b.PeriodEndUtc)) return false;
            return true;
        }

        // ================= 養成 =================

        public Result<int> LevelUp(string charId, List<ItemStack> materials)
        {
            CharacterDef def = Master.Characters.GetOrNull(charId);
            CharacterState s = Player.Char(charId);
            if (def == null || s == null) return Result<int>.Fail("character_not_owned");
            if (s.Level >= Growth.LevelCap(s.Awaken)) return Result<int>.Fail("level_cap");
            if (materials == null || materials.Count == 0) return Result<int>.Fail("no_material");

            long exp = 0;
            var merged = new Dictionary<string, long>();
            foreach (ItemStack m in materials)
            {
                int per;
                if (m.N <= 0 || !Master.Config.Growth.ExpItems.TryGetValue(m.Item, out per)) return Result<int>.Fail("invalid_material");
                long prev;
                merged.TryGetValue(m.Item, out prev);
                merged[m.Item] = prev + m.N;
                exp += per * m.N;
            }
            var cost = merged.Select(kv => new ItemStack(kv.Key, kv.Value)).ToList();
            long gold = exp * Master.Config.Growth.GoldPerExp;
            if (gold > 0) cost.Add(new ItemStack(Master.Config.Growth.GoldItem, gold));
            if (!HasItems(cost)) return Result<int>.Fail("insufficient_item");
            ConsumeItems(cost);

            int gained = Growth.AddExp(s, def.Rarity, exp);
            if (gained > 0) Bus.Publish(new DomainEvent(Ev.CharacterLeveledUp).With("char", charId).With("level", s.Level).With("gained", gained));
            RefreshPortraits(charId, s);
            Dirty = true;
            return Result<int>.Success(gained);
        }

        public Result Awaken(string charId)
        {
            CharacterState s = Player.Char(charId);
            if (s == null) return Result.Fail("character_not_owned");
            if (s.Awaken >= Growth.MaxAwaken) return Result.Fail("awaken_max");
            var cost = new List<ItemStack> { new ItemStack(Growth.ShardItem(charId), Growth.AwakenCost(s.Awaken)) };
            if (!HasItems(cost)) return Result.Fail("insufficient_item");
            ConsumeItems(cost);
            s.Awaken++;
            Bus.Publish(new DomainEvent(Ev.CharacterAwakened).With("char", charId).With("awaken", s.Awaken));
            RefreshPortraits(charId, s);
            return Result.Success();
        }

        public sealed class AdvanceResult
        {
            public string CharId { get; set; }
            public string Rarity { get; set; }
            public int Tier { get; set; }
            public bool IsRarityPromoted { get; set; }
            public string UnlockedCg { get; set; }
            public string DisplayGrade => Tier > 0 ? $"{Rarity}+{Tier}" : Rarity;
        }

        /// <summary>
        /// 角色進階系統：消耗同角色卡片（或碎片）進行 +1 進階，+5 後晉升至下一等級 (R -> SR -> SSR -> UR)。
        /// 每次進階與升等均自動解鎖對應 CG 美術圖並加入收藏。
        /// </summary>
        public Result<AdvanceResult> AdvanceCharacter(string charId)
        {
            CharacterState s = Player.Char(charId);
            if (s == null) return Result<AdvanceResult>.Fail("character_not_owned");

            // 檢查是否已達最高等級 (UR+5)
            if (s.CurrentRarity == "UR" && s.Tier >= 5)
            {
                return Result<AdvanceResult>.Fail("max_grade");
            }

            // 檢查消耗：優先消耗 1 張同角色卡片，若無則支援 10 個碎片
            string cardItem = "card_" + charId;
            string shardItem = Growth.ShardItem(charId);

            if (Player.ItemCount(cardItem) >= 1)
            {
                Player.AddItem(cardItem, -1);
            }
            else if (Player.ItemCount(shardItem) >= 10)
            {
                Player.AddItem(shardItem, -10);
            }
            else
            {
                return Result<AdvanceResult>.Fail("insufficient_item");
            }

            bool isPromoted = false;
            string oldRarity = s.CurrentRarity;

            if (s.Tier < 5)
            {
                s.Tier++;
                s.Awaken = Math.Min(s.Tier, Growth.MaxAwaken);
                Bus.Publish(new DomainEvent(Ev.CharacterTierAdvanced)
                    .With("char", charId)
                    .With("rarity", s.CurrentRarity)
                    .With("tier", s.Tier));
            }
            else
            {
                // +5 後晉升為下一等級 (R->SR->SSR->UR)
                string nextRarity = Rarity.Next(s.CurrentRarity);
                if (nextRarity != null)
                {
                    s.CurrentRarity = nextRarity;
                    s.Tier = 0;
                    s.Awaken = 0;
                    isPromoted = true;
                    Bus.Publish(new DomainEvent(Ev.CharacterRarityPromoted)
                        .With("char", charId)
                        .With("oldRarity", oldRarity)
                        .With("newRarity", s.CurrentRarity));
                }
            }

            // 解鎖進階/升級對應的 CG 美術圖並收藏
            string unlockedCg = UnlockGradeCg(charId, s.CurrentRarity, s.Tier);
            RefreshPortraits(charId, s);
            Dirty = true;

            return Result<AdvanceResult>.Success(new AdvanceResult
            {
                CharId = charId,
                Rarity = s.CurrentRarity,
                Tier = s.Tier,
                IsRarityPromoted = isPromoted,
                UnlockedCg = unlockedCg
            });
        }

        public string UnlockGradeCg(string charId, string rarity, int tier)
        {
            string cgId = tier > 0
                ? $"cg_{charId}_{rarity.ToLower()}_plus_{tier}"
                : $"cg_{charId}_{rarity.ToLower()}";

            UnlockCg(cgId);
            return cgId;
        }

        public Result SetPortraitStage(string charId, string stageId)
        {
            CharacterState s = Player.Char(charId);
            PortraitStageDef p = Master.PortraitStages.GetOrNull(stageId);
            if (s == null) return Result.Fail("character_not_owned");
            if (p == null || p.Character != charId) return Result.Fail("stage_not_found");
            if (!s.UnlockedPortraitStages.Contains(stageId)) return Result.Fail("stage_locked");
            if (!Rating.Allows(p.Rating)) return Result.Fail("rating_not_allowed");
            s.PortraitStage = stageId;
            Dirty = true;
            return Result.Success();
        }

        public Result AddAffection(string charId, int delta)
        {
            CharacterState s = Player.Char(charId);
            if (s == null) return Result.Fail("character_not_owned");
            s.Affection = Math.Max(0, s.Affection + delta);
            Bus.Publish(new DomainEvent(Ev.AffectionChanged).With("char", charId).With("affection", s.Affection).With("delta", delta));
            RefreshPortraits(charId, s);
            return Result.Success();
        }

        public Result GiveItem(string itemId, long amount)
        {
            if (string.IsNullOrWhiteSpace(itemId)) return Result.Fail("invalid_item_id");
            if (amount <= 0) return Result.Fail("invalid_amount");
            if (Player.ItemCount(itemId) > long.MaxValue - amount) return Result.Fail("item_amount_overflow");
            Player.AddItem(itemId, amount);
            Dirty = true;
            Bus.Publish(new DomainEvent(Ev.ItemGiven).With("item", itemId).With("n", amount));
            return Result.Success();
        }

        private void RefreshPortraits(string charId, CharacterState s)
        {
            foreach (string id in Growth.RefreshPortraitUnlocks(charId, s, _ctx))
                Bus.Publish(new DomainEvent(Ev.PortraitStageUnlocked).With("char", charId).With("stage", id));
        }

        private void ObtainCharacter(string charId, string via, string initialRarity = null)
        {
            if (Player.Char(charId) != null) return;
            CharacterDef def = Master.Characters[charId];
            CharacterState s = IngestCharacter(Player, Master, charId, Iso(_clock.UtcNow), initialRarity);
            if (def.DefaultCostume != null)
                Bus.Publish(new DomainEvent(Ev.CostumeUnlocked).With("costume", def.DefaultCostume).With("char", charId));

            Bus.Publish(new DomainEvent(Ev.CgUnlocked).With("cg", $"cg_{charId}_{s.CurrentRarity.ToLower()}"));
            Bus.Publish(new DomainEvent(Ev.CharacterObtained).With("char", charId).With("rarity", s.CurrentRarity).With("via", via));
            RefreshPortraits(charId, s);
        }

        private static CharacterState NewCharacterState(MasterDatabase master, string charId, string nowIso)
        {
            CharacterDef def = master.Characters.GetOrNull(charId);
            var s = new CharacterState
            {
                ObtainedAtUtc = nowIso,
                CurrentRarity = def?.Rarity ?? "R",
                Tier = 0
            };
            PortraitStageDef first = master.StagesOf(charId).FirstOrDefault();
            if (first != null)
            {
                s.UnlockedPortraitStages.Add(first.Id);
                s.PortraitStage = first.Id;
            }
            return s;
        }

        // ================= 編隊、換裝、Lobby =================

        public Result SetTeam(List<string> slots)
        {
            if (slots == null || slots.Count > 10 || !slots.Any(c => c != null)) return Result.Fail("invalid_team");
            var seen = new HashSet<string>();
            foreach (string c in slots)
            {
                if (c == null) continue;
                if (Player.Char(c) == null) return Result.Fail("character_not_owned");
                if (!seen.Add(c)) return Result.Fail("duplicate_member");
            }
            Player.Team = new List<string>(slots);
            Dirty = true;
            return Result.Success();
        }

        public Result EquipCostume(string charId, string costumeId)
        {
            CharacterState s = Player.Char(charId);
            if (s == null) return Result.Fail("character_not_owned");
            if (costumeId != null)
            {
                CostumeDef c = Master.Costumes.GetOrNull(costumeId);
                if (c == null || c.Character != charId) return Result.Fail("costume_not_found");
                if (!Player.Costumes.Contains(costumeId)) return Result.Fail("costume_locked");
                if (!Rating.Allows(c.Rating)) return Result.Fail("rating_not_allowed");
                if (c.RequiresPortraitStage != null && !s.UnlockedPortraitStages.Contains(c.RequiresPortraitStage))
                    return Result.Fail("portrait_stage_required");
            }
            s.Costume = costumeId;
            Dirty = true;
            return Result.Success();
        }

        public Result SetLobby(string sceneId, string charId, string costumeId)
        {
            SceneDef sc = Master.Scenes.GetOrNull(sceneId);
            if (sc == null || !Player.Scenes.Contains(sceneId)) return Result.Fail("scene_locked");
            if (!Rating.Allows(sc.Rating)) return Result.Fail("rating_not_allowed");
            if (Player.Char(charId) == null) return Result.Fail("character_not_owned");
            Result r = EquipCostume(charId, costumeId);
            if (!r.Ok) return r;
            Player.Lobby.Scene = sceneId;
            Player.Lobby.Character = charId;
            Player.Lobby.Costume = costumeId;
            Dirty = true;
            return Result.Success();
        }

        /// <summary>依解鎖條件檢查服裝與場景（例如通關、成就之後）。回傳新取得的 ID。</summary>
        public List<string> RefreshUnlocks()
        {
            var newly = new List<string>();
            foreach (CostumeDef c in Master.Costumes.Values.OrderBy(x => x.Id, StringComparer.Ordinal))
            {
                if (c.Unlock == null || Player.Costumes.Contains(c.Id) || Player.Char(c.Character) == null) continue;
                _ctx.SelfChar = c.Character;
                bool ok = ConditionEvaluator.Evaluate(c.Unlock, _ctx);
                _ctx.SelfChar = null;
                if (!ok) continue;
                Player.Costumes.Add(c.Id);
                newly.Add(c.Id);
                Bus.Publish(new DomainEvent(Ev.CostumeUnlocked).With("costume", c.Id).With("char", c.Character));
            }
            foreach (SceneDef s in Master.Scenes.Values.OrderBy(x => x.Id, StringComparer.Ordinal))
            {
                if (s.Unlock == null || Player.Scenes.Contains(s.Id) || !ConditionEvaluator.Evaluate(s.Unlock, _ctx)) continue;
                Player.Scenes.Add(s.Id);
                newly.Add(s.Id);
            }
            foreach (SpotDef sp in Master.Spots.Values.OrderBy(x => x.Id, StringComparer.Ordinal))
            {
                if (sp.Unlock == null || Player.Encounter.UnlockedSpots.Contains(sp.Id) || !ConditionEvaluator.Evaluate(sp.Unlock, _ctx)) continue;
                Player.Encounter.UnlockedSpots.Add(sp.Id);
                newly.Add(sp.Id);
            }
            if (newly.Count > 0) Dirty = true;
            return newly;
        }

        // ================= 戰鬥 =================

        /// <summary>依目前編隊與養成狀態建立戰鬥設定。關卡未解鎖時失敗。</summary>
        public Result<BattleSetup> PrepareBattle(string stageId, ulong seed)
        {
            StageDef st = Master.Stages.GetOrNull(stageId);
            if (st == null) return Result<BattleSetup>.Fail("stage_not_found");
            StageProgress prog;
            if (!Player.Stages.TryGetValue(stageId, out prog) || !prog.Unlocked) return Result<BattleSetup>.Fail("stage_locked");
            return Result<BattleSetup>.Success(BuildSetup(st, Player.Team, seed));
        }

        public BattleSetup BuildSetup(StageDef st, List<string> team, ulong seed)
        {
            BattleRules rules = Master.Config.Battle;
            var setup = new BattleSetup { Stage = st, Rules = rules, Seed = seed };
            foreach (string c in team)
            {
                CharacterDef def = c != null ? Master.Characters.GetOrNull(c) : null;
                CharacterState s = Player.Char(c);
                if (def == null || s == null) { setup.Team.Add(null); continue; }
                ComputedStats cs = Growth.ComputeStats(def, s, Master.PortraitStages.GetOrNull(s.PortraitStage));
                setup.Team.Add(UnitTemplate.From(c, "normal", def.Battle, cs.Hp, cs.Atk, rules.TicksPerSecond));
            }
            foreach (WaveDef w in st.Waves)
            {
                if (setup.EnemyTemplates.ContainsKey(w.Enemy)) continue;
                EnemyDef e = Master.Enemies[w.Enemy];
                int hp = (int)((long)e.Battle.Hp * st.EnemyStatPct / 100);
                int atk = (int)((long)e.Battle.Atk * st.EnemyStatPct / 100);
                setup.EnemyTemplates[w.Enemy] = UnitTemplate.From(e.Id, e.Type, e.Battle, hp, atk, rules.TicksPerSecond);
            }
            return setup;
        }

        /// <summary>
        /// 提交戰鬥紀錄：依紀錄重新模擬一次取得結果（不信任呼叫端回報的勝負），再發放獎勵。
        /// 連線版時由伺服器執行同樣的重新模擬。
        /// </summary>
        public Result<BattleReward> SubmitBattle(BattleReport report)
        {
            StageDef st = Master.Stages.GetOrNull(report.StageId);
            if (st == null) return Result<BattleReward>.Fail("stage_not_found");
            StageProgress prog;
            if (!Player.Stages.TryGetValue(st.Id, out prog) || !prog.Unlocked) return Result<BattleReward>.Fail("stage_locked");
            foreach (string c in report.Team)
                if (c != null && Player.Char(c) == null) return Result<BattleReward>.Fail("character_not_owned");

            BattleResult result = BattleSimulator.Run(BuildSetup(st, report.Team, report.Seed), report.Commands);
            var reward = new BattleReward { Result = result };

            if (result.Outcome == BattleOutcome.Win)
            {
                reward.FirstClear = !prog.Cleared;
                reward.Stars = CountStars(st, result);
                prog.Cleared = true;
                prog.ClearCount++;
                prog.Stars = Math.Max(prog.Stars, reward.Stars);

                if (reward.FirstClear) reward.Items.AddRange(st.Rewards.FirstClear);
                foreach (Drop d in st.Rewards.Drop)
                    if (_rng.NextDouble() < d.Rate) reward.Items.Add(new ItemStack(d.Item, d.N));
                foreach (ItemStack it in reward.Items) Player.AddItem(it.Item, it.N);

                foreach (string u in st.UnlocksOnClear)
                {
                    if (Master.Stages.ContainsKey(u))
                    {
                        StageProgress p2;
                        if (!Player.Stages.TryGetValue(u, out p2)) Player.Stages[u] = p2 = new StageProgress();
                        if (!p2.Unlocked) { p2.Unlocked = true; reward.Unlocked.Add(u); }
                    }
                    else if (Master.Spots.ContainsKey(u) && Player.Encounter.UnlockedSpots.Add(u)) reward.Unlocked.Add(u);
                }

                foreach (string c in report.Team.Where(x => x != null).Distinct())
                {
                    CharacterState s = Player.Char(c);
                    int gained = Growth.AddExp(s, Master.Characters[c].Rarity, st.Rewards.TeamExp);
                    if (gained > 0) Bus.Publish(new DomainEvent(Ev.CharacterLeveledUp).With("char", c).With("level", s.Level).With("gained", gained));
                    if (Master.Config.Growth.AffectionPerWin > 0) AddAffection(c, Master.Config.Growth.AffectionPerWin);
                    else RefreshPortraits(c, s);
                }
            }

            Bus.Publish(new DomainEvent(Ev.BattleFinished)
                .With("stage", st.Id).With("outcome", result.Outcome).With("stars", reward.Stars)
                .With("enemiesDefeated", result.EnemiesDefeated).With("firstClear", reward.FirstClear));
            reward.Unlocked.AddRange(RefreshUnlocks());
            Dirty = true;
            return Result<BattleReward>.Success(reward);
        }

        public int CountStars(StageDef st, BattleResult r)
        {
            int stars = 0;
            int tps = Master.Config.Battle.TicksPerSecond;
            foreach (string sc in st.StarConditions)
            {
                if (sc == "clear") stars++;
                else if (sc.StartsWith("no_base_damage_pct_", StringComparison.Ordinal))
                {
                    int pct = int.Parse(sc.Substring("no_base_damage_pct_".Length), CultureInfo.InvariantCulture);
                    long lost = (long)(r.AllyBaseMaxHp - r.AllyBaseHp) * 100;
                    if (lost <= (long)pct * r.AllyBaseMaxHp) stars++;
                }
                else if (sc.StartsWith("within_sec_", StringComparison.Ordinal))
                {
                    int sec = int.Parse(sc.Substring("within_sec_".Length), CultureInfo.InvariantCulture);
                    if (r.Ticks <= sec * tps) stars++;
                }
            }
            return stars;
        }

        // ================= 巧遇 =================

        public Result<EncounterStart> StartEncounter(string spotId)
        {
            if (!Master.Spots.ContainsKey(spotId)) return Result<EncounterStart>.Fail("spot_not_found");
            EncounterState st = Player.Encounter;
            if (!st.UnlockedSpots.Contains(spotId)) return Result<EncounterStart>.Fail("spot_locked");
            if (st.Pending != null)
            {
                EncounterEventDef pendingEvent = Master.Encounters.GetOrNull(st.Pending);
                if (pendingEvent != null && pendingEvent.Spot == spotId)
                {
                    return Result<EncounterStart>.Success(new EncounterStart { Event = pendingEvent });
                }
                return Result<EncounterStart>.Fail("encounter_pending");
            }
            EncounterEngine.RegenStamina(st, Master.Config.Encounter, _clock.UtcNow);
            if (st.Stamina <= 0) return Result<EncounterStart>.Fail("no_stamina");

            st.Stamina--;
            if (st.Stamina == Master.Config.Encounter.StaminaMax - 1) st.StaminaUpdatedAtUtc = Iso(_clock.UtcNow);
            var start = new EncounterStart { Event = EncounterEngine.Select(spotId, _ctx, _rng) };
            if (start.Event == null)
            {
                start.FallbackReward.AddRange(Master.Config.Encounter.FallbackReward);
                foreach (ItemStack it in start.FallbackReward) Player.AddItem(it.Item, it.N);
            }
            else st.Pending = start.Event.Id;
            Dirty = true;
            return Result<EncounterStart>.Success(start);
        }

        /// <summary>巧遇劇情播完後，依玩家選擇（outcomes 的 key，例如 "choice0"）結算。巧遇不招募角色。</summary>
        public Result<List<Outcome>> ResolveEncounter(string choiceKey)
        {
            EncounterState st = Player.Encounter;
            EncounterEventDef e = Master.Encounters.GetOrNull(st.Pending);
            if (e == null) return Result<List<Outcome>>.Fail("no_pending_encounter");
            List<Outcome> outcomes;
            if (!e.Outcomes.TryGetValue(choiceKey, out outcomes)) return Result<List<Outcome>>.Fail("invalid_choice");

            st.Pending = null;
            st.Seen.Add(e.Id);
            foreach (Outcome o in outcomes)
            {
                if (o.AffectionChar != null && Player.Char(o.AffectionChar) != null) AddAffection(o.AffectionChar, (int)o.N);
                if (o.Item != null) Player.AddItem(o.Item, o.N);
                if (o.Costume != null && Player.Costumes.Add(o.Costume))
                    Bus.Publish(new DomainEvent(Ev.CostumeUnlocked).With("costume", o.Costume).With("char", Master.Costumes[o.Costume].Character));
            }
            Bus.Publish(new DomainEvent(Ev.EncounterResolved).With("event", e.Id).With("spot", e.Spot).With("char", e.Character).With("choice", choiceKey));
            RefreshUnlocks();
            Dirty = true;
            return Result<List<Outcome>>.Success(outcomes);
        }

        // ================= 劇情（由 StoryPlayer 呼叫） =================

        public void MarkNodeRead(string node)
        {
            if (string.IsNullOrWhiteSpace(node)) return;
            if (Player.Story.ReadNodes.Add(node))
            {
                Dirty = true;
                Bus.Publish(new DomainEvent(Ev.StoryNodeReached).With("node", node));
            }
        }

        public void MarkLineRead(string lineId)
        {
            if (string.IsNullOrWhiteSpace(lineId)) return;
            if (Player.Story.ReadLines.Add(lineId))
            {
                Dirty = true;
            }
        }

        public void SetGlobalFlag(string key, double value)
        {
            if (string.IsNullOrWhiteSpace(key)) return;
            Player.Story.Flags[key] = value;
            Dirty = true;
        }

        public void ReachEnding(string endingId)
        {
            if (string.IsNullOrWhiteSpace(endingId)) return;
            if (Player.Endings.ContainsKey(endingId)) return;
            Player.Endings[endingId] = Iso(_clock.UtcNow);
            Dirty = true;
            Bus.Publish(new DomainEvent(Ev.EndingReached).With("ending", endingId));
            RefreshUnlocks();
        }

        public void UnlockCg(string cgId)
        {
            if (string.IsNullOrWhiteSpace(cgId)) return;
            if (Player.Gallery.Add(cgId))
            {
                Dirty = true;
                Bus.Publish(new DomainEvent(Ev.CgUnlocked).With("cg", cgId));
            }
        }

        public bool IsRouteChapterUnlocked(string charId, int chapter)
        {
            return CharacterRouteService.IsChapterUnlocked(Player, charId, chapter, Growth);
        }

        public Result CompleteRouteChapter(string charId, int chapter)
        {
            Result res = CharacterRouteService.CompleteChapter(Player, charId, chapter, Growth);
            if (res.Ok)
            {
                Dirty = true;
                Bus.Publish(new DomainEvent(Ev.RouteChapterCompleted).With("char", charId).With("chapter", chapter));
            }
            return res;
        }

        // ================= 成就 =================

        public Result<List<ItemStack>> ClaimAchievement(string id)
        {
            AchievementDef a = Master.Achievements.GetOrNull(id);
            AchievementProgress p;
            if (a == null || !Player.Achievements.TryGetValue(id, out p) || p.UnlockedAtUtc == null)
                return Result<List<ItemStack>>.Fail("achievement_locked");
            if (p.Claimed) return Result<List<ItemStack>>.Fail("already_claimed");
            p.Claimed = true;
            foreach (ItemStack it in a.Reward) Player.AddItem(it.Item, it.N);
            Dirty = true;
            return Result<List<ItemStack>>.Success(a.Reward);
        }

        // ================= 工具 =================

        private bool HasItems(List<ItemStack> cost)
        {
            var need = new Dictionary<string, long>();
            foreach (ItemStack c in cost)
            {
                long v;
                need.TryGetValue(c.Item, out v);
                need[c.Item] = v + c.N;
            }
            return need.All(kv => Player.ItemCount(kv.Key) >= kv.Value);
        }

        private void ConsumeItems(List<ItemStack> cost)
        {
            foreach (ItemStack c in cost) Player.AddItem(c.Item, -c.N);
        }

        public static string Iso(DateTime utc) { return utc.ToString("o", CultureInfo.InvariantCulture); }

        public static DateTime ParseIso(string s)
        {
            return DateTime.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime();
        }
    }
}
