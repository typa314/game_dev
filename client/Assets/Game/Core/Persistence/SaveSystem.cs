using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Game.Domain.Player;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Game.Core.Persistence
{
    /// <summary>
    /// 存檔外層封裝（design/11_save_load.md §2）。
    /// hash 只用來偵測檔案損壞，不是防作弊。
    /// </summary>
    public sealed class SaveEnvelope
    {
        public string Format = "gsave";
        public int SchemaVersion;
        public string GameVersion;
        public string SavedAtUtc;
        public JObject Payload;
        public string Hash;
    }

    public static class SaveCodec
    {
        public static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Include,
            DateParseHandling = DateParseHandling.None,
            Converters = { new Newtonsoft.Json.Converters.StringEnumConverter() },
        };

        /// <summary>版本遷移函式：key = 來源版本，把 payload 從 v 升到 v+1。</summary>
        public static readonly Dictionary<int, Func<JObject, JObject>> Migrations = new Dictionary<int, Func<JObject, JObject>>();

        public static string Encode(PlayerState state, string gameVersion, DateTime utcNow)
        {
            JObject payload = JObject.FromObject(state, JsonSerializer.Create(Settings));
            var env = new SaveEnvelope
            {
                SchemaVersion = state.SchemaVersion,
                GameVersion = gameVersion,
                SavedAtUtc = GameCore.Iso(utcNow),
                Payload = payload,
                Hash = HashOf(payload),
            };
            return JsonConvert.SerializeObject(env, Settings);
        }

        /// <summary>解碼並驗證 hash、依序套用遷移。損壞時丟出 InvalidDataException。</summary>
        public static PlayerState Decode(string json)
        {
            SaveEnvelope env;
            try { env = JsonConvert.DeserializeObject<SaveEnvelope>(json, Settings); }
            catch (JsonException e) { throw new InvalidDataException("save is not valid json", e); }
            if (env == null || env.Format != "gsave" || env.Payload == null) throw new InvalidDataException("not a save file");
            if (env.Hash != HashOf(env.Payload)) throw new InvalidDataException("save hash mismatch (corrupted)");
            if (env.SchemaVersion > PlayerState.CurrentSchemaVersion)
                throw new InvalidDataException("save was written by a newer game version");

            JObject payload = env.Payload;
            for (int v = env.SchemaVersion; v < PlayerState.CurrentSchemaVersion; v++)
            {
                Func<JObject, JObject> m;
                if (!Migrations.TryGetValue(v, out m)) throw new InvalidDataException("no migration from schema " + v);
                payload = m(payload);
                payload["SchemaVersion"] = v + 1;
            }
            return payload.ToObject<PlayerState>(JsonSerializer.Create(Settings));
        }

        public static string EncodeStorySlot(StorySlot slot)
        {
            if (slot == null) throw new ArgumentNullException(nameof(slot));
            return JsonConvert.SerializeObject(slot, Settings);
        }

        public static StorySlot DecodeStorySlot(string json)
        {
            if (string.IsNullOrEmpty(json)) throw new InvalidDataException("story slot json is empty");
            try
            {
                var slot = JsonConvert.DeserializeObject<StorySlot>(json, Settings);
                if (slot == null) throw new InvalidDataException("not a story slot");
                return slot;
            }
            catch (JsonException e)
            {
                throw new InvalidDataException("story slot is not valid json", e);
            }
        }

        public static string ComputeHash(JObject payload)
        {
            if (payload == null) return string.Empty;
            // 以不縮排的序列化結果計算，讓縮排格式不影響 hash
            byte[] bytes = Encoding.UTF8.GetBytes(payload.ToString(Formatting.None));
            using (SHA256 sha = SHA256.Create())
            {
                return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
            }
        }

        private static string HashOf(JObject payload) => ComputeHash(payload);
    }

    /// <summary>
    /// 本機檔案存檔：原子寫入（tmp → 取代）＋ 3 份輪替備份。只存在本機，不做任何雲端同步。
    /// </summary>
    public sealed class FileSaveStore
    {
        public const int BackupCount = 3;
        private readonly string _dir;

        public FileSaveStore(string dir)
        {
            _dir = dir;
            Directory.CreateDirectory(dir);
        }

        public string PathOf(string name) { return Path.Combine(_dir, name); }

        public void Write(string name, string content)
        {
            string main = PathOf(name);
            string tmp = main + ".tmp";
            byte[] bytes = new UTF8Encoding(false).GetBytes(content);
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                fs.Write(bytes, 0, bytes.Length);
                fs.Flush(true);
            }
            // 輪替備份：bak2→bak3、bak1→bak2、main→bak1
            for (int i = BackupCount - 1; i >= 1; i--)
            {
                string from = main + ".bak" + i, to = main + ".bak" + (i + 1);
                if (File.Exists(from)) { if (File.Exists(to)) File.Delete(to); File.Move(from, to); }
            }
            if (File.Exists(main)) File.Copy(main, main + ".bak1", true);
            if (File.Exists(main)) File.Replace(tmp, main, null);
            else File.Move(tmp, main);
        }

        /// <summary>
        /// 依序嘗試主檔與備份，回傳第一個能成功解碼的結果。全部失敗回傳 null，
        /// 並在 errors 中列出原因；損壞的檔案保留原樣，不自動覆蓋。
        /// </summary>
        public T ReadWithFallback<T>(string name, Func<string, T> decode, List<string> errors) where T : class
        {
            var candidates = new List<string> { PathOf(name) };
            for (int i = 1; i <= BackupCount; i++) candidates.Add(PathOf(name) + ".bak" + i);
            foreach (string p in candidates)
            {
                if (!File.Exists(p)) continue;
                try { return decode(File.ReadAllText(p, Encoding.UTF8)); }
                catch (Exception e) { if (errors != null) errors.Add(Path.GetFileName(p) + ": " + e.Message); }
            }
            return null;
        }

        public bool Exists(string name) { return File.Exists(PathOf(name)); }
    }

    /// <summary>劇情存檔欄位（design/11 §1）。只保存劇情狀態，讀取時不改變 Profile。</summary>
    public sealed class StorySlot
    {
        public string SlotId;
        public string SavedAtUtc;
        public int MasterDataVersion;
        public string ScriptVersion;
        public string Node;
        public string LineId;
        public string ChapterStartNode;
        [JsonConverter(typeof(StoryVariablesConverter))]
        public Dictionary<string, object> Variables = new Dictionary<string, object>();
        public StoryStaging Staging = new StoryStaging();
        public string PreviewLineId;
        public string Memo;
    }

    public sealed class StoryStaging
    {
        public string Background;
        public string BackgroundVariant;
        public string Bgm;
        public List<StagedCharacter> Characters = new List<StagedCharacter>();
    }

    public sealed class StagedCharacter
    {
        public string Char;
        public string Position;
        public string Expression;
        public string ForcedCostume;
    }

    /// <summary>
    /// 劇情變數 JSON 轉換器（design/17 B4-1）。
    /// 保證 float、string、bool 型別來回轉換不變形。
    /// </summary>
    public sealed class StoryVariablesConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return typeof(Dictionary<string, object>).IsAssignableFrom(objectType);
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            var dict = (Dictionary<string, object>)value;
            writer.WriteStartObject();
            if (dict != null)
            {
                foreach (var kv in dict)
                {
                    writer.WritePropertyName(kv.Key);
                    writer.WriteValue(kv.Value);
                }
            }
            writer.WriteEndObject();
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null) return null;
            var dict = new Dictionary<string, object>();
            JObject obj = JObject.Load(reader);
            foreach (var prop in obj.Properties())
            {
                JToken token = prop.Value;
                object val;
                switch (token.Type)
                {
                    case JTokenType.Boolean:
                        val = token.Value<bool>();
                        break;
                    case JTokenType.Float:
                        val = token.Value<float>();
                        break;
                    case JTokenType.Integer:
                        val = token.Value<float>();
                        break;
                    case JTokenType.String:
                        val = token.Value<string>();
                        break;
                    default:
                        val = token.ToString();
                        break;
                }
                dict[prop.Name] = val;
            }
            return dict;
        }
    }

    /// <summary>
    /// 存檔欄位命名規則與輪替輔助（design/11 §1, design/17 B4-2）。
    /// 手動 slot_001～slot_060、快速 quick_1～quick_3、自動 auto_1～auto_3。
    /// </summary>
    public static class SaveSlotNaming
    {
        public const int ManualSlotCount = 60;
        public const int QuickSlotCount = 3;
        public const int AutoSlotCount = 3;

        public static string Manual(int index)
        {
            if (index < 1 || index > ManualSlotCount)
                throw new ArgumentOutOfRangeException(nameof(index), $"Manual slot index must be between 1 and {ManualSlotCount}");
            return $"slot_{index:D3}";
        }

        public static string Quick(int index)
        {
            if (index < 1 || index > QuickSlotCount)
                throw new ArgumentOutOfRangeException(nameof(index), $"Quick slot index must be between 1 and {QuickSlotCount}");
            return $"quick_{index}";
        }

        public static string Auto(int index)
        {
            if (index < 1 || index > AutoSlotCount)
                throw new ArgumentOutOfRangeException(nameof(index), $"Auto slot index must be between 1 and {AutoSlotCount}");
            return $"auto_{index}";
        }

        public static bool IsValidSlotId(string slotId)
        {
            if (string.IsNullOrEmpty(slotId)) return false;
            if (slotId.StartsWith("slot_") && slotId.Length == 8)
            {
                if (int.TryParse(slotId.Substring(5), out int idx))
                    return idx >= 1 && idx <= ManualSlotCount;
            }
            if (slotId.StartsWith("quick_") && slotId.Length == 7)
            {
                if (int.TryParse(slotId.Substring(6), out int idx))
                    return idx >= 1 && idx <= QuickSlotCount;
            }
            if (slotId.StartsWith("auto_") && slotId.Length == 6)
            {
                if (int.TryParse(slotId.Substring(5), out int idx))
                    return idx >= 1 && idx <= AutoSlotCount;
            }
            return false;
        }

        public static string NextAutoSlot(string currentSlotId)
        {
            if (string.IsNullOrEmpty(currentSlotId) || !currentSlotId.StartsWith("auto_"))
                return Auto(1);
            if (int.TryParse(currentSlotId.Substring(5), out int idx))
            {
                int next = (idx % AutoSlotCount) + 1;
                return Auto(next);
            }
            return Auto(1);
        }
    }
}
