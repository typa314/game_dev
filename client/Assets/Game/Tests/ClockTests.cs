using System;
using System.IO;
using System.Text.RegularExpressions;
using Game.Domain;
using NUnit.Framework;

namespace Game.Tests
{
    [TestFixture]
    public sealed class ClockTests
    {
        [Test]
        public void D6_1_ClockControlsTimeDeterministically()
        {
            var startUtc = new DateTime(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc);
            var clock = new FakeClock(startUtc);

            Assert.AreEqual(startUtc, clock.UtcNow);

            clock.Advance(TimeSpan.FromHours(2));
            Assert.AreEqual(startUtc.AddHours(2), clock.UtcNow);
        }

        [Test]
        public void D6_1_NoDirectDateTimeNowOrUtcNowInDomainAndCoreExceptSystemClock()
        {
            // Scan all .cs files in Domain and Core
            string baseDir = TestPaths.AssetsGameDir();
            string domainDir = Path.Combine(baseDir, "Domain");
            string coreDir = Path.Combine(baseDir, "Core");

            var rx = new Regex(@"DateTime\.(Now|UtcNow)", RegexOptions.Compiled);

            void CheckDir(string dir)
            {
                if (!Directory.Exists(dir)) return;
                string[] files = Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories);
                foreach (string file in files)
                {
                    string fileName = Path.GetFileName(file);
                    // SystemClock is the designated implementation allowed to read system clock
                    if (string.Equals(fileName, "Common.cs", StringComparison.OrdinalIgnoreCase))
                        continue;

                    string content = File.ReadAllText(file);
                    var matches = rx.Matches(content);
                    Assert.AreEqual(0, matches.Count,
                        $"Found forbidden DateTime.Now/UtcNow in '{file}'. All time queries in Domain/Core must go through IClock.");
                }
            }

            CheckDir(domainDir);
            CheckDir(coreDir);
        }
    }
}
