using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Threading;

namespace MosaicToolkit
{
    // .NET serialization works for types loaded by BepInEx at runtime. Unity's
    // native JsonUtility can silently omit arrays of these external custom types.
    public static class JsonCodec
    {
        private static class Cache<T>
        {
            [ThreadStatic] private static DataContractJsonSerializer serializer;
            public static DataContractJsonSerializer Serializer { get { return serializer ?? (serializer = new DataContractJsonSerializer(typeof(T))); } }
        }
        public static string Write<T>(T value)
        {
            using (var stream = new MemoryStream())
            {
                Cache<T>.Serializer.WriteObject(stream, value);
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }
        public static T Read<T>(string text)
        {
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(text)))
                return (T)Cache<T>.Serializer.ReadObject(stream);
        }
    }
    [Serializable] public class Rule
    {
        public string name = "";
        public string shader = "";
        public string rendererType = "";
        public string path = "";
        public bool enabled = true;
        public string mode = "renderer";
        public SlotRule[] slots;
        public bool generated;
        public bool Matches(string objectName, string type, string fullPath, string[] shaders)
        {
            // Empty rules must never match the whole scene.
            if (!enabled || String.IsNullOrEmpty(name)) return false;
            if (!String.Equals(name, objectName, StringComparison.Ordinal)) return false;
            if (!String.IsNullOrEmpty(rendererType) && rendererType != type) return false;
            if (!String.IsNullOrEmpty(path) && path != fullPath) return false;
            return String.IsNullOrEmpty(shader) || Array.IndexOf(shaders ?? new string[0], shader) >= 0;
        }
        public bool Matches(Candidate c)
        {
            if (!Matches(c.name, c.rendererType, c.path, c.shaders)) return false;
            if (slots == null || slots.Length == 0) return mode != "slots";
            if (c.slots == null) return false;
            if (mode != "slots" && c.slots.Length != slots.Length) return false;
            bool any = false;
            foreach (SlotRule slot in slots)
            {
                bool hit = slot.index >= 0 && slot.index < c.slots.Length && slot.Matches(c.slots[slot.index]);
                if (mode != "slots" && !hit) return false;
                any |= hit;
            }
            return any;
        }
        public string Key()
        {
            string key = name + "\n" + shader + "\n" + rendererType + "\n" + path + "\n" + (mode ?? "renderer");
            foreach (SlotRule slot in slots ?? new SlotRule[0]) key += "\n" + slot.index + ":" + slot.material + ":" + slot.shader;
            return key;
        }
    }
    [Serializable] public class MaterialSlot
    {
        public int index;
        public string material;
        public string shader;
        public bool present;
    }
    [Serializable] public class SlotRule
    {
        public int index;
        public string material;
        public string shader;
        public bool Matches(MaterialSlot slot) { return slot != null && slot.present && index == slot.index && material == slot.material && shader == slot.shader; }
    }
    [Serializable] public class Profile
    {
        public int protocol = 1;
        public bool applyRules = false;
        public bool autoDiscover;
        public bool backupBeforeChanges;
        // Missing in older profiles: enable compatibility while a viewer is present.
        [System.Runtime.Serialization.OptionalField]
        public bool? backgroundWhileConnected;
        public string[] blockedRuleKeys = new string[0];
        public string[] keywords = new string[] { "mosaic", "モザ", "censor" };
        public Rule[] rules = new Rule[0];
    }
    [Serializable] public class Candidate
    {
        public int id;
        public string name;
        public string path;
        public string rendererType;
        public string[] shaders;
        public string[] materials;
        public bool active;
        public bool enabled;
        public bool originalEnabled;
        public bool held;
        public bool ruleMatched;
        public string evidence;
        public MaterialSlot[] slots;
        public int[] hiddenSlots;
        public Candidate Copy() { return (Candidate)MemberwiseClone(); }
    }
    [Serializable] public class Snapshot
    {
        public int protocol = 1;
        public string session;
        public int pid;
        public string executable;
        public string utc;
        public int rendererCount;
        public int candidateCount;
        public bool truncated;
        public bool allTruncated;
        public bool applyRules;
        public string ack;
        public string message;
        public Candidate[] candidates = new Candidate[0];
        public Candidate[] renderers = new Candidate[0];
        public int features;
        [System.Runtime.Serialization.OptionalField]
        public bool backgroundRunActive;
        public bool scanComplete;
        public bool autoDiscover;
    }
    [Serializable] public class Command
    {
        public int protocol = 1;
        public string session;
        public string id;
        public string action;
        public int[] ids = new int[0];
        public string targetPath, targetName, targetType;
        public SlotRule[] slots;
    }
    public static class Protocol
    {
        public const string Folder = "MosaicToolkit";
        public static void ValidateSlots(Candidate target, SlotRule[] slots)
        {
            if (target == null || String.IsNullOrEmpty(target.path) || slots == null || slots.Length == 0 || target.slots == null)
                throw new InvalidDataException("请选择有效对象的材质槽。");
            var indices = new HashSet<int>();
            foreach (SlotRule slot in slots)
                if (slot == null || !indices.Add(slot.index) || slot.index < 0 || slot.index >= target.slots.Length ||
                    String.IsNullOrEmpty(slot.material) || String.IsNullOrEmpty(slot.shader) || !slot.Matches(target.slots[slot.index]))
                    throw new InvalidDataException("材质槽已变化或为空，请重新勾选。");
        }
        public static Rule[] ManualSlotRules(Candidate target, SlotRule[] slots)
        {
            ValidateSlots(target, slots);
            var rules = new List<Rule>();
            foreach (SlotRule slot in slots)
                rules.Add(new Rule { name = target.name, rendererType = target.rendererType, path = target.path, mode = "slots",
                    slots = new[] { new SlotRule { index = slot.index, material = slot.material, shader = slot.shader } } });
            return rules.ToArray();
        }
        private sealed class ProfileLease : IDisposable
        {
            private Mutex mutex;
            public ProfileLease(Mutex value) { mutex = value; }
            public void Dispose() { mutex.ReleaseMutex(); mutex.Dispose(); }
        }
        public static IDisposable LockProfile(string path, int timeout)
        {
            string key;
            using (var sha = SHA256.Create()) key = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(Path.GetFullPath(path).ToUpperInvariant()))).Replace("-", "");
            var mutex = new Mutex(false, "Local\\MosaicToolkitProfile-" + key);
            bool acquired;
            try { acquired = mutex.WaitOne(timeout); } catch (AbandonedMutexException) { acquired = true; }
            if (acquired) return new ProfileLease(mutex);
            mutex.Dispose(); return null;
        }
        public static int AppendRules(Profile profile, IEnumerable<Rule> incoming)
        {
            var keys = new HashSet<string>(profile.blockedRuleKeys ?? new string[0]);
            var list = new List<Rule>(profile.rules);
            foreach (Rule rule in list) keys.Add(rule.Key());
            int count = 0;
            foreach (Rule rule in incoming)
                if (list.Count < 2000 && keys.Add(rule.Key())) { list.Add(rule); count++; }
            profile.rules = list.ToArray();
            return count;
        }
        public static string ReadText(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var reader = new StreamReader(stream, Encoding.UTF8)) return reader.ReadToEnd();
        }
        public static string KeywordEvidence(Profile p, string name, string[] materials, string[] shaders)
        {
            foreach (string raw in p.keywords ?? new string[0])
            {
                string k = (raw ?? "").Trim();
                if (k.Length == 0) continue;
                if ((name ?? "").IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0) return "对象名: " + k;
                foreach (string s in shaders ?? new string[0])
                    if ((s ?? "").IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0) return "Shader: " + k;
                foreach (string m in materials ?? new string[0])
                    if ((m ?? "").IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0) return "材质: " + k;
            }
            return "";
        }
        public static bool Matches(Profile p, Candidate c)
        {
            foreach (Rule r in p.rules ?? new Rule[0])
                if (r != null && r.Matches(c)) return true;
            return false;
        }
        public static Rule[] DefaultRules(Profile profile, Candidate[] candidates, out int skipped)
        {
            skipped = 0; var result = new List<Rule>(); var keys = new HashSet<string>();
            foreach (Candidate c in candidates)
            {
                if (c.slots == null || c.slots.Length == 0) { skipped++; continue; }
                var hits = new List<SlotRule>(); bool all = true;
                foreach (MaterialSlot slot in c.slots)
                {
                    bool hit = slot != null && slot.present && !String.IsNullOrEmpty(slot.shader) &&
                        KeywordEvidence(profile, "", new[] { slot.material }, new[] { slot.shader }).Length > 0;
                    // A single-material object can also be discovered by its own name.
                    if (!hit && c.slots.Length == 1 && slot != null && slot.present && !String.IsNullOrEmpty(slot.shader))
                        hit = KeywordEvidence(profile, c.name, null, null).Length > 0;
                    if (hit) hits.Add(new SlotRule { index = slot.index, material = slot.material, shader = slot.shader }); else all = false;
                }
                if (hits.Count == 0) { skipped++; continue; }
                var rule = new Rule { name = c.name, rendererType = c.rendererType, mode = all ? "renderer" : "slots", slots = hits.ToArray(), generated = true };
                if (keys.Add(rule.Key())) result.Add(rule);
            }
            return result.ToArray();
        }
        public static void Validate(Profile p)
        {
            if (p == null || p.protocol < 1 || p.protocol > 3) throw new InvalidDataException("配置格式不兼容。");
            if (p.autoDiscover && p.protocol < 3) throw new InvalidDataException("自动补充规则需要协议 3 / 扫描插件 0.1.7。");
            if (p.blockedRuleKeys != null && p.blockedRuleKeys.Length > 10000) throw new InvalidDataException("排除规则过多。");
            if (p.keywords == null || p.keywords.Length > 100 || p.rules == null || p.rules.Length > 2000)
                throw new InvalidDataException("配置条目过多或缺少字段。");
            foreach (Rule r in p.rules)
            {
                if (r == null || String.IsNullOrWhiteSpace(r.name)) throw new InvalidDataException("规则必须包含准确对象名。");
                if (!String.IsNullOrEmpty(r.mode) && r.mode != "renderer" && r.mode != "slots") throw new InvalidDataException("未知规则模式。");
                if (r.mode == "slots" && (r.slots == null || r.slots.Length == 0)) throw new InvalidDataException("材质规则缺少目标槽。");
                if (r.slots != null && r.slots.Length > 0)
                {
                    if (p.protocol < 2) throw new InvalidDataException("材质槽规则需要协议 2 / 扫描插件 0.1.6。");
                    var indices = new HashSet<int>();
                    foreach (SlotRule slot in r.slots)
                        if (slot == null || slot.index < 0 || String.IsNullOrEmpty(slot.material) || String.IsNullOrEmpty(slot.shader) || !indices.Add(slot.index))
                            throw new InvalidDataException("材质槽规则无效或重复。");
                }
            }
        }
        public static void ValidateSnapshot(Snapshot s)
        {
            if (s == null || s.protocol != 1) throw new InvalidDataException("快照协议无效。");
            if (s.candidates == null || (!s.truncated && s.candidateCount != s.candidates.Length) ||
                (s.truncated && s.candidates.Length == 0 && s.candidateCount > 0))
                throw new InvalidDataException("扫描插件只传出了数量，候选明细缺失。请退出游戏，用 0.1.3 或更新版本重新安装扫描插件。");
            if (s.renderers == null || (!s.allTruncated && s.rendererCount != s.renderers.Length) ||
                (s.allTruncated && s.rendererCount > 0 && s.renderers.Length == 0))
                throw new InvalidDataException("扫描插件未提供全部 Renderer 明细。请退出游戏，用 0.1.3 或更新版本重新安装扫描插件。");
        }
        public static void AtomicWrite(string path, string content)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temp, content, new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(temp, path, null);
                else File.Move(temp, path);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
    }
}
