using System;
using System.Collections.Generic;
using System.Linq;
using Game.Domain.MasterData;

namespace Game.Domain.Gacha
{
    public sealed class GachaDraw
    {
        public string Char;
        public string Rarity;
        public bool IsPickup;
        public bool ByPity;
        public bool ByTenGuarantee;
    }

    /// <summary>
    /// 轉蛋抽取（純函式加上 pity 計數器）。規則見 design/07_gacha_and_encounter.md §1.2。
    /// 扣貨幣、處理重複角色由 GameCore 負責，這裡只決定抽到什麼。
    /// </summary>
    public static class GachaEngine
    {
        public static List<GachaDraw> Pull(BannerDef banner, int count, ref int pity, IRandom rng)
        {
            ValidatePools(banner);
            var result = new List<GachaDraw>(count);
            int guaranteeRank = banner.TenPullGuarantee != null ? Rarity.Rank(banner.TenPullGuarantee) : -1;
            int pityRank = banner.Pity != null ? Rarity.Rank(banner.Pity.Rarity) : -1;
            bool gotGuaranteeRarity = false;

            for (int i = 0; i < count; i++)
            {
                pity++;
                string rarity;
                bool byPity = false, byGuarantee = false;

                if (banner.Pity != null && banner.Pity.HardAt > 0 && pity >= banner.Pity.HardAt)
                {
                    rarity = banner.Pity.Rarity;
                    byPity = true;
                }
                else
                {
                    rarity = RollRarity(banner, rng);
                }

                if (count == 10 && i == 9 && guaranteeRank >= 0 && !gotGuaranteeRarity && Rarity.Rank(rarity) < guaranteeRank)
                {
                    rarity = banner.TenPullGuarantee;
                    byGuarantee = true;
                }

                int rank = Rarity.Rank(rarity);
                if (guaranteeRank >= 0 && rank >= guaranteeRank) gotGuaranteeRarity = true;
                if (pityRank >= 0 && rank >= pityRank) pity = 0;

                bool isPickup;
                string ch = PickCharacter(banner, rarity, rng, out isPickup);
                result.Add(new GachaDraw { Char = ch, Rarity = rarity, IsPickup = isPickup, ByPity = byPity, ByTenGuarantee = byGuarantee });
            }
            return result;
        }

        private static void ValidatePools(BannerDef banner)
        {
            if (banner.Rates == null || !banner.Rates.Values.Any(v => v > 0))
                throw new InvalidOperationException("banner " + banner.Id + " has no positive rates");

            foreach (KeyValuePair<string, double> rate in banner.Rates)
                if (rate.Value > 0) ValidatePool(banner, rate.Key);

            if (banner.Pity != null && banner.Pity.HardAt > 0)
                ValidatePool(banner, banner.Pity.Rarity);
            if (banner.TenPullGuarantee != null)
                ValidatePool(banner, banner.TenPullGuarantee);
        }

        private static void ValidatePool(BannerDef banner, string rarity)
        {
            List<string> pool;
            if (rarity == null || !banner.Pool.TryGetValue(rarity, out pool) || pool == null || pool.Count == 0)
                throw new InvalidOperationException("banner " + banner.Id + " has empty pool for " + rarity);
        }

        private static string RollRarity(BannerDef banner, IRandom rng)
        {
            double r = rng.NextDouble();
            double acc = 0;
            string last = null;
            // 由高稀有度往低累加，讓結果不受 Dictionary 列舉順序影響
            for (int i = Rarity.Order.Length - 1; i >= 0; i--)
            {
                double p;
                if (!banner.Rates.TryGetValue(Rarity.Order[i], out p) || p <= 0) continue;
                acc += p;
                last = Rarity.Order[i];
                if (r < acc) return last;
            }
            return last; // 浮點誤差導致 r 落在 [acc, 1) 時，歸給最低稀有度
        }

        private static string PickCharacter(BannerDef banner, string rarity, IRandom rng, out bool isPickup)
        {
            List<string> pool;
            if (!banner.Pool.TryGetValue(rarity, out pool) || pool.Count == 0)
                throw new InvalidOperationException("banner " + banner.Id + " has empty pool for " + rarity);

            List<string> pickups = banner.Pickup != null ? pool.Where(c => banner.Pickup.Chars.Contains(c)).ToList() : new List<string>();
            if (pickups.Count > 0)
            {
                List<string> others = pool.Where(c => !pickups.Contains(c)).ToList();
                if (others.Count == 0 || rng.NextDouble() < banner.Pickup.ShareOfRarity)
                {
                    isPickup = true;
                    return pickups[rng.NextInt(pickups.Count)];
                }
                isPickup = false;
                return others[rng.NextInt(others.Count)];
            }
            isPickup = false;
            return pool[rng.NextInt(pool.Count)];
        }

        /// <summary>
        /// 公開用的「每個角色機率」（不含天井和十連保底的修正），供機率詳情頁顯示。
        /// 由主資料計算，不手寫，所以和實際抽取邏輯一致。
        /// </summary>
        public static Dictionary<string, double> DisplayRates(BannerDef banner)
        {
            var rates = new Dictionary<string, double>();
            foreach (KeyValuePair<string, double> kv in banner.Rates)
            {
                List<string> pool;
                if (!banner.Pool.TryGetValue(kv.Key, out pool) || pool.Count == 0) continue;
                List<string> pickups = banner.Pickup != null ? pool.Where(c => banner.Pickup.Chars.Contains(c)).ToList() : new List<string>();
                List<string> others = pool.Where(c => !pickups.Contains(c)).ToList();
                double pickupShare = pickups.Count == 0 ? 0 : (others.Count == 0 ? 1 : banner.Pickup.ShareOfRarity);
                foreach (string c in pickups) rates[c] = kv.Value * pickupShare / pickups.Count;
                foreach (string c in others) rates[c] = kv.Value * (1 - pickupShare) / others.Count;
            }
            return rates;
        }
    }
}
