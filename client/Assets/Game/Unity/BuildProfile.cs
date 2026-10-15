using Game.Domain;
using UnityEngine;

namespace Game.Unity
{
    /// <summary>
    /// 建置設定（design/02 ADR-003、design/03 §4）：本體版 / DLC 版各一份資產。
    /// MaxRating 是「建置版本上限」，實際允許分級 = min(MaxRating, 玩家設定)，見 RatingPolicy。
    /// TODO: 建置腳本依 BuildProfile 切換 Addressables group（R18 只進 DLC group）。
    /// </summary>
    [CreateAssetMenu(fileName = "BuildProfile", menuName = "Game/Build Profile")]
    public sealed class BuildProfile : ScriptableObject
    {
        public Rating MaxRating = Rating.G;
        public string GameVersion = "0.1.0-dev";
    }
}
