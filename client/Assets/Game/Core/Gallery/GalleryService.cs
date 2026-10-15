using System;
using System.Collections.Generic;
using Game.Domain;
using Game.Domain.Player;

namespace Game.Core.Gallery
{
    /// <summary>
    /// CG 圖鑑項目定義。
    /// </summary>
    public sealed class CgDef
    {
        public string Id { get; }
        public string CharacterId { get; }
        public Rating Rating { get; }
        public string Title { get; }
        public string ArtKey { get; }

        public CgDef(string id, string characterId, Rating rating = Rating.G, string title = null, string artKey = null)
        {
            Id = id ?? string.Empty;
            CharacterId = characterId ?? string.Empty;
            Rating = rating;
            Title = title ?? id;
            ArtKey = artKey ?? id;
        }
    }

    /// <summary>
    /// 圖鑑展示用條目，包含解鎖狀態。
    /// </summary>
    public sealed class GalleryItem
    {
        public CgDef Def { get; }
        public bool IsUnlocked { get; }

        public GalleryItem(CgDef def, bool isUnlocked)
        {
            Def = def ?? throw new ArgumentNullException(nameof(def));
            IsUnlocked = isUnlocked;
        }
    }

    /// <summary>
    /// CG 圖鑑服務，負責依角色分組，並套用 RatingPolicy 分級過濾。
    /// </summary>
    public sealed class GalleryService
    {
        private readonly List<CgDef> _cgList;

        public GalleryService(IEnumerable<CgDef> cgs = null)
        {
            _cgList = cgs != null ? new List<CgDef>(cgs) : new List<CgDef>();
        }

        public void AddCg(CgDef cg)
        {
            if (cg != null) _cgList.Add(cg);
        }

        /// <summary>
        /// 依角色分組取得 CG 圖鑑。分級不允許的 CG 絕對不列出。
        /// </summary>
        public Dictionary<string, List<GalleryItem>> GetGroupedGallery(PlayerState player, RatingPolicy rating)
        {
            var result = new Dictionary<string, List<GalleryItem>>(StringComparer.Ordinal);
            if (player == null || rating == null) return result;

            var knownCgs = new HashSet<string>(StringComparer.Ordinal);

            foreach (CgDef cg in _cgList)
            {
                // 分級不允許的 CG 不會列出
                if (!rating.Allows(cg.Rating))
                    continue;

                knownCgs.Add(cg.Id);
                string charId = cg.CharacterId ?? string.Empty;
                if (!result.TryGetValue(charId, out List<GalleryItem> list))
                {
                    list = new List<GalleryItem>();
                    result[charId] = list;
                }

                bool isUnlocked = player.Gallery != null && player.Gallery.Contains(cg.Id);
                list.Add(new GalleryItem(cg, isUnlocked));
            }

            // 自動將玩家在 Player.Gallery 中解鎖的 CG 也納入圖鑑顯示
            if (player.Gallery != null)
            {
                foreach (string unlockedId in player.Gallery)
                {
                    if (knownCgs.Contains(unlockedId)) continue;
                    string charId = ParseCharIdFromCg(unlockedId);
                    if (!result.TryGetValue(charId, out List<GalleryItem> list))
                    {
                        list = new List<GalleryItem>();
                        result[charId] = list;
                    }
                    var def = new CgDef(unlockedId, charId, Rating.G, unlockedId);
                    list.Add(new GalleryItem(def, true));
                }
            }

            return result;
        }

        private static string ParseCharIdFromCg(string cgId)
        {
            if (string.IsNullOrEmpty(cgId)) return string.Empty;
            if (cgId.StartsWith("cg_"))
            {
                string rest = cgId.Substring(3);
                // 尋找角色 ID 模式如 char01_claire
                int nextUnderscore = rest.IndexOf('_');
                if (nextUnderscore >= 0)
                {
                    int secondUnderscore = rest.IndexOf('_', nextUnderscore + 1);
                    if (secondUnderscore > 0)
                    {
                        return rest.Substring(0, secondUnderscore);
                    }
                }
            }
            return string.Empty;
        }
    }
}
