using System;
using System.IO;

namespace Game.Tests
{
    /// <summary>從目前目錄往上找 data/master（Unity Editor 和 dotnet test 兩邊都適用）。</summary>
    public static class TestPaths
    {
        public static string MasterDataDir()
        {
            foreach (string start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
            {
                DirectoryInfo d = new DirectoryInfo(start);
                while (d != null)
                {
                    string candidate = Path.Combine(d.FullName, "data", "master");
                    if (File.Exists(Path.Combine(candidate, "config.json"))) return candidate;
                    d = d.Parent;
                }
            }
            throw new DirectoryNotFoundException("data/master not found above " + Directory.GetCurrentDirectory());
        }

        public static string GoldenDir()
        {
            foreach (string start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
            {
                DirectoryInfo d = new DirectoryInfo(start);
                while (d != null)
                {
                    string candidate = Path.Combine(d.FullName, "client", "Assets", "Game", "Tests", "Golden");
                    if (Directory.Exists(candidate)) return candidate;
                    string directCandidate = Path.Combine(d.FullName, "Assets", "Game", "Tests", "Golden");
                    if (Directory.Exists(directCandidate)) return directCandidate;
                    d = d.Parent;
                }
            }
            string master = MasterDataDir();
            string repoRoot = new DirectoryInfo(master).Parent.Parent.FullName;
            string target = Path.Combine(repoRoot, "client", "Assets", "Game", "Tests", "Golden");
            if (!Directory.Exists(target)) Directory.CreateDirectory(target);
            return target;
        }

        public static string SaveFixturesDir()
        {
            foreach (string start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
            {
                DirectoryInfo d = new DirectoryInfo(start);
                while (d != null)
                {
                    string candidate = Path.Combine(d.FullName, "client", "Assets", "Game", "Tests", "SaveFixtures");
                    if (Directory.Exists(candidate)) return candidate;
                    string directCandidate = Path.Combine(d.FullName, "Assets", "Game", "Tests", "SaveFixtures");
                    if (Directory.Exists(directCandidate)) return directCandidate;
                    d = d.Parent;
                }
            }
            string master = MasterDataDir();
            string repoRoot = new DirectoryInfo(master).Parent.Parent.FullName;
            string target = Path.Combine(repoRoot, "client", "Assets", "Game", "Tests", "SaveFixtures");
            if (!Directory.Exists(target)) Directory.CreateDirectory(target);
            return target;
        }

        public static string RepoRootDir()
        {
            string master = MasterDataDir();
            return new DirectoryInfo(master).Parent.Parent.FullName;
        }

        public static string DataDir()
        {
            string master = MasterDataDir();
            return new DirectoryInfo(master).Parent.FullName;
        }

        public static string AssetsGameDir()
        {
            return Path.Combine(RepoRootDir(), "client", "Assets", "Game");
        }
    }

    public sealed class FakeClock : Game.Domain.IClock
    {
        public DateTime UtcNow { get; set; }
        public DateTime LocalNow => UtcNow.ToLocalTime();

        public FakeClock(DateTime utcNow)
        {
            UtcNow = utcNow;
        }

        public void Advance(TimeSpan duration)
        {
            UtcNow = UtcNow.Add(duration);
        }
    }
}
