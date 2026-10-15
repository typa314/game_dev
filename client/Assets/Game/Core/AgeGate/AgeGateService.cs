using System;
using Game.Domain;

namespace Game.Core.AgeGate
{
    public sealed class AgeGateState
    {
        public bool IsVerified { get; set; }
        public DateTime? VerifiedAtUtc { get; set; }
        public Rating RatingOptIn { get; set; } = Rating.G;
    }

    public static class AgeGateService
    {
        /// <summary>
        /// 執行年齡確認驗證（D5-2：記錄時間，並設定玩家的 Opt-In 選擇）。
        /// </summary>
        public static void Verify(AgeGateState state, Rating optIn, IClock clock)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            IClock effectiveClock = clock ?? new SystemClock();
            state.IsVerified = true;
            state.VerifiedAtUtc = effectiveClock.UtcNow;
            state.RatingOptIn = optIn;
        }

        /// <summary>
        /// 計算實際生效的分級上限（PlayerMax）。
        /// D5-1: 沒有通過年齡確認時，PlayerMax 固定為 G，即使 DLC 已經安裝。
        /// D5-2: 通過確認後，以玩家選擇為基準，但不超過 DLC 與建置版本之上限。
        /// </summary>
        public static Rating GetEffectiveRating(AgeGateState state, Rating buildMax, bool isDlcInstalled)
        {
            // D5-1: 未通過年齡驗證時，一律強制固定為 G
            if (state == null || !state.IsVerified)
            {
                return Rating.G;
            }

            // 依 DLC 與建置版本計算天花板（無成人 DLC 時最高僅至 R15）
            Rating platformCeiling = isDlcInstalled ? buildMax : (buildMax > Rating.R15 ? Rating.R15 : buildMax);

            // 玩家 Opt-In 選擇不能超過平台天花板
            return state.RatingOptIn > platformCeiling ? platformCeiling : state.RatingOptIn;
        }
    }
}
