using System;
using System.Collections.Generic;
using System.Globalization;
using Game.Domain;

namespace Game.Core.Story
{
    /// <summary>
    /// ADV 劇情指令註冊表，負責將 15 種劇情指令分派至呈現層（IStoryPresenter）與核心狀態機（GameCore）。
    /// </summary>
    public sealed class StoryCommandRegistry
    {
        public static readonly IReadOnlyList<string> SupportedCommands = new[]
        {
            "bg", "show", "hide", "expr", "motion", "cg", "bgm", "se", "voice",
            "affection", "give", "ending", "wait", "fade", "shake"
        };

        public IStoryRunner Runner { get; }
        public GameCore Core { get; }
        public IStoryPresenter Presenter { get; }
        public Action<string> OnError { get; }
        public Func<string, Rating> CgRatingResolver { get; }
        public bool IsRecallMode { get; set; }

        public StoryCommandRegistry(
            IStoryRunner runner,
            GameCore core,
            IStoryPresenter presenter,
            Action<string> onError = null,
            Func<string, Rating> cgRatingResolver = null,
            bool isRecallMode = false)
        {
            Runner = runner ?? throw new ArgumentNullException(nameof(runner));
            Core = core ?? throw new ArgumentNullException(nameof(core));
            Presenter = presenter ?? throw new ArgumentNullException(nameof(presenter));
            OnError = onError;
            CgRatingResolver = cgRatingResolver;
            IsRecallMode = isRecallMode;

            RegisterAll(runner, core, presenter, onError, cgRatingResolver, this);
        }

        public static StoryCommandRegistry Register(
            IStoryRunner runner,
            GameCore core,
            IStoryPresenter presenter,
            Action<string> onError = null,
            Func<string, Rating> cgRatingResolver = null,
            bool isRecallMode = false)
        {
            return new StoryCommandRegistry(runner, core, presenter, onError, cgRatingResolver, isRecallMode);
        }

        private static void RegisterAll(
            IStoryRunner runner,
            GameCore core,
            IStoryPresenter presenter,
            Action<string> onError,
            Func<string, Rating> cgRatingResolver,
            StoryCommandRegistry registry)
        {
            runner.RegisterCommand("bg", args => HandleBg(args, presenter, onError));
            runner.RegisterCommand("show", args => HandleShow(args, presenter, onError));
            runner.RegisterCommand("hide", args => HandleHide(args, presenter, onError));
            runner.RegisterCommand("expr", args => HandleExpr(args, presenter, onError));
            runner.RegisterCommand("motion", args => HandleMotion(args, presenter, onError));
            runner.RegisterCommand("cg", args => HandleCg(args, core, presenter, onError, cgRatingResolver, registry));
            runner.RegisterCommand("bgm", args => HandleBgm(args, presenter, onError));
            runner.RegisterCommand("se", args => HandleSe(args, presenter, onError));
            runner.RegisterCommand("voice", args => HandleVoice(args, presenter, onError));
            runner.RegisterCommand("affection", args => HandleAffection(args, core, onError, registry));
            runner.RegisterCommand("give", args => HandleGive(args, core, onError, registry));
            runner.RegisterCommand("ending", args => HandleEnding(args, core, onError, registry));
            runner.RegisterCommand("wait", args => HandleWait(args, presenter, onError));
            runner.RegisterCommand("fade", args => HandleFade(args, presenter, onError));
            runner.RegisterCommand("shake", args => HandleShake(args, presenter, onError));
        }

        private static void HandleBg(string[] args, IStoryPresenter presenter, Action<string> onError)
        {
            args = args ?? Array.Empty<string>();
            if (args.Length < 1 || args.Length > 2)
            {
                onError?.Invoke($"bg: expected 1 or 2 arguments, got {args.Length}");
                return;
            }

            if (string.IsNullOrWhiteSpace(args[0]))
            {
                onError?.Invoke("bg: scene ID cannot be empty");
                return;
            }

            string sceneId = args[0];
            string variant = args.Length > 1 ? args[1] : null;
            presenter.Bg(sceneId, variant);
        }

        private static void HandleShow(string[] args, IStoryPresenter presenter, Action<string> onError)
        {
            args = args ?? Array.Empty<string>();
            if (args.Length < 1)
            {
                onError?.Invoke("show: expected at least 1 argument");
                return;
            }

            if (string.IsNullOrWhiteSpace(args[0]))
            {
                onError?.Invoke("show: character ID cannot be empty");
                return;
            }

            string charId = args[0];
            string[] options = new string[args.Length - 1];
            if (options.Length > 0)
            {
                Array.Copy(args, 1, options, 0, options.Length);
            }

            presenter.Show(charId, options);
        }

        private static void HandleHide(string[] args, IStoryPresenter presenter, Action<string> onError)
        {
            args = args ?? Array.Empty<string>();
            if (args.Length != 1)
            {
                onError?.Invoke($"hide: expected exactly 1 argument, got {args.Length}");
                return;
            }

            if (string.IsNullOrWhiteSpace(args[0]))
            {
                onError?.Invoke("hide: character ID cannot be empty");
                return;
            }

            presenter.Hide(args[0]);
        }

        private static void HandleExpr(string[] args, IStoryPresenter presenter, Action<string> onError)
        {
            args = args ?? Array.Empty<string>();
            if (args.Length != 2)
            {
                onError?.Invoke($"expr: expected 2 arguments, got {args.Length}");
                return;
            }

            if (string.IsNullOrWhiteSpace(args[0]))
            {
                onError?.Invoke("expr: character ID cannot be empty");
                return;
            }

            if (string.IsNullOrWhiteSpace(args[1]))
            {
                onError?.Invoke("expr: expression name cannot be empty");
                return;
            }

            presenter.Expr(args[0], args[1]);
        }

        private static void HandleMotion(string[] args, IStoryPresenter presenter, Action<string> onError)
        {
            args = args ?? Array.Empty<string>();
            if (args.Length != 2)
            {
                onError?.Invoke($"motion: expected 2 arguments, got {args.Length}");
                return;
            }

            if (string.IsNullOrWhiteSpace(args[0]))
            {
                onError?.Invoke("motion: character ID cannot be empty");
                return;
            }

            if (string.IsNullOrWhiteSpace(args[1]))
            {
                onError?.Invoke("motion: motion name cannot be empty");
                return;
            }

            presenter.Motion(args[0], args[1]);
        }

        private static void HandleCg(
            string[] args,
            GameCore core,
            IStoryPresenter presenter,
            Action<string> onError,
            Func<string, Rating> cgRatingResolver,
            StoryCommandRegistry registry)
        {
            args = args ?? Array.Empty<string>();
            if (args.Length != 1)
            {
                onError?.Invoke($"cg: expected 1 argument, got {args.Length}");
                return;
            }

            if (string.IsNullOrWhiteSpace(args[0]))
            {
                onError?.Invoke("cg: CG ID cannot be empty");
                return;
            }

            string cgId = args[0];

            Rating cgRating = Rating.G;
            if (cgRatingResolver != null)
            {
                cgRating = cgRatingResolver(cgId);
            }
            else
            {
                string lower = cgId.ToLowerInvariant();
                if (lower.Contains("_t2_") || lower.Contains("_t3_") || lower.Contains("_t4_") || lower.Contains("_r18") || lower.EndsWith("_r18"))
                {
                    cgRating = Rating.R18;
                }
            }

            if (core.Rating != null && !core.Rating.Allows(cgRating))
            {
                return;
            }

            if (registry == null || !registry.IsRecallMode)
            {
                core.UnlockCg(cgId);
            }
            presenter.Cg(cgId);
        }

        private static void HandleBgm(string[] args, IStoryPresenter presenter, Action<string> onError)
        {
            args = args ?? Array.Empty<string>();
            if (args.Length != 1)
            {
                onError?.Invoke($"bgm: expected 1 argument, got {args.Length}");
                return;
            }

            if (string.IsNullOrWhiteSpace(args[0]))
            {
                onError?.Invoke("bgm: BGM ID cannot be empty");
                return;
            }

            presenter.Bgm(args[0]);
        }

        private static void HandleSe(string[] args, IStoryPresenter presenter, Action<string> onError)
        {
            args = args ?? Array.Empty<string>();
            if (args.Length != 1)
            {
                onError?.Invoke($"se: expected 1 argument, got {args.Length}");
                return;
            }

            if (string.IsNullOrWhiteSpace(args[0]))
            {
                onError?.Invoke("se: SE ID cannot be empty");
                return;
            }

            presenter.Se(args[0]);
        }

        private static void HandleVoice(string[] args, IStoryPresenter presenter, Action<string> onError)
        {
            args = args ?? Array.Empty<string>();
            if (args.Length != 1)
            {
                onError?.Invoke($"voice: expected 1 argument, got {args.Length}");
                return;
            }

            if (string.IsNullOrWhiteSpace(args[0]))
            {
                onError?.Invoke("voice: voice ID cannot be empty");
                return;
            }

            presenter.Voice(args[0]);
        }

        private static void HandleAffection(string[] args, GameCore core, Action<string> onError, StoryCommandRegistry registry)
        {
            if (registry != null && registry.IsRecallMode) return;

            args = args ?? Array.Empty<string>();
            if (args.Length != 2)
            {
                onError?.Invoke($"affection: expected 2 arguments, got {args.Length}");
                return;
            }

            if (string.IsNullOrWhiteSpace(args[0]))
            {
                onError?.Invoke("affection: character ID cannot be empty");
                return;
            }

            if (!int.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int delta))
            {
                onError?.Invoke($"affection: invalid amount '{args[1]}'");
                return;
            }

            Result res = core.AddAffection(args[0], delta);
            if (!res.Ok)
            {
                onError?.Invoke($"affection: {res.Error}");
            }
        }

        private static void HandleGive(string[] args, GameCore core, Action<string> onError, StoryCommandRegistry registry)
        {
            if (registry != null && registry.IsRecallMode) return;

            args = args ?? Array.Empty<string>();
            if (args.Length != 2)
            {
                onError?.Invoke($"give: expected 2 arguments, got {args.Length}");
                return;
            }

            if (string.IsNullOrWhiteSpace(args[0]))
            {
                onError?.Invoke("give: item ID cannot be empty");
                return;
            }

            if (!long.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out long amount) || amount <= 0)
            {
                onError?.Invoke($"give: invalid item amount '{args[1]}'");
                return;
            }

            Result res = core.GiveItem(args[0], amount);
            if (!res.Ok)
            {
                onError?.Invoke($"give: {res.Error}");
            }
        }

        private static void HandleEnding(string[] args, GameCore core, Action<string> onError, StoryCommandRegistry registry)
        {
            if (registry != null && registry.IsRecallMode) return;

            args = args ?? Array.Empty<string>();
            if (args.Length != 1)
            {
                onError?.Invoke($"ending: expected 1 argument, got {args.Length}");
                return;
            }

            if (string.IsNullOrWhiteSpace(args[0]))
            {
                onError?.Invoke("ending: ending ID cannot be empty");
                return;
            }

            core.ReachEnding(args[0]);
        }

        private static void HandleWait(string[] args, IStoryPresenter presenter, Action<string> onError)
        {
            args = args ?? Array.Empty<string>();
            if (args.Length != 1)
            {
                onError?.Invoke($"wait: expected 1 argument, got {args.Length}");
                return;
            }

            if (!float.TryParse(args[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float sec) ||
                float.IsNaN(sec) || float.IsInfinity(sec) || sec < 0f)
            {
                onError?.Invoke($"wait: invalid duration '{args[0]}'");
                return;
            }

            presenter.Wait(sec);
        }

        private static void HandleFade(string[] args, IStoryPresenter presenter, Action<string> onError)
        {
            args = args ?? Array.Empty<string>();
            presenter.Fade(args);
        }

        private static void HandleShake(string[] args, IStoryPresenter presenter, Action<string> onError)
        {
            args = args ?? Array.Empty<string>();
            presenter.Shake(args);
        }
    }
}
