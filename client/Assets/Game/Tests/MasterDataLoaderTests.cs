using System;
using System.IO;
using Game.Core;
using Game.Domain.MasterData;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>
    /// 任務 A2：主資料載入器（MasterDataLoader.cs）與重複 ID 檢查單元測試
    /// 涵蓋查核點 A2-3, A2-4
    /// </summary>
    [TestFixture]
    public class MasterDataLoaderTests
    {
        private static string CreateTempMasterDataDir()
        {
            string srcDir = TestPaths.MasterDataDir();
            string tempDir = Path.Combine(Path.GetTempPath(), "game_a2_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            foreach (string file in Directory.GetFiles(srcDir, "*.json"))
            {
                string destFile = Path.Combine(tempDir, Path.GetFileName(file));
                File.Copy(file, destFile, true);
            }
            return tempDir;
        }

        // -------------------------------------------------------------
        // A2-3a: 把 data/master 所有 *.json 複製到暫存資料夾，將 characters.json 中第一個 "rarity" 改成 "rarty"。
        // LoadFromDirectory 要丟出 System.IO.InvalidDataException，而且 Message 包含 characters.json。
        // -------------------------------------------------------------
        [Test]
        public void A2_3a_LoadFromDirectory_CorruptedJsonField_ThrowsInvalidDataException()
        {
            string tempDir = CreateTempMasterDataDir();
            try
            {
                string charJsonPath = Path.Combine(tempDir, "characters.json");
                string text = File.ReadAllText(charJsonPath);
                int idx = text.IndexOf("\"rarity\"", StringComparison.Ordinal);
                Assert.IsTrue(idx >= 0, "characters.json 中必須包含 \"rarity\"");
                string modified = text.Substring(0, idx) + "\"rarty\"" + text.Substring(idx + "\"rarity\"".Length);
                File.WriteAllText(charJsonPath, modified);

                InvalidDataException ex = Assert.Throws<InvalidDataException>(() =>
                {
                    MasterDataLoader.LoadFromDirectory(tempDir);
                });
                Assert.IsTrue(ex.Message.Contains("characters.json"), "例外訊息必須包含 'characters.json'，實際訊息: " + ex.Message);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
        }

        // -------------------------------------------------------------
        // A2-3b: 同樣的暫存資料夾，但刪除 config.json：丟出 FileNotFoundException。
        // -------------------------------------------------------------
        [Test]
        public void A2_3b_LoadFromDirectory_MissingConfigFile_ThrowsFileNotFoundException()
        {
            string tempDir = CreateTempMasterDataDir();
            try
            {
                string configPath = Path.Combine(tempDir, "config.json");
                File.Delete(configPath);

                Assert.Throws<FileNotFoundException>(() =>
                {
                    MasterDataLoader.LoadFromDirectory(tempDir);
                });
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
        }

        // -------------------------------------------------------------
        // A2-3c（對照組）：直接複製、不做修改的暫存資料夾可以正常載入，Characters.Count 等於 5。
        // -------------------------------------------------------------
        [Test]
        public void A2_3c_LoadFromDirectory_UnmodifiedTempDir_LoadsSuccessfully()
        {
            string tempDir = CreateTempMasterDataDir();
            try
            {
                MasterDatabase db = MasterDataLoader.LoadFromDirectory(tempDir);
                Assert.IsNotNull(db);
                Assert.AreEqual(5, db.Characters.Count);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
        }

        // -------------------------------------------------------------
        // A2-4a: 呼叫 MasterDatabase.Build，傳入兩個 Id = "dup" 的 CharacterDef（其他參數傳 null）：丟出 ArgumentException。
        // -------------------------------------------------------------
        [Test]
        public void A2_4a_Build_DuplicateCharacterId_ThrowsArgumentException()
        {
            CharacterDef[] characters = new CharacterDef[]
            {
                new CharacterDef { Id = "dup" },
                new CharacterDef { Id = "dup" }
            };

            Assert.Catch<ArgumentException>(() =>
            {
                MasterDatabase.Build(
                    null,
                    characters,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null);
            });
        }

        // -------------------------------------------------------------
        // A2-4b: 同樣檢查兩個 Id = "dup" 的 StageDef。
        // -------------------------------------------------------------
        [Test]
        public void A2_4b_Build_DuplicateStageId_ThrowsArgumentException()
        {
            StageDef[] stages = new StageDef[]
            {
                new StageDef { Id = "dup" },
                new StageDef { Id = "dup" }
            };

            Assert.Catch<ArgumentException>(() =>
            {
                MasterDatabase.Build(
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    stages,
                    null,
                    null,
                    null,
                    null);
            });
        }

        // -------------------------------------------------------------
        // A2-4c（對照組）：所有參數都傳 null 時不會丟出例外，Characters.Count == 0，Config 不是 null。
        // -------------------------------------------------------------
        [Test]
        public void A2_4c_Build_AllNullArguments_DoesNotThrow()
        {
            MasterDatabase db = null;
            Assert.DoesNotThrow(() =>
            {
                db = MasterDatabase.Build(
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null);
            });

            Assert.IsNotNull(db);
            Assert.IsNotNull(db.Config);
            Assert.AreEqual(0, db.Characters.Count);
        }
    }
}
