using System;
using Game.Domain;
using Game.Domain.Growth;
using Game.Domain.Player;

namespace Game.Core.Story
{
    /// <summary>
    /// 角色專屬劇情路線開放與進度管理服務（C7 查核點）。
    /// 規則：
    /// 1. 每位角色 5 章，第 1–5 章依好感等級 1–5 開放。
    /// 2. 第 1 章只要持有角色且好感等級 >= 1 即可遊玩。
    /// 3. 第 N 章 (N > 1) 必須在前一章已完成且好感等級 >= N 時方能開放。
    /// </summary>
    public static class CharacterRouteService
    {
        public const int MaxChapters = 5;

        public static string GetChapterKey(string charId, int chapter)
        {
            if (string.IsNullOrWhiteSpace(charId)) throw new ArgumentNullException(nameof(charId));
            return $"{charId}_ch{chapter}";
        }

        public static bool IsChapterUnlocked(PlayerState player, string charId, int chapter, GrowthRules growth)
        {
            if (player == null || string.IsNullOrWhiteSpace(charId) || growth == null)
                return false;

            if (chapter < 1 || chapter > MaxChapters)
                return false;

            CharacterState cs = player.Char(charId);
            if (cs == null)
                return false;

            int affLevel = growth.AffectionLevel(cs.Affection);
            if (affLevel < chapter)
                return false;

            // 第 1 章無前置章節要求
            if (chapter == 1)
                return true;

            // 第 2 章以上要求前一章已完成
            string prevKey = GetChapterKey(charId, chapter - 1);
            return player.Story.CompletedChapters.Contains(prevKey);
        }

        public static Result CompleteChapter(PlayerState player, string charId, int chapter, GrowthRules growth)
        {
            if (player == null || string.IsNullOrWhiteSpace(charId) || growth == null)
                return Result.Fail("invalid_arguments");

            if (chapter < 1 || chapter > MaxChapters)
                return Result.Fail("invalid_chapter");

            if (!IsChapterUnlocked(player, charId, chapter, growth))
                return Result.Fail("chapter_locked");

            string key = GetChapterKey(charId, chapter);
            player.Story.CompletedChapters.Add(key);
            return Result.Success();
        }
    }
}
