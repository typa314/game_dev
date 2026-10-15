using System;
using System.Collections.Generic;
using System.IO;
using Game.Core.Story;
using Game.Core.Story.MiniScript;
using Game.Domain;

namespace Game.Core.Persistence
{
    /// <summary>
    /// 雙層存檔架構整合服務（B5 查核點）。
    /// 負責儲存與載入劇情存檔槽（StorySlot），將劇情進度與全域 Profile 隔離。
    /// 讀取劇情存檔時絕不倒退 Profile，且當記錄的節點遺失時自動退回章節開頭並發出警告。
    /// </summary>
    public sealed class StorySaveService
    {
        private readonly string _saveDir;
        private readonly IClock _clock;
        private readonly Dictionary<string, StorySlot> _memorySlots;

        public StorySaveService(string saveDir = null, IClock clock = null)
        {
            _saveDir = saveDir;
            _clock = clock ?? new SystemClock();
            _memorySlots = saveDir == null ? new Dictionary<string, StorySlot>(StringComparer.Ordinal) : null;
        }

        public StorySlot Save(
            string slotId,
            IStoryRunner runner,
            string chapterStartNode = null,
            StoryStaging staging = null,
            string memo = null)
        {
            if (string.IsNullOrWhiteSpace(slotId)) throw new ArgumentNullException(nameof(slotId));
            if (runner == null) throw new ArgumentNullException(nameof(runner));

            var slot = new StorySlot
            {
                SlotId = slotId,
                SavedAtUtc = GameCore.Iso(_clock.UtcNow),
                Node = runner.CurrentNodeName,
                LineId = runner.CurrentLine?.LineId,
                ChapterStartNode = chapterStartNode,
                Staging = staging ?? new StoryStaging(),
                Memo = memo,
                Variables = new Dictionary<string, object>(runner.Variables, StringComparer.Ordinal)
            };

            if (_saveDir != null)
            {
                if (!Directory.Exists(_saveDir)) Directory.CreateDirectory(_saveDir);
                string path = Path.Combine(_saveDir, slotId + ".json");
                string json = SaveCodec.EncodeStorySlot(slot);
                File.WriteAllText(path, json);
            }
            else
            {
                _memorySlots[slotId] = slot;
            }

            return slot;
        }

        public StorySlot Load(string slotId)
        {
            if (string.IsNullOrWhiteSpace(slotId)) throw new ArgumentNullException(nameof(slotId));

            if (_saveDir != null)
            {
                string path = Path.Combine(_saveDir, slotId + ".json");
                if (!File.Exists(path)) return null;
                string json = File.ReadAllText(path);
                return SaveCodec.DecodeStorySlot(json);
            }

            _memorySlots.TryGetValue(slotId, out StorySlot slot);
            return slot;
        }

        /// <summary>
        /// 將 StorySlot 狀態套用回 StoryRunner。
        /// 若記錄的節點已不存在於劇本中，退回章節開頭節點並觸發警告回呼（B5-3）。
        /// 嚴格保證不碰觸任何 PlayerState / Profile（B5-1, B5-2）。
        /// </summary>
        public bool ApplyToRunner(
            StorySlot slot,
            IStoryRunner runner,
            MiniScript script,
            Action<string> onWarning = null)
        {
            if (slot == null || runner == null || script == null)
                return false;

            string targetNode = slot.Node;

            // B5-3: 節點存在性檢查
            if (string.IsNullOrEmpty(targetNode) || !script.Nodes.ContainsKey(targetNode))
            {
                string fallback = slot.ChapterStartNode;
                if (string.IsNullOrEmpty(fallback) || !script.Nodes.ContainsKey(fallback))
                {
                    fallback = script.Nodes.ContainsKey("Start") ? "Start" : null;
                }

                onWarning?.Invoke($"Node '{targetNode}' no longer exists in script. Falling back to '{fallback}'.");
                targetNode = fallback;
            }

            if (!string.IsNullOrEmpty(targetNode) && script.Nodes.ContainsKey(targetNode))
            {
                runner.Start(targetNode);
            }

            // 恢復劇情變數
            runner.Variables.Clear();
            if (slot.Variables != null)
            {
                foreach (var kvp in slot.Variables)
                {
                    runner.Variables[kvp.Key] = kvp.Value;
                }
            }

            return true;
        }
    }
}
