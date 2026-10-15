using System;
using System.Collections.Generic;

namespace Game.Domain
{
    /// <summary>內容分級。數值越大越嚴格，比較時用 &lt;=。</summary>
    public enum Rating { G = 0, R15 = 1, R18 = 2 }

    public static class Rarity
    {
        public static readonly string[] Order = { "N", "R", "SR", "SSR", "UR" };

        public static int Rank(string rarity)
        {
            int i = Array.IndexOf(Order, rarity);
            if (i < 0) throw new ArgumentException("unknown rarity: " + rarity);
            return i;
        }

        public static string Next(string rarity)
        {
            switch (rarity)
            {
                case "R": return "SR";
                case "SR": return "SSR";
                case "SSR": return "UR";
                default: return null;
            }
        }

        public static bool IsMax(string rarity) => rarity == "UR";
    }

    /// <summary>命令結果。Error 是給程式判斷的錯誤碼（例如 "insufficient_item"），不是顯示文字。</summary>
    public class Result
    {
        public bool Ok { get; protected set; }
        public string Error { get; protected set; }

        public static Result Success() { return new Result { Ok = true }; }
        public static Result Fail(string error) { return new Result { Ok = false, Error = error }; }
    }

    public sealed class Result<T> : Result
    {
        public T Value { get; private set; }

        public static Result<T> Success(T value) { return new Result<T> { Ok = true, Value = value }; }
        public static new Result<T> Fail(string error) { return new Result<T> { Ok = false, Error = error }; }
    }

    public sealed class ItemStack
    {
        public string Item;
        public long N;

        public ItemStack() { }
        public ItemStack(string item, long n) { Item = item; N = n; }
    }

    public interface IRandom
    {
        /// <summary>[0, maxExclusive)</summary>
        int NextInt(int maxExclusive);
        /// <summary>[0, 1)</summary>
        double NextDouble();
    }

    /// <summary>可指定 seed 的決定性亂數（SplitMix64）。同一個 seed 一定產生同一串數列。</summary>
    public sealed class SplitMix64Random : IRandom
    {
        private ulong _state;

        public SplitMix64Random(ulong seed) { _state = seed; }

        public ulong State { get { return _state; } }

        private ulong Next()
        {
            ulong z = (_state += 0x9E3779B97F4A7C15UL);
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        public double NextDouble()
        {
            return (Next() >> 11) * (1.0 / (1UL << 53));
        }

        public int NextInt(int maxExclusive)
        {
            if (maxExclusive <= 0) throw new ArgumentOutOfRangeException("maxExclusive");
            return (int)(NextDouble() * maxExclusive);
        }
    }

    public interface IClock
    {
        DateTime UtcNow { get; }
        /// <summary>玩家裝置當地時間（巧遇的時段判定用）。</summary>
        DateTime LocalNow { get; }
    }

    public sealed class SystemClock : IClock
    {
        public DateTime UtcNow { get { return DateTime.UtcNow; } }
        public DateTime LocalNow { get { return DateTime.Now; } }
    }

    /// <summary>目前允許顯示的最高分級 = min(建置版本上限, 玩家設定)。</summary>
    public sealed class RatingPolicy
    {
        public Rating BuildMax;
        public Rating PlayerMax;

        public RatingPolicy(Rating buildMax, Rating playerMax)
        {
            BuildMax = buildMax;
            PlayerMax = playerMax;
        }

        public Rating Effective { get { return BuildMax < PlayerMax ? BuildMax : PlayerMax; } }

        public bool Allows(Rating r) { return r <= Effective; }
    }

    internal static class DictExt
    {
        public static TV GetOrNull<TK, TV>(this Dictionary<TK, TV> d, TK key) where TV : class
        {
            TV v;
            return key != null && d != null && d.TryGetValue(key, out v) ? v : null;
        }
    }
}
