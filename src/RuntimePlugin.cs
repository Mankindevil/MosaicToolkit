using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using BepInEx;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace MosaicToolkit
{
    [BepInPlugin("local.mosaictoolkit.scanner", "Mosaic Toolkit Scanner", "0.1.11")]
    [DefaultExecutionOrder(32000)]
    public sealed class RuntimePlugin : BaseUnityPlugin
    {
        private class Tracked
        {
            public Renderer renderer;
            public Candidate info;
            public bool temporary;
            public bool changed;
            public bool original;
            public bool ruleWhole;
            public List<SlotRule> ruleSlots = new List<SlotRule>();
            public Dictionary<int, SlotRule> temporarySlots = new Dictionary<int, SlotRule>();
            public Dictionary<int, Material> originals = new Dictionary<int, Material>();
            public float nextMaterialCheck;
        }
        private Material hiddenMaterial;
        private bool materialWarning, scanComplete;
        private Dictionary<int, Tracked> targets = new Dictionary<int, Tracked>();
        private List<Tracked> controlled = new List<Tracked>();
        private Renderer[] scanItems;
        private int scanIndex;
        private Dictionary<int, Tracked> scanNext;
        private int publishing;
        private volatile string publishError;
        private bool viewer;
        private bool backgroundOwned, originalBackground;
        private Profile profile = new Profile();
        private string directory, session, executable, ack = "", message = "扫描插件已启动，只读发现模式。";
        private int pid, rendererCount, candidateCount;
        private float nextScan, nextPoll, nextSnapshot;
        private DateTime profileStamp, commandStamp;
        private bool ready;

        private void Awake()
        {
            directory = Path.Combine(Path.GetDirectoryName(typeof(RuntimePlugin).Assembly.Location), "session");
            Directory.CreateDirectory(directory);
            session = Guid.NewGuid().ToString("N");
            using (Process process = Process.GetCurrentProcess()) { pid = process.Id; executable = process.MainModule.FileName; }
            LoadProfile();
            ready = true;
            UpdateBackground(ViewerPresent());
            Logger.LogInfo("Mosaic Toolkit Scanner 0.1.11 ready; session=" + session + "; auto rules=" + profile.applyRules + "; temporary material slots supported");
        }
        private string ProfilePath { get { return Path.Combine(Path.GetDirectoryName(directory), "profile.json"); } }
        private void PersistProfile(Profile current)
        {
            string text = JsonCodec.Write(current);
            string suffix = Path.Combine("BepInEx", "plugins", Protocol.Folder, "profile.json");
            string full = Path.GetFullPath(ProfilePath);
            string work = full.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
                ? Path.Combine(full.Substring(0, full.Length - suffix.Length), "MosaicToolkit") : null;
            if (work != null)
            {
                for (DirectoryInfo d = new DirectoryInfo(Path.Combine(work, "backups")); d != null; d = d.Parent)
                    if (d.Exists && (d.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("规则输出目录包含链接，请使用实际目录。");
                if (current.backupBeforeChanges && File.Exists(ProfilePath))
                    Protocol.AtomicWrite(Path.Combine(work, "backups", "rules-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N") + ".json"), Protocol.ReadText(ProfilePath));
            }
            Protocol.AtomicWrite(ProfilePath, text);
            if (work != null) Protocol.AtomicWrite(Path.Combine(work, "profile.json"), text);
        }
        private void OnEnable()
        {
            if (directory != null) { ready = true; nextScan = nextSnapshot = 0; }
            SceneManager.sceneLoaded += OnSceneLoaded;
            RenderPipelineManager.beginCameraRendering += BeforeCamera;
        }
        private void OnSceneLoaded(Scene s, LoadSceneMode m) { CancelScan(); nextScan = 0; }
        private void BeforeCamera(ScriptableRenderContext context, Camera camera) { if (ready) Enforce(); }
        private bool ViewerPresent()
        {
            string path = Path.Combine(directory, "viewer.heartbeat");
            double age = (DateTime.UtcNow - File.GetLastWriteTimeUtc(path)).TotalSeconds;
            return File.Exists(path) && age >= -2 && age < 6;
        }
        private void UpdateBackground(bool connected)
        {
            // Unity APIs must stay on the game thread. The heartbeat is only a lease;
            // exported rules alone must never force the game to run in the background.
            if (connected && profile.backgroundWhileConnected != false)
            {
                if (!backgroundOwned) { originalBackground = Application.runInBackground; backgroundOwned = true; }
                Application.runInBackground = true;
            }
            else RestoreBackground();
        }
        private void RestoreBackground()
        {
            if (!backgroundOwned) return;
            Application.runInBackground = originalBackground;
            backgroundOwned = false;
        }
        private void OnApplicationFocus(bool focused)
        {
            if (!ready) return;
            // Acquire before the first unfocused frame can be paused by Unity.
            try { LoadProfile(); UpdateBackground(ViewerPresent()); }
            catch (Exception e) { Logger.LogWarning("后台运行设置更新失败: " + e.Message); }
        }
        private void LateUpdate()
        {
            if (!ready) return;
            try
            {
                float now = Time.realtimeSinceStartup;
                if (now >= nextPoll)
                {
                    nextPoll = now + 0.2f;
                    LoadProfile();
                    ReadCommand();
                    bool connected = ViewerPresent();
                    UpdateBackground(connected);
                    if (connected && !viewer) nextScan = nextSnapshot = 0;
                    viewer = connected;
                    if (publishError != null) { Logger.LogWarning(publishError); publishError = null; }
                }
                if (scanItems == null && now >= nextScan && (viewer || profile.applyRules || controlled.Count > 0))
                { nextScan = now + 5f; Scan(); }
                if (scanItems != null) ScanBatch();
                Enforce();
                if (viewer && now >= nextSnapshot) { nextSnapshot = now + 2f; Publish(); }
            }
            catch (Exception e)
            {
                message = "插件错误: " + e.Message;
                Logger.LogWarning(message);
                // Back off file/scan retries so a filesystem failure cannot spam every frame.
                nextPoll = nextScan = nextSnapshot = Time.realtimeSinceStartup + 3f;
            }
        }
        private void LoadProfile()
        {
            if (!File.Exists(ProfilePath))
            {
                if (ready) { profile = new Profile(); RestoreAll(); message = "配置被移除，已停止自动规则。"; }
                return;
            }
            DateTime stamp = File.GetLastWriteTimeUtc(ProfilePath);
            if (stamp == profileStamp) return;
            try
            {
                Profile next = JsonCodec.Read<Profile>(Protocol.ReadText(ProfilePath));
                Protocol.Validate(next);
                profile = next;
                profileStamp = stamp;
                nextScan = nextSnapshot = 0;
                message = profile.applyRules ? "已启用保存的规则。" : "自动规则已停用；临时开关仍有效。";
                foreach (Tracked t in targets.Values) t.info.ruleMatched = Protocol.Matches(profile, t.info);
                CancelScan(); RebuildControlled();
            }
            catch (Exception e)
            {
                profile = new Profile(); profileStamp = stamp;
                RestoreAll();
                message = "配置无效，已恢复并停止规则: " + e.Message;
                Logger.LogWarning(message);
            }
        }
        private static string FullPath(Transform t)
        {
            string value = t.name;
            while (t.parent != null) { t = t.parent; value = t.name + "/" + value; }
            return value;
        }
        private static int RuntimeId(Renderer renderer)
        {
            // Unity 6.4 retains this int API but emits CS0618. Older supported Unity
            // versions lack GetEntityId; keep the existing session/IPC ID contract.
            // Suppress only this compatibility call, not other compiler warnings.
#pragma warning disable 618
            return renderer.GetInstanceID();
#pragma warning restore 618
        }
        private Candidate Describe(Renderer r, Tracked tracked)
        {
            var shaders = new List<string>(); var materials = new List<string>();
            var slots = new List<MaterialSlot>();
            Material[] actual = r.sharedMaterials;
            for (int i = 0; i < actual.Length; i++)
            {
                Material mat = actual[i], original;
                if (hiddenMaterial != null && mat == hiddenMaterial && tracked != null && tracked.originals.TryGetValue(i, out original)) mat = original;
                slots.Add(new MaterialSlot { index = i, present = mat != null, material = mat == null ? "" : mat.name, shader = mat == null || mat.shader == null ? "" : mat.shader.name });
                if (mat == null) continue;
                materials.Add(mat.name);
                if (mat.shader != null) shaders.Add(mat.shader.name);
            }
            var c = new Candidate { id = RuntimeId(r), name = r.gameObject.name,
                path = r.gameObject.scene.name + ":/" + FullPath(r.transform), rendererType = r.GetType().Name,
                shaders = shaders.ToArray(), materials = materials.ToArray(), active = r.gameObject.activeInHierarchy,
                enabled = r.enabled, originalEnabled = r.enabled, slots = slots.ToArray() };
            c.evidence = Protocol.KeywordEvidence(profile, c.name, c.materials, c.shaders);
            c.ruleMatched = Protocol.Matches(profile, c);
            return c;
        }
        private void Scan()
        {
            scanItems = Resources.FindObjectsOfTypeAll<Renderer>(); scanIndex = 0;
            scanNext = new Dictionary<int, Tracked>();
        }
        private void CancelScan()
        {
            if (scanNext != null) foreach (var pair in scanNext) if (!targets.ContainsKey(pair.Key)) Restore(pair.Value);
            scanItems = null; scanNext = null;
        }
        private void ScanBatch()
        {
            var watch = Stopwatch.StartNew(); int processed = 0;
            while (scanIndex < scanItems.Length && processed < 32 && (processed == 0 || watch.ElapsedMilliseconds < 2))
            {
                Renderer r = scanItems[scanIndex++]; processed++;
                if (r == null || !r.gameObject.scene.IsValid() || !r.gameObject.scene.isLoaded) continue;
                Tracked old;
                targets.TryGetValue(RuntimeId(r), out old);
                Candidate c = Describe(r, old);
                bool same = old != null && old.renderer == r && old.info.path == c.path && old.info.name == c.name;
                // If pooled objects change identity, restore their previous state before re-evaluating.
                if (old != null && !same) { Restore(old); c = Describe(r, null); }
                Tracked t = same ? old : new Tracked { renderer = r };
                t.info = c;
                Configure(t);
                scanNext[c.id] = t;
                if (t.temporary || t.temporarySlots.Count > 0 || (profile.applyRules && c.ruleMatched) || t.changed || t.originals.Count > 0)
                    if (!controlled.Contains(t)) controlled.Add(t);
            }
            if (scanIndex < scanItems.Length) return;
            foreach (var pair in targets)
                if (!scanNext.ContainsKey(pair.Key)) Restore(pair.Value);
            targets = scanNext; scanNext = null; scanItems = null;
            DiscoverRules();
            RebuildControlled(); nextSnapshot = 0; scanComplete = true;
        }
        private void DiscoverRules()
        {
            if (!profile.autoDiscover || !profile.applyRules) return;
            var candidates = new List<Candidate>(); foreach (Tracked t in targets.Values) candidates.Add(t.info);
            int skipped; Rule[] found = Protocol.DefaultRules(profile, candidates.ToArray(), out skipped);
            var known = new HashSet<string>(profile.blockedRuleKeys ?? new string[0]); foreach (Rule rule in profile.rules) known.Add(rule.Key());
            bool any = false; foreach (Rule rule in found) if (!known.Contains(rule.Key())) { any = true; break; }
            if (!any || profile.rules.Length >= 2000) return;
            // The game never waits for the desktop to finish a profile edit.
            using (var lease = Protocol.LockProfile(ProfilePath, 0))
            {
                if (lease == null) return;
                Profile current = JsonCodec.Read<Profile>(Protocol.ReadText(ProfilePath)); Protocol.Validate(current);
                if (!current.autoDiscover || !current.applyRules) return;
                found = Protocol.DefaultRules(current, candidates.ToArray(), out skipped);
                int added = Protocol.AppendRules(current, found);
                if (added == 0) return;
                PersistProfile(current);
                profileStamp = File.GetLastWriteTimeUtc(ProfilePath); profile = current;
                message = "已自动补充 " + added + " 条规则，共 " + current.rules.Length + " 条。" + (current.rules.Length >= 2000 ? " 已达 2000 条上限。" : "");
            }
        }
        private void Configure(Tracked t)
        {
            t.ruleWhole = false; t.ruleSlots.Clear(); t.nextMaterialCheck = 0;
            foreach (Rule rule in profile.rules)
            {
                if (!rule.Matches(t.info)) continue;
                if (rule.mode == "slots") t.ruleSlots.AddRange(rule.slots);
                else t.ruleWhole = true;
            }
            t.info.ruleMatched = t.ruleWhole || t.ruleSlots.Count > 0;
        }
        private void RebuildControlled()
        {
            controlled.Clear();
            foreach (Tracked t in targets.Values)
            {
                Configure(t);
                if (t.temporary || t.temporarySlots.Count > 0 || t.changed || t.originals.Count > 0 || (profile.applyRules && t.info.ruleMatched)) controlled.Add(t);
            }
        }
        private void Enforce()
        {
            foreach (Tracked t in controlled)
            {
                if (t.renderer == null) continue;
                bool hold = t.temporary || (profile.applyRules && t.ruleWhole);
                bool slots = !hold && (t.temporarySlots.Count > 0 || (profile.applyRules && t.ruleSlots.Count > 0));
                // Ordinary catalog entries need no per-frame native property reads.
                if (!hold && !slots && !t.changed && t.originals.Count == 0) continue;
                // Name changes release a target immediately; the next scan handles other metadata changes.
                if (t.renderer.gameObject.name != t.info.name) { t.temporary = false; Restore(t); continue; }
                if (hold)
                {
                    RestoreSlots(t);
                    if (!t.changed) { t.original = t.renderer.enabled; t.changed = true; }
                    if (t.renderer.enabled) t.renderer.enabled = false;
                }
                else if (slots)
                {
                    if (t.changed) { t.renderer.enabled = t.original; t.changed = false; }
                    if (Time.realtimeSinceStartup >= t.nextMaterialCheck)
                    { t.nextMaterialCheck = Time.realtimeSinceStartup + 0.25f; ApplySlots(t); }
                }
                else Restore(t);
                t.info.enabled = t.renderer.enabled;
                t.info.originalEnabled = t.changed ? t.original : t.renderer.enabled;
                t.info.active = t.renderer.gameObject.activeInHierarchy;
                t.info.held = hold || t.originals.Count > 0;
            }
        }
        private Material InvisibleMaterial()
        {
            if (hiddenMaterial != null) return hiddenMaterial;
            Shader shader = Shader.Find("UI/Default");
            if (shader == null || !shader.isSupported) return null;
            var mat = new Material(shader) { name = "MosaicToolkit Invisible", hideFlags = HideFlags.HideAndDontSave };
            if (!mat.HasProperty("_ColorMask")) { UnityEngine.Object.Destroy(mat); return null; }
            mat.SetInt("_ColorMask", 0); mat.SetInt("_StencilWriteMask", 0); mat.SetInt("_StencilOp", 0);
            // UI/Default has ZWrite Off and a configurable stencil comparison.
            // Never pass the stencil test, and also mask color writes.
            mat.SetInt("_StencilComp", 1);
            hiddenMaterial = mat; return mat;
        }
        private void ApplySlots(Tracked t)
        {
            Material invisible = InvisibleMaterial();
            if (invisible == null)
            {
                if (!materialWarning) { message = "此游戏缺少可用的隐藏材质，已跳过材质槽规则；不会改为关闭整个 Renderer。"; Logger.LogWarning(message); materialWarning = true; }
                return;
            }
            Material[] current = t.renderer.sharedMaterials; bool changed = false;
            var next = new Dictionary<int, Material>();
            for (int i = 0; i < current.Length; i++)
            {
                Material original, material = current[i];
                if (material == invisible && t.originals.TryGetValue(i, out original)) material = original;
                var slot = new MaterialSlot { index = i, present = material != null, material = material == null ? "" : material.name,
                    shader = material == null || material.shader == null ? "" : material.shader.name };
                bool hide = false;
                if (profile.applyRules) foreach (SlotRule rule in t.ruleSlots) if (rule.Matches(slot)) { hide = true; break; }
                SlotRule temporary;
                if (t.temporarySlots.TryGetValue(i, out temporary))
                {
                    if (temporary.Matches(slot)) hide = true;
                    else t.temporarySlots.Remove(i);
                }
                if (hide)
                {
                    next[i] = material;
                    if (current[i] != invisible) { current[i] = invisible; changed = true; }
                }
                else if (current[i] == invisible && t.originals.ContainsKey(i)) { current[i] = material; changed = true; }
            }
            if (changed) t.renderer.sharedMaterials = current;
            foreach (int index in new List<int>(t.temporarySlots.Keys)) if (index >= current.Length) t.temporarySlots.Remove(index);
            t.originals = next;
            t.info.hiddenSlots = new List<int>(next.Keys).ToArray();
        }
        private void RestoreSlots(Tracked t)
        {
            if (t.originals.Count == 0) return;
            if (t.renderer != null)
            {
                Material[] current = t.renderer.sharedMaterials; bool changed = false;
                foreach (var pair in t.originals)
                    if (pair.Key < current.Length && hiddenMaterial != null && current[pair.Key] == hiddenMaterial)
                    { current[pair.Key] = pair.Value; changed = true; }
                if (changed) t.renderer.sharedMaterials = current;
            }
            t.originals.Clear();
            if (t.info != null) t.info.hiddenSlots = new int[0];
        }
        private void Restore(Tracked t)
        {
            t.temporarySlots.Clear();
            RestoreSlots(t);
            if (t.changed && t.renderer != null) t.renderer.enabled = t.original;
            t.changed = false;
            if (t.info != null && t.renderer != null) { t.info.enabled = t.info.originalEnabled = t.renderer.enabled; t.info.held = false; t.info.hiddenSlots = new int[0]; }
        }
        private void RestoreAll()
        { CancelScan(); foreach (Tracked t in targets.Values) { t.temporary = false; Restore(t); } RebuildControlled(); }
        private void ReadCommand()
        {
            string path = Path.Combine(directory, "command.json");
            if (!File.Exists(path)) return;
            DateTime stamp = File.GetLastWriteTimeUtc(path);
            if (stamp == commandStamp) return;
            Command c = JsonCodec.Read<Command>(Protocol.ReadText(path)); commandStamp = stamp;
            if (c == null || c.protocol != 1 || c.session != session || String.IsNullOrEmpty(c.id) || c.id == ack) return;
            if (c.action == "scan") { CancelScan(); nextScan = 0; }
            else if (c.action == "restoreAll")
            {
                using (var lease = Protocol.LockProfile(ProfilePath, 0))
                {
                    if (lease == null) { commandStamp = DateTime.MinValue; return; }
                    var current = JsonCodec.Read<Profile>(Protocol.ReadText(ProfilePath)); Protocol.Validate(current);
                    current.applyRules = false; current.autoDiscover = false;
                    PersistProfile(current); profile = current;
                    profileStamp = File.GetLastWriteTimeUtc(ProfilePath);
                }
                RestoreAll();
            }
            else if (c.action == "off" || c.action == "restore")
            {
                foreach (int id in c.ids ?? new int[0])
                {
                    Tracked t;
                    if (!targets.TryGetValue(id, out t) || t.renderer == null) continue;
                    t.temporary = c.action == "off";
                    if (!t.temporary) Restore(t);
                }
            }
            else if (c.action == "slotsOff" || c.action == "slotsRestore")
            {
                try
                {
                    Tracked t;
                    if (c.ids == null || c.ids.Length != 1 || !targets.TryGetValue(c.ids[0], out t) || t.renderer == null)
                        throw new InvalidOperationException("对象已失效，请重新打开材质详情。");
                    Candidate actual = Describe(t.renderer, t);
                    if (actual.path != c.targetPath || actual.name != c.targetName || actual.rendererType != c.targetType)
                        throw new InvalidOperationException("对象身份已变化，已拒绝旧窗口操作。");
                    Protocol.ValidateSlots(actual, c.slots); t.info = actual; Configure(t);
                    if (c.action == "slotsOff")
                    {
                        if (!t.renderer.enabled || t.temporary || (profile.applyRules && t.ruleWhole))
                            throw new InvalidOperationException("整个 Renderer 已关闭，请先恢复或停用对应规则，再测试材质槽。");
                        if (InvisibleMaterial() == null) throw new InvalidOperationException("游戏缺少可用的隐藏材质，未修改材质槽。");
                        foreach (SlotRule slot in c.slots) t.temporarySlots[slot.index] = slot;
                    }
                    else
                    {
                        if (profile.applyRules)
                            foreach (SlotRule slot in c.slots)
                            {
                                if (t.ruleWhole) throw new InvalidOperationException("整个对象受规则控制，请先停用对应规则。");
                                foreach (SlotRule rule in t.ruleSlots) if (rule.Matches(actual.slots[slot.index]))
                                    throw new InvalidOperationException("选中槽受自动规则控制，请先停用对应规则再恢复。");
                            }
                        foreach (SlotRule slot in c.slots) t.temporarySlots.Remove(slot.index);
                    }
                    t.info = actual; RebuildControlled(); Enforce();
                    message = "已" + (c.action == "slotsOff" ? "临时隐藏" : "恢复") + "材质槽 " + c.slots.Length + " 项。";
                }
                catch (Exception e) { message = "材质操作未完成: " + e.Message; }
                ack = c.id; nextSnapshot = 0; return;
            }
            else { ack = c.id; message = "不支持的操作。"; nextSnapshot = 0; return; }
            ack = c.id;
            RebuildControlled();
            message = "已执行: " + c.action + "; 目标 " + (c.ids ?? new int[0]).Length;
            nextSnapshot = 0;
        }
        private void Publish()
        {
            if (Interlocked.CompareExchange(ref publishing, 1, 0) != 0) return;
            try
            {
            var list = new List<Candidate>();
            var all = new List<Candidate>();
            foreach (Tracked t in targets.Values)
            {
                if (t.renderer == null) continue;
                // Snapshot plain managed data. The worker must never access Unity objects.
                Candidate copy = t.info.Copy();
                all.Add(copy);
                if (!String.IsNullOrEmpty(copy.evidence) || copy.ruleMatched || t.temporary || t.temporarySlots.Count > 0) list.Add(copy);
            }
            candidateCount = list.Count;
            rendererCount = all.Count;
            bool truncated = list.Count > 1500;
            if (truncated) list.RemoveRange(1500, list.Count - 1500);
            bool allTruncated = all.Count > 10000;
            if (allTruncated) all.RemoveRange(10000, all.Count - 10000);
            var snapshot = new Snapshot { session = session, pid = pid, executable = executable,
                utc = DateTime.UtcNow.ToString("o"), rendererCount = rendererCount, candidateCount = candidateCount,
                applyRules = profile.applyRules, truncated = truncated, allTruncated = allTruncated, ack = ack, message = message, candidates = list.ToArray(), renderers = all.ToArray(), features = 5, backgroundRunActive = backgroundOwned && Application.runInBackground, scanComplete = scanComplete, autoDiscover = profile.autoDiscover };
            Protocol.ValidateSnapshot(snapshot);
            string outputPath = Path.Combine(directory, "snapshot.json");
            ThreadPool.QueueUserWorkItem(delegate
            {
                try { Protocol.AtomicWrite(outputPath, JsonCodec.Write(snapshot)); }
                catch (Exception e) { publishError = "快照写入失败: " + e.Message; }
                finally { Interlocked.Exchange(ref publishing, 0); }
            });
            }
            catch { Interlocked.Exchange(ref publishing, 0); throw; }
        }
        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            RenderPipelineManager.beginCameraRendering -= BeforeCamera;
            RestoreBackground(); RestoreAll(); ready = false;
        }
        // Awake is not called again if the component is manually re-enabled.
        private void OnDestroy() { RestoreBackground(); RestoreAll(); if (hiddenMaterial != null) UnityEngine.Object.Destroy(hiddenMaterial); }
    }
}
