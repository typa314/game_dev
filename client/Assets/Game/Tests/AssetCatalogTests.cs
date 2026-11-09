using System.IO;
using Game.Core;
using Game.Core.Assets;
using Game.Domain;
using Game.Domain.MasterData;
using NUnit.Framework;

namespace Game.Tests
{
    [TestFixture]
    public sealed class AssetCatalogTests
    {
        private MasterDatabase _master;

        [SetUp]
        public void SetUp()
        {
            _master = MasterDataLoader.LoadFromDirectory(TestPaths.MasterDataDir());
        }

        [Test]
        public void D3_1_GPolicyResolvingR18KeyReturnsNotAllowedAndPlaceholder()
        {
            var catalog = new AssetCatalog();
            catalog.LoadFromCsv(@"key,source,rating,status,note
cg/cg_claire_secret,sample/anime/tier3_climax_poses/cg01.png,R18,draft,
char/claire/stand,sample/anime/characters_roster/claire.png,G,draft,
");

            var gPolicy = new RatingPolicy(Rating.G, Rating.G);
            var resR18 = catalog.Resolve("cg/cg_claire_secret", gPolicy);
            Assert.IsFalse(resR18.Allowed);
            Assert.IsTrue(resR18.IsPlaceholder);
            Assert.AreEqual("RatingDisallowed", resR18.Reason);

            var resG = catalog.Resolve("char/claire/stand", gPolicy);
            Assert.IsTrue(resG.Allowed);
            Assert.IsFalse(resG.IsPlaceholder);

            // R18 policy allows R18 key
            var r18Policy = new RatingPolicy(Rating.R18, Rating.R18);
            var resR18UnderR18 = catalog.Resolve("cg/cg_claire_secret", r18Policy);
            Assert.IsTrue(resR18UnderR18.Allowed);
        }

        [Test]
        public void D3_2_FolderRatingLowerBoundValidation()
        {
            var catalog = new AssetCatalog();
            // Negative control: tier2, tier3, tier4 marked as G or R15 must fail
            catalog.LoadFromCsv(@"key,source,rating,status,note
cg/bad_tier2,sample/anime/tier2_tease/pic1.png,G,draft,
cg/bad_tier3,sample/anime/tier3_climax_poses/pic2.png,R15,draft,
cg/bad_tier4,sample/anime/tier4_aftermath/pic3.png,G,draft,
cg/good_tier3,sample/anime/tier3_climax_poses/pic4.png,R18,draft,
");

            var result = AssetManifestValidator.Validate(catalog);
            Assert.IsFalse(result.IsValid);
            Assert.AreEqual(3, result.Errors.Count);
            Assert.IsTrue(result.Errors.Exists(e => e.Contains("bad_tier2") && e.Contains("ERR_RATING_BELOW_FOLDER_MINIMUM")));
            Assert.IsTrue(result.Errors.Exists(e => e.Contains("bad_tier3") && e.Contains("ERR_RATING_BELOW_FOLDER_MINIMUM")));
            Assert.IsTrue(result.Errors.Exists(e => e.Contains("bad_tier4") && e.Contains("ERR_RATING_BELOW_FOLDER_MINIMUM")));
        }

        [Test]
        public void D3_3_MasterDataArtKeyCheckListsWarningsWithoutFailing()
        {
            var catalog = new AssetCatalog();
            // Empty catalog with real master data -> should warn for missing keys, not fail
            var result = AssetManifestValidator.Validate(catalog, master: _master, isReleaseMode: false);

            Assert.IsTrue(result.IsValid, "Missing art keys should be warnings, not errors.");
            Assert.IsTrue(result.Warnings.Count > 0);
            Assert.IsTrue(result.Warnings.Exists(w => w.Contains("WARN_MISSING_MANIFEST_KEY")));
        }

        [Test]
        public void D3_4_StatusCheckFailsOnPlaceholderInReleaseMode()
        {
            var catalog = new AssetCatalog();
            catalog.LoadFromCsv(@"key,source,rating,status,note
art/stand,sample/stand.png,G,placeholder,未完成佔位符
art/bg,sample/bg.png,G,draft,草稿
art/icon,sample/icon.png,G,final,正式產物
");

            // Non-release mode: valid
            var devResult = AssetManifestValidator.Validate(catalog, isReleaseMode: false);
            Assert.IsTrue(devResult.IsValid);

            // Release mode: placeholder fails, draft warns
            var relResult = AssetManifestValidator.Validate(catalog, isReleaseMode: true);
            Assert.IsFalse(relResult.IsValid);
            Assert.IsTrue(relResult.Errors.Exists(e => e.Contains("ERR_PLACEHOLDER_IN_RELEASE") && e.Contains("art/stand")));
            Assert.IsTrue(relResult.Warnings.Exists(w => w.Contains("WARN_DRAFT_IN_RELEASE") && w.Contains("art/bg")));
        }

        [Test]
        public void D3_DiskAssetManifestLoadsAndValidatesCleanly()
        {
            string manifestPath = Path.Combine(TestPaths.DataDir(), "asset_manifest.csv");
            if (File.Exists(manifestPath))
            {
                var catalog = new AssetCatalog();
                catalog.LoadFromFile(manifestPath);
                Assert.IsTrue(catalog.Entries.Count > 0);

                var result = AssetManifestValidator.Validate(catalog, master: _master, isReleaseMode: false);
                Assert.IsTrue(result.IsValid, $"Disk manifest should be valid. Errors: {string.Join("; ", result.Errors)}");
            }
        }

        [Test]
        public void OPT1_2_Catalog_ResolvingR18UnderGPolicy_DisallowsAndReportsPlaceholder()
        {
            var catalog = new AssetCatalog();
            catalog.LoadFromCsv(@"key,source,rating,status,note
cg/cg_char01_t3_01,sample/anime/tier3_climax_poses/anime_tier3_char01_claire_pony.png,R18,draft,克萊兒高潮CG
char/char01_claire/portrait/ps_char01_s1/stand,sample/anime/characters_roster/anime_char01_claire_tsundere_stand_fullbody.png,G,draft,
");

            var gPolicy = new RatingPolicy(Rating.G, Rating.G);
            var resR18 = catalog.Resolve("cg/cg_char01_t3_01", gPolicy);
            Assert.IsFalse(resR18.Allowed, "G 政策下必須禁止 R18 資產");
            Assert.IsTrue(resR18.IsPlaceholder, "禁止的資產應標記為 Placeholder");
            Assert.AreEqual("RatingDisallowed", resR18.Reason);

            var r18Policy = new RatingPolicy(Rating.R18, Rating.R18);
            var resR18Allowed = catalog.Resolve("cg/cg_char01_t3_01", r18Policy);
            Assert.IsTrue(resR18Allowed.Allowed, "R18 政策下應允許 R18 資產");
        }

#if UNITY_2021_1_OR_NEWER
        [Test]
        public void OPT1_2_AssetService_WithCatalog_EnforcesR18Filtering()
        {
            var catalog = new AssetCatalog();
            catalog.LoadFromCsv(@"key,source,rating,status,note
cg/cg_char01_t3_01,sample/anime/tier3_climax_poses/anime_tier3_char01_claire_pony.png,R18,draft,克萊兒高潮CG
");

            var gPolicy = new RatingPolicy(Rating.G, Rating.G);
            var assetService = new Game.Unity.AssetService(gPolicy, catalog);

            var sprite = assetService.LoadSprite("cg/cg_char01_t3_01");
            Assert.IsNotNull(sprite);
            Assert.AreSame(assetService.Placeholder, sprite, "G 政策下載入 R18 資產必須強制回傳 Placeholder");
        }
#endif
    }
}
