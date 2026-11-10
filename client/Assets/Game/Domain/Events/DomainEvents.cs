using System;
using System.Collections.Generic;
using System.Globalization;

namespace Game.Domain.Events
{
    /// <summary>
    /// 領域事件。用「名稱 + 字串欄位」表示，讓成就這類資料驅動的訂閱者可以用欄位名稱篩選，不必認得每個事件類別。
    /// </summary>
    public sealed class DomainEvent
    {
        public readonly string Name;
        public readonly Dictionary<string, string> Fields = new Dictionary<string, string>();

        public DomainEvent(string name) { Name = name; }

        public DomainEvent With(string key, object value)
        {
            Fields[key] = Convert.ToString(value, CultureInfo.InvariantCulture);
            return this;
        }

        public string Get(string key)
        {
            string v;
            return Fields.TryGetValue(key, out v) ? v : null;
        }

        public override string ToString() { return Name; }
    }

    /// <summary>事件名稱常數（和成就資料的 trigger.event 對應）。</summary>
    public static class Ev
    {
        public const string GachaPulled = "GachaPulled";             // banner, count
        public const string GachaDraw = "GachaDraw";                 // banner, char, rarity, isNew
        public const string CharacterObtained = "CharacterObtained"; // char, rarity, via
        public const string CharacterLeveledUp = "CharacterLeveledUp"; // char, level
        public const string CharacterAwakened = "CharacterAwakened"; // char, awaken
        public const string PortraitStageUnlocked = "PortraitStageUnlocked"; // char, stage
        public const string AffectionChanged = "AffectionChanged";   // char, affection, delta
        public const string ItemGiven = "ItemGiven";                 // item, n
        public const string BattleFinished = "BattleFinished";       // stage, outcome, stars, enemiesDefeated
        public const string EncounterResolved = "EncounterResolved"; // event, spot, char, choice
        public const string StoryNodeReached = "StoryNodeReached";   // node
        public const string EndingReached = "EndingReached";         // ending
        public const string CgUnlocked = "CgUnlocked";               // cg
        public const string CostumeUnlocked = "CostumeUnlocked";     // costume, char
        public const string AchievementUnlocked = "AchievementUnlocked"; // achievement
        public const string RouteChapterCompleted = "RouteChapterCompleted"; // char, chapter
        public const string CharacterTierAdvanced = "CharacterTierAdvanced"; // char, rarity, tier
        public const string CharacterRarityPromoted = "CharacterRarityPromoted"; // char, oldRarity, newRarity
        public const string BaseUpgraded = "BaseUpgraded"; // level, maxHp, cost
    }

    /// <summary>
    /// 同步事件匯流排。發佈時先放進佇列，再依序派送，避免處理事件時又發佈新事件造成重入問題
    /// （例如成就解鎖本身也是一個事件）。
    /// </summary>
    public sealed class EventBus
    {
        private readonly List<Action<DomainEvent>> _handlers = new List<Action<DomainEvent>>();
        private readonly Queue<DomainEvent> _queue = new Queue<DomainEvent>();
        private bool _dispatching;

        public void Subscribe(Action<DomainEvent> handler) { _handlers.Add(handler); }

        public void Unsubscribe(Action<DomainEvent> handler) { _handlers.Remove(handler); }

        public void Publish(DomainEvent e)
        {
            _queue.Enqueue(e);
            if (_dispatching) return;
            _dispatching = true;
            try
            {
                while (_queue.Count > 0)
                {
                    DomainEvent next = _queue.Dequeue();
                    // 複製一份，允許處理器在派送中訂閱 / 取消訂閱
                    foreach (Action<DomainEvent> h in _handlers.ToArray()) h(next);
                }
            }
            finally
            {
                _dispatching = false;
                // 處理器丟出例外時，捨棄剩下的事件，避免下一次發佈時誤送舊事件
                _queue.Clear();
            }
        }
    }
}
