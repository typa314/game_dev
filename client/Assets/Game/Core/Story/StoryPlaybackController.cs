using System;
using System.Globalization;
using Game.Domain.Player;

namespace Game.Core.Story
{
    /// <summary>
    /// ADV 劇情播放控制器，負責協同 StoryRunner 推進、自動標記已讀節點/行，以及提供快進判定（只快進已讀 vs 快進全部）。
    /// 支援回憶重播模式 (IsRecallMode)：回憶模式下不寫入已讀節點或全域旗標，確保正式存檔 100% 不受污染。
    /// </summary>
    public sealed class StoryPlaybackController
    {
        public IStoryRunner Runner { get; }
        public GameCore Core { get; }
        public bool IsRecallMode { get; set; }

        public StoryPlaybackController(IStoryRunner runner, GameCore core, bool isRecallMode = false)
        {
            Runner = runner ?? throw new ArgumentNullException(nameof(runner));
            Core = core ?? throw new ArgumentNullException(nameof(core));
            IsRecallMode = isRecallMode;
        }

        /// <summary>
        /// 判斷目前所在的節點或台詞是否允許快進（Fast Forward）。
        /// 若玩家設定 Settings.SkipUnread == true，允許快進所有內容（包含未讀）；
        /// 若 Settings.SkipUnread == false，僅在該節點或台詞為已讀時允許快進。
        /// </summary>
        public bool CanSkipCurrent()
        {
            SettingsState settings = Core.Player?.Settings;
            if (settings != null && settings.SkipUnread)
            {
                return true;
            }

            StoryLine line = Runner.CurrentLine;
            if (line != null && !string.IsNullOrEmpty(line.LineId))
            {
                return Core.Player.Story.ReadLines.Contains(line.LineId) ||
                       Core.Player.Story.ReadNodes.Contains(line.LineId);
            }

            string node = Runner.CurrentNodeName;
            return !string.IsNullOrEmpty(node) && Core.Player.Story.ReadNodes.Contains(node);
        }

        /// <summary>
        /// 推進劇情一步，並自動將造訪之節點與行標記為已讀。
        /// </summary>
        public void Advance()
        {
            Runner.Continue();
            RecordCurrentAsRead();
            SyncVariablesToFlags();
        }

        /// <summary>
        /// 選擇選項並推進劇情一步，並自動記錄已讀。
        /// </summary>
        public void Choose(int index)
        {
            Runner.Choose(index);
            RecordCurrentAsRead();
            SyncVariablesToFlags();
        }

        /// <summary>
        /// 依據快進規則連續推進，直到故事結束、遇到選項，或在「只快進已讀」模式下遭遇未讀內容為止。
        /// </summary>
        /// <param name="maxSteps">防禦無限迴圈的步數上限</param>
        /// <returns>實際快進步數</returns>
        public int FastForward(int maxSteps = 1000)
        {
            int steps = 0;
            while (!Runner.IsEnded && (Runner.CurrentOptions == null || Runner.CurrentOptions.Count == 0) && steps < maxSteps)
            {
                if (!CanSkipCurrent() && steps > 0)
                {
                    // 遭遇未讀內容，停止快進
                    break;
                }

                Advance();
                steps++;
            }
            return steps;
        }

        private void RecordCurrentAsRead()
        {
            if (Core == null || IsRecallMode) return;

            string node = Runner.CurrentNodeName;
            if (!string.IsNullOrEmpty(node))
            {
                Core.MarkNodeRead(node);
            }

            StoryLine line = Runner.CurrentLine;
            if (line != null && !string.IsNullOrEmpty(line.LineId))
            {
                Core.MarkLineRead(line.LineId);
                Core.MarkNodeRead(line.LineId);
            }
        }

        private void SyncVariablesToFlags()
        {
            if (Core == null || IsRecallMode || Runner?.Variables == null) return;
            foreach (var kvp in Runner.Variables)
            {
                if (string.IsNullOrWhiteSpace(kvp.Key) || kvp.Value == null) continue;
                if (kvp.Value is int || kvp.Value is long || kvp.Value is float || kvp.Value is double || kvp.Value is decimal)
                {
                    double val = Convert.ToDouble(kvp.Value, CultureInfo.InvariantCulture);
                    Core.SetGlobalFlag(kvp.Key, val);
                }
                else if (kvp.Value is bool b)
                {
                    Core.SetGlobalFlag(kvp.Key, b ? 1.0 : 0.0);
                }
            }
        }
    }
}
