using System;
using System.Collections.Generic;

namespace Game.Core.Story
{
    /// <summary>
    /// 代表目前對話行資料（唯讀）。
    /// </summary>
    public sealed class StoryLine
    {
        public string LineId { get; }
        public string Speaker { get; }
        public string TextKey { get; }

        public StoryLine(string lineId, string speaker, string textKey)
        {
            LineId = lineId ?? string.Empty;
            Speaker = speaker ?? string.Empty;
            TextKey = textKey ?? string.Empty;
        }

        public override string ToString()
        {
            return $"[{LineId}] {Speaker}: {TextKey}";
        }
    }

    /// <summary>
    /// 代表目前選項資料（唯讀）。
    /// </summary>
    public sealed class StoryOption
    {
        public string TextKey { get; }
        public string TargetNode { get; }
        public bool IsSelectable { get; }
        public bool IsEnabled => IsSelectable;

        public StoryOption(string textKey, string targetNode, bool isSelectable = true)
        {
            TextKey = textKey ?? string.Empty;
            TargetNode = targetNode ?? string.Empty;
            IsSelectable = isSelectable;
        }

        public override string ToString()
        {
            return $"{TextKey} -> {TargetNode} (selectable: {IsSelectable})";
        }
    }

    /// <summary>
    /// 劇情執行器狀態快照，用於存檔或回溯。
    /// 快照內儲存獨立的變數字典副本，與執行器之 live 變數字典完全隔離不別名（no aliasing）。
    /// </summary>
    public sealed class StorySnapshot
    {
        public string NodeName { get; }
        public int InstructionIndex { get; }
        public StoryLine CurrentLine { get; }
        public IReadOnlyList<StoryOption> CurrentOptions { get; }
        public bool IsEnded { get; }
        public IReadOnlyDictionary<string, object> Variables { get; }

        public StorySnapshot(
            string nodeName,
            int instructionIndex,
            StoryLine currentLine,
            IEnumerable<StoryOption> currentOptions,
            bool isEnded,
            IDictionary<string, object> variables)
        {
            NodeName = nodeName;
            InstructionIndex = instructionIndex;
            CurrentLine = currentLine;
            CurrentOptions = currentOptions != null
                ? new List<StoryOption>(currentOptions).AsReadOnly()
                : (IReadOnlyList<StoryOption>)Array.Empty<StoryOption>();
            IsEnded = isEnded;
            var dict = new Dictionary<string, object>(StringComparer.Ordinal);
            if (variables != null)
            {
                foreach (var kvp in variables)
                {
                    dict[kvp.Key] = kvp.Value;
                }
            }
            Variables = dict;
        }
    }

    /// <summary>
    /// 劇情執行器介面，提供腳本推進、選項選擇、狀態快照與變數存取能力。
    /// </summary>
    public interface IStoryRunner
    {
        string CurrentNodeName { get; }
        StoryLine CurrentLine { get; }
        IReadOnlyList<StoryOption> CurrentOptions { get; }
        bool IsEnded { get; }
        IDictionary<string, object> Variables { get; }

        void Start(string node);
        void Continue();
        void Choose(int index);

        StorySnapshot Snapshot();
        void Restore(StorySnapshot snapshot);

        void RegisterFunction(string name, Func<object[], object> function);
        void RegisterCommand(string name, Action<string[]> handler);
    }
}
