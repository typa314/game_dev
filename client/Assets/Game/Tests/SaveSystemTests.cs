using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Game.Core.Persistence;
using Game.Domain;
using Game.Domain.Player;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>
    /// B 區塊：存檔持久化與版本遷移測試
    /// 涵蓋查核點 B1-1～B1-6、B2-1～B2-5、B3-1～B3-3、B4-1～B4-2
    /// </summary>
    [TestFixture]
    public class SaveSystemTests
    {
        private string _testDir;

        [SetUp]
        public void SetUp()
        {
            // 每次測試使用獨立暫存目錄
            _testDir = Path.Combine(Path.GetTempPath(), "SaveStoreTest_" + Guid.NewGuid().ToString("N"));
            SaveCodec.Migrations.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            SaveCodec.Migrations.Clear();
            if (Directory.Exists(_testDir))
            {
                try { Directory.Delete(_testDir, true); }
                catch { /* 測試目錄清理容錯 */ }
            }
        }

        private static PlayerState CreateSamplePlayer()
        {
            var p = new PlayerState();
            p.Profile.CreatedAtUtc = "2026-10-14T12:00:00Z";
            p.Profile.Locale = "zh-TW";
            p.Profile.RatingOptIn = Rating.R18;
            p.Profile.AgeConfirmedAtUtc = "2026-10-14T12:00:00Z";

            p.Items["gold"] = 100000;
            p.Items["diamond"] = 3000;
            p.Items["exp_s"] = 50;

            var c = new CharacterState
            {
                Level = 25,
                Exp = 5400,
                Awaken = 2,
                Affection = 80,
                PortraitStage = "S2",
                Costume = "cos_char01_academy",
                ObtainedAtUtc = "2026-10-14T12:00:00Z",
            };
            c.UnlockedPortraitStages.Add("S1");
            c.UnlockedPortraitStages.Add("S2");
            p.Characters["char01"] = c;

            p.Costumes.Add("cos_char01_default");
            p.Costumes.Add("cos_char01_academy");
            p.Scenes.Add("scene_lobby_default");
            p.Scenes.Add("scene_night");

            p.Lobby.Scene = "scene_lobby_default";
            p.Lobby.Character = "char01";
            p.Lobby.Costume = "cos_char01_academy";

            p.Team.Add("char01");

            p.Stages["st_1_01"] = new StageProgress { Unlocked = true, Cleared = true, Stars = 3, ClearCount = 5 };
            p.Stages["st_1_02"] = new StageProgress { Unlocked = true, Cleared = false, Stars = 0, ClearCount = 0 };

            p.Gacha.TotalPulls = 30;
            p.Gacha.Pity["standard"] = 30;
            p.Gacha.History.Add(new GachaHistoryEntry
            {
                Banner = "standard",
                Char = "char01",
                Rarity = "SSR",
                AtUtc = "2026-10-14T12:00:00Z",
            });

            p.Encounter.Stamina = 5;
            p.Encounter.StaminaUpdatedAtUtc = "2026-10-14T12:00:00Z";
            p.Encounter.UnlockedSpots.Add("spot_park");

            p.Story.Flags["route_a_cleared"] = 1.0;
            p.Story.ReadNodes.Add("ch01_node01");
            p.Endings["ending_true"] = "2026-10-14T12:00:00Z";
            p.Gallery.Add("cg_char01_01");

            p.Achievements["ach_gacha_10"] = new AchievementProgress
            {
                Progress = 10,
                UnlockedAtUtc = "2026-10-14T12:00:00Z",
                Claimed = true,
            };

            p.Settings.Bgm = 0.7f;
            p.Settings.Se = 0.9f;
            p.Settings.Voice = 1.0f;
            p.Settings.TextSpeed = 50;
            p.Settings.UseLive2d = true;

            return p;
        }

        #region B1 存檔封裝與 Hash

        [Test]
        public void B1_1_EncodeDecode_Roundtrip_IdenticalSerialization()
        {
            // B1-1：Encode 後 Decode，PlayerState 的所有欄位都相同（序列化後的字串完全相同）
            PlayerState original = CreateSamplePlayer();
            DateTime now = new DateTime(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc);

            string encodedJson = SaveCodec.Encode(original, "0.4.1", now);
            PlayerState decoded = SaveCodec.Decode(encodedJson);

            string originalJson = JsonConvert.SerializeObject(original, SaveCodec.Settings);
            string decodedJson = JsonConvert.SerializeObject(decoded, SaveCodec.Settings);

            Assert.AreEqual(originalJson, decodedJson, "Encode 後 Decode 的序列化結果必須完全相同");
        }

        [Test]
        public void B1_2_CorruptedPayload_ThrowsInvalidDataException()
        {
            // B1-2：修改 payload 的 1 個字元，Decode 會丟出 InvalidDataException("...hash mismatch...")
            PlayerState player = CreateSamplePlayer();
            string json = SaveCodec.Encode(player, "0.4.1", DateTime.UtcNow);

            // 篡改 payload 內容（例如將 "diamond": 3000 改為 "diamond": 3001）
            Assert.IsTrue(json.Contains("\"diamond\": 3000"));
            string corrupted = json.Replace("\"diamond\": 3000", "\"diamond\": 3001");

            var ex = Assert.Throws<InvalidDataException>(() => SaveCodec.Decode(corrupted));
            StringAssert.Contains("hash mismatch", ex.Message);
        }

        [Test]
        public void B1_3_InvalidJson_OrNonGsave_OrMissingPayload_ThrowsInvalidDataException()
        {
            // B1-3：不是 JSON、format 不是 gsave、沒有 payload 時，都會丟出 InvalidDataException
            // 1. 不是 JSON
            var ex1 = Assert.Throws<InvalidDataException>(() => SaveCodec.Decode("not a valid json content"));
            StringAssert.Contains("not valid json", ex1.Message);

            // 2. format 不是 gsave
            string nonGsave = JsonConvert.SerializeObject(new
            {
                Format = "other_format",
                SchemaVersion = 1,
                GameVersion = "0.4.1",
                SavedAtUtc = "2026-10-14T12:00:00Z",
                Payload = new { },
                Hash = "some_hash"
            });
            var ex2 = Assert.Throws<InvalidDataException>(() => SaveCodec.Decode(nonGsave));
            StringAssert.Contains("not a save file", ex2.Message);

            // 3. 沒有 payload
            string missingPayload = JsonConvert.SerializeObject(new
            {
                Format = "gsave",
                SchemaVersion = 1,
                GameVersion = "0.4.1",
                SavedAtUtc = "2026-10-14T12:00:00Z",
                Hash = "some_hash"
            });
            var ex3 = Assert.Throws<InvalidDataException>(() => SaveCodec.Decode(missingPayload));
            StringAssert.Contains("not a save file", ex3.Message);
        }

        [Test]
        public void B1_4_NewerSchemaVersion_Rejected()
        {
            // B1-4：存檔的 schemaVersion 比程式新時，拒絕讀取
            PlayerState player = CreateSamplePlayer();
            string json = SaveCodec.Encode(player, "0.4.1", DateTime.UtcNow);

            JObject env = JObject.Parse(json);
            env["SchemaVersion"] = PlayerState.CurrentSchemaVersion + 1;
            // 重新計算 Hash 使其通過 hash check，專門驗證 schema version
            env["Hash"] = SaveCodec.ComputeHash((JObject)env["Payload"]);

            var ex = Assert.Throws<InvalidDataException>(() => SaveCodec.Decode(env.ToString()));
            StringAssert.Contains("newer game version", ex.Message);
        }

        [Test]
        public void B1_5_IndentationChange_DoesNotAffectHash()
        {
            // B1-5：只改變 JSON 的縮排，不影響 hash 驗證
            PlayerState player = CreateSamplePlayer();
            string indentedJson = SaveCodec.Encode(player, "0.4.1", DateTime.UtcNow);

            // 轉成無縮排格式
            JObject parsed = JObject.Parse(indentedJson);
            string unindentedJson = parsed.ToString(Formatting.None);

            // 依然能成功 Decode，不丟出 hash mismatch
            PlayerState decoded = SaveCodec.Decode(unindentedJson);
            Assert.IsNotNull(decoded);
            Assert.AreEqual(player.Profile.CreatedAtUtc, decoded.Profile.CreatedAtUtc);
        }

        [Test]
        public void B1_6_CollectionsAndEnums_RoundtripSuccessfully()
        {
            // B1-6：HashSet、Dictionary、enum（以字串儲存）都能正確來回轉換
            PlayerState original = new PlayerState();
            original.Profile.RatingOptIn = Rating.R18;
            original.Costumes.Add("cos_alpha");
            original.Costumes.Add("cos_beta");
            original.Items["item_gold"] = 123456789L;

            string json = SaveCodec.Encode(original, "1.0.0", DateTime.UtcNow);
            PlayerState decoded = SaveCodec.Decode(json);

            // enum 儲存為字串並成功轉回
            Assert.AreEqual(Rating.R18, decoded.Profile.RatingOptIn);
            StringAssert.Contains("\"RatingOptIn\": \"R18\"", json);

            // HashSet 正確復原
            Assert.AreEqual(2, decoded.Costumes.Count);
            Assert.IsTrue(decoded.Costumes.Contains("cos_alpha"));
            Assert.IsTrue(decoded.Costumes.Contains("cos_beta"));

            // Dictionary 正確復原
            Assert.AreEqual(123456789L, decoded.Items["item_gold"]);
        }

        #endregion

        #region B2 原子寫入與輪替備份

        [Test]
        public void B2_1_FiveWrites_MaintainsMainAndThreeBackups()
        {
            // B2-1：連續寫入 5 次後，存在 main、bak1、bak2、bak3，內容分別是第 5、4、3、2 次寫入
            var store = new FileSaveStore(_testDir);
            string filename = "profile.json";

            for (int i = 1; i <= 5; i++)
            {
                store.Write(filename, $"content_{i}");
            }

            string mainPath = store.PathOf(filename);
            string bak1Path = mainPath + ".bak1";
            string bak2Path = mainPath + ".bak2";
            string bak3Path = mainPath + ".bak3";
            string bak4Path = mainPath + ".bak4";

            Assert.IsTrue(File.Exists(mainPath), "main 應存在");
            Assert.IsTrue(File.Exists(bak1Path), "bak1 應存在");
            Assert.IsTrue(File.Exists(bak2Path), "bak2 應存在");
            Assert.IsTrue(File.Exists(bak3Path), "bak3 應存在");
            Assert.IsFalse(File.Exists(bak4Path), "bak4 不應存在（備份上限為 3）");

            Assert.AreEqual("content_5", File.ReadAllText(mainPath));
            Assert.AreEqual("content_4", File.ReadAllText(bak1Path));
            Assert.AreEqual("content_3", File.ReadAllText(bak2Path));
            Assert.AreEqual("content_2", File.ReadAllText(bak3Path));
        }

        [Test]
        public void B2_2_NoTmpFilesRemaining()
        {
            // B2-2：寫入完成後不會留下 .tmp 檔
            var store = new FileSaveStore(_testDir);
            for (int i = 1; i <= 3; i++)
            {
                store.Write("profile.json", $"data_{i}");
            }

            string[] tmpFiles = Directory.GetFiles(_testDir, "*.tmp");
            Assert.AreEqual(0, tmpFiles.Length, "寫入完成後不應殘留任何 .tmp 檔案");
        }

        [Test]
        public void B2_3_MainCorrupted_ReadWithFallbackReturnsBak1_AndLogsError()
        {
            // B2-3：主檔損壞時，ReadWithFallback 回傳 bak1 的內容，並在 errors 中記錄主檔的錯誤原因
            var store = new FileSaveStore(_testDir);
            PlayerState p1 = CreateSamplePlayer();
            p1.Profile.Locale = "en-US";
            PlayerState p2 = CreateSamplePlayer();
            p2.Profile.Locale = "ja-JP";

            string json1 = SaveCodec.Encode(p1, "1.0", DateTime.UtcNow);
            string json2 = SaveCodec.Encode(p2, "1.0", DateTime.UtcNow);

            // 寫入兩次：main 為 json2，bak1 為 json1
            store.Write("profile.json", json1);
            store.Write("profile.json", json2);

            // 故意破壞 main
            File.WriteAllText(store.PathOf("profile.json"), "corrupted main file content");

            var errors = new List<string>();
            PlayerState restored = store.ReadWithFallback("profile.json", SaveCodec.Decode, errors);

            Assert.IsNotNull(restored, "應成功回退讀取 bak1");
            Assert.AreEqual("en-US", restored.Profile.Locale, "還原之內容應為 bak1 (p1)");
            Assert.AreEqual(1, errors.Count, "errors 中應記錄主檔之錯誤");
            StringAssert.Contains("profile.json", errors[0]);
        }

        [Test]
        public void B2_4_AllCorrupted_ReturnsNull_AndFilesAreUntouched()
        {
            // B2-4：所有檔案都損壞時回傳 null，而且所有檔案的內容和修改時間都沒有改變
            var store = new FileSaveStore(_testDir);
            string main = store.PathOf("profile.json");
            string bak1 = main + ".bak1";
            string bak2 = main + ".bak2";
            string bak3 = main + ".bak3";

            File.WriteAllText(main, "broken main");
            File.WriteAllText(bak1, "broken bak1");
            File.WriteAllText(bak2, "broken bak2");
            File.WriteAllText(bak3, "broken bak3");

            // 記錄初始內容與時間
            string[] paths = { main, bak1, bak2, bak3 };
            var originalContents = new Dictionary<string, string>();
            var originalTimes = new Dictionary<string, DateTime>();

            foreach (string p in paths)
            {
                originalContents[p] = File.ReadAllText(p);
                originalTimes[p] = File.GetLastWriteTimeUtc(p);
            }

            // 稍作延遲確保若有修改，時間戳有機會改變
            Thread.Sleep(50);

            var errors = new List<string>();
            PlayerState result = store.ReadWithFallback("profile.json", SaveCodec.Decode, errors);

            Assert.IsNull(result, "所有檔案損壞時應回傳 null");
            Assert.AreEqual(4, errors.Count, "4 個檔案都應記錄錯誤");

            // 驗證內容與時間完全沒有改變
            foreach (string p in paths)
            {
                Assert.AreEqual(originalContents[p], File.ReadAllText(p), $"檔案 {p} 內容不可被覆寫或更動");
                Assert.AreEqual(originalTimes[p], File.GetLastWriteTimeUtc(p), $"檔案 {p} 修改時間不可改變");
            }
        }

        [Test]
        public void B2_5_DirectoryCreatedAutomatically()
        {
            // B2-5：存檔資料夾不存在時會自動建立
            string deepSubdir = Path.Combine(_testDir, "nested", "saves", "dir");
            Assert.IsFalse(Directory.Exists(deepSubdir));

            var store = new FileSaveStore(deepSubdir);
            Assert.IsTrue(Directory.Exists(deepSubdir), "實例化 FileSaveStore 後目錄應自動建立");
        }

        #endregion

        #region B3 版本遷移

        [Test]
        public void B3_1_MigrateV0ToV1_FillsDefaults_AndPreservesExistingData()
        {
            // B3-1：v0 範例存檔遷移後，新欄位使用預設值，原有資料保留
            string fixturePath = Path.Combine(TestPaths.SaveFixturesDir(), "profile_v0.json");
            Assert.IsTrue(File.Exists(fixturePath), "profile_v0.json 必須存在");
            string v0Json = File.ReadAllText(fixturePath);

            // 註冊 v0 -> v1 的遷移函式
            SaveCodec.Migrations[0] = payload =>
            {
                var profile = (JObject)payload["Profile"];
                if (profile != null && profile["Locale"] == null)
                {
                    profile["Locale"] = "zh-TW";
                }
                return payload;
            };

            PlayerState migrated = SaveCodec.Decode(v0Json);

            Assert.IsNotNull(migrated);
            Assert.AreEqual(1, migrated.SchemaVersion, "遷移後版本應為 CurrentSchemaVersion = 1");

            // 原有資料保留
            Assert.AreEqual(50000, migrated.Items["gold"]);
            Assert.AreEqual(10, migrated.Characters["char01"].Level);
            Assert.AreEqual("2026-10-14T12:00:00Z", migrated.Profile.CreatedAtUtc);

            // 新欄位獲得預設值
            Assert.AreEqual("zh-TW", migrated.Profile.Locale);
            Assert.AreEqual(0.8f, migrated.Settings.Bgm);
            Assert.IsTrue(migrated.Settings.UseLive2d);
        }

        [Test]
        public void B3_2_MissingMigration_ThrowsException()
        {
            // B3-2：缺少某一版的遷移函式時，丟出「no migration from schema N」
            string fixturePath = Path.Combine(TestPaths.SaveFixturesDir(), "profile_v0.json");
            string v0Json = File.ReadAllText(fixturePath);

            // 未註冊 Migrations[0]
            SaveCodec.Migrations.Clear();

            var ex = Assert.Throws<InvalidDataException>(() => SaveCodec.Decode(v0Json));
            StringAssert.Contains("no migration from schema 0", ex.Message);
        }

        [Test]
        public void B3_3_DocumentedInReadme()
        {
            // B3-3：遷移規則寫進 README：「改 PlayerState 欄位，就要提高 CurrentSchemaVersion，並新增 Migration 和範例存檔」
            string readmePath = Path.Combine(new DirectoryInfo(TestPaths.MasterDataDir()).Parent.Parent.FullName, "client", "README.md");
            Assert.IsTrue(File.Exists(readmePath));
            string content = File.ReadAllText(readmePath);
            StringAssert.Contains("CurrentSchemaVersion", content);
            StringAssert.Contains("Migration", content);
        }

        #endregion

        #region B4 劇情存檔欄位

        [Test]
        public void B4_1_StorySlot_Variables_Staging_Memo_Roundtrip_TypesPreserved()
        {
            // B4-1：StorySlot 的變數（float、string、bool）、Staging、Memo 都能來回轉換，型別不變
            var slot = new StorySlot
            {
                SlotId = "slot_001",
                SavedAtUtc = "2026-10-14T12:00:00Z",
                MasterDataVersion = 1,
                ScriptVersion = "1.0.0",
                Node = "chapter1_choice",
                LineId = "line_042",
                PreviewLineId = "勇者選擇了握住少女的手。",
                Memo = "第一章命運分歧點存檔",
            };

            slot.Variables["affection"] = 15.5f;           // float
            slot.Variables["player_name"] = "Alice";       // string
            slot.Variables["is_flag_set"] = true;          // bool

            slot.Staging.Background = "bg_academy_hall";
            slot.Staging.BackgroundVariant = "evening";
            slot.Staging.Bgm = "bgm_theme_01";
            slot.Staging.Characters.Add(new StagedCharacter
            {
                Char = "char01",
                Position = "center",
                Expression = "smile",
                ForcedCostume = "cos_char01_academy"
            });

            string json = SaveCodec.EncodeStorySlot(slot);
            StorySlot decoded = SaveCodec.DecodeStorySlot(json);

            Assert.IsNotNull(decoded);
            Assert.AreEqual("slot_001", decoded.SlotId);
            Assert.AreEqual("第一章命運分歧點存檔", decoded.Memo);
            Assert.AreEqual("bg_academy_hall", decoded.Staging.Background);
            Assert.AreEqual("evening", decoded.Staging.BackgroundVariant);
            Assert.AreEqual("bgm_theme_01", decoded.Staging.Bgm);
            Assert.AreEqual(1, decoded.Staging.Characters.Count);
            Assert.AreEqual("smile", decoded.Staging.Characters[0].Expression);

            // 關鍵查核：變數型別必須保持不變
            Assert.IsTrue(decoded.Variables.ContainsKey("affection"));
            Assert.IsInstanceOf<float>(decoded.Variables["affection"], "浮點數變數反序列化後必須依然為 float 型別");
            Assert.AreEqual(15.5f, (float)decoded.Variables["affection"]);

            Assert.IsTrue(decoded.Variables.ContainsKey("player_name"));
            Assert.IsInstanceOf<string>(decoded.Variables["player_name"], "字串變數反序列化後必須依然為 string 型別");
            Assert.AreEqual("Alice", (string)decoded.Variables["player_name"]);

            Assert.IsTrue(decoded.Variables.ContainsKey("is_flag_set"));
            Assert.IsInstanceOf<bool>(decoded.Variables["is_flag_set"], "布林值變數反序列化後必須依然為 bool 型別");
            Assert.AreEqual(true, (bool)decoded.Variables["is_flag_set"]);
        }

        [Test]
        public void B4_2_SaveSlotNaming_RulesAndRotation()
        {
            // B4-2：存檔欄位命名規則：手動 slot_001～slot_060、快速 quick_1～quick_3、自動 auto_1～auto_3（輪替）
            // 手動
            Assert.AreEqual("slot_001", SaveSlotNaming.Manual(1));
            Assert.AreEqual("slot_060", SaveSlotNaming.Manual(60));
            Assert.Throws<ArgumentOutOfRangeException>(() => SaveSlotNaming.Manual(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => SaveSlotNaming.Manual(61));

            // 快速
            Assert.AreEqual("quick_1", SaveSlotNaming.Quick(1));
            Assert.AreEqual("quick_3", SaveSlotNaming.Quick(3));
            Assert.Throws<ArgumentOutOfRangeException>(() => SaveSlotNaming.Quick(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => SaveSlotNaming.Quick(4));

            // 自動
            Assert.AreEqual("auto_1", SaveSlotNaming.Auto(1));
            Assert.AreEqual("auto_3", SaveSlotNaming.Auto(3));
            Assert.Throws<ArgumentOutOfRangeException>(() => SaveSlotNaming.Auto(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => SaveSlotNaming.Auto(4));

            // 合法性檢查
            Assert.IsTrue(SaveSlotNaming.IsValidSlotId("slot_001"));
            Assert.IsTrue(SaveSlotNaming.IsValidSlotId("slot_060"));
            Assert.IsTrue(SaveSlotNaming.IsValidSlotId("quick_1"));
            Assert.IsTrue(SaveSlotNaming.IsValidSlotId("quick_3"));
            Assert.IsTrue(SaveSlotNaming.IsValidSlotId("auto_1"));
            Assert.IsTrue(SaveSlotNaming.IsValidSlotId("auto_3"));

            Assert.IsFalse(SaveSlotNaming.IsValidSlotId("slot_000"));
            Assert.IsFalse(SaveSlotNaming.IsValidSlotId("slot_061"));
            Assert.IsFalse(SaveSlotNaming.IsValidSlotId("slot_1"));
            Assert.IsFalse(SaveSlotNaming.IsValidSlotId("quick_0"));
            Assert.IsFalse(SaveSlotNaming.IsValidSlotId("quick_4"));
            Assert.IsFalse(SaveSlotNaming.IsValidSlotId("auto_0"));
            Assert.IsFalse(SaveSlotNaming.IsValidSlotId("auto_4"));
            Assert.IsFalse(SaveSlotNaming.IsValidSlotId("invalid_slot"));
            Assert.IsFalse(SaveSlotNaming.IsValidSlotId(null));

            // 自動輪替 1 -> 2 -> 3 -> 1
            Assert.AreEqual("auto_2", SaveSlotNaming.NextAutoSlot("auto_1"));
            Assert.AreEqual("auto_3", SaveSlotNaming.NextAutoSlot("auto_2"));
            Assert.AreEqual("auto_1", SaveSlotNaming.NextAutoSlot("auto_3"));
            Assert.AreEqual("auto_1", SaveSlotNaming.NextAutoSlot(null));
        }

        #endregion
    }
}
