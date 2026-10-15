using System;
using System.Collections.Generic;
using Game.Core.UI;

namespace Game.Core.Story
{
    public sealed class BacklogEntry
    {
        public string Speaker { get; }
        public string TextKey { get; }
        public string VoiceId { get; }

        public BacklogEntry(string speaker, string textKey, string voiceId = null)
        {
            Speaker = speaker;
            TextKey = textKey;
            VoiceId = voiceId;
        }
    }

    public interface IStoryView : IView
    {
        void SetSpeaker(string name);
        void SetDialogue(string textKey);
        void ShowOptions(IReadOnlyList<string> optionKeys);
        void HideOptions();
        void SetBackground(string bgId, string variant);
        void SetCharacterVisible(string characterId, bool visible);
        void SetCharacterExpression(string characterId, string expression);
        void SetCharacterMotion(string characterId, string motion);
        void SetCg(string cgId);
        void SetUiVisible(bool visible);
        void UpdateBacklog(IReadOnlyList<BacklogEntry> history);
        void PlayBgm(string bgmId);
        void PlaySe(string seId);
        void PlayVoice(string voiceId);
    }

    /// <summary>
    /// ADV 劇情播放器 Presenter，實作 IStoryPresenter。
    /// 負責協同 IStoryRunner、StoryPlaybackController、Backlog 與各種播放模式（Auto、Skip、UI 隱藏、快照存檔）。
    /// </summary>
    public sealed class StoryPresenter : PresenterBase<IStoryView>, IStoryPresenter
    {
        private readonly List<BacklogEntry> _backlog = new List<BacklogEntry>();
        private string _lastVoiceId;

        public StoryPlaybackController Controller { get; }
        public IStoryRunner Runner => Controller.Runner;
        public GameCore Core => Controller.Core;

        public IReadOnlyList<BacklogEntry> Backlog => _backlog;

        public bool IsAutoPlay { get; private set; }
        public bool IsFastForward { get; private set; }
        public bool IsUiHidden { get; private set; }

        public float AutoPlayDelaySec { get; set; } = 1.5f;

        public StoryPresenter(StoryPlaybackController controller)
        {
            Controller = controller ?? throw new ArgumentNullException(nameof(controller));
        }

        public void Start(string node = "Start")
        {
            if (Runner == null) return;
            Runner.Start(node);
            Advance();
        }

        public override void Initialize()
        {
            base.Initialize();
            if (Runner != null && Runner.CurrentLine == null && !Runner.IsEnded && !string.IsNullOrEmpty(Runner.CurrentNodeName))
            {
                Advance();
            }
            else
            {
                SyncCurrentLine();
            }
        }

        // =========================================================================================
        // 控制器操作與推進
        // =========================================================================================
        public void Advance()
        {
            if (Runner == null || Runner.IsEnded) return;
            if (Runner.CurrentOptions != null && Runner.CurrentOptions.Count > 0) return;

            Controller.Advance();
            SyncCurrentLine();
        }

        public void ChooseOption(int index)
        {
            if (Runner.IsEnded) return;

            Controller.Choose(index);
            View?.HideOptions();
            SyncCurrentLine();
        }

        public void ToggleAutoPlay()
        {
            IsAutoPlay = !IsAutoPlay;
            if (IsAutoPlay)
            {
                IsFastForward = false;
            }
        }

        public void SetFastForward(bool enabled)
        {
            IsFastForward = enabled;
            if (IsFastForward)
            {
                IsAutoPlay = false;
                if (Controller.CanSkipCurrent())
                {
                    Advance();
                }
            }
        }

        public void ToggleHideUi()
        {
            IsUiHidden = !IsUiHidden;
            View?.SetUiVisible(!IsUiHidden);
        }

        public void RestoreUi()
        {
            if (IsUiHidden)
            {
                IsUiHidden = false;
                View?.SetUiVisible(true);
            }
        }

        // =========================================================================================
        // 劇情中快照與回溯入口
        // =========================================================================================
        public StorySnapshot CreateSnapshot()
        {
            return Runner?.Snapshot();
        }

        public void RestoreSnapshot(StorySnapshot snapshot)
        {
            if (snapshot == null || Runner == null) return;
            Runner.Restore(snapshot);
            SyncCurrentLine();
        }

        private void SyncCurrentLine()
        {
            if (View == null) return;

            if (Runner.IsEnded)
            {
                View.SetDialogue(string.Empty);
                View.SetSpeaker(string.Empty);
                View.HideOptions();
                return;
            }

            var opts = Runner.CurrentOptions;
            if (opts != null && opts.Count > 0)
            {
                var optKeys = new List<string>();
                for (int i = 0; i < opts.Count; i++)
                {
                    optKeys.Add(opts[i].TextKey);
                }
                View.ShowOptions(optKeys);
            }
            else
            {
                View.HideOptions();
            }

            var line = Runner.CurrentLine;
            if (line != null)
            {
                string speaker = line.Speaker ?? string.Empty;
                string text = line.TextKey ?? string.Empty;

                View.SetSpeaker(speaker);
                View.SetDialogue(text);

                // 寫入 Backlog
                if (!string.IsNullOrEmpty(text))
                {
                    var entry = new BacklogEntry(speaker, text, _lastVoiceId);
                    _backlog.Add(entry);
                    View.UpdateBacklog(_backlog);
                }
            }

            _lastVoiceId = null;
        }

        // =========================================================================================
        // 實作 IStoryPresenter 演出指令
        // =========================================================================================
        public void Bg(string sceneId, string variant = null)
        {
            View?.SetBackground(sceneId, variant);
        }

        public void Show(string characterId, IReadOnlyList<string> options = null)
        {
            View?.SetCharacterVisible(characterId, true);
        }

        public void Hide(string characterId)
        {
            View?.SetCharacterVisible(characterId, false);
        }

        public void Expr(string characterId, string expression)
        {
            View?.SetCharacterExpression(characterId, expression);
        }

        public void Motion(string characterId, string motion)
        {
            View?.SetCharacterMotion(characterId, motion);
        }

        public void Cg(string cgId)
        {
            View?.SetCg(cgId);
        }

        public void Bgm(string bgmId)
        {
            View?.PlayBgm(bgmId);
        }

        public void Se(string seId)
        {
            View?.PlaySe(seId);
        }

        public void Voice(string voiceId)
        {
            _lastVoiceId = voiceId;
            View?.PlayVoice(voiceId);
        }

        public void Wait(float seconds) { }
        public void Fade(IReadOnlyList<string> args = null) { }
        public void Shake(IReadOnlyList<string> args = null) { }
    }
}
